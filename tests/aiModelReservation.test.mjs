import assert from "node:assert/strict";
import { readFileSync, readdirSync, statSync } from "node:fs";
import { join } from "node:path";
import test from "node:test";

// 5 Oct 2026: money is set aside only by the authorization asked right before a model call. These keep every governed
// call site behind one, so a new call site fails here until it is reviewed and reserves its own cost.
const read = (path) => readFileSync(path, "utf8");
const api = "server/Scmos.Api";

function sources(dir) {
  return readdirSync(dir).flatMap((name) => {
    const path = join(dir, name);
    if (name === "bin" || name === "obj") return [];
    return statSync(path).isDirectory() ? sources(path) : path.endsWith(".cs") ? [path.replaceAll("\\", "/")] : [];
  });
}

test("every governed provider call site is a reviewed one", () => {
  const callers = sources(api).filter((path) => read(path).includes("provider.CompleteAsync(")).sort();
  assert.deepEqual(callers, [
    `${api}/Ai/AgentOrchestrator.cs`, // the development mock only; a live run never reaches it
    `${api}/Ai/Communication/CommunicationAgent.cs`,
    `${api}/Ai/Data/DataAgent.cs`,
    `${api}/Ai/Documents/DocumentAgent.cs`,
    `${api}/Ai/Engineering/EngineeringAgent.cs`,
    `${api}/Ai/Management/ManagementAgent.cs`,
    `${api}/Ai/Operations/OperationsAgent.cs`,
    `${api}/Ai/Sre/SreAgent.cs`,
    `${api}/Services/BillingAiService.cs`,
    `${api}/Services/EvaluationSummaryService.cs`,
  ]);
});

test("a chat run reserves for its first call, and Engineering for each further one", () => {
  const registry = read(`${api}/Ai/ToolRegistry.cs`);
  const run = registry.slice(registry.indexOf("public Task<AiAuthorizationDecision> AuthorizeRunAsync"), registry.indexOf("public Task<AiAuthorizationDecision> ReserveModelCallAsync"));
  assert.match(run, /with \{ ModelCall = true \}/);
  for (const agent of ["Communication/CommunicationAgent", "Data/DataAgent", "Documents/DocumentAgent", "Engineering/EngineeringAgent",
    "Management/ManagementAgent", "Operations/OperationsAgent", "Sre/SreAgent"]) {
    const source = read(`${api}/Ai/${agent}.cs`);
    assert.ok(source.indexOf("tools.AuthorizeRunAsync(") > 0 && source.indexOf("tools.AuthorizeRunAsync(") < source.indexOf("provider.CompleteAsync("), agent);
    assert.equal((source.match(/provider\.CompleteAsync\(/g) ?? []).length, 1, agent);
  }
  const engineering = read(`${api}/Ai/Engineering/EngineeringAgent.cs`);
  const loop = engineering.slice(engineering.indexOf("for (var round = 0;"), engineering.indexOf("provider.CompleteAsync("));
  assert.match(loop, /if \(round > 0\)\s*\{[\s\S]*tools\.ReserveModelCallAsync\(/);
});

test("single-call paths reserve on their own authorization, and the mail pass for each mail", () => {
  for (const path of ["Ai/Booking/BookingDraftService.cs", "Ai/Documents/ExtractionRun.cs", "Services/EvaluationSummaryService.cs"])
    assert.match(read(`${api}/${path}`), /with \{ ModelCall = true \}/, path);
  assert.match(read(`${api}/Services/BillingAiService.cs`), /with \{ ModelCall = !BillingAiKinds\.IsDocument\(kind\) \}/);
  const mail = read(`${api}/Ai/Booking/BookingMailPass.cs`);
  const each = mail.slice(mail.indexOf("foreach (var mail in batch)"));
  assert.ok(each.indexOf("ModelCall = true") > 0 && each.indexOf("ModelCall = true") < each.indexOf("await ReadAsync(mail.Id"));
});

test("only a model call sets money aside; every allowed authorization still counts", () => {
  const writer = read(`${api}/Ai/SqlAiExecutionAudit.cs`);
  assert.match(writer, /else if \(decision\.Allowed && entry\.Request\.ModelCall\) cost = budget\.MaxReservationCost;/);
  assert.match(writer, /x\.Decision == allow, ct\)/);
  assert.match(writer, /modelCall = request\.ModelCall/);
});
