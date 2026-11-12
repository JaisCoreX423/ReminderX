import { handle } from "../../lib/api.mjs";
import { createFirestoreStore } from "../../lib/store-firestore.mjs";
import { createMemoryStore } from "../../lib/store-memory.mjs";

// FIREBASE_SERVICE_ACCOUNT holds the whole service-account JSON (set in Netlify > Environment variables).
// Without it the API refuses to work instead of silently keeping data in memory; local runs can opt
// into the memory store with REMINDERX_MEMORY_STORE=1.
let store;
const getStore = () => {
  if (store) return store;
  if (process.env.FIREBASE_SERVICE_ACCOUNT) store = createFirestoreStore(process.env.FIREBASE_SERVICE_ACCOUNT);
  else if (process.env.REMINDERX_MEMORY_STORE === "1") store = createMemoryStore();
  return store;
};

export default (req) => {
  const s = getStore();
  if (!s)
    return Response.json(
      { error: "Storage not configured: set FIREBASE_SERVICE_ACCOUNT in Netlify" },
      { status: 503 });
  return handle(req, s);
};

export const config = { path: "/api/*" };
