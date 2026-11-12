// Local stand-in for the Netlify site: same API, in-memory storage, nothing touches Firestore.
// Used to test the desktop and Android apps (REMINDERX_SYNC_URL=http://127.0.0.1:8899).
//   node dev-server.mjs [port]
import { createServer } from "node:http";
import { handle } from "./lib/api.mjs";
import { createMemoryStore } from "./lib/store-memory.mjs";

const port = Number(process.argv[2] ?? 8899);
const store = createMemoryStore();

createServer(async (req, res) => {
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  const body = chunks.length > 0 ? Buffer.concat(chunks) : undefined;

  const response = await handle(new Request(`http://127.0.0.1:${port}${req.url}`, {
    method: req.method,
    headers: req.headers,
    body: req.method === "GET" || req.method === "HEAD" ? undefined : body,
  }), store);

  res.writeHead(response.status, Object.fromEntries(response.headers));
  res.end(Buffer.from(await response.arrayBuffer()));
  console.log(`${req.method} ${req.url} -> ${response.status}`);
}).listen(port, "127.0.0.1", () => console.log(`ReminderX sync dev server on http://127.0.0.1:${port}`));
