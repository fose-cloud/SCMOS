import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

// The SQL checks that prove these run against LocalDB, not in CI; these keep the shape in CI.
const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const logCalls = (source) => [...source.matchAll(/log\??\.Log\w+\(([\s\S]*?)\);/g)].map((match) => match[1]);

test("no evaluation log line is given the token, its hash or a request header", () => {
  const files = ["ExternalEvaluationService", "EvaluationSnapshotService", "EvaluationScoringService", "EvaluationSummaryService", "LegacyEvaluationService"];
  for (const name of files) {
    const calls = logCalls(read(`server/Scmos.Api/Services/${name}.cs`));
    assert.ok(calls.length > 0, `${name} logs its failures`);
    for (const call of calls) assert.doesNotMatch(call, /\btoken\b|TokenHash|HashOf|Headers/, `${name}: ${call}`);
  }
  const limit = read("server/Scmos.Api/Program.cs").match(/CreateLogger\("Scmos\.Api\.ExternalEvaluation"\)[\s\S]*?;/)[0];
  assert.doesNotMatch(limit, /Headers|token/i);
});

test("a snapshot, a calculation and a submitted sheet are each written in one transaction", () => {
  for (const name of ["EvaluationSnapshotService", "EvaluationScoringService", "ExternalEvaluationService"])
    assert.match(read(`server/Scmos.Api/Services/${name}.cs`), /EvaluationWrites\.InOneAsync\(db,/, name);
  const writes = read("server/Scmos.Api/Services/EvaluationWrites.cs");
  assert.match(writes, /CreateExecutionStrategy\(\)\.ExecuteAsync/);
  assert.match(writes, /BeginTransactionAsync/);
  // Nothing a failed attempt added may be written by the next save — the audit row's.
  assert.match(writes, /catch\s*\{\s*db\.ChangeTracker\.Clear\(\);\s*throw;/);
});
