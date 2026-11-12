// ReminderX sync API. Devices of one "vault" (a desktop install plus the phones paired to it) push
// and pull encrypted reminder/to-do records. Payloads are AES-GCM ciphertext made on the devices
// with a key that only travels inside the pairing QR: this service never sees reminder content.
//
// Routes (all JSON):
//   POST   /api/vaults                  desktop creates its vault           -> { vaultId, deviceId, token }
//   DELETE /api/vaults/current          (auth) delete the vault and everything in it
//   POST   /api/pairings                (auth) desktop starts a pairing     -> { code, expiresAt }
//   POST   /api/pairings/complete       phone redeems the QR code           -> { vaultId, deviceId, token }
//   GET    /api/devices                 (auth) devices of the vault
//   DELETE /api/devices/:id             (auth) unlink a device
//   POST   /api/sync                    (auth) { since, changes[] }         -> { cursor, changes[], more }
//   GET    /api/health                  liveness + whether storage is configured

import { createHash, randomBytes, timingSafeEqual } from "node:crypto";

const ID = /^[A-Za-z0-9_-]{1,64}$/;
const KINDS = new Set(["reminder", "todo"]);
const MAX_PAYLOAD = 200_000;        // base64 chars per record (attachments never sync)
const MAX_CHANGES_PER_PUSH = 200;   // stays well under Firestore's 500 writes per transaction
const MAX_PULL = 500;
const MAX_DEVICES = 10;
const PAIRING_TTL_MS = 10 * 60 * 1000;

export const newId = (bytes = 12) => randomBytes(bytes).toString("base64url");
const sha256 = (text) => createHash("sha256").update(text).digest("hex");

class HttpError extends Error {
  constructor(status, message) {
    super(message);
    this.status = status;
  }
}

const json = (status, body) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "content-type": "application/json", "cache-control": "no-store" },
  });

async function readBody(req) {
  try {
    return await req.json();
  } catch {
    throw new HttpError(400, "Body must be JSON");
  }
}

function cleanName(value, fallback) {
  const text = typeof value === "string" ? value.trim().slice(0, 60) : "";
  return text || fallback;
}

/** Bearer <vaultId>.<deviceId>.<secret>; only sha256(secret) is stored. */
async function authenticate(req, store) {
  const header = req.headers.get("authorization") ?? "";
  const match = /^Bearer ([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]+)$/.exec(header);
  if (!match) throw new HttpError(401, "Missing or malformed token");

  const [, vaultId, deviceId, secret] = match;
  const device = await store.getDevice(vaultId, deviceId);
  if (!device || device.revoked) throw new HttpError(401, "This device is not linked");

  const expected = Buffer.from(device.tokenHash, "hex");
  const actual = Buffer.from(sha256(secret), "hex");
  if (expected.length !== actual.length || !timingSafeEqual(expected, actual))
    throw new HttpError(401, "This device is not linked");

  return { vaultId, deviceId, device };
}

async function issueDevice(store, vaultId, { name, platform }) {
  const deviceId = newId(9);
  const secret = newId(32);
  await store.putDevice(vaultId, deviceId, {
    name,
    platform,
    tokenHash: sha256(secret),
    createdAt: Date.now(),
    lastSeenAt: Date.now(),
    revoked: false,
  });
  return { vaultId, deviceId, token: `${vaultId}.${deviceId}.${secret}` };
}

function validateChange(change) {
  if (!change || typeof change !== "object") throw new HttpError(400, "Each change must be an object");
  const { id, kind, updatedAt, deleted, data } = change;
  if (typeof id !== "string" || !ID.test(id)) throw new HttpError(400, "Change id must be 1-64 URL-safe characters");
  if (!KINDS.has(kind)) throw new HttpError(400, "Change kind must be reminder or todo");
  if (!Number.isSafeInteger(updatedAt) || updatedAt <= 0) throw new HttpError(400, "updatedAt must be epoch milliseconds");
  if (deleted !== undefined && typeof deleted !== "boolean") throw new HttpError(400, "deleted must be a boolean");
  if (!deleted && (typeof data !== "string" || data.length === 0)) throw new HttpError(400, "data is required unless deleted");
  if (typeof data === "string" && data.length > MAX_PAYLOAD) throw new HttpError(413, "Record too large");
  return { id, kind, updatedAt, deleted: deleted === true, data: deleted ? null : data };
}

const routes = [
  ["GET", /^\/api\/health$/, async (_req, store) => json(200, { ok: true, storage: store.name })],

  ["POST", /^\/api\/vaults$/, async (req, store) => {
    const body = await readBody(req);
    const vaultId = newId(12);
    await store.createVault(vaultId, { createdAt: Date.now(), seq: 0 });
    const issued = await issueDevice(store, vaultId, {
      name: cleanName(body.name, "Desktop"),
      platform: "windows",
    });
    return json(201, issued);
  }],

  // "Unlink everything" on the desktop: removes the vault, its devices and every synced record.
  ["DELETE", /^\/api\/vaults\/current$/, async (req, store) => {
    const { vaultId } = await authenticate(req, store);
    await store.deleteVault(vaultId);
    return json(200, { ok: true });
  }],

  ["POST", /^\/api\/pairings$/, async (req, store) => {
    const { vaultId } = await authenticate(req, store);
    const devices = await store.listDevices(vaultId);
    if (devices.filter((d) => !d.revoked).length >= MAX_DEVICES)
      throw new HttpError(409, `A vault can link at most ${MAX_DEVICES} devices; unlink one first`);

    const code = newId(18);
    const expiresAt = Date.now() + PAIRING_TTL_MS;
    await store.putPairing(vaultId, sha256(code), { expiresAt, used: false });
    return json(201, { code, expiresAt });
  }],

  ["POST", /^\/api\/pairings\/complete$/, async (req, store) => {
    const body = await readBody(req);
    if (typeof body.vaultId !== "string" || !ID.test(body.vaultId) || typeof body.code !== "string")
      throw new HttpError(400, "vaultId and code are required");

    const ok = await store.takePairing(body.vaultId, sha256(body.code), Date.now());
    if (!ok) throw new HttpError(403, "This QR code is invalid, expired or already used. Show a new one on the desktop.");

    const issued = await issueDevice(store, body.vaultId, {
      name: cleanName(body.name, "Phone"),
      platform: cleanName(body.platform, "android"),
    });
    return json(201, issued);
  }],

  ["GET", /^\/api\/devices$/, async (req, store) => {
    const { vaultId, deviceId } = await authenticate(req, store);
    const devices = await store.listDevices(vaultId);
    return json(200, {
      devices: devices
        .filter((d) => !d.revoked)
        .map(({ id, name, platform, createdAt, lastSeenAt }) => ({
          id, name, platform, createdAt, lastSeenAt, self: id === deviceId,
        })),
    });
  }],

  ["DELETE", /^\/api\/devices\/([A-Za-z0-9_-]{1,64})$/, async (req, store, [targetId]) => {
    const { vaultId } = await authenticate(req, store);
    const target = await store.getDevice(vaultId, targetId);
    if (!target) throw new HttpError(404, "No such device");
    await store.putDevice(vaultId, targetId, { ...target, revoked: true, revokedAt: Date.now() });
    return json(200, { ok: true });
  }],

  ["POST", /^\/api\/sync$/, async (req, store) => {
    const { vaultId, deviceId } = await authenticate(req, store);
    const body = await readBody(req);

    const since = body.since ?? 0;
    if (!Number.isSafeInteger(since) || since < 0) throw new HttpError(400, "since must be a non-negative integer");

    const changes = Array.isArray(body.changes) ? body.changes : [];
    if (changes.length > MAX_CHANGES_PER_PUSH)
      throw new HttpError(413, `Push at most ${MAX_CHANGES_PER_PUSH} changes at a time`);

    const accepted = changes.length > 0
      ? await store.applyChanges(vaultId, deviceId, changes.map(validateChange))
      : 0;

    const { items, cursor, more } = await store.pull(vaultId, since, MAX_PULL);
    await store.touchDevice(vaultId, deviceId, Date.now());

    return json(200, {
      accepted,
      cursor,
      more,
      changes: items.map(({ id, kind, updatedAt, deleted, data, seq, from }) => ({
        id, kind, updatedAt, deleted, data, seq, from,
      })),
    });
  }],
];

export async function handle(req, store) {
  const url = new URL(req.url);
  try {
    for (const [method, pattern, fn] of routes) {
      const match = pattern.exec(url.pathname);
      if (match && req.method === method) return await fn(req, store, match.slice(1));
    }
    return json(404, { error: "Not found" });
  } catch (err) {
    if (err instanceof HttpError) return json(err.status, { error: err.message });
    console.error(err);
    return json(500, { error: "Server error" });
  }
}

/**
 * Last writer wins per record: a change replaces the stored one only if it is newer
 * (ties broken by device id so every device converges on the same answer).
 */
export function isNewer(incoming, incomingDevice, existing) {
  if (!existing) return true;
  if (incoming.updatedAt !== existing.updatedAt) return incoming.updatedAt > existing.updatedAt;
  return incomingDevice > (existing.from ?? "");
}
