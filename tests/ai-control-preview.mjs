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
let controlEnabled = false;
let controlRevision = 0;
// AI findings for the multi-select answer (6 Oct 2026): plain findings, one a person may not answer, two message drafts.
const finding = (id, agentId, decisionType, riskLevel, resultStatus, summary, canAnswer = true, extra = {}) => ({
  id, agentId, decisionType, entityType: "job", entityId: "FIXTURE-" + id, summary, resultStatus, status: "OPEN", riskLevel,
  shadow: true, autonomy: 2, findings: { facts: [{ text: "FIXTURE ONLY — ข้อมูลทดสอบ", source: "job:FIXTURE-" + id }],
    ruleResults: [], observations: [], inferences: [], recommendations: [], blockingIssues: [], ...extra },
  ruleReferences: [], createdAt: "2026-10-06T01:00:00Z", humanChoice: "", overrideReason: "", decidedBy: "", canAnswer,
  runId: "", decidedAt: null, evidenceReferences: [] });
const freshFindings = () => [
  finding(101, "otd-agent", "otd_risk", "WATCH", "RISK", "FIXTURE งาน A · ใกล้เวลาแผน ยังไม่มีรถ"),
  finding(102, "otd-agent", "otd_risk", "WATCH", "RISK", "FIXTURE งาน B · ใกล้เวลาแผน ยังไม่มีรถ"),
  finding(103, "validation-agent", "validation_issue", "LOW", "INSUFFICIENT_INFORMATION", "FIXTURE งาน C · ข้อมูลยังไม่ครบ"),
  finding(104, "validation-agent", "validation_issue", "LOW", "INSUFFICIENT_INFORMATION", "FIXTURE งาน D · ข้อมูลยังไม่ครบ"),
  finding(105, "otd-agent", "otd_risk", "WATCH", "RISK", "FIXTURE งาน E · ของคนอื่น ตอบไม่ได้", false),
  finding(106, "communication-agent", "communication_draft", "LOW", "DRAFT", "FIXTURE งาน F · ร่างข้อความถึงผู้ขนส่ง", true,
    { recommendations: [{ text: "FIXTURE ร่างข้อความ", source: "template:CARRIER_CONFIRMATION_REMINDER" }] }),
  finding(107, "communication-agent", "communication_draft", "LOW", "DRAFT", "FIXTURE งาน G · ร่างข้อความถึงผู้ขนส่ง", true,
    { recommendations: [{ text: "FIXTURE ร่างข้อความ", source: "template:CARRIER_CONFIRMATION_REMINDER" }] }),
];
let openFindings = freshFindings();
// Administration in tabs (14 Oct 2026): a directory, roles, a calendar and an SLA rule — fixture rows only.
const staffRole = (name, scopeTh, grants) => ({ name, scopeEn: name, scopeTh, grants });
const staffPerson = (id, name, role, jobs, active = true) => ({ id, email: active ? `fixture.${id.toLowerCase()}@example.test` : "",
  name, account: "", role, supplierId: null, supplierName: "", active, note: "", jobs, can: role === "Administrator" ? ["AdministerData"] : [],
  updatedBy: "fixture", updatedAt: "2026-10-14T01:00:00Z" });
const staff = {
  people: [staffPerson("AD-01", "FIXTURE Admin", "Administrator", 0), staffPerson("OP-01", "FIXTURE Operator", "Operation User", 12),
    staffPerson("OP-09", "FIXTURE Closed", "Operation User", 0, false)],
  roles: [staffRole("Administrator", "ดูแลระบบทั้งหมด", ["ViewDashboard", "AdministerData"]),
    staffRole("Operation User", "แก้งานของตัวเอง", ["ViewDashboard", "EditOwnJobs"])],
  carriers: [], canManage: true, you: "AD-01", signIn: { ready: true, why: "" },
};
const calendarDays = [{ date: "2026-10-23", kind: "public-holiday", name: "FIXTURE วันหยุด", updatedBy: "fixture", updatedAt: "2026-10-14T01:00:00Z" }];
const slaRules = [{ id: 1, code: "STANDARD", startDay: "day1", targetWorkingDays: 4, effectiveFrom: "2026-09-30", effectiveTo: null,
  active: true, updatedBy: "fixture", updatedAt: "2026-10-14T01:00:00Z" }];
// KPI in tabs (14 Oct 2026): the operational report and the measures engine, fixture figures only.
const kpiReport = { total: 120, byCategory: [{ label: "IMPORT", value: 60 }, { label: "EXPORT", value: 40 }, { label: "DELIVERY", value: 20 }],
  byStatus: [{ label: "COMPLETED", value: 80 }, { label: "WAITING_SUPPLIER", value: 40 }], onTime: { base: 90, met: 80, percent: 89 },
  actionRequired: 5, formatErrors: 2, gateInRisk: 1, undated: 0,
  team: [{ owner: "FIXTURE Owner", ownerId: "OP-01", total: 60, open: 10, action: 2, late: 1 }],
  carriers: [{ carrier: "FIXTURE Carrier 1", total: 70, measured: 60, onTime: 55, percent: 92 },
    { carrier: "FIXTURE Carrier 2", total: 50, measured: 30, onTime: 25, percent: 83 }, { carrier: "FIXTURE Unmeasured", total: 5, measured: 0, onTime: 0, percent: 0 }],
  byDay: [{ label: "01/10/2026", value: 10 }], computedAt: "2026-10-14T01:00:00Z" };
const kpiMeasure = (id, thai, available, value, target) => ({ id, english: id, thai, kind: "Rate", available, value, base: available ? 90 : 0,
  unit: "%", note: "FIXTURE ONLY", breakdown: [], target, meetsTarget: available && target !== null ? value >= target : null,
  trend: [{ period: "2026-09", value: 85, base: 80 }, { period: "2026-10", value, base: 90 }] });
const kpiEngine = { jobs: 120, suppliers: [], unattributedIssues: 0, issuesInPeriod: 1,
  measures: [kpiMeasure("OnTimeDelivery", "ส่งมอบตรงเวลา", true, 89, 95), kpiMeasure("Billing", "การวางบิล", false, null, null)],
  scorecard: [{ carrier: "FIXTURE Carrier 1", shipments: 70, weighted: 97.5, weightAvailable: 100, ungradedAccidents: 0,
    lines: [{ id: "on-time", english: "On time", thai: "ตรงเวลา", weight: 10, percent: 96, count: 3, base: 70, target: 95, note: "" }],
    tally: { transportAccidentMajor: 0, transportAccidentMinor: 1, loadingAccident: 0, complaints: 0, breakdownNoComplaint: 0 } }] };
const send = (res, body, code = 200) => {
  res.writeHead(code, { "content-type": "application/json", "cache-control": "no-store" });
  res.end(JSON.stringify(body));
};
const api = createServer(async (req, res) => {
  const url = new URL(req.url, "http://127.0.0.1:4108");
  if (url.pathname === "/__fixture") {
    if (req.method === "POST") {
      const requested = url.searchParams.get("mode");
      if (!["ready", "disabled", "audit-missing", "no-access", "empty", "slow", "error", "mock", "operator", "carrier", "control", "control-unready", "control-conflict", "admin"].includes(requested))
        return send(res, { error: "unknown fixture" }, 400);
      mode = requested; calls.length = 0;
      controlEnabled = false; controlRevision = 0;
      openFindings = freshFindings();
    }
    return send(res, { fixture: "scmos-ai-local-qa", mode, calls });
  }
  calls.push({ method: req.method, path: url.pathname });
  if (url.pathname === "/api/ai/operations-control" && req.method === "POST" && mode.startsWith("control")) {
    let body = "";
    for await (const chunk of req) { body += chunk; if (body.length > 1024) return send(res, {}, 413); }
    const value = JSON.parse(body);
    if (req.headers["x-scmos-ai-control"] !== "1") return send(res, {}, 400);
    if (mode === "control-conflict" || value.revision !== controlRevision) return send(res, { code: "control_conflict" }, 409);
    if (mode === "control-unready") return send(res, { code: "control_not_ready" }, 503);
    controlEnabled = value.enabled; controlRevision++;
    return send(res, { saved: true });
  }
  const answered = /^\/api\/ai\/decisions\/(\d+)\/outcome$/.exec(url.pathname);
  if (answered && req.method === "POST") {
    if (req.headers["x-scmos-ai-control"] !== "1") return send(res, {}, 400);
    let body = "";
    for await (const chunk of req) { body += chunk; if (body.length > 1024) return send(res, {}, 413); }
    calls[calls.length - 1].body = JSON.parse(body);
    const id = Number(answered[1]);
    const one = openFindings.find(item => item.id === id);
    if (!one) return send(res, { code: "already_answered" }, 409);
    if (!one.canAnswer) return send(res, { code: "forbidden" }, 403);
    openFindings = openFindings.filter(item => item.id !== id);
    return send(res, { saved: true });
  }
  if (url.pathname === "/api/ai/decisions") return send(res, { items: openFindings, total: openFindings.length });
  if (url.pathname === "/api/ai/tasks") {
    const mine = openFindings.filter(item => item.canAnswer).length;
    return send(res, { jobsMonitored: 12, aiHandling: openFindings.length, needsMyDecision: mine, highRisk: 0, blocked: 0,
      informationRequired: openFindings.filter(item => item.resultStatus === "INSUFFICIENT_INFORMATION").length,
      carrierEscalation: 0, pendingApproval: 0, agentFailure: 0 });
  }
  if (req.method !== "GET" && !(url.pathname === "/api/ai/chat" && req.method === "POST"))
    return send(res, { error: "fixture refuses writes" }, 405);
  if (url.pathname === "/api/me") return send(res, {
    account: { role: mode === "carrier" ? "Subcontractor" : mode === "operator" ? "Operation User" : mode === "admin" ? "Administrator" : "Operation Supervisor", opId: "fixture-op", name: "FIXTURE ONLY", init: "QA", full: "LOCAL QA · NO PRODUCTION DATA" },
    can: mode === "no-access" || mode === "carrier" ? [] : ["ViewDashboard", "ViewTeam", "ViewAudit", ...(mode === "operator" ? [] : ["ApproveAi"]),
      ...(mode === "admin" ? ["ViewDirectory", "AdministerData"] : [])],
    known: true, authorised: true, actingFor: [],
  });
  if (url.pathname === "/api/ai/status") return send(res, {
    ...status, enabled: mode !== "disabled", auditReady: mode !== "audit-missing",
    agents: mode === "no-access" || mode === "carrier" ? [] : status.agents,
    mock: mode === "mock", liveToolsReady: mode !== "mock" && mode !== "audit-missing",
    ...(mode.startsWith("control") ? {
      enabled: controlEnabled, chatEnabled: controlEnabled,
      agents: status.agents.map(a => a.id === "operations-agent" ? { ...a, enabled: controlEnabled } : a),
      operationsControl: { available: true, enabled: controlEnabled, revision: controlRevision, canManage: true,
        canEnable: mode !== "control-unready", emergencyDisabled: false, blockReason: mode === "control-unready" ? "control_not_ready" : "" },
    } : {}),
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
  if (url.pathname === "/api/kpi") return send(res, kpiReport);
  if (url.pathname === "/api/kpi/measures") return send(res, kpiEngine);
  if (url.pathname === "/api/staff") return mode === "admin" ? send(res, staff) : send(res, { error: "fixture: admin mode only" }, 403);
  if (url.pathname === "/api/carrier-billing/foundation/calendar") return send(res, mode === "admin" ? calendarDays : []);
  if (url.pathname === "/api/carrier-billing/foundation/sla") return send(res, mode === "admin" ? slaRules : []);
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
