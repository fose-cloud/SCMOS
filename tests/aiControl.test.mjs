import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  askBody, availability, communicationAvailability, ControlError, controlRequest, dataAvailability, documentAvailability, engineeringAvailability, errorText, issueTarget, managementAvailability, number, sreAvailability,
  parseStatus, parseToday, parseBrief, parseReply, parseAuditPage, parseAuditRun, stamp,
} from "../app/scmos/aiControl.ts";
import { status, today, brief, reply, kpiReply, messagesReply, documentsReply, engineeringReply, sourceReply, platformReply, run, audit } from "./fixtures/ai-control.mjs";
import { allowedOperationsControlRequest } from "../app/scmos/aiControlRequest.ts";
const copy = value => structuredClone(value);
const rejects = (parser, value) => assert.throws(() => parser(value), /invalid_response/);
const response = (body, code = 200) => new Response(JSON.stringify(body), { status: code });

test("control CSRF guard handles reverse proxy Host and refuses cross-origin requests", () => {
  const headers = { host: "scmos.example.invalid", origin: "https://scmos.example.invalid",
    "x-forwarded-proto": "https", "x-scmos-ai-control": "1", "sec-fetch-site": "same-origin" };
  const allowed = patch => allowedOperationsControlRequest(new Request("http://localhost:3000/api/ai/operations-control", { headers: { ...headers, ...patch } }));
  assert.equal(allowed({}), true);
  for (const patch of [{ origin: "https://evil.example" }, { origin: "null" }, { "sec-fetch-site": "cross-site" },
    { "sec-fetch-site": "same-site" }, { "x-scmos-ai-control": "" }, { origin: "http://scmos.example.invalid" }])
    assert.equal(allowed(patch), false);
});

test("Operations switch is strictly parsed and overrides stale ready flags", () => {
  const operationsControl = { available: true, enabled: true, revision: 1, canManage: true,
    canEnable: true, emergencyDisabled: false, blockReason: "" };
  assert.equal(parseStatus({ ...status, operationsControl }).operationsControl.revision, 1);
  for (const patch of [{ available: false }, { enabled: false }, { emergencyDisabled: true }])
    assert.equal(availability({ ...status, operationsControl: { ...operationsControl, ...patch } }).ready, false);
  for (const patch of [{ revision: -1 }, { revision: "1" }, { canManage: "true" }, { enabled: 1 }, { blockReason: null }])
    rejects(parseStatus, { ...status, operationsControl: { ...operationsControl, ...patch } });
});

test("Control Tower accepts current dashboard/AI/audit contracts", () => {
  for (const [parser, fixture] of [[parseStatus, status], [parseToday, today], [parseBrief, brief], [parseReply, reply], [parseAuditRun, run], [parseAuditPage, audit]])
    assert.equal(parser(fixture), fixture);
});
test("Administrator status accepts all ten agents without hiding AI controls", () => {
  const agentIds = ["operations-agent", "vendor-agent", "rate-agent", "data-agent", "incident-agent",
    "document-agent", "compliance-agent", "management-agent", "communication-agent", "engineering-agent"];
  const full = { ...status, agents: agentIds.map(id => ({ id, name: id, enabled: true, connected: true })),
    operationsControl: { available: true, enabled: true, revision: 1, canManage: true,
      canEnable: true, emergencyDisabled: false, blockReason: "" } };
  assert.equal(parseStatus(full), full);
  assert.equal(engineeringAvailability(parseStatus(full)).ready, true);
  rejects(parseStatus, { ...full, agents: Array.from({ length: 17 }, (_, n) => ({ ...full.agents[0], id: `agent-${n}` })) });
});
test("missing metrics are N/A while real zero stays zero", () => {
  assert.equal(number(null), "N/A"); assert.equal(number(undefined), "N/A"); assert.equal(number(0), "0");
  assert.equal(parseToday(today).attention[0].value, null);
  assert.equal(parseToday(today).volume[2].value, 0);
  const bad = copy(today); bad.volume[0].value = -1; rejects(parseToday, bad);
});
test("parsers reject malformed and HTML responses instead of invented zero data", () => {
  for (const parse of [parseStatus, parseToday, parseBrief, parseReply, parseAuditRun, parseAuditPage])
    for (const value of [null, {}, [], "<html>error</html>"]) rejects(parse, value);
});
test("evidence validates totals, cap, source and truncation", () => {
  for (const patch of [
    { total: 0 }, { returned: 2 }, { truncated: false }, { invalidRows: -1 },
    { rows: Array(51).fill(reply.evidence.rows[0]), total: 51, returned: 51, truncated: false },
    { rows: [{ ...reply.evidence.rows[0], source: "invented" }] },
  ]) rejects(parseReply, { ...reply, evidence: { ...reply.evidence, ...patch } });
  assert.equal(parseReply({ ...reply, evidence: { ...reply.evidence, total: 0, returned: 0, truncated: false, rows: [] } }).evidence.total, 0);
});
test("mock is explicit and cannot carry real-looking operational evidence", () => {
  assert.equal(parseReply({ ...reply, mock: true, evidence: null }).mock, true);
  rejects(parseReply, { ...reply, mock: true });
  assert.equal(availability({ ...status, mock: true, auditReady: false, liveToolsReady: false }).title, "Development Mock");
});
test("every live prerequisite is fail-closed and write flags grant no UI writes", () => {
  assert.equal(availability(null).ready, false);
  for (const field of ["enabled", "chatEnabled", "providerConfigured", "configurationValid", "auditReady", "liveToolsReady"])
    assert.equal(availability({ ...status, [field]: false }).ready, false, field);
  assert.equal(availability({ ...status, agents: [] }).ready, false);
  assert.equal(availability({ ...status, agents: [{ ...status.agents[0], enabled: false }] }).ready, false);
  assert.equal(availability({ ...status, agents: [{ ...status.agents[0], connected: false }] }).ready, false);
  assert.equal(availability({ ...status, writeToolsReady: true }).ready, true);
});
test("only the user's bounded message and fixed page/agent leave the browser", () => {
  assert.deepEqual(askBody("  งานวันนี้  "), { message: "งานวันนี้", agentId: "operations-agent", context: { page: "operations" } });
  for (const text of ["", "  ", "ก".repeat(4001)]) assert.throws(() => askBody(text), /invalid_request/);
  assert.equal(askBody("ก".repeat(4000)).message.length, 4000);
  assert.equal(askBody('{"role":"Admin","tool":"delete"}').message, '{"role":"Admin","tool":"delete"}');
});
test("navigation uses existing screen allowlist, never arbitrary URLs or instructions", () => {
  assert.equal(issueTarget("monitoring"), "monitoring");
  for (const value of ["https://example.com", "javascript:alert(1)", "admin", "__proto__", "constructor"]) assert.equal(issueTarget(value), null);
  rejects(parseBrief, { ...brief, findings: [{ ...brief.findings[0], urgency: "CRITICAL INVENTED" }] });
});
test("Audit keeps incomplete/running events and cursor; rejects malformed scopes", () => {
  assert.equal(parseAuditPage(audit).runs[1].status, "incomplete");
  for (const patch of [{ runId: "../../admin" }, { scope: null }, { events: [] }, { sourceKeys: [9] }, { events: Array(19).fill(run.events[0]) },
    { correlationId: 7 }, { correlationId: "x".repeat(65) }, { steps: 9 }, { steps: -1 }])
    rejects(parseAuditRun, { ...run, ...patch });
  // A run from before 1D carries neither a correlation id nor steps, and a two-step run carries six events.
  const older = copy(run); delete older.correlationId; delete older.steps; older.events.forEach(e => { delete e.step; delete e.tool; });
  assert.equal(parseAuditRun(older), older);
  const twoSteps = { ...run, steps: 2, events: [run.events[0], ...run.events.slice(1, 3), ...run.events.slice(1, 3).map(e => ({ ...e, step: 2 })), run.events[3]] };
  assert.equal(parseAuditRun(twoSteps).events.length, 6);
  assert.equal(parseReply({ ...reply, correlationId: "req-1", contextUsed: true }).contextUsed, true);
  rejects(parseReply, { ...reply, contextUsed: "yes" });
});
test("the Data Agent's figure is parsed strictly, and never accepted as Operations evidence", () => {
  assert.equal(parseReply(kpiReply), kpiReply);
  assert.equal(parseReply({ ...reply, kpi: null }).kpi, null);
  const kpi = kpiReply.kpi;
  for (const patch of [{ kpi: null }, { kpi: { ...kpi, onTime: 7 } }, { kpi: { ...kpi, measured: 11 } }, { kpi: { ...kpi, onTimePercent: 101 } },
    { kpi: { ...kpi, returned: 1 } }, { kpi: { ...kpi, view: "today" } }, { kpi: { ...kpi, source: "model" } }, { kpi: { ...kpi, rule: null } },
    { kpi: { ...kpi, carriers: Array(51).fill(kpi.carriers[0]), returned: 51 } }, { evidence: reply.evidence }])
    rejects(parseReply, { ...kpiReply, ...patch });
  assert.equal(askBody("KPI", "data-agent").agentId, "data-agent"); assert.equal(askBody("KPI", "data-agent").context.page, "kpi");
  assert.equal(askBody("x").agentId, "operations-agent");
  const withData = { ...status, agents: [...status.agents, { id: "data-agent", name: "Data Agent", enabled: true, connected: true }] };
  assert.equal(dataAvailability(status).ready, false);
  assert.equal(dataAvailability(withData).ready, true);
  assert.equal(dataAvailability({ ...withData, agents: withData.agents.map(a => a.id === "data-agent" ? { ...a, enabled: false } : a) }).ready, false);
  assert.equal(dataAvailability({ ...withData, auditReady: false }).ready, false);
});
test("the Communication Agent's messages are parsed strictly; nothing is ever mistaken for Operations evidence", () => {
  assert.equal(parseReply(messagesReply), messagesReply);
  const m = messagesReply.messages;
  for (const patch of [{ messages: null }, { messages: { ...m, view: "risk_today" } }, { messages: { ...m, returned: 1 } },
    { messages: { ...m, rows: [{ ...m.rows[0], channel: "sms" }, m.rows[1]] } }, { messages: { ...m, rows: [{ ...m.rows[0], state: "sent" }, m.rows[1]] } },
    { messages: { ...m, rows: [{ ...m.rows[0], source: "model" }, m.rows[1]] } }, { messages: { ...m, jobs: Array(6).fill(m.jobs[0]) } }, { evidence: reply.evidence }])
    rejects(parseReply, { ...messagesReply, ...patch });
  assert.equal(parseReply({ ...reply, messages: null }).messages, null);
  assert.equal(askBody("x", "communication-agent").context.page, "line");
  const withAgent = { ...status, agents: [...status.agents, { id: "communication-agent", name: "Communication Agent", enabled: true, connected: true }] };
  assert.equal(communicationAvailability(status).ready, false);
  assert.equal(communicationAvailability(withAgent).ready, true);
  assert.equal(communicationAvailability({ ...withAgent, mock: true }).ready, false);
  rejects(parseAuditPage, { ...audit, nextBeforeId: -5 });
});
test("the Document & Invoice Agent's paperwork is parsed strictly; nothing is ever mistaken for Operations evidence", () => {
  assert.equal(parseReply(documentsReply), documentsReply);
  const d = documentsReply.documents;
  for (const patch of [{ documents: null }, { documents: { ...d, view: "waiting" } }, { documents: { ...d, returned: 1 } },
    { documents: { ...d, rows: [{ ...d.rows[0], kind: "invoice" }, d.rows[1]] } }, { documents: { ...d, rows: [{ ...d.rows[0], state: "approved" }, d.rows[1]] } },
    { documents: { ...d, rows: [{ ...d.rows[0], source: "model" }, d.rows[1]] } }, { documents: { ...d, rows: [{ ...d.rows[0], daysLeft: "soon" }, d.rows[1]] } },
    { documents: { ...d, jobs: [{ ...d.jobs[0], missingFolders: "Images" }] } }, { documents: { ...d, rule: 4 } }, { evidence: reply.evidence }])
    rejects(parseReply, { ...documentsReply, ...patch });
  assert.equal(parseReply({ ...reply, documents: null }).documents, null);
  assert.equal(askBody("x", "document-agent").context.page, "documents");
  const withAgent = { ...status, agents: [...status.agents, { id: "document-agent", name: "Document & Invoice Agent", enabled: true, connected: true }] };
  assert.equal(documentAvailability(status).ready, false);
  assert.equal(documentAvailability(withAgent).ready, true);
  assert.equal(documentAvailability({ ...withAgent, mock: true }).ready, false);
  assert.equal(documentAvailability({ ...withAgent, agents: withAgent.agents.map(a => a.id === "document-agent" ? { ...a, enabled: false } : a) }).ready, false);
});
test("Phase 6 GitHub metadata is parsed strictly and never treated as Operations evidence", () => {
  assert.deepEqual(parseReply(engineeringReply), engineeringReply);
  const e = engineeringReply.engineering;
  const first = e.rows[0];
  for (const patch of [
    { engineering: null }, { evidence: reply.evidence },
    { engineering: { ...e, repository: "other/repo" } },
    { engineering: { ...e, total: 500 } },
    { engineering: { ...e, returned: 2 } },
    { engineering: { ...e, rows: [{ ...first, url: "javascript:alert(1)" }] } },
    { engineering: { ...e, rows: [{ ...first, url: "https://github.com/fose-cloud/SCMOS/issues/12/evil" }] } },
    { engineering: { ...e, rows: [{ ...first, body: "malicious issue body" }] } },
    { engineering: { ...e, rows: [{ ...first, title: "bad\nheading" }] } },
    { engineering: { ...e, rows: Array(21).fill(first), total: 21, returned: 21 } },
  ]) rejects(parseReply, { ...engineeringReply, ...patch });
  assert.equal(askBody("open issues", "engineering-agent").context.page, "engineering");
  const withAgent = { ...status, agents: [...status.agents, { id: "engineering-agent", name: "Engineering Agent", enabled: true, connected: true }] };
  assert.equal(engineeringAvailability(status).ready, false);
  assert.equal(engineeringAvailability(withAgent).ready, true);
  assert.equal(engineeringAvailability({ ...withAgent, mock: true }).ready, false);
  assert.equal(engineeringAvailability({ ...withAgent, auditReady: false }).ready, false);
});
test("the Engineering Agent's source read is parsed strictly; the analysis is text, the steps are bounded, nothing else is mistaken for it", () => {
  assert.deepEqual(parseReply(sourceReply), sourceReply);
  const src = sourceReply.source;
  for (const patch of [{ source: { ...src, ref: "main" } }, { source: { ...src, repository: "other/repo" } }, { source: { ...src, returned: 1 } },
    { source: { ...src, steps: [{ ...src.steps[0], mode: "exec" }, src.steps[1]] } }, { source: { ...src, steps: [{ ...src.steps[0], path: "../etc" }, src.steps[1]] } },
    { source: { ...src, steps: [{ ...src.steps[0], step: 2 }, src.steps[1]] } }, { source: { ...src, steps: [{ ...src.steps[0], source: "shell" }, src.steps[1]] } },
    { source: { ...src, steps: [{ ...src.steps[0], diff: "x" }, src.steps[1]] } }, { source: { ...src, analysis: 4 } },
    { engineering: engineeringReply.engineering }, { source: null }])
    rejects(parseReply, { ...sourceReply, ...patch });
  assert.equal(parseReply({ ...reply, source: null }).source, null);
  assert.deepEqual(parseReply(engineeringReply), engineeringReply);
});
test("the SRE Agent's platform signals are parsed strictly; a URL, an address or a secret in a row is refused", () => {
  assert.deepEqual(parseReply(platformReply), platformReply);
  const pl = platformReply.platform;
  for (const patch of [{ platform: null }, { platform: { ...pl, view: "restart" } }, { platform: { ...pl, returned: 1 } },
    { platform: { ...pl, rows: [{ ...pl.rows[0], state: "down" }, pl.rows[1]] } }, { platform: { ...pl, rows: [{ ...pl.rows[0], kind: "action" }, pl.rows[1]] } },
    { platform: { ...pl, rows: [{ ...pl.rows[0], detail: "see https://portal.azure.com" }, pl.rows[1]] } },
    { platform: { ...pl, rows: [{ ...pl.rows[0], detail: "mail admin@leschaco.com" }, pl.rows[1]] } },
    { platform: { ...pl, rows: [{ ...pl.rows[0], secret: "x" }, pl.rows[1]] } }, { evidence: reply.evidence }])
    rejects(parseReply, { ...platformReply, ...patch });
  assert.equal(parseReply({ ...reply, platform: null }).platform, null);
  assert.equal(askBody("x", "sre-agent").context.page, "sre");
  const withAgent = { ...status, agents: [...status.agents, { id: "sre-agent", name: "SRE Agent", enabled: true, connected: true }] };
  assert.equal(sreAvailability(status).ready, false);
  assert.equal(sreAvailability(withAgent).ready, true);
  assert.equal(sreAvailability({ ...withAgent, mock: true }).ready, false);
});
test("Phase 8 Management plans require the exact specialist trail and their own answer views", () => {
  const jobPlan = {
    plan: "summarise_job", title: "สรุปงานหนึ่งงาน", steps: 3, retrievedAt: "2026-09-22T05:00:00Z", basis: "FIXTURE ONLY · no cause inferred",
    trail: [
      { step: 1, agentId: "operations-agent", tool: "search_shipment", view: "search", purpose: "หางาน", total: 1, returned: 1, truncated: false, status: "succeeded" },
      { step: 2, agentId: "document-agent", tool: "query_documents", view: "job", purpose: "เอกสาร", total: 2, returned: 2, truncated: false, status: "succeeded" },
      { step: 3, agentId: "communication-agent", tool: "query_messages", view: "job", purpose: "ข้อความ", total: 2, returned: 2, truncated: false, status: "succeeded" },
    ],
    findings: [{ id: "job", label: "งาน", value: "TEST-JOB-001", detail: "FIXTURE ONLY", jobKeys: ["TEST-ONLY-001"] }],
  };
  const full = { ...reply, agentId: "management-agent", evidence: { ...reply.evidence, view: "search" },
    documents: documentsReply.documents, messages: messagesReply.messages, collaboration: jobPlan };
  assert.equal(parseReply(full), full);
  for (const patch of [{ collaboration: null }, { evidence: reply.evidence }, { documents: null }, { messages: null },
    { collaboration: { ...jobPlan, steps: 4 } }, { collaboration: { ...jobPlan, trail: [jobPlan.trail[1], jobPlan.trail[0], jobPlan.trail[2]] } },
    { collaboration: { ...jobPlan, trail: [{ ...jobPlan.trail[0], tool: "execute_sql" }, ...jobPlan.trail.slice(1)] } }])
    rejects(parseReply, { ...full, ...patch });
  const latePlan = { ...jobPlan, plan: "summarise_late_paperwork", steps: 2,
    trail: [{ ...jobPlan.trail[0], tool: "query_delays", view: "delays" }, { ...jobPlan.trail[1], view: "missing" }] };
  const late = { ...full, evidence: { ...reply.evidence, view: "delays" }, documents: { ...documentsReply.documents, view: "missing" },
    messages: null, collaboration: latePlan };
  assert.equal(parseReply(late), late);
  rejects(parseReply, { ...late, messages: messagesReply.messages });
  rejects(parseReply, { ...reply, collaboration: jobPlan });
  assert.equal(askBody("สรุปงาน", "management-agent").context.page, "management");
  const withAgent = { ...status, agents: [...status.agents, { id: "management-agent", name: "Management Agent", enabled: true, connected: true }] };
  assert.equal(managementAvailability(status).ready, false);
  assert.equal(managementAvailability(withAgent).ready, true);
  assert.equal(managementAvailability({ ...withAgent, auditReady: false }).ready, false);
});
test("requests preserve abort signal and use same-origin JSON without client secrets", async () => {
  const controller = new AbortController();
  let calls = 0;
  const result = await controlRequest(async (path, init) => {
    calls++;
    assert.equal(path, "/api/ai/chat"); assert.equal(init.method, "POST");
    assert.equal(init.signal, controller.signal);
    assert.deepEqual(Object.keys(init.headers).sort(), ["accept", "content-type"]);
    assert.deepEqual(JSON.parse(init.body), askBody("งานวันนี้"));
    return response(reply);
  }, "/api/ai/chat", parseReply, controller.signal, askBody("งานวันนี้"));
  assert.equal(result.evidence.total, 2); assert.equal(calls, 1);
});
test("non-OK POST never retries, even when service supplies retry guidance", async () => {
  let calls = 0;
  await assert.rejects(controlRequest(async () => {
    calls++; return response({ code: "provider_busy", error: "PRIVATE SERVER DETAILS" }, 429);
  }, "/api/ai/chat", parseReply, new AbortController().signal, askBody("งานวันนี้")), /provider_busy/);
  assert.equal(calls, 1);
});
test("raw server text, unknown codes and prototype keys are never rendered as errors", async () => {
  for (const code of ["unrecognised-secret", "__proto__", "toString"]) {
    let failure;
    try { await controlRequest(async () => response({ code, error: "SECRET" }, 503), "/api/ai/status", parseStatus, new AbortController().signal); }
    catch (error) { failure = error; }
    assert.equal(errorText(failure), errorText(new ControlError("unavailable")));
    assert.equal(errorText(new ControlError(code)), errorText(new Error("SECRET")));
  }
  assert.doesNotMatch(errorText(new Error("SECRET")), /SECRET/);
});
test("permission errors, invalid JSON, and cancelled requests are not empty successes", async () => {
  for (const [code, expected] of [[401, "unauthenticated"], [403, "forbidden"], [404, "not_found"]])
    await assert.rejects(controlRequest(async () => response({}, code), "/api/ai/audit", parseAuditPage, new AbortController().signal), new RegExp(expected));
  await assert.rejects(controlRequest(async () => new Response("<html>"), "/api/ai/status", parseStatus, new AbortController().signal), /invalid_response/);
  const controller = new AbortController(); controller.abort();
  await assert.rejects(controlRequest(async (_path, init) => { init.signal.throwIfAborted(); }, "/api/ai/status", parseStatus, controller.signal));
});
test("timestamps use Thai operational time and explicitly absent source timestamps", () => {
  assert.equal(stamp(null), "—"); assert.equal(stamp("invalid-date"), "invalid-date");
  assert.match(stamp("2026-09-07T03:00:00Z"), /10:00:00/);
});
test("the route keeps the Home identity guard, and the assistant is a section of the tower rather than a menu of its own", () => {
  const nav = readFileSync(new URL("../app/scmos/nav.ts", import.meta.url), "utf8");
  const route = readFileSync(new URL("../app/ai-control-tower/page.tsx", import.meta.url), "utf8");
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  const tower = readFileSync(new URL("../app/scmos/screens/AiControlTower.tsx", import.meta.url), "utf8");
  assert.match(nav, /\["ai", "AI Control Tower"/);
  // 23 Sep 2026: one place to look at the AI. The screen id is gone from the
  // menu, the icons and the titles, and its panels are a section of the tower.
  assert.doesNotMatch(nav, /"assistant"/);
  assert.doesNotMatch(app, /screen === "assistant"/);
  assert.match(tower, /import \{ Assistant \} from "\.\/Assistant"/);
  assert.match(tower, /<Assistant canApprove=\{canApprove\}/);
  assert.match(route, /await getUser\(\)/); assert.match(route, /NODE_ENV !== "production"/); assert.match(route, /initialScreen="ai"/);
  assert.match(app, /initialScreen \?\? stored.landing/);
  assert.match(app, /screen === "ai" && !isCarrier/);
});
test("UI keeps read-only boundaries and transient state; no auto AI prompt or raw HTML", () => {
  const source = readFileSync(new URL("../app/scmos/screens/AiControlTower.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(source, /dangerouslySetInnerHTML|localStorage\.|useRemembered|\/api\/risk|\/api\/ai\/invoke|\/api\/ai\/approvals/);
  assert.match(source, /canViewAudit \? "\/api\/ai\/audit/);
  assert.match(source, /canViewDashboard \? "\/api\/dashboard/);
  // Only Operations may bypass question readiness for its separate change-draft path.
  assert.match(source, /!asking.ready && !\(agent === "operations-agent" && isChangeCommand\(message\)\)/);
  assert.match(source, /agent === "document-agent" \? documentsReady : agent === "engineering-agent" \? engineeringReady : agent === "sre-agent" \? sreReady\s*: agent === "management-agent" \? managementReady : ready;/);
  assert.match(source, /operations-changes\/interpret/);
  assert.match(source, /setChangeDraft\(parseChangeDraft\(result\)\)/);
  assert.match(source, /request.current === controller/);
});
