import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parsePolicyReport } from "../app/scmos/aiPolicy.ts";

const matrix = JSON.parse(readFileSync(new URL("../server/Scmos.Api/Ai/Policy/permission-matrix.json", import.meta.url), "utf8"));
const report = () => ({
  policyVersion: matrix.policyVersion, valid: true, auditAvailable: true, readOnly: true,
  sharedBudget: { ...matrix.sharedBudget, reservedCostMonth: 0.04, remainingCostMonth: 9.96, periodTimeZone: "UTC" },
  agents: matrix.agents.map(manifest => ({
    id: manifest.agentId, name: manifest.agentId, status: "CONFIGURATION_REQUIRED", reasonCode: "policy_review_required",
    policyVersion: matrix.policyVersion, allowedTools: manifest.allowedTools,
    read: [], analyze: [], draft: [], execute: [], humanApproval: [], forbidden: ["DirectProductionSql"],
    budget: null, reservedCostMonth: 0, lastExecution: null, lastSecurityEvent: null,
  })),
});
test("policy report preserves all fourteen immutable identities and is read-only", () => {
  const parsed = parsePolicyReport(report());
  assert.equal(parsed.agents.length, 14);
  assert.ok(parsed.agents.some(agent => agent.id === "document-agent"));
  assert.equal(parsed.readOnly, true);
});

test("confirmed money is one shared pool across fourteen agents, not fourteen allocations", () => {
  assert.deepEqual(matrix.sharedBudget, { scope: "all-registered-agents", currency: "USD", monthlyCostLimit: 10, dailyCostLimit: 0.20 });
  const shared = parsePolicyReport(report()).sharedBudget;
  assert.equal(shared.dailyCostLimit, 0.20);
  assert.equal(shared.monthlyCostLimit, 10);
  assert.equal(shared.remainingCostMonth, 9.96);
  assert.equal(shared.periodTimeZone, "UTC");
});

test("unknown shared budget accounting remains unknown and legacy reports stay readable", () => {
  const value = report(); value.auditAvailable = false;
  value.sharedBudget.reservedCostMonth = null; value.sharedBudget.remainingCostMonth = null;
  assert.equal(parsePolicyReport(value).sharedBudget.remainingCostMonth, null);
  delete value.sharedBudget;
  assert.equal(parsePolicyReport(value).sharedBudget, undefined);
});

test("shared budget parser rejects forged scope, currency, period and invalid amounts", () => {
  for (const change of [shared => shared.scope = "per-agent", shared => shared.currency = "THB",
    shared => shared.periodTimeZone = "unknown", shared => shared.dailyCostLimit = -1,
    shared => shared.dailyCostLimit = 11, shared => shared.dailyCostLimit = 0,
    shared => shared.monthlyCostLimit = 0, shared => shared.reservedCostMonth = -1,
    shared => shared.remainingCostMonth = 11, shared => shared.remainingCostMonth = -1]) {
    const value = report(); change(value.sharedBudget);
    assert.throws(() => parsePolicyReport(value), /invalid_response/);
  }
});
test("missing audit and budget remain unavailable, never invented zero readiness", () => {
  const value = report(); value.auditAvailable = false; value.agents[0].reservedCostMonth = null;
  const parsed = parsePolicyReport(value);
  assert.equal(parsed.auditAvailable, false);
  assert.equal(parsed.agents[0].budget, null);
  assert.equal(parsed.agents[0].reservedCostMonth, null);
});
test("policy parser rejects partial, duplicate and mutable reports", () => {
  for (const change of [value => value.agents.pop(), value => value.agents[1].id = value.agents[0].id,
    value => value.readOnly = false, value => value.agents[0].allowedTools = "execute_sql",
    value => value.agents[0].lastSecurityEvent = "not-a-date", value => value.agents[0].reservedCostMonth = -1,
    value => value.agents[0].id = "unknown-agent", value => value.agents[0].policyVersion = "mismatched"]) {
    const value = report(); change(value);
    assert.throws(() => parsePolicyReport(value), /invalid_response/);
  }
});
test("candidate nominates only the human-provided owner pair, without grants or invented per-agent budgets", () => {
  assert.equal(matrix.approvalReference, null);
  // 5 Oct 2026: the nominated backup address matched no staff row; the human chose the verified Staff ID AM-01.
  assert.equal(matrix.policyVersion, "scmos-permission-5-candidate");
  assert.equal(matrix.previousVersion, "scmos-permission-4-candidate");
  for (const agent of matrix.agents) {
    assert.equal(agent.humanOwner, "email:K.nattikorn-fos@hotmail.com");
    assert.equal(agent.fallbackOwner, "AM-01");
    assert.equal(agent.budget, null);
    assert.equal(agent.failClosed, true);
    assert.equal(agent.auditRequired, true);
    assert.ok(!Object.values(agent.permissions).includes("Execute"));
  }
});
test("grant panel has no policy write request and shows fail-closed configuration", () => {
  const panel = readFileSync(new URL("../app/scmos/screens/AiPolicyPanel.tsx", import.meta.url), "utf8");
  assert.ok(panel.includes("/api/ai/policies"));
  assert.ok(panel.includes("CONFIGURATION_REQUIRED"));
  assert.ok(!panel.includes('method: "PUT"') && !panel.includes('method: "POST"'));
});
test("owner identifiers may be absent while unconfigured, but cannot be forged objects", () => {
  const value = report();
  value.agents[0].humanOwner = "OP-OWNER";
  value.agents[0].fallbackOwner = null;
  assert.equal(parsePolicyReport(value).agents[0].humanOwner, "OP-OWNER");
  value.agents[0].humanOwner = { role: "Administrator" };
  assert.throws(() => parsePolicyReport(value), /invalid_response/);
});
test("new approval/communication bindings are metadata only, never candidate grants", () => {
  assert.equal(matrix.tools.update_shipment, "BookingUpdateCriticalField");
  assert.equal(matrix.tools.request_communication_draft, "CommunicationDraft");
  assert.equal(matrix.tools.summarize_evaluation, "ManagementAnalyze");
  for (const agent of matrix.agents) {
    assert.ok(!agent.allowedTools.includes("update_shipment"));
    assert.ok(!agent.allowedTools.includes("request_communication_draft"));
    assert.ok(!agent.allowedTools.includes("summarize_evaluation"));
  }
});
test("operations confirmation quotes the immutable payload hash displayed by the queue", () => {
  const screen = readFileSync(new URL("../app/scmos/screens/OperationsChanges.tsx", import.meta.url), "utf8");
  assert.ok(screen.includes("payloadHash: review.payloadHash"));
  assert.ok(screen.includes("reviewed_payload_required"));
  assert.ok(screen.includes("approval_mfa_required"));
});
