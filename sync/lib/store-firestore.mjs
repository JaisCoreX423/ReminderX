// Firestore layout (all under one vault):
//   vaults/{vaultId}                      { createdAt, seq }   seq = last change number handed out
//   vaults/{vaultId}/devices/{deviceId}   { name, platform, tokenHash, createdAt, lastSeenAt, revoked }
//   vaults/{vaultId}/pairings/{codeHash}  { expiresAt, used }
//   vaults/{vaultId}/items/{itemId}       { kind, updatedAt, deleted, data, seq, from }
// Only this service touches it (firebase-admin bypasses security rules); firestore.rules deny everyone else.

import { cert, getApps, initializeApp } from "firebase-admin/app";
import { getFirestore } from "firebase-admin/firestore";
import { isNewer } from "./api.mjs";

export function createFirestoreStore(serviceAccountJson) {
  const app = getApps()[0] ?? initializeApp({ credential: cert(JSON.parse(serviceAccountJson)) });
  const db = getFirestore(app);
  const vault = (id) => db.collection("vaults").doc(id);

  return {
    name: "firestore",

    async createVault(vaultId, data) {
      await vault(vaultId).create(data);
    },

    async deleteVault(vaultId) {
      await db.recursiveDelete(vault(vaultId));
    },

    async getDevice(vaultId, deviceId) {
      const snap = await vault(vaultId).collection("devices").doc(deviceId).get();
      return snap.exists ? { id: snap.id, ...snap.data() } : null;
    },

    async putDevice(vaultId, deviceId, data) {
      const { id, ...fields } = data;
      await vault(vaultId).collection("devices").doc(deviceId).set(fields);
    },

    async touchDevice(vaultId, deviceId, at) {
      await vault(vaultId).collection("devices").doc(deviceId).update({ lastSeenAt: at });
    },

    async listDevices(vaultId) {
      const snap = await vault(vaultId).collection("devices").get();
      return snap.docs.map((d) => ({ id: d.id, ...d.data() }));
    },

    async putPairing(vaultId, codeHash, data) {
      await vault(vaultId).collection("pairings").doc(codeHash).set(data);
    },

    /** Single use: marks the code used inside a transaction so two phones can't redeem it. */
    async takePairing(vaultId, codeHash, now) {
      const ref = vault(vaultId).collection("pairings").doc(codeHash);
      return db.runTransaction(async (tx) => {
        const snap = await tx.get(ref);
        if (!snap.exists) return false;
        const { used, expiresAt } = snap.data();
        if (used || expiresAt < now) return false;
        tx.update(ref, { used: true, usedAt: now });
        return true;
      });
    },

    async applyChanges(vaultId, deviceId, changes) {
      const vaultRef = vault(vaultId);
      const items = vaultRef.collection("items");
      return db.runTransaction(async (tx) => {
        // Firestore transactions need every read before the first write.
        const vaultSnap = await tx.get(vaultRef);
        const existing = await tx.getAll(...changes.map((c) => items.doc(c.id)));

        let seq = vaultSnap.data()?.seq ?? 0;
        let accepted = 0;
        changes.forEach((change, i) => {
          const current = existing[i].exists ? existing[i].data() : null;
          if (!isNewer(change, deviceId, current)) return;
          seq += 1;
          accepted += 1;
          tx.set(items.doc(change.id), { ...change, seq, from: deviceId });
        });
        if (accepted > 0) tx.update(vaultRef, { seq });
        return accepted;
      });
    },

    async pull(vaultId, since, limit) {
      const vaultRef = vault(vaultId);
      const snap = await vaultRef.collection("items")
        .where("seq", ">", since)
        .orderBy("seq")
        .limit(limit + 1)
        .get();
      const docs = snap.docs.slice(0, limit).map((d) => ({ id: d.id, ...d.data() }));
      const more = snap.docs.length > limit;
      const cursor = docs.length > 0 ? docs[docs.length - 1].seq : since;
      return { items: docs, cursor, more };
    },
  };
}
