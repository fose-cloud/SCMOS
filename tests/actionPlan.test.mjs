import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { MOVES, NO_FILTERS, REFERENCE_KINDS, STATUS, filterQuery, shownStatus } from "../app/scmos/actionPlan.ts";
import { listenForActionPlanRequests, requestActionPlan } from "../app/scmos/actionPlanRequest.ts";
import { NAV, NAV_GROUPS } from "../app/scmos/nav.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("the list's filters go to the API as a query, empty ones left out", () => {
  assert.equal(filterQuery(NO_FILTERS), "");
  assert.equal(filterQuery({ ...NO_FILTERS, year: "2026", status: "overdue", query: " OTD " }), "?year=2026&status=overdue&query=OTD");
});

test("Overdue is shown when the API works it out, over whatever state the plan is in", () => {
  assert.equal(shownStatus({ status: "in-progress", overdue: true }), "overdue");
  assert.equal(shownStatus({ status: "in-progress", overdue: false }), "in-progress");
  for (const state of ["draft", "planned", "in-progress", "waiting", "pending-review", "completed", "overdue", "cancelled"]) assert.ok(STATUS[state], state);
});

test("no button completes a plan — only a review does", () => {
  const targets = Object.values(MOVES).flat().map((move) => move.to);
  assert.ok(!targets.includes("completed"));
  assert.ok(targets.includes("pending-review"));
  assert.deepEqual(MOVES["pending-review"], undefined);
});

test("Action Plan is its own entry in the menu, after KPI", () => {
  assert.ok(NAV.some(([key, label]) => key === "actionplan" && label === "Action Plan"));
  const quality = NAV_GROUPS.find((group) => group.label === "QUALITY & COMPLIANCE");
  assert.deepEqual(quality.keys.slice(0, 3), ["quality", "kpi", "actionplan"]);
  assert.match(read("app/SCMOSApp.tsx"), /\{screen === "actionplan" && <ActionPlan onToast=\{setToast\} \/>\}/);
});

test("the screens send plans, steps and evidence through the API's own routes", () => {
  const wizard = read("app/scmos/screens/ActionPlanWizard.tsx");
  assert.match(wizard, /apiFetch\("\/api\/action-plans", \{\s*method: "POST"/);
  const detail = read("app/scmos/screens/ActionPlanDetail.tsx");
  assert.match(detail, /body\.append\("actionPlanId", String\(detail\.plan\.id\)\);/);
  assert.match(detail, /send\("\/api\/documents", "POST", body\)/);
  assert.match(detail, /send\(`\$\{base\}\/review`, "POST"/);
  // The two chart colours are the dataviz reference palette's slots 1 and 2, validated against white.
  const parts = read("app/scmos/screens/ActionPlanParts.tsx");
  assert.match(parts, /SERIES_1 = "#2a78d6"/);
  assert.match(parts, /SERIES_2 = "#eb6834"/);
});

/* ---- Round two (1 Oct 2026): Skill Matrix, the bell, plans started from other screens ---- */

test("a request left before Action Plan opens is taken when it does; one sent while it is open arrives at once", () => {
  requestActionPlan({ open: 7 });
  const heard = [];
  const stop = listenForActionPlanRequests((request) => heard.push(request));
  assert.deepEqual(heard, [{ open: 7 }]);
  requestActionPlan({ create: { developmentType: "subcontractor", supplierId: 3 } });
  assert.deepEqual(heard[1], { create: { developmentType: "subcontractor", supplierId: 3 } });
  stop();
  requestActionPlan({ open: 9 });
  assert.equal(heard.length, 2);
  // Left for the next time Action Plan opens, not lost.
  const later = [];
  listenForActionPlanRequests((request) => later.push(request))();
  assert.deepEqual(later, [{ open: 9 }]);
});

test("the reference kinds the screens offer are the API's, both ways", () => {
  const service = read("server/Scmos.Api/Services/ActionPlanService.cs");
  const kinds = [...service.match(/ReferenceKinds = \[([^\]]+)\]/)[1].matchAll(/"([a-z]+)"/g)].map((one) => one[1]);
  assert.deepEqual(Object.keys(REFERENCE_KINDS).sort(), [...kinds].sort());
  for (const kind of ["skill", "supplier", "evaluation", "audit"]) assert.ok(kinds.includes(kind), kind);
});

test("Skill Matrix is Action Plan's third tab, reading and assessing through the API", () => {
  const screen = read("app/scmos/screens/ActionPlan.tsx");
  assert.match(screen, /\["skills", "Skill Matrix"\]/);
  assert.match(screen, /<SkillMatrix canPlan=\{meta\.canEdit\}/);
  assert.match(screen, /listenForActionPlanRequests\(/);
  const matrix = read("app/scmos/screens/SkillMatrix.tsx");
  assert.match(matrix, /apiFetch\("\/api\/action-plans\/skills", /);
  assert.match(matrix, /apiFetch\("\/api\/action-plans\/skills\/assess", /);
  assert.match(matrix, /\/api\/action-plans\/skills\/history\?employeeId=/);
  assert.match(matrix, /kind: "skill"/);
  // Levels 1–5 are one hue, light to dark: the dataviz blue ramp, checked with the validator's --ordinal mode.
  assert.match(matrix, /LEVEL_FILL = \["", "#86b6ef", "#5598e7", "#2a78d6", "#1c5cab", "#104281"\]/);
  const endpoints = read("server/Scmos.Api/Endpoints/ActionPlanEndpoints.cs");
  for (const route of ['"/skills"', '"/skills/history"', '"/skills/assess"']) assert.ok(endpoints.includes(route), route);
});

test("a plan started elsewhere arrives in the form filled in, and keeps where it came from", () => {
  const wizard = read("app/scmos/screens/ActionPlanWizard.tsx");
  assert.match(wizard, /prefill\?: PlanPrefill/);
  assert.match(wizard, /references: prefill\?\.references \?\? \[\]/);
  const app = read("app/SCMOSApp.tsx");
  // Only somebody who may write plans is offered the button.
  assert.match(app, /const startActionPlan = able\("EditActionPlans"\)/);
  assert.match(app, /requestActionPlan\(\{ create: prefill \}\); go\("actionplan"\);/);
  for (const screen of ["Suppliers", "AuditPlanning", "AnnualEvaluation"]) {
    assert.match(app, new RegExp(`<${screen} [^\\n]*onActionPlan=\\{startActionPlan\\}`), screen);
  }
  // An Action Plan alert opens the plan it names.
  assert.match(app, /target\.screen === "actionplan" && target\.jobKey[^\n]*requestActionPlan\(\{ open: Number\(target\.jobKey\) \}\)/);
  assert.match(read("app/scmos/screens/Suppliers.tsx"), /kind: "supplier"/);
  // The annual evaluation's plan comes from a carrier's result (1 Oct 2026) — the old evaluation screen it replaced did the same.
  assert.match(read("app/scmos/screens/EvaluationCarrierPanel.tsx"), /kind: "evaluation"/);
  assert.match(read("app/scmos/screens/AuditPlanning.tsx"), /kind: "audit"/);
});

test("the bell's Action Plan alerts open the Action Plan screen and are worked out for the person signed in", () => {
  const rules = read("server/Scmos.Api/Rules/Notifications.cs");
  for (const kind of ["ActionPlanAssigned", "ActionPlanDueSoon", "ActionPlanOverdue", "ActionPlanReviewWaiting", "ActionPlanReviewed"]) {
    assert.match(rules, new RegExp(`new\\(AlertKind\\.${kind},[^)]*"actionplan"\\)`), kind);
  }
  assert.match(read("server/Scmos.Api/Endpoints/DashboardEndpoints.cs"), /notifications\.BuildAsync\(scope, token, user\)/);
});
