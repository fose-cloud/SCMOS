import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { ALL_PERIOD, NO_DATE, inChosenPeriod, periodOptions } from "../app/scmos/period.ts";

/*
 * 21 Sep 2026: Operational Issues filters by the day an issue was found —
 * year, month, day — with the same picker rule the dashboard and KPI apply
 * to the plan date. An issue is not a job, so the options are built from
 * dates, not jobs.
 */
const issues = [
  { code: "OTL-0030", foundOn: "14/09/2026" },
  { code: "OTL-0031", foundOn: "21/09/2026" },
  { code: "OTL-0032", foundOn: "21/09/2026" },
  { code: "OTL-0001", foundOn: "03/08/2026" },
  { code: "OTL-0002", foundOn: "" },
];
const dated = issues.map((issue) => ({ date: issue.foundOn }));

test("the period pickers are built from the issues' found dates, each level narrowed by the one above", () => {
  const all = periodOptions(dated, ALL_PERIOD);
  assert.deepEqual(all.years, ["2026"]);
  assert.deepEqual(all.months, ["08", "09"]);
  assert.equal(all.undated, 1);
  const september = periodOptions(dated, { year: "2026", month: "09", day: "ALL" });
  assert.deepEqual(september.days, ["14", "21"]);
});

test("choosing a day, a month or 'no date' keeps only the issues found then", () => {
  const found = (period) => issues.filter((issue) => inChosenPeriod(issue.foundOn, period)).map((issue) => issue.code);
  assert.deepEqual(found({ year: "2026", month: "09", day: "21" }), ["OTL-0031", "OTL-0032"]);
  assert.deepEqual(found({ year: "2026", month: "08", day: "ALL" }), ["OTL-0001"]);
  assert.deepEqual(found({ year: NO_DATE, month: "ALL", day: "ALL" }), ["OTL-0002"]);
  assert.equal(found(ALL_PERIOD).length, 5);
});

test("the screen filters by the found date before the search, offers the three pickers, and starts at page one on a change", () => {
  const source = readFileSync(new URL("../app/scmos/screens/OperationalIssues.tsx", import.meta.url), "utf8");
  assert.match(source, /inChosenPeriod\(issue\.foundOn, period\)/);
  assert.match(source, /periodOptions\(dated, period\)/);
  for (const label of ['label="ปีที่พบ"', 'label="เดือน"', 'label="วัน"']) assert.ok(source.includes(label), label);
  assert.match(source, /const filterPeriod = \(next: Period\) => \{ setPeriod\(next\); setPage\(1\); \};/);
  // A year resets the month and the day; a month resets the day.
  assert.match(source, /filterPeriod\(\{ year: v, month: "ALL", day: "ALL" \}\)/);
  assert.match(source, /filterPeriod\(\{ \.\.\.period, month: v, day: "ALL" \}\)/);
  // The export names the period it was narrowed to.
  assert.match(source, /exportIssues\(rows, scope\)/);
});
