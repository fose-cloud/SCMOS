import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { prefillOf } from "../app/scmos/annualEvaluation.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const draft = {
  developmentType: "subcontractor", targetType: "carrier", supplierId: 7, category: "Safety Improvement", title: "ALPHA — แผนพัฒนาจาก AE-2026",
  developmentArea: "Safety & Incident", currentLevel: "0.8", targetLevel: "0", gap: "• Safety: 0.8 (เป้า ≤ 0)", objective: "ใช้งานต่อ พร้อมแผนปรับปรุง",
  metric: "Safety & Incident", baseline: 0.8, targetValue: 0, priority: "high", items: [{ action: "ปรับปรุง Safety", expectedResult: "ถึงเป้า 0" }],
  references: [{ kind: "evaluation", refId: "AE-2026:3", label: "Annual Evaluation AE-2026 · ALPHA" }], findings: [],
};

test("the API's plan draft reaches the wizard whole — the findings stay behind, every field the wizard takes goes", () => {
  const prefill = prefillOf(draft);
  assert.equal(prefill.supplierId, 7);
  assert.equal(prefill.targetValue, 0);
  assert.deepEqual(prefill.items, draft.items);
  assert.deepEqual(prefill.references, draft.references);
  assert.equal("findings" in prefill, false);
  const wizard = read("app/scmos/screens/ActionPlanWizard.tsx");
  for (const field of ["gap", "objective", "metric", "baseline", "targetValue", "priority", "items", "category", "developmentArea", "currentLevel", "targetLevel", "title", "supplierId"])
    assert.match(wizard, new RegExp(`prefill\\?\\.${field}`), field);
  // A target of nought is a target: the wizard keeps it rather than dropping a falsy number.
  assert.match(wizard, /prefill\?\.targetValue != null/);
  assert.match(wizard, /prefill\?\.baseline != null/);
});

test("the plan's fields are the API's draft and the Action Plan module's input — the same names on both sides", () => {
  const service = read("server/Scmos.Api/Services/EvaluationPlanService.cs");
  const record = service.match(/public record PlanDraft\(([^)]+)\)/)[1];
  for (const key of Object.keys(draft)) assert.match(record, new RegExp(`\\b${key[0].toUpperCase()}${key.slice(1)}\\b`), key);
  const input = read("server/Scmos.Api/Services/ActionPlanService.cs").match(/public record ActionPlanInput\(([\s\S]+?)\);/)[1];
  for (const key of ["Gap", "Objective", "Metric", "Baseline", "TargetValue", "Priority", "Category", "DevelopmentArea", "CurrentLevel", "TargetLevel", "References"])
    assert.match(input, new RegExp(`\\b${key}\\b`), key);
});

test("the campaign asks the API for the draft and lists the plans by its route; an existing plan opens in Action Plan", () => {
  const screen = read("app/scmos/screens/AnnualEvaluation.tsx");
  assert.match(screen, /\/carriers\/\$\{carrier\}\/plan-draft`/);
  assert.match(screen, /read\("\/plans"\)/);
  assert.match(screen, /onActionPlan\(prefillOf\(draft\)\)/);
  const app = read("app/SCMOSApp.tsx");
  assert.match(app, /const openActionPlan = \(id: number\) => \{ requestActionPlan\(\{ open: id \}\); go\("actionplan"\); \};/);
  const endpoints = read("server/Scmos.Api/Endpoints/AnnualEvaluationEndpoints.cs");
  assert.match(endpoints, /MapGet\("\/\{id:int\}\/carriers\/\{carrier:int\}\/plan-draft"/);
  assert.match(endpoints, /MapGet\("\/\{id:int\}\/plans"/);
});

test("a plan's status is shown in Action Plan's own words, overdue included", () => {
  for (const file of ["app/scmos/screens/EvaluationDecisions.tsx", "app/scmos/screens/EvaluationCarrierPanel.tsx"]) {
    const source = read(file);
    assert.match(source, /import \{ STATUS as PLAN_STATUS, shownStatus \} from "\.\.\/actionPlan";/, file);
    assert.match(source, /PLAN_STATUS\[shownStatus\(plan\)\]/, file);
  }
});
