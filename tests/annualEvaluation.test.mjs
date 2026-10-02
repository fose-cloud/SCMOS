import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { MOVE_LABEL, ORDER, STATUS, isBackward, shown } from "../app/scmos/annualEvaluation.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("the screen's states are the API's, in the API's order", () => {
  const rules = read("server/Scmos.Api/Rules/AnnualEvaluationRules.cs");
  const server = rules.match(/Statuses = \[([^\]]+)\]/)[1].split(",").map((name) => name.trim());
  const values = Object.fromEntries([...rules.matchAll(/public const string (\w+) = "([a-z-]+)";/g)].map((match) => [match[1], match[2]]));
  assert.deepEqual(ORDER, server.map((name) => values[name]));
  for (const status of ORDER) {
    assert.ok(STATUS[status], status);
    assert.ok(MOVE_LABEL[status], status);
  }
  assert.equal(isBackward("open", "draft"), true);
  assert.equal(isBackward("ready", "open"), false);
});

test("scores are shown to two places and a missing one as a dash, never a nought", () => {
  assert.equal(shown(87.5), "87.50");
  assert.equal(shown(null), "—");
  assert.equal(shown(undefined), "—");
  assert.equal(shown(0), "0.00");
});

test("Annual Evaluation replaces the old screen, and reads and writes only through the API's routes", () => {
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /\{screen === "evaluation" && <AnnualEvaluation canManage=\{able\("ManageAnnualEvaluation"\)\} onToast=\{setToast\} onActionPlan=\{startActionPlan\}\s+onOpenPlan=\{openActionPlan\} \/>\}/);
  assert.doesNotMatch(app, /<Evaluation /);
  const screen = read("app/scmos/screens/AnnualEvaluation.tsx");
  for (const route of ['"/status"', '"/generate-snapshot"', '"/calculate"', '"/carriers"', '"/carriers/count"']) assert.ok(screen.includes(route), route);
  assert.match(screen, /apiFetch\("\/api\/annual-evaluations", \{\s*method: "POST"/);
  const setup = read("app/scmos/screens/AnnualEvaluationSetup.tsx");
  for (const route of ['onSave("", {', 'onSave("/kpis"', 'onSave("/score-bands"', 'onSave("/departments"', 'onSave("/questions"']) assert.ok(setup.includes(route), route);
  const panel = read("app/scmos/screens/EvaluationCarrierPanel.tsx");
  assert.match(panel, /\$\{base\}\/result/);
  assert.match(panel, /\$\{base\}\/snapshot/);
  assert.match(panel, /\$\{base\}\/manual-score/);
  const endpoints = read("server/Scmos.Api/Endpoints/AnnualEvaluationEndpoints.cs");
  for (const route of ['"/{id:int}/generate-snapshot"', '"/{id:int}/calculate"', '"/{id:int}/results"', '"/{id:int}/carriers/{carrier:int}/result"',
    '"/{id:int}/carriers/{carrier:int}/snapshot"', '"/{id:int}/carriers/{carrier:int}/manual-score"']) assert.ok(endpoints.includes(route), route);
});

test("a step back asks why before it is sent, as the API requires", () => {
  const screen = read("app/scmos/screens/AnnualEvaluation.tsx");
  assert.match(screen, /reasonFor\(isBackward\(campaign\.status, to\)/);
  assert.match(screen, /reasonFor\(view!\.locked, /);
});
