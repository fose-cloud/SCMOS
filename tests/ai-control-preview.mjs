/**
 * Isolated browser QA: node tests/ai-control-preview.mjs
 * Only loopback fixture API; never starts .NET, SQL or an OpenAI client.
 * Uses a separate Next build folder and tsconfig, not the user's running dev server.
 */
import { createServer } from "node:http";
import { fileURLToPath } from "node:url";
import { status, today, brief, reply, audit, run, job } from "./fixtures/ai-control.mjs";

process.env.NODE_ENV = "development";
process.env.SCMOS_API_BASE_URL = "http://127.0.0.1:4108";
process.env.SCMOS_API_PROXY_KEY = "local-fixture-not-a-secret";
process.env.NEXT_TELEMETRY_DISABLED = "1";
const calls = [];
let mode = "ready";
const send = (res, body, code = 200) => {
  res.writeHead(code, { "content-type": "application/json", "cache-control": "no-store" });
  res.end(JSON.stringify(body));
};
const api = createServer(async (req, res) => {
  const url = new URL(req.url, "http://127.0.0.1:4108");
  if (url.pathname === "/__fixture") {
    if (req.method === "POST") {
      const requested = url.searchParams.get("mode");
      if (!["ready", "disabled", "audit-missing", "no-access", "empty", "slow", "error", "mock", "operator", "carrier"].includes(requested))
        return send(res, { error: "unknown fixture" }, 400);
      mode = requested; calls.length = 0;
    }
    return send(res, { fixture: "scmos-ai-local-qa", mode, calls });
  }
  calls.push({ method: req.method, path: url.pathname });
  if (req.method !== "GET" && !(url.pathname === "/api/ai/chat" && req.method === "POST"))
    return send(res, { error: "fixture refuses writes" }, 405);
  if (url.pathname === "/api/me") return send(res, {
    account: { role: mode === "carrier" ? "Subcontractor" : mode === "operator" ? "Operation User" : "Operation Supervisor", opId: "fixture-op", name: "FIXTURE ONLY", init: "QA", full: "LOCAL QA · NO PRODUCTION DATA" },
    can: mode === "no-access" || mode === "carrier" ? [] : ["ViewDashboard", "ViewTeam", "ViewAudit", ...(mode === "operator" ? [] : ["ApproveAi"])],
    known: true, authorised: true, actingFor: [],
  });
  if (url.pathname === "/api/ai/status") return send(res, {
    ...status, enabled: mode !== "disabled", auditReady: mode !== "audit-missing",
    agents: mode === "no-access" || mode === "carrier" ? [] : status.agents,
    mock: mode === "mock", liveToolsReady: mode !== "mock" && mode !== "audit-missing",
  });
  if (mode === "no-access" && ["/api/dashboard/today", "/api/dashboard/briefing", "/api/ai/audit"].includes(url.pathname))
    return send(res, { code: "forbidden" }, 403);
  if (url.pathname === "/api/dashboard/today") return mode === "error" ? send(res, {}, 503) : send(res, today);
  if (url.pathname === "/api/dashboard/briefing") return send(res, mode === "empty" ? { ...brief, findings: [], quiet: "FIXTURE · ไม่มีงาน active" } : brief);
  if (url.pathname === "/api/ai/audit") {
    if (mode === "audit-missing") return send(res, { code: "audit_not_ready" }, 503);
    return send(res, mode === "empty" || url.searchParams.has("beforeId") ? { runs: [], nextBeforeId: null } : audit);
  }
  if (url.pathname.startsWith("/api/ai/audit/")) return send(res, audit.runs.find(item => item.runId === url.pathname.split("/").at(-1)) || run);
  if (url.pathname === "/api/jobs") return send(res, { jobs: [job], updatedAt: "2026-09-07T03:00:00Z" });
  if (url.pathname === "/api/jobs/page") return send(res, {
    jobs: [], total: 0, pageCount: 1, page: 1, counts: {}, dates: [], customers: [], truckers: [],
    updatedAt: "2026-09-07T03:00:00Z",
  });
  if (url.pathname === "/api/ai/chat") {
    // Consume bounded synthetic requests; never record content or forward anywhere.
    for await (const chunk of req) { if (chunk.length > 16384) return send(res, {}, 413); }
    if (mode === "slow") await new Promise(resolve => setTimeout(resolve, 4000));
    if (mode === "error") return send(res, { code: "source_unavailable", error: "fixture-private-error-must-not-be-visible" }, 503);
    return send(res, mode === "mock" ? { ...reply, mock: true, evidence: null, summary: "Development Mock · ไม่ได้อ่านงานจริง" } : reply);
  }
  // Other app-chrome calls receive no records, and cannot escape this fixture server.
  if (url.pathname === "/api/alerts") return send(res, { alerts: [], total: 0 });
  return send(res, { error: "endpoint not supplied by local fixture" }, 404);
});
await new Promise(resolve => api.listen(4108, "127.0.0.1", resolve));
const { default: next } = await import("next");
const app = next({
  dev: true, dir: fileURLToPath(new URL("./ai-preview-app", import.meta.url)), hostname: "127.0.0.1", port: 4107,
});
await app.prepare();
const web = createServer(app.getRequestHandler());
await new Promise(resolve => web.listen(4107, "127.0.0.1", resolve));
console.log("LOCAL FIXTURE ONLY: http://127.0.0.1:4107/ai-control-tower (no Production / SQL / OpenAI)");
async function close() { web.close(); api.close(); await app.close(); process.exit(0); }
process.on("SIGINT", close); process.on("SIGTERM", close);
