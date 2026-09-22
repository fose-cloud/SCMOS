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
  // 22 Sep 2026: the six measured cards follow the pickers too — the engine is asked with them,
  // and the line that said they were not filtered is gone.
  assert.doesNotMatch(dashboard, /ไม่ได้กรองตาม CUSTOMER \/ TRUCKER/);
  assert.match(dashboard, /filters=\{filters\}/);
  const tower = readFileSync(new URL("../app/scmos/screens/ControlTower.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(tower, /ไม่ได้กรองตาม CUSTOMER \/ TRUCKER/);
  assert.match(tower, /if \(filters\.customer && filters\.customer !== "ALL"\) query\.set\("customer", filters\.customer\);/);
  assert.match(tower, /if \(filters\.trucker && filters\.trucker !== "ALL"\) query\.set\("trucker", filters\.trucker\);/);
  assert.match(tower, /useReport\(period, p\.filters\)/);
  const endpoint = readFileSync(new URL("../server/Scmos.Api/Endpoints/KpiEndpoints.cs", import.meta.url), "utf8");
  assert.match(endpoint, /KpiScope\.Parse\(customer, trucker\)/);
});

test("the tile beside the OTD gauge is the late arrivals of the measured base, not the delay-record count", () => {
  // 22 Sep 2026: "Delayed 0" sat under a 45% gauge — it was the Delay measure (logged delay records and
  // held statuses). The gauge's own measure now names its two halves, and the tile reads the late one.
  const tower = readFileSync(new URL("../app/scmos/screens/ControlTower.tsx", import.meta.url), "utf8");
  assert.match(tower, /otd\?\.breakdown\?\.find\(\(b\) => b\.label === "ถึงช้ากว่าแผน"\)\?\.value \?\? null/);
  assert.match(tower, /\["ถึงช้ากว่าแผน", lateArrivals === null \? "—" : nf\(lateArrivals\), "Late Arrivals"/);
  assert.doesNotMatch(tower, /const delay = measure\("Delay"\);/);
  const engine = readFileSync(new URL("../server/Scmos.Api/Services/KpiEngine.cs", import.meta.url), "utf8");
  assert.match(engine, /public const string LateArrivalLabel = "ถึงช้ากว่าแผน";/);
  assert.match(engine, /\[new Counted\(OnTimeLabel, met\), new Counted\(LateArrivalLabel, measurable\.Count - met\)\]/);
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
