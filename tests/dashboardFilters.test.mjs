import assert from "node:assert/strict";
import test from "node:test";
import { readFileSync } from "node:fs";
import { stripTypeScriptTypes } from "node:module";
import { filterPeriod } from "../app/scmos/period.ts";

const source = readFileSync(new URL("../app/scmos/dashboardFilters.ts", import.meta.url), "utf8")
  .replace('"./filterChoices"', JSON.stringify(new URL("../app/scmos/filterChoices.ts", import.meta.url).href));
const { filterDashboardJobs, ALL_DASHBOARD_FILTERS } = await import(
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
