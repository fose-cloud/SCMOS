import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  AUTONOMY_LABEL, autonomyChoices, costText, GOVERNANCE_STATUS_LABEL, parseGovernance, PLATFORM_CHOICES, saveError,
  SETTABLE_STATUSES, settingsBody,
} from "../app/scmos/aiGovernance.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const agent = (over = {}) => ({
  id: "data-agent", name: "Data Agent", flagEnabled: true, maxAutonomy: 2, autonomy: 2, effectiveAutonomy: 2,
  shadowMode: false, status: "ACTIVE", effectiveStatus: "ACTIVE", reason: "", revision: 0, updatedBy: "", updatedAt: null,
  stored: false, runs24h: 4, failures24h: 1, failureRate24h: 0.25, consecutiveFailures: 0, lastSuccess: "2026-09-28T01:00:00Z",
  lastFailure: "2026-09-27T23:00:00Z", averageMs: 2100, breaker: "Closed",
  usage: { inputTokens24h: 1200, outputTokens24h: 300, cost24h: 0.01, inputTokens30d: 40000, outputTokens30d: 9000, cost30d: 0.15 },
  ...over,
});
const report = (over = {}) => ({
  available: true, canManage: true,
  platform: { autonomy: 4, executionEnabled: true, stopped: false, reason: "", revision: 0, updatedBy: "", updatedAt: null, stored: false },
  agents: [agent(), agent({ id: "operations-agent", name: "Operations Agent", maxAutonomy: 3, autonomy: 3, effectiveAutonomy: 3 })],
  promptVersion: "build:65fd552a1b2c", priceCurrency: "USD", pricesConfigured: true,
  breakerDegradedAfter: 3, breakerPauseAfter: 5, breakerCoolDownMinutes: 10,
  ...over,
});
const rejects = (value) => assert.throws(() => parseGovernance(value), /invalid_response/);

test("a governance report the API sends is read as it is", () => {
  const parsed = parseGovernance(report());
  assert.equal(parsed.agents.length, 2);
  assert.equal(parsed.platform.executionEnabled, true);
});

test("a report that does not match is not drawn", () => {
  rejects(null);
  rejects(report({ agents: [agent({ effectiveAutonomy: 3 })] }));            // above the agent's design
  rejects(report({ agents: [agent({ status: "DEGRADED_ISH" })] }));
  rejects(report({ agents: [agent({ failures24h: 9 })] }));                  // more failures than runs
  rejects(report({ agents: [agent(), agent()] }));                            // the same agent twice
  rejects(report({ agents: [agent({ usage: { ...agent().usage, cost24h: -1 } })] }));
  rejects(report({ platform: { ...report().platform, autonomy: 2 } }));      // execution flag disagrees with the ceiling
  rejects(report({ platform: { ...report().platform, autonomy: 0, executionEnabled: false } })); // stopped flag disagrees
  rejects(report({ agents: [agent({ breaker: "Melted" })] }));
});

test("the levels on offer never exceed what the agent was built for", () => {
  assert.deepEqual(autonomyChoices({ maxAutonomy: 2 }), [0, 1, 2]);
  assert.deepEqual(autonomyChoices({ maxAutonomy: 3 }), [0, 1, 2, 3]);
  assert.equal(AUTONOMY_LABEL.length, 5);
  assert.deepEqual(PLATFORM_CHOICES.map(c => c.autonomy), [4, 2, 0]);
});

test("DEGRADED is shown but never offered as a setting", () => {
  assert.ok(Object.hasOwn(GOVERNANCE_STATUS_LABEL, "DEGRADED"));
  assert.ok(!SETTABLE_STATUSES.includes("DEGRADED"));
  assert.deepEqual([...SETTABLE_STATUSES], ["ACTIVE", "PAUSED", "MAINTENANCE", "DISABLED"]);
});

test("cost says when there is no price rather than showing zero", () => {
  assert.equal(costText(null, "USD", false), "ไม่ได้ตั้งราคา");
  assert.equal(costText(null, "USD", true), "ราคาไม่ครบ");
  assert.equal(costText(1.5, "USD", true), "1.50 USD");
});

test("a change is sent with its reason trimmed and the revision it was read at", () => {
  assert.deepEqual(settingsBody(1, true, "PAUSED", "  drill  ", 3), { autonomy: 1, shadowMode: true, status: "PAUSED", reason: "drill", revision: 3 });
  assert.equal(saveError("conflict"), "มีผู้เปลี่ยนค่าไปแล้ว — รีเฟรชแล้วลองใหม่");
  assert.equal(saveError("anything else"), saveError("unavailable"));
});

test("the panel sits in the Control Tower for audit readers and writes only through the control header", () => {
  const tower = read("app/scmos/screens/AiControlTower.tsx");
  const panel = read("app/scmos/screens/AiGovernancePanel.tsx");
  assert.match(tower, /\{canViewAudit && <AiGovernancePanel \/>\}/);
  assert.match(panel, /"X-SCMOS-AI-Control": "1"/);
  assert.match(panel, /method: "PUT"/);
  assert.match(panel, /report\.canManage &&/);           // controls only when the server says this person may manage
  assert.match(panel, /disabled=\{!!saving \|\| !draft\.reason\.trim\(\)\}/);
});

test("the gate's refusals read in Thai, not as codes", () => {
  const control = read("app/scmos/aiControl.ts");
  for (const code of ["ai_stopped", "agent_paused", "agent_maintenance", "agent_circuit_open", "governance_unavailable",
    "autonomy_insufficient", "execution_disabled", "shadow_mode"])
    assert.match(control, new RegExp(`${code}: "`), code);
});
