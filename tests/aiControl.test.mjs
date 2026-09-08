import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  askBody, availability, ControlError, controlRequest, errorText, issueTarget, number,
  parseStatus, parseToday, parseBrief, parseReply, parseAuditPage, parseAuditRun, stamp,
} from "../app/scmos/aiControl.ts";
import { status, today, brief, reply, run, audit } from "./fixtures/ai-control.mjs";
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
  for (const patch of [{ runId: "../../admin" }, { scope: null }, { events: [] }, { sourceKeys: [9] }, { events: Array(5).fill(run.events[0]) }])
    rejects(parseAuditRun, { ...run, ...patch });
  rejects(parseAuditPage, { ...audit, nextBeforeId: -5 });
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
test("new route uses Home identity guard and keeps legacy Assistant/other menus", () => {
  const nav = readFileSync(new URL("../app/scmos/nav.ts", import.meta.url), "utf8");
  const route = readFileSync(new URL("../app/ai-control-tower/page.tsx", import.meta.url), "utf8");
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.match(nav, /\["ai", "AI Control Tower"/); assert.match(nav, /\["assistant", "AI Assistant"/);
  assert.match(route, /await getUser\(\)/); assert.match(route, /NODE_ENV !== "production"/); assert.match(route, /initialScreen="ai"/);
  assert.match(app, /initialScreen \?\? stored.landing/);
  assert.match(app, /screen === "ai" && !isCarrier/);
});
test("UI keeps read-only boundaries and transient state; no auto AI prompt or raw HTML", () => {
  const source = readFileSync(new URL("../app/scmos/screens/AiControlTower.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(source, /dangerouslySetInnerHTML|localStorage\.|useRemembered|\/api\/risk|\/api\/ai\/invoke|\/api\/ai\/approvals/);
  assert.match(source, /canViewAudit \? "\/api\/ai\/audit/);
  assert.match(source, /canViewDashboard \? "\/api\/dashboard/);
  assert.match(source, /if \(request.current \|\| !ready.ready/);
  assert.match(source, /request.current === controller/);
});
