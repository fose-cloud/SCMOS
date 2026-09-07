import assert from "node:assert/strict";
import test from "node:test";

import {
  averageFor, averages, daysInMonth, describe, expand, monthKey, monthOf, rateForJob,
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

/* ------------------------------------ changes expanded into daily prices */

test("the team's own May'25 sheet reproduces to 36.05", () => {
  // Their sheet: 37.5 for the 1st to the 7th, 34.94 for the 8th to the 21st,
  // 35.79 on the 22nd, 36.69 from the 23rd. The green cell says 36.05.
  //
  // The 37.5 was set in April — a month opens at the price the month before
  // left it at, which is what makes the backward look necessary.
  const changes = [
    { date: "28/04/2025", price: 37.5 },
    { date: "08/05/2025", price: 34.94 },
    { date: "22/05/2025", price: 35.79 },
    { date: "23/05/2025", price: 36.69 },
  ];
  const days = expand(changes, "05/2025");
  assert.equal(days.length, 31);
  assert.equal(days[0].price, 37.5);
  assert.equal(days[6].price, 37.5);
  assert.equal(days[7].price, 34.94);
  assert.equal(days[20].price, 34.94);
  assert.equal(days[21].price, 35.79);
  assert.equal(days[22].price, 36.69);
  assert.equal(days[30].price, 36.69);

  const got = averageFor(days, "05/2025");
  assert.equal(got.average, 36.05, "the figure in the green cell");
  assert.equal(got.closed, true);
});

test("a price holds until the next change, however far away that is", () => {
  // One change all month is one price all month, not one day of it.
  const days = expand([{ date: "01/08/2026", price: 39.14 }], "08/2026");
  assert.equal(days.length, 31);
  assert.ok(days.every((day) => day.price === 39.14));
});

test("a month opens at the price the month before left it at", () => {
  // July's changes were the 3rd, 8th, 22nd and 23rd. August opens at the 23rd
  // of July's price and holds it until August's first change.
  const days = expand([{ date: "23/07/2026", price: 36.69 }], "08/2026");
  assert.equal(days[0].price, 36.69);
  assert.equal(days.length, 31);
});

test("a month before any recorded price is empty, not guessed backwards", () => {
  // Borrowing the next change backwards would invent a price for days nobody
  // published one for.
  assert.deepEqual(expand([{ date: "08/05/2025", price: 34.94 }], "03/2025"), []);
  assert.deepEqual(expand([], "05/2025"), []);
});

test("a month whose prices start mid-way is not closed", () => {
  // The first change lands on the 8th and nothing before it is known, so seven
  // days are missing and the average is not the month's.
  const days = expand([{ date: "08/05/2025", price: 34.94 }], "05/2025");
  assert.equal(days.length, 24);
  assert.equal(averageFor(days, "05/2025").closed, false);
});

test("changes arrive in any order and are read in date order", () => {
  const jumbled = [
    { date: "23/05/2025", price: 36.69 },
    { date: "28/04/2025", price: 37.5 },
    { date: "08/05/2025", price: 34.94 },
    { date: "22/05/2025", price: 35.79 },
  ];
  assert.equal(averageFor(expand(jumbled, "05/2025"), "05/2025").average, 36.05);
});

test("a nonsense row is left out rather than averaged in", () => {
  const changes = [
    { date: "28/04/2025", price: 37.5 },
    { date: "not a date", price: 99 },
    { date: "08/05/2025", price: 0 },
    { date: "08/05/2025", price: 34.94 },
    { date: "22/05/2025", price: 35.79 },
    { date: "23/05/2025", price: 36.69 },
  ];
  assert.equal(averageFor(expand(changes, "05/2025"), "05/2025").average, 36.05);
});
