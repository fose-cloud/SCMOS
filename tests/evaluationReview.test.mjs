import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DECISION_NOTE_MINIMUM, decisionLabel, needsDecisionNote, scoreState } from "../app/scmos/annualEvaluation.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("the screen asks for a decision's reason exactly when the API will", () => {
  const rules = read("server/Scmos.Api/Rules/EvaluationReview.cs");
  assert.match(rules, /NeedsNote\(string decision, string previous\) => decision != Continue \|\| \(previous\.Length > 0 && previous != decision\);/);
  assert.match(rules, /public const string Continue = "continue";/);
  assert.equal(Number(rules.match(/public const int MinimumNote = (\d+);/)[1]), DECISION_NOTE_MINIMUM);
  // The same truth table the C# expression gives.
  const csharp = (decision, previous) => decision !== "continue" || (previous.length > 0 && previous !== decision);
  const codes = ["continue", "continue-with-improvement-plan", "corrective-action-required", "management-review-required", "suspend-new-allocation", "inactive"];
  for (const decision of codes) for (const previous of ["", ...codes]) assert.equal(needsDecisionNote(decision, previous), csharp(decision, previous), `${decision} after ${previous}`);
});

test("the decisions and their words are the API's; the screens hold none of their own", () => {
  const rules = read("server/Scmos.Api/Rules/EvaluationReview.cs");
  const listed = [...rules.matchAll(/\((Continue|ContinueWithPlan|CorrectiveAction|ManagementReview|SuspendNewAllocation|Inactive), "([^"]+)"\)/g)];
  assert.equal(listed.length, 6);
  for (const file of ["app/scmos/screens/EvaluationDecisions.tsx", "app/scmos/screens/EvaluationBoard.tsx", "app/scmos/annualEvaluation.ts"]) {
    const source = read(file);
    for (const [, , label] of listed) assert.ok(!source.includes(label), `${file} repeats "${label}"`);
  }
  const decisions = [{ code: "inactive", label: "เลิกใช้งาน" }];
  assert.equal(decisionLabel(decisions, "inactive"), "เลิกใช้งาน");
  assert.equal(decisionLabel(decisions, ""), "—");
  assert.equal(decisionLabel(decisions, "unknown"), "unknown");
});

test("a score is not calculated, out of date, short of data or calculated — out of date wins over the rest", () => {
  assert.equal(scoreState({ version: 0, status: "", stale: null }), "not-calculated");
  assert.equal(scoreState({ version: 2, status: "calculated", stale: ["มีคำตอบจากผู้ประเมินหลังคำนวณ"] }), "stale");
  assert.equal(scoreState({ version: 2, status: "insufficient-data", stale: ["x"] }), "stale");
  assert.equal(scoreState({ version: 1, status: "insufficient-data", stale: [] }), "insufficient");
  assert.equal(scoreState({ version: 1, status: "calculated", stale: null }), "calculated");
});

test("the campaign screen reads the summary, records decisions through the API, and asks before finalizing", () => {
  const screen = read("app/scmos/screens/AnnualEvaluation.tsx");
  assert.match(screen, /read\("\/summary"\)/);
  assert.match(screen, /send\(`\/carriers\/\$\{carrier\}\/decision`, "PUT", \{ decision, note \}\)/);
  assert.match(screen, /to === "finalized" && !window\.confirm\(/);
  assert.match(screen, /tab === "results" && <EvaluationBoard /);
  assert.match(screen, /tab === "review" && \(/);
  assert.match(screen, /editable=\{\(manage \|\| view\.canDecide\) && campaign\.status === "under-review"\}/);
  const endpoints = read("server/Scmos.Api/Endpoints/AnnualEvaluationEndpoints.cs");
  assert.match(endpoints, /MapGet\("\/\{id:int\}\/summary"/);
  assert.match(endpoints, /MapPut\("\/\{id:int\}\/carriers\/\{carrier:int\}\/decision"/);
});

test("the board filters by carrier, department, score state, band and answers", () => {
  const board = read("app/scmos/screens/EvaluationBoard.tsx");
  for (const label of ['aria-label="ค้นหาผู้ขนส่ง"', 'aria-label="แผนก"', 'aria-label="สถานะคะแนน"', 'aria-label="ช่วงคะแนน"', 'aria-label="การตอบ"'])
    assert.ok(board.includes(label), label);
  for (const state of ['"calculated"', '"stale"', '"insufficient"', '"not-calculated"']) assert.ok(board.includes(`value=${state}`), state);
});
