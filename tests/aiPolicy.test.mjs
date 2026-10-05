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
// The per-agent bounds the human approved on 5 Oct 2026 — nine agents; the other five keep none.
const APPROVED_BOUNDS = {
  "operations-agent": [800, 2, 3, 0, 30, 2, 40, 0.03, 1, 0.0015],
  "data-agent": [800, 2, 3, 0, 30, 1, 40, 0.03, 1, 0.0015],
  "sre-agent": [800, 2, 3, 0, 30, 1, 20, 0.02, 0.5, 0.0015],
  "management-agent": [800, 4, 5, 0, 30, 1, 60, 0.1, 1.5, 0.015],
  "document-agent": [800, 2, 3, 0, 30, 1, 40, 0.09, 1, 0.03],
  "engineering-agent": [800, 4, 5, 0, 30, 1, 30, 0.12, 1.5, 0.03],
  "otd-agent": [800, 1, 1, 0, 30, 1, 500, 0.001, 0.03, 0.000001],
  "validation-agent": [800, 1, 1, 0, 30, 1, 500, 0.001, 0.03, 0.000001],
  "vendor-agent": [800, 1, 1, 0, 30, 1, 700, 0.001, 0.03, 0.000001],
};
const BOUND_NAMES = ["maxOutputTokens", "maxToolCalls", "maxApiCalls", "maxRetry", "maxRuntimeSeconds", "maxConcurrentTasks",
  "dailyRequests", "dailyCostLimit", "monthlyCostLimit", "maxReservationCost"];
test("the reviewed policy carries the human's approval, owner pair, bounds and a dated isolation acceptance, without grants", () => {
  // 5 Oct 2026: readiness review SCMOS-AI-RR-2026-10-05, decided by the human, recorded in docs/ai.
  assert.match(matrix.approvalReference, /^SCMOS-AI-RR-2026-10-05 — approved by K\.nattikorn-fos@hotmail\.com \(AD-01, Administrator\)/);
  assert.equal(matrix.policyVersion, "scmos-permission-7");
  assert.equal(matrix.previousVersion, "scmos-permission-6-candidate");
  for (const agent of matrix.agents) {
    assert.equal(agent.humanOwner, "email:K.nattikorn-fos@hotmail.com");
    assert.equal(agent.fallbackOwner, "AM-01");
    const approved = APPROVED_BOUNDS[agent.agentId];
    if (approved) assert.deepEqual(BOUND_NAMES.map((name) => agent.budget[name]), approved, agent.agentId);
    else assert.equal(agent.budget, null, agent.agentId);
    // The shared runtime is accepted for exactly the bounded agents, and only until the end of 2026.
    if (approved) {
      assert.match(agent.runtimeIsolationApproval, /^SCMOS-AI-RR-2026-10-05: /, agent.agentId);
      assert.equal(agent.runtimeIsolationExpiresAt, "2026-12-31T23:59:59+07:00", agent.agentId);
    } else {
      assert.equal(agent.runtimeIsolationApproval ?? null, null, agent.agentId);
    }
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
