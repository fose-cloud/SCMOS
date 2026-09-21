import test from "node:test";
import assert from "node:assert/strict";
import { agentReadiness } from "../app/scmos/agentReadiness.ts";
import { status } from "./fixtures/ai-control.mjs";
const live = () => ({ ...structuredClone(status), enabled: true, chatEnabled: true, configurationValid: true,
  providerConfigured: true, mock: false, auditReady: true, liveToolsReady: true,
  agents: [{ id: "operations-agent", name: "Operations", enabled: true, connected: true }] });
test("readiness requires every live prerequisite, never the write flag", () => {
  assert.equal(agentReadiness(live(), "operations-agent").ready, true);
  for (const flag of ["enabled", "chatEnabled", "configurationValid", "providerConfigured", "auditReady", "liveToolsReady"])
    assert.equal(agentReadiness({ ...live(), [flag]: false, writeToolsReady: true }, "operations-agent").ready, false, flag);
  assert.equal(agentReadiness({ ...live(), mock: true }, "operations-agent").code, "mock");
  assert.equal(agentReadiness({ ...live(), mock: true }, "operations-agent").ready, false);
});
test("missing scope and future agents never inherit Operations readiness", () => {
  assert.equal(agentReadiness(null, "operations-agent").code, "unknown");
  assert.equal(agentReadiness(live(), "rate-agent").code, "unavailable");
  const value = live();
  value.agents.push({ id: "rate-agent", name: "Rates", enabled: true, connected: true });
  assert.equal(agentReadiness(value, "rate-agent").code, "not_connected");
});
test("reviewed read specialists use their own connection, not the Operations-only liveToolsReady bit", () => {
  for (const id of ["data-agent", "communication-agent", "document-agent", "engineering-agent"]) {
    const scoped = { ...live(), liveToolsReady: false,
      agents: [{ id, name: id, enabled: true, connected: true }] };
    assert.equal(agentReadiness(scoped, id).ready, true, id);
    assert.equal(agentReadiness({ ...scoped, agents: [{ ...scoped.agents[0], connected: false }] }, id).ready, false);
  }
});
test("durable stop and unavailable control override otherwise ready fields", () => {
  const control = { available: true, enabled: true, emergencyDisabled: false, revision: 1, canManage: true, canEnable: true, blockReason: "" };
  for (const patch of [{ available: false }, { enabled: false }, { emergencyDisabled: true }])
    assert.equal(agentReadiness({ ...live(), operationsControl: { ...control, ...patch } }, "operations-agent").ready, false);
});
