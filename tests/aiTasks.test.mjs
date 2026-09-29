import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { myTasks, parseTasks, SECTIONS, sectionOf, TASK_CARDS } from "../app/scmos/aiTasks.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const counts = (over = {}) => ({
  jobsMonitored: 120, aiHandling: 9, needsMyDecision: 4, highRisk: 2, blocked: 0, informationRequired: 3,
  carrierEscalation: 1, pendingApproval: 1, agentFailure: 0, ...over,
});
const d = (over = {}) => ({ id: 1, canAnswer: true, resultStatus: "COMPLETED", riskLevel: "", agentId: "otd-agent", ruleReferences: [], ...over });

test("the cards read the server's counts as they are", () => {
  assert.deepEqual(parseTasks(counts()), counts());
  assert.equal(TASK_CARDS.length, 8);
  assert.ok(TASK_CARDS.every(card => Object.hasOwn(counts(), card.key)));
});

test("counts that cannot be true are not drawn", () => {
  for (const bad of [
    counts({ needsMyDecision: 10 }),                                                  // more to answer than there is
    counts({ highRisk: 10 }), counts({ informationRequired: 10 }), counts({ carrierEscalation: 10 }),
    counts({ jobsMonitored: -1 }), counts({ agentFailure: 1.5 }), counts({ pendingApproval: "1" }),
    { ...counts(), blocked: undefined }, null, [],
  ]) assert.throws(() => parseTasks(bad), /invalid_response/);
});

test("a decision's section is the server's: blocked, high risk, carrier, information, then the rest", () => {
  // The same cases as AgentScanChecks' "tasks:" check, so the two copies of the rule cannot drift.
  assert.equal(sectionOf(d({ resultStatus: "BLOCKED", riskLevel: "HIGH" })), "blocked");
  assert.equal(sectionOf(d({ riskLevel: "CRITICAL" })), "high_risk");
  assert.equal(sectionOf(d({ resultStatus: "REQUIRES_HUMAN_REVIEW", riskLevel: "MEDIUM", agentId: "vendor-agent", ruleReferences: ["Carrier.NoEligible"] })), "carrier");
  assert.equal(sectionOf(d({ agentId: "communication-agent", ruleReferences: ["Template:CARRIER_CONFIRMATION_REMINDER"] })), "carrier");
  assert.equal(sectionOf(d({ agentId: "communication-agent", ruleReferences: ["Template:POD_REMINDER"] })), "decision");
  assert.equal(sectionOf(d({ resultStatus: "INSUFFICIENT_INFORMATION", riskLevel: "MEDIUM", agentId: "validation-agent" })), "information");
  assert.equal(sectionOf(d({ riskLevel: "WATCH" })), "decision");
});

test("my tasks are only what I may answer, by section, empty sections left out, in the spec's order", () => {
  const sections = myTasks([
    d({ id: 1, riskLevel: "WATCH" }), d({ id: 2, riskLevel: "HIGH" }), d({ id: 3, riskLevel: "HIGH", canAnswer: false }),
    d({ id: 4, resultStatus: "INSUFFICIENT_INFORMATION" }),
  ]);
  assert.deepEqual(sections.map(s => [s.id, s.items.map(i => i.id)]), [["high_risk", [2]], ["information", [4]], ["decision", [1]]]);
  assert.deepEqual(SECTIONS.map(s => s.id), ["blocked", "high_risk", "carrier", "information", "decision"]);
});

test("the panel opens on my tasks, shows the cards, and offers answers only where the server allows them", () => {
  const panel = read("app/scmos/screens/AiFindingsPanel.tsx");
  assert.match(panel, /useState<Filter>\("mine"\)/);
  assert.match(panel, /apiFetch\("\/api\/ai\/tasks"/);
  assert.match(panel, /data-testid="ai-task-cards"/);
  assert.match(panel, /\{!item\.canAnswer \? null/);
  assert.match(panel, />ของฉัน \{mineCount\}</);
  // Items are rendered by a function, not a component declared inside the panel (which would remount them on every keystroke).
  assert.doesNotMatch(panel, /function Item\(/);
  assert.match(panel, /section\.items\.map\(renderItem\)/);
});
