import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { CITATION, SUMMARY_PARTS, citedLine } from "../app/scmos/annualEvaluation.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("a summary line splits into its words and the facts it cites", () => {
  assert.deepEqual(citedLine("OTD ฝั่งผู้ขนส่ง 97.1% [F1]"), { text: "OTD ฝั่งผู้ขนส่ง 97.1%", cites: ["F1"] });
  assert.deepEqual(citedLine("เคลม 2 ครั้ง [F2][F5] และ [F2, F7]"), { text: "เคลม 2 ครั้ง และ", cites: ["F2", "F5", "F7"] });
  assert.deepEqual(citedLine("ไม่มีการอ้างอิง F3"), { text: "ไม่มีการอ้างอิง F3", cites: [] });
});

test("the screen reads citations with the API's own pattern", () => {
  // The API keeps a line only when it cites; the screen must find the same citations in it.
  const rules = read("server/Scmos.Api/Rules/EvaluationSummary.cs");
  assert.ok(rules.includes(`[GeneratedRegex(@"${CITATION.source}")]`));
});

test("the four parts are the service's four fields", () => {
  const service = read("server/Scmos.Api/Services/EvaluationSummaryService.cs");
  for (const part of SUMMARY_PARTS) assert.match(service, new RegExp(`new AiArgument\\("${part.key}"`));
});

test("the summary sits in the carrier panel and never writes a score or a decision", () => {
  const panel = read("app/scmos/screens/EvaluationCarrierPanel.tsx");
  assert.match(panel, /<EvaluationAiSummary /);
  const screen = read("app/scmos/screens/EvaluationAiSummary.tsx");
  assert.match(screen, /\/ai-summary`, \{ method: "POST"/);
  assert.doesNotMatch(screen, /manual-score|\/decision|\/calculate/);
  // Shown only once there is a summary or the account may make one; the button only for the latter.
  assert.match(screen, /!state\.latest && !state\.availability\.canRun\)\) return null/);
  assert.match(screen, /state\.availability\.canRun && \(/);
});

test("the tool is in the AI history's vocabulary, as in the API's", () => {
  assert.match(read("app/scmos/aiHistory.ts"), /"summarize_evaluation"/);
  assert.match(read("server/Scmos.Api/Ai/AiAuditRules.cs"), /"summarize_evaluation" => "annual_evaluation"/);
});
