import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { AGENT_LABEL, answerBody, answerError, inReadingOrder, parseDecisions, RISK_LABEL, RISK_ORDER } from "../app/scmos/aiFindings.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const decision = (over = {}) => ({
  id: 1, runId: "", agentId: "otd-agent", decisionType: "otd_risk", entityType: "job", entityId: "J1",
  summary: "J-1 · เลยเวลาแผน 65 นาที ยังไม่มีเวลาถึง", resultStatus: "COMPLETED", status: "OPEN", riskLevel: "CRITICAL",
  confidence: null, requiresApproval: false, shadow: true, autonomy: 2, promptVersion: "build:abc",
  findings: {
    facts: [{ text: "แผน 28/09/2026 08:00", source: "job:J1.date+planTime" }],
    ruleResults: [{ text: "ถึงภายใน 08:30 จึงนับตรงเวลา", source: "CustomerTerms.Default" }],
    observations: [], inferences: [{ text: "งานนี้มีแนวโน้มไม่ตรงเวลา", source: null }],
    recommendations: [{ text: "ติดต่อ SHORE เพื่อยืนยันเวลาถึงจริง", source: null }], blockingIssues: [],
  },
  ruleReferences: ["OTD.PAST_WINDOW_NO_ARRIVAL"], evidenceReferences: ["job:J1"], humanChoice: "", humanMatches: null,
  overrideReason: "", decidedBy: "", decidedAt: null, createdAt: "2026-09-28T03:00:00Z",
  ...over,
});
const page = (items) => ({ items, total: items.length, page: 1, pageSize: 200 });
const rejects = (value) => assert.throws(() => parseDecisions(value), /invalid_response/);

test("the open decisions the API sends are read as they are", () => {
  assert.equal(parseDecisions(page([decision()])).items.length, 1);
});

test("a decision that mixes or loses its evidence is not drawn", () => {
  const withoutSource = decision();
  withoutSource.findings.facts = [{ text: "แผน 08:00", source: null }];
  rejects(page([withoutSource]));                                     // a fact must name its source
  const noRuleSource = decision();
  noRuleSource.findings.ruleResults = [{ text: "ถึงภายใน 08:30", source: null }];
  rejects(page([noRuleSource]));
  const missingList = decision();
  delete missingList.findings.inferences;
  rejects(page([missingList]));                                        // the four kinds are always there, apart
  rejects(page([decision({ riskLevel: "SEVERE" })]));
  rejects(page([decision({ entityId: "" })]));
  rejects({ items: [decision()], total: 0 });                           // total below what was sent
});

test("the most serious come first, then the newest", () => {
  const order = inReadingOrder([
    decision({ id: 1, riskLevel: "LOW" }), decision({ id: 2, riskLevel: "WATCH" }),
    decision({ id: 3, riskLevel: "CRITICAL", createdAt: "2026-09-28T01:00:00Z" }), decision({ id: 4, riskLevel: "CRITICAL" }),
  ]).map(d => d.id);
  assert.deepEqual(order, [4, 3, 2, 1]);
  assert.ok(RISK_ORDER.every(level => level === "" || Object.hasOwn(RISK_LABEL, level)));
});

test("the Carrier Agent's recommendations are a filter of their own, named for what they are about", () => {
  assert.equal(AGENT_LABEL["vendor-agent"], "ผู้ขนส่ง");
  assert.match(read("app/scmos/screens/AiFindingsPanel.tsx"), /\["all", "otd-agent", "validation-agent", "vendor-agent"\] as const/);
});

test("an answer is sent trimmed; an override carries what was done and why", () => {
  assert.deepEqual(answerBody("OVERRIDDEN", "  Carrier B ", " no DG driver "), { outcome: "OVERRIDDEN", choice: "Carrier B", reason: "no DG driver" });
  assert.deepEqual(answerBody("ACCEPTED"), { outcome: "ACCEPTED", choice: "", reason: "" });
  assert.equal(answerError("forbidden"), "เจ้าของงานหรือหัวหน้างานเท่านั้นที่ตอบได้");
  assert.equal(answerError("?"), "บันทึกไม่สำเร็จ ลองใหม่");
});

test("the panel shows the four kinds apart, answers through the control header, and sits in the Control Tower", () => {
  const panel = read("app/scmos/screens/AiFindingsPanel.tsx");
  const tower = read("app/scmos/screens/AiControlTower.tsx");
  for (const label of ["ข้อเท็จจริง", "ผลตามกฎ", "ข้อสันนิษฐาน", "ข้อแนะนำ"]) assert.ok(panel.includes(`title="${label}"`), label);
  assert.match(panel, /"X-SCMOS-AI-Control": "1"/);
  assert.match(panel, /\/api\/ai\/decisions\?status=OPEN/);
  assert.match(panel, /disabled=\{busy !== null \|\| !choice\.trim\(\) \|\| !reason\.trim\(\)\}/);
  assert.match(tower, /\{canViewDashboard && <AiFindingsPanel onOpenJob=\{onOpenJob\} \/>\}/);
});
