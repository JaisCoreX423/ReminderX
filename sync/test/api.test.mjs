import { test } from "node:test";
import assert from "node:assert/strict";
import { handle } from "../lib/api.mjs";
import { createMemoryStore } from "../lib/store-memory.mjs";

const BASE = "https://sync.test";

function client(store) {
  return async (method, path, { token, body } = {}) => {
    const headers = { "content-type": "application/json" };
    if (token) headers.authorization = `Bearer ${token}`;
    const res = await handle(new Request(BASE + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    }), store);
    return { status: res.status, body: await res.json() };
  };
}

async function setup() {
  const store = createMemoryStore();
  const call = client(store);
  const desktop = (await call("POST", "/api/vaults", { body: { name: "Alfred's PC" } })).body;
  const { code } = (await call("POST", "/api/pairings", { token: desktop.token })).body;
  const phone = (await call("POST", "/api/pairings/complete", {
    body: { vaultId: desktop.vaultId, code, name: "Pixel", platform: "android" },
  })).body;
  return { store, call, desktop, phone, code };
}

const change = (id, updatedAt, extra = {}) => ({ id, kind: "reminder", updatedAt, data: `cipher-${id}-${updatedAt}`, ...extra });

test("health", async () => {
  const { call } = await setup();
  assert.deepEqual((await call("GET", "/api/health")).body, { ok: true, storage: "memory" });
});

test("desktop registers and pairs a phone; the code is single use", async () => {
  const { call, desktop, phone, code } = await setup();
  assert.equal(phone.vaultId, desktop.vaultId);
  assert.notEqual(phone.deviceId, desktop.deviceId);

  const again = await call("POST", "/api/pairings/complete", { body: { vaultId: desktop.vaultId, code } });
  assert.equal(again.status, 403);

  const devices = (await call("GET", "/api/devices", { token: phone.token })).body.devices;
  assert.deepEqual(devices.map((d) => [d.name, d.platform, d.self]).sort(),
    [["Alfred's PC", "windows", false], ["Pixel", "android", true]]);
});

test("expired and wrong codes are rejected", async () => {
  const { call, desktop } = await setup();
  assert.equal((await call("POST", "/api/pairings/complete", { body: { vaultId: desktop.vaultId, code: "nope" } })).status, 403);

  const { code } = (await call("POST", "/api/pairings", { token: desktop.token })).body;
  const realNow = Date.now;
  Date.now = () => realNow() + 11 * 60 * 1000;
  try {
    assert.equal((await call("POST", "/api/pairings/complete", { body: { vaultId: desktop.vaultId, code } })).status, 403);
  } finally {
    Date.now = realNow;
  }
});

test("bad tokens are refused", async () => {
  const { call, desktop } = await setup();
  assert.equal((await call("POST", "/api/sync", { body: {} })).status, 401);
  const forged = desktop.token.replace(/\.[^.]+$/, ".AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
  assert.equal((await call("POST", "/api/sync", { token: forged, body: {} })).status, 401);
});

test("changes flow both ways with a cursor", async () => {
  const { call, desktop, phone } = await setup();

  const push = await call("POST", "/api/sync", { token: desktop.token, body: { since: 0, changes: [change("r1", 1000), change("t1", 1000, { kind: "todo" })] } });
  assert.equal(push.status, 200);
  assert.equal(push.body.accepted, 2);
  const desktopCursor = push.body.cursor;

  const pull = await call("POST", "/api/sync", { token: phone.token, body: { since: 0 } });
  assert.deepEqual(pull.body.changes.map((c) => [c.id, c.kind, c.from === desktop.deviceId]), [["r1", "reminder", true], ["t1", "todo", true]]);
  assert.equal(pull.body.more, false);

  await call("POST", "/api/sync", { token: phone.token, body: { since: pull.body.cursor, changes: [change("r2", 2000)] } });
  const back = await call("POST", "/api/sync", { token: desktop.token, body: { since: desktopCursor } });
  assert.deepEqual(back.body.changes.map((c) => c.id), ["r2"]);

  const nothing = await call("POST", "/api/sync", { token: desktop.token, body: { since: back.body.cursor } });
  assert.deepEqual(nothing.body.changes, []);
  assert.equal(nothing.body.cursor, back.body.cursor);
});

test("last writer wins, older edits are ignored, deletes travel", async () => {
  const { call, desktop, phone } = await setup();
  await call("POST", "/api/sync", { token: desktop.token, body: { changes: [change("r1", 5000)] } });

  const stale = await call("POST", "/api/sync", { token: phone.token, body: { changes: [change("r1", 4000)] } });
  assert.equal(stale.body.accepted, 0);

  const newer = await call("POST", "/api/sync", { token: phone.token, body: { changes: [{ id: "r1", kind: "reminder", updatedAt: 6000, deleted: true }] } });
  assert.equal(newer.body.accepted, 1);

  const all = await call("POST", "/api/sync", { token: desktop.token, body: { since: 0 } });
  assert.deepEqual(all.body.changes.map((c) => [c.id, c.deleted, c.data]), [["r1", true, null]]);
});

test("an unlinked device loses access", async () => {
  const { call, desktop, phone } = await setup();
  assert.equal((await call("DELETE", `/api/devices/${phone.deviceId}`, { token: desktop.token })).status, 200);
  assert.equal((await call("POST", "/api/sync", { token: phone.token, body: {} })).status, 401);
  const devices = (await call("GET", "/api/devices", { token: desktop.token })).body.devices;
  assert.equal(devices.length, 1);
});

test("deleting the vault removes access for every device", async () => {
  const { call, desktop, phone } = await setup();
  await call("POST", "/api/sync", { token: desktop.token, body: { changes: [change("r1", 1)] } });
  assert.equal((await call("DELETE", "/api/vaults/current", { token: phone.token })).status, 200);
  assert.equal((await call("POST", "/api/sync", { token: desktop.token, body: {} })).status, 401);
  assert.equal((await call("POST", "/api/sync", { token: phone.token, body: {} })).status, 401);
});

test("input is validated", async () => {
  const { call, desktop } = await setup();
  const bad = async (changes) => (await call("POST", "/api/sync", { token: desktop.token, body: { changes } })).status;
  assert.equal(await bad([{ id: "x/y", kind: "reminder", updatedAt: 1, data: "a" }]), 400);
  assert.equal(await bad([{ id: "x", kind: "note", updatedAt: 1, data: "a" }]), 400);
  assert.equal(await bad([{ id: "x", kind: "todo", updatedAt: "now", data: "a" }]), 400);
  assert.equal(await bad([{ id: "x", kind: "todo", updatedAt: 1 }]), 400);
  assert.equal(await bad([{ id: "x", kind: "todo", updatedAt: 1, data: "a".repeat(200_001) }]), 413);
  assert.equal(await bad(Array.from({ length: 201 }, (_, i) => change(`r${i}`, 1))), 413);
});

test("pull pages through large backlogs", async () => {
  const { call, desktop, phone } = await setup();
  for (let batch = 0; batch < 3; batch++)
    await call("POST", "/api/sync", { token: desktop.token, body: { changes: Array.from({ length: 200 }, (_, i) => change(`r${batch}-${i}`, 1)) } });

  const first = await call("POST", "/api/sync", { token: phone.token, body: { since: 0 } });
  assert.equal(first.body.changes.length, 500);
  assert.equal(first.body.more, true);
  const second = await call("POST", "/api/sync", { token: phone.token, body: { since: first.body.cursor } });
  assert.equal(second.body.changes.length, 100);
  assert.equal(second.body.more, false);
});
