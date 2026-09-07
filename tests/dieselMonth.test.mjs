import assert from "node:assert/strict";
import test from "node:test";

import {
  averageFor, averages, daysInMonth, describe, monthKey, monthOf, rateForJob,
} from "../app/scmos/dieselMonth.ts";

/*
 * The diesel rate is a month's average, not a day's price.
 *
 * Given by the account team on 2026-09-07: average the daily pump price over
 * the whole month, and that figure is the rate for that month.
 */

const august = (price) => Array.from({ length: 31 }, (_, i) => ({
  date: `${String(i + 1).padStart(2, "0")}/08/2026`,
  price: typeof price === "function" ? price(i) : price,
}));

test("a full month averages to one figure", () => {
  assert.deepEqual(averageFor(august(39.14), "08/2026"),
    { month: "08/2026", average: 39.14, days: 31, closed: true });
});

test("the average is the mean of the days, not the last of them", () => {
  // Half the month at 38, half at 40. A spot price on the 31st says 40.
  const days = august((i) => (i < 16 ? 38 : 40));
  const got = averageFor(days, "08/2026");
  assert.equal(got.average, Math.round(((38 * 16 + 40 * 15) / 31) * 100) / 100);
  assert.notEqual(got.average, 40);
});

test("a month still running is not closed, and says how many days it has", () => {
  // Fifteen days of August. The figure will move.
  const partial = august(39.14).slice(0, 15);
  const got = averageFor(partial, "08/2026");
  assert.equal(got.days, 15);
  assert.equal(got.closed, false);
  assert.equal(got.average, 39.14);
});

test("a month with a gap in the middle is not closed either", () => {
  // Thirty of thirty-one days is not a month's average, whatever the calendar
  // says about the date today.
  const holed = august(39.14).filter((_, i) => i !== 12);
  assert.equal(averageFor(holed, "08/2026").closed, false);
});

test("a month nobody has entered has no average, and that is not zero", () => {
  const got = averageFor([], "08/2026");
  assert.equal(got.average, null);
  assert.equal(got.days, 0);
  assert.equal(got.closed, false);
});

test("the average keeps two places, because the bands are quoted to two", () => {
  // Rounding 33.995 to a whole baht would cross 33.00–36.29 into the next step
  // of the fuel clause, which is a different price on every lane.
  const days = august((i) => (i === 0 ? 33.99 : 34.0)).slice(0, 2);
  assert.equal(averageFor(days, "08/2026").average, 34);
  const finer = [{ date: "01/08/2026", price: 33.99 }, { date: "02/08/2026", price: 33.98 }];
  assert.equal(averageFor(finer, "08/2026").average, 33.99);
});

test("a job is priced at the average of the month it ran in", () => {
  const days = [...august(39.14), { date: "01/09/2026", price: 45 }];
  assert.equal(rateForJob(days, "15/08/2026").average, 39.14);
  assert.equal(rateForJob(days, "01/09/2026").average, 45);
});

test("a job in a month with no prices is refused, not given the newest month", () => {
  // Falling back would bill July's work at September's diesel.
  const days = august(39.14);
  assert.equal(rateForJob(days, "15/07/2026"), null);
  assert.equal(rateForJob(days, ""), null);
  assert.equal(rateForJob(days, undefined), null);
});

test("December does not sort before February", () => {
  const days = [
    { date: "10/02/2026", price: 30 },
    { date: "10/12/2026", price: 40 },
    { date: "10/08/2026", price: 35 },
  ];
  assert.deepEqual(averages(days).map((one) => one.month), ["12/2026", "08/2026", "02/2026"]);
  assert.ok(monthKey("12/2026") > monthKey("02/2026"));
});

test("month length is right, leap years included", () => {
  assert.equal(daysInMonth("08/2026"), 31);
  assert.equal(daysInMonth("09/2026"), 30);
  assert.equal(daysInMonth("02/2026"), 28);
  assert.equal(daysInMonth("02/2028"), 29);
  assert.equal(daysInMonth("13/2026"), 0);
  assert.equal(daysInMonth(""), 0);
});

test("the month a date belongs to, and nothing else", () => {
  assert.equal(monthOf("15/08/2026"), "08/2026");
  assert.equal(monthOf("2026-08-15"), "");
  assert.equal(monthOf(""), "");
});

test("an unfinished month says so wherever it is shown", () => {
  // A figure that will move before the month ends must not read like one that
  // will not.
  const open = averageFor(august(39.14).slice(0, 10), "08/2026");
  assert.match(describe(open), /ยังไม่สิ้นเดือน/);

  const closed = averageFor(august(39.14), "08/2026");
  assert.ok(!/ยังไม่สิ้นเดือน/.test(describe(closed)));
  assert.match(describe(closed), /เฉลี่ยทั้งเดือน/);

  assert.match(describe(null), /ยังไม่มีราคาน้ำมัน/);
});
