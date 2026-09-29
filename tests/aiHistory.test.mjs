import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  AUDIT_AGENTS, AUDIT_TOOLS, auditUrl, DECISION_STATUS, decisionsUrl, EMPTY_AUDIT_QUERY, EMPTY_DECISION_QUERY, queryProblem, RUN_RESULTS,
} from "../app/scmos/aiHistory.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("the history's query leaves blanks out and encodes the rest", () => {
  assert.equal(decisionsUrl(EMPTY_DECISION_QUERY, 1), "/api/ai/decisions?page=1&pageSize=25");
  assert.equal(decisionsUrl({ ...EMPTY_DECISION_QUERY, from: "2026-09-01", customer: " BASF ", q: "J-1 & co" }, 3),
    "/api/ai/decisions?page=3&pageSize=25&from=2026-09-01&q=J-1%20%26%20co&customer=BASF");
  assert.equal(decisionsUrl(EMPTY_DECISION_QUERY, 0), "/api/ai/decisions?page=1&pageSize=25");
  assert.equal(auditUrl(EMPTY_AUDIT_QUERY, null), "/api/ai/audit?take=8");
  assert.equal(auditUrl({ ...EMPTY_AUDIT_QUERY, tool: "draft_booking", key: "mail:41" }, 120),
    "/api/ai/audit?take=8&beforeId=120&tool=draft_booking&key=mail%3A41");
});

test("a range that cannot be asked is caught before it is sent", () => {
  assert.equal(queryProblem({ from: "", to: "" }), "");
  assert.equal(queryProblem({ from: "2026-09-01", to: "2026-09-30" }), "");
  assert.equal(queryProblem({ from: "2026-09-30", to: "2026-09-01" }), "วันสิ้นสุดอยู่ก่อนวันเริ่ม");
  assert.equal(queryProblem({ from: "01/09/2026", to: "" }), "วันที่ไม่ถูกต้อง");
});

test("the choices offered are the server's own words", () => {
  // The audit's vocabulary (AiAuditRules.KnownAgents / KnownTools, AuditSearch.Results): a choice outside it is a 400.
  const rules = read("server/Scmos.Api/Ai/AiAuditRules.cs");
  const reader = read("server/Scmos.Api/Ai/AiAuditReader.cs");
  for (const agent of AUDIT_AGENTS) assert.ok(rules.includes(`"${agent}"`), agent);
  for (const tool of AUDIT_TOOLS) assert.ok(rules.includes(`"${tool}"`), tool);
  for (const result of RUN_RESULTS) assert.ok(reader.includes(`"${result}"`), result);
  assert.deepEqual(Object.keys(DECISION_STATUS), ["OPEN", "ACCEPTED", "OVERRIDDEN", "DISMISSED", "SUPERSEDED", "RESOLVED"]);
});

test("the history panel searches every status, opens each decision to why and what it rests on, and links a run", () => {
  const panel = read("app/scmos/screens/AiHistoryPanel.tsx");
  const tower = read("app/scmos/screens/AiControlTower.tsx");
  assert.match(panel, /decisionsUrl\(q, p, PAGE_SIZE\)/);
  for (const title of ["ข้อเท็จจริง", "ผลตามกฎ", "ข้อสันนิษฐาน", "ข้อแนะนำ", "หลักฐาน"]) assert.ok(panel.includes(`title="${title}"`), title);
  assert.match(panel, /item\.runId && onShowRun/);
  assert.match(tower, /\{canViewDashboard && <AiHistoryPanel onOpenJob=\{onOpenJob\}/);
  assert.match(tower, /useRemote\(canViewAudit \? auditUrl\(auditQuery, beforeId\) : null, parseAuditPage\)/);
  assert.match(tower, /data-testid="ai-activity-search"/);
});
