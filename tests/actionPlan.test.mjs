import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { MOVES, NO_FILTERS, STATUS, filterQuery, shownStatus } from "../app/scmos/actionPlan.ts";
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
