import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { filterPeriod } from "../app/scmos/period.ts";

const source = readFileSync(new URL("../app/scmos/dashboardFilters.ts", import.meta.url), "utf8")
  .replace('"./filterChoices"', JSON.stringify(new URL("../app/scmos/filterChoices.ts", import.meta.url).href));
const { filterDashboardJobs, dashboardOptions, ALL_DASHBOARD_FILTERS } = await import(
  `data:text/javascript;base64,${Buffer.from(stripTypeScriptTypes(source)).toString("base64")}`);
const jobs = [
  { key: 1, customer: "A", trucker: "X", date: "01/09/2026" },
  { key: 2, customer: "A", trucker: "Y", date: "02/09/2026" },
  { key: 3, customer: "B", trucker: "X", date: "01/09/2026" },
  { key: 4, customer: "C", trucker: "Z", date: "WAIT" },
  { key: 5, customer: "", trucker: "", date: "WAIT" },
];
test("ALL preserves the full register, including blank dimensions", () => {
  assert.deepEqual(filterDashboardJobs(jobs, ALL_DASHBOARD_FILTERS), jobs);
});
test("multiple choices are OR within and AND across dimensions", () => {
  assert.deepEqual(filterDashboardJobs(jobs, { customer: "A|B", trucker: "X" }).map(j => j.key), [1, 3]);
  assert.deepEqual(filterDashboardJobs(jobs, { customer: "A", trucker: "X|Y" }).map(j => j.key), [1, 2]);
});
test("date and dimension filters compose without mutating the source", () => {
  const before = structuredClone(jobs);
  const filtered = filterDashboardJobs(filterPeriod(jobs, { year: "2026", month: "09", day: "01" }),
    { customer: "A", trucker: "ALL" });
  assert.deepEqual(filtered.map(j => j.key), [1]);
  assert.deepEqual(jobs, before);
});
test("unmatched choices are empty and resetting restores all jobs", () => {
  assert.equal(filterDashboardJobs(jobs, { customer: "A", trucker: "Z" }).length, 0);
  assert.equal(filterDashboardJobs(jobs, ALL_DASHBOARD_FILTERS).length, 5);
});
test("both tabs and Excel use the same filtered data, drill preserves dimensions", () => {
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.match(app, /jobs=\{dashboardJobs\}/);
  assert.match(app, /function handleDashboardExport\(\)[\s\S]*?const jobs = dashboardJobs;/);
  assert.match(app, /cust: dashboardFilters.customer, trucker: dashboardFilters.trucker/);
  const dashboard = readFileSync(new URL("../app/scmos/screens/Dashboard.tsx", import.meta.url), "utf8");
  assert.match(dashboard, /FilterPickMany label="CUSTOMER"/);
  assert.match(dashboard, /FilterPickMany label="TRUCKER"/);
  assert.match(dashboard, /ไม่ได้กรองตาม CUSTOMER \/ TRUCKER/);
});

/* --------------------------------------- one picker narrows the other */

test("choosing a customer offers only the hauliers who carry for them", () => {
  // A has X and Y; B has X only. The point is that picking A and then Z — a
  // haulier who has never carried for A — is not a thing the screen offers.
  assert.deepEqual(dashboardOptions(jobs, "trucker", { customer: "A", trucker: "ALL" }), ["X", "Y"]);
  assert.deepEqual(dashboardOptions(jobs, "trucker", { customer: "B", trucker: "ALL" }), ["X"]);
});

test("and choosing a haulier offers only the customers they carry for", () => {
  assert.deepEqual(dashboardOptions(jobs, "customer", { customer: "ALL", trucker: "X" }), ["A", "B"]);
  assert.deepEqual(dashboardOptions(jobs, "customer", { customer: "ALL", trucker: "Z" }), ["C"]);
});

test("with nothing chosen, both offer everything the register holds", () => {
  assert.deepEqual(dashboardOptions(jobs, "customer", ALL_DASHBOARD_FILTERS), ["A", "B", "C"]);
  assert.deepEqual(dashboardOptions(jobs, "trucker", ALL_DASHBOARD_FILTERS), ["X", "Y", "Z"]);
});

test("a picker never narrows itself, or you could not widen your own choice", () => {
  // Having ticked A, B and C must still be on offer — otherwise adding a second
  // customer would be impossible without first clearing the first.
  assert.deepEqual(dashboardOptions(jobs, "customer", { customer: "A", trucker: "ALL" }), ["A", "B", "C"]);
});

test("what is already ticked stays on the list even when the other filter hides it", () => {
  // Z carries for C only. With C chosen and then the customer changed to A, Z
  // is no longer among A's hauliers — and must remain visible, or unticking it
  // would mean first undoing the filter that hid it.
  const stuck = dashboardOptions(jobs, "trucker", { customer: "A", trucker: "Z" });
  assert.ok(stuck.includes("Z"), "the ticked haulier vanished from its own picker");
  assert.deepEqual(stuck, ["X", "Y", "Z"]);
});

test("a blank dimension is not offered as a value to filter on", () => {
  // Job 5 has neither. "" in a picker is a row nobody can act on.
  assert.ok(!dashboardOptions(jobs, "customer", ALL_DASHBOARD_FILTERS).includes(""));
  assert.ok(!dashboardOptions(jobs, "trucker", ALL_DASHBOARD_FILTERS).includes(""));
});

test("the list is sorted, so a picker does not reorder as choices change", () => {
  const shuffled = [...jobs].reverse();
  assert.deepEqual(dashboardOptions(shuffled, "customer", ALL_DASHBOARD_FILTERS), ["A", "B", "C"]);
});
