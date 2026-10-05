import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

// 5 Oct 2026: the scheduled Communication pass asked the gateway twice for every row of the register — about 9,700
// serializable audit writes a round. It asks once per round now; the per-object entry stays for other callers.
const scanner = readFileSync("server/Scmos.Api/Ai/AgentScanner.cs", "utf8");
const gateway = readFileSync("server/Scmos.Api/Services/AiGateway.Communication.cs", "utf8");

test("the scheduled pass authorizes once per round, then judges rows without asking again", () => {
  assert.match(scanner, /AiGateway\.CommunicationPassAsync\(policyGateway,\s*\(\) => CommunicationContextAsync\(jobs, now, token\), now, token\)/);
  assert.doesNotMatch(scanner, /DraftCommunicationAsync/);
  const pass = gateway.slice(gateway.indexOf("CommunicationPassAsync("), gateway.indexOf("private static async Task AuthorizeDraftAsync"));
  assert.ok(pass.indexOf("await AuthorizeDraftAsync(") > 0 && pass.indexOf("await AuthorizeDraftAsync(") < pass.indexOf("await readContext()"),
    "both checks pass before the round's context is read");
  assert.doesNotMatch(pass.slice(pass.indexOf("return row =>")), /Authorize/);
});

test("the per-object entry keeps its binding checks and the same two authorizations", () => {
  const draft = gateway.slice(gateway.indexOf("DraftCommunicationAsync("), gateway.indexOf("CommunicationPassAsync("));
  assert.match(draft, /initiating\.ResourceType != "shipment" \|\| initiating\.ResourceId != row\.Key/);
  assert.match(draft, /await AuthorizeDraftAsync\(gateway, initiating, token\);/);
  const both = gateway.slice(gateway.indexOf("private static async Task AuthorizeDraftAsync"));
  assert.equal((both.match(/AiPolicyEntry\.AuthorizeAsync\(/g) ?? []).length, 2);
});
