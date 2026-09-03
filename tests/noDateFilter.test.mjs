import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  ALL_PERIOD, NO_DATE, inChosenPeriod, inPeriod, periodLabel, periodOptions,
} from "../app/scmos/period.ts";

const job = (date) => ({ date });

/**
 * Looking at the jobs whose date will not parse.
 *
 * The bar has always counted them — "วันที่ใช้ไม่ได้ 28" — and there was no way
 * to list them, which is backwards: those are the ones somebody has to go and
 * fix, and choosing any year, month or day is precisely what hides them.
 */

test("asking for no date returns only the jobs that have none", () => {
  const jobs = [job("01/07/2026"), job(""), job("WAIT"), job("31/12/2026"), job("—")];
  const found = jobs.filter((one) => inPeriod(one, { ...ALL_PERIOD, year: NO_DATE }));

  assert.equal(found.length, 3, "empty, WAIT and a dash all fail to parse");
  assert.deepEqual(found.map((one) => one.date), ["", "WAIT", "—"]);
});

test("a month left over from a previous choice cannot narrow it to nothing", () => {
  // The pickers put the month away when this is chosen, but a saved view or a
  // URL can still carry one. It must not silently empty the list.
  const undated = job("WAIT");
  assert.ok(inPeriod(undated, { year: NO_DATE, month: "07", day: "15" }));
});

test("the full register keeps the undated rows returned by the API page", () => {
  const period = { year: NO_DATE, month: "ALL", day: "ALL" };
  const rows = ["01/07/2026", "WAIT", "", "—"].filter((date) =>
    inChosenPeriod(date, period));

  assert.deepEqual(rows, ["WAIT", "", "—"]);
});

test("no date may be selected beside a real year in the any-of picker", () => {
  const period = { year: `${NO_DATE}|2026`, month: "ALL", day: "ALL" };
  assert.ok(inChosenPeriod("WAIT", period));
  assert.ok(inChosenPeriod("01/07/2026", period));
  assert.equal(inChosenPeriod("01/07/2025", period), false);
});

test("day filters accept the period bar and Workspace value shapes", () => {
  assert.ok(inPeriod(job("15/07/2026"), {
    year: "2026", month: "07", day: "15",
  }));
  assert.ok(inChosenPeriod("15/07/2026", {
    year: "2026", month: "07", day: "15/07/2026",
  }));
  assert.equal(inChosenPeriod("16/07/2026", {
    year: "2026", month: "07", day: "15/07/2026",
  }), false);
});

test("a dated job is never in the no-date period", () => {
  for (const date of ["01/07/2026", "31/12/2025", "15/01/2026"]) {
    assert.equal(inPeriod(job(date), { ...ALL_PERIOD, year: NO_DATE }), false, date);
  }
});

test("choosing a real year still excludes the undated, as it always did", () => {
  // The point of the new option is that these jobs are reachable, not that they
  // start appearing inside months nobody can place them in.
  assert.equal(inPeriod(job("WAIT"), { ...ALL_PERIOD, year: "2026" }), false);
  assert.equal(inPeriod(job(""), { year: "2026", month: "07", day: "ALL" }), false);
});

test("no period chosen still shows everything, dated or not", () => {
  assert.ok(inPeriod(job("WAIT"), ALL_PERIOD));
  assert.ok(inPeriod(job("01/07/2026"), ALL_PERIOD));
});

test("the period says in words which one it is", () => {
  assert.equal(periodLabel({ ...ALL_PERIOD, year: NO_DATE }), "ไม่มีวันที่");
  assert.equal(periodLabel(ALL_PERIOD), "ทั้งแผน");
});

test("the option is only worth offering when something is undated", () => {
  const none = periodOptions([job("01/07/2026")], ALL_PERIOD);
  assert.equal(none.undated, 0);

  const some = periodOptions([job("01/07/2026"), job("WAIT"), job("")], ALL_PERIOD);
  assert.equal(some.undated, 2);
  // And an unparseable date is not offered as a year to pick.
  assert.deepEqual(some.years, ["2026"]);
});

test("the screen and the API use the same word for it", () => {
  // The workspace grid is paged server-side, so the same filter is written
  // twice — once here and once in C#. Every rule this codebase has written
  // twice has drifted; this is what stops that one.
  // The word lives in AnyOfFilter now, which is what the rate sheet's own bar
  // reads too — the rule was extracted when a second screen wanted it, which
  // is the moment it would otherwise have been written a third time.
  const rules = readFileSync("server/Scmos.Api/Rules/AnyOfFilter.cs", "utf8");
  const found = /public const string NoDate = "([^"]+)";/.exec(rules);

  assert.ok(found, "AnyOfFilter should name the no-date value");
  assert.equal(found[1], NO_DATE,
    `the API says "${found?.[1]}" where the screen says "${NO_DATE}"`);

  // And the workspace must still be reading it from there rather than holding
  // a second spelling of its own.
  const service = readFileSync("server/Scmos.Api/Services/WorkspaceService.cs", "utf8");
  assert.match(service, /public const string NoDate = AnyOfFilter\.NoDate;/,
    "WorkspaceService must take the word from AnyOfFilter, not restate it");
  assert.match(service, /MatchesPeriod\([^)]*\) =>\s+AnyOfFilter\.InPeriod\(/,
    "the workspace must run the shared period rule rather than its own copy");

  const workspace = readFileSync("app/scmos/screens/Workspace.tsx", "utf8");
  assert.match(workspace, /catBase\.filter\(\(j\) => inChosenPeriod\(j\.date/,
    "the full register must reuse the tested no-date rule instead of filtering WAIT out again");
});
