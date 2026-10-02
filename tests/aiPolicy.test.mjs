import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { parsePolicyReport } from "../app/scmos/aiPolicy.ts";

const matrix = JSON.parse(readFileSync(new URL("../server/Scmos.Api/Ai/Policy/permission-matrix.json", import.meta.url), "utf8"));
const report = () => ({
  policyVersion: matrix.policyVersion, valid: true, auditAvailable: true, readOnly: true,
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
test("candidate policy has no new execution permissions or invented human owners/budgets", () => {
  assert.equal(matrix.approvalReference, null);
  for (const agent of matrix.agents) {
    assert.equal(agent.humanOwner, null);
    assert.equal(agent.fallbackOwner, null);
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
