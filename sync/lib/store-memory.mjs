// In-memory store with the same contract as store-firestore.mjs. Used by the tests and by
// `netlify dev` when FIREBASE_SERVICE_ACCOUNT isn't set; data is lost when the process exits.

import { isNewer } from "./api.mjs";

export function createMemoryStore() {
  const vaults = new Map();

  const getVault = (id) => {
    const v = vaults.get(id);
    if (!v) throw Object.assign(new Error(`No vault ${id}`), { status: 404 });
    return v;
  };

  return {
    name: "memory",

    async createVault(vaultId, data) {
      vaults.set(vaultId, { ...data, devices: new Map(), pairings: new Map(), items: new Map() });
    },

    async deleteVault(vaultId) {
      vaults.delete(vaultId);
    },

    async getDevice(vaultId, deviceId) {
      const d = vaults.get(vaultId)?.devices.get(deviceId);
      return d ? { id: deviceId, ...d } : null;
    },

    async putDevice(vaultId, deviceId, data) {
      const { id, ...fields } = data;
      getVault(vaultId).devices.set(deviceId, fields);
    },

    async touchDevice(vaultId, deviceId, at) {
      const d = getVault(vaultId).devices.get(deviceId);
      if (d) d.lastSeenAt = at;
    },

    async listDevices(vaultId) {
      return [...getVault(vaultId).devices].map(([id, d]) => ({ id, ...d }));
    },

    async putPairing(vaultId, codeHash, data) {
      getVault(vaultId).pairings.set(codeHash, { ...data });
    },

    async takePairing(vaultId, codeHash, now) {
      const p = vaults.get(vaultId)?.pairings.get(codeHash);
      if (!p || p.used || p.expiresAt < now) return false;
      p.used = true;
      return true;
    },

    async applyChanges(vaultId, deviceId, changes) {
      const v = getVault(vaultId);
      let accepted = 0;
      for (const change of changes) {
        if (!isNewer(change, deviceId, v.items.get(change.id))) continue;
        v.seq += 1;
        accepted += 1;
        v.items.set(change.id, { ...change, id: change.id, seq: v.seq, from: deviceId });
      }
      return accepted;
    },

    async pull(vaultId, since, limit) {
      const all = [...getVault(vaultId).items.values()].filter((i) => i.seq > since).sort((a, b) => a.seq - b.seq);
      const items = all.slice(0, limit);
      return { items, cursor: items.length > 0 ? items[items.length - 1].seq : since, more: all.length > limit };
    },
  };
}
