import assert from "node:assert/strict";
import test from "node:test";

import { BLANK, bucket, busiest, byField, byPeriod, weekStart } from "../app/scmos/volumeReport.ts";

const job = (date, cat = "IMPORT", extra = {}) => ({ date, cat, status: "WAITING_SUPPLIER", ...extra });

// Stands in for `isCancelled`, which lives in ops and is ops' to test. What is
// checked here is that a job the rule rejects stays out of the volume and is
// counted where somebody can see it — not how cancellation is recognised.
const scope = { cancelledRule: (j) => /cancel/i.test(j.status ?? "") };

test("a week is named by the Monday it starts on", () => {
  // 05/08/2026 is a Wednesday; 03/08 is its Monday.
  assert.deepEqual(weekStart("05/08/2026"), { d: 3, m: 8, y: 2026 });
  assert.deepEqual(weekStart("03/08/2026"), { d: 3, m: 8, y: 2026 });
  // Sunday belongs to the week that began six days earlier, not a new one.
  assert.deepEqual(weekStart("09/08/2026"), { d: 3, m: 8, y: 2026 });
  assert.deepEqual(weekStart("10/08/2026"), { d: 10, m: 8, y: 2026 });
});

test("a week spanning a month end stays one week", () => {
  // 31/08/2026 is a Monday, so 02/09 is in the week that started in August.
  const a = bucket("31/08/2026", "week");
  const b = bucket("02/09/2026", "week");
  assert.equal(a.order, b.order, "one week, one row");
  assert.match(a.label, /31\/08\/2026/);
});

test("a date shaped right but not real is not a date", () => {
  // All match dd/MM/yyyy and none is a day anybody planned for. Guessing would
  // put trips in a month they did not happen in.
  for (const bad of ["31/02/2026", "00/07/2026", "07/13/2026", "soon", "", undefined]) {
    assert.equal(bucket(bad, "day"), null, `${bad} should not bucket`);
  }
});

test("cancelled work is counted apart, not as volume", () => {
  const out = byPeriod([
    job("01/07/2026"), job("01/07/2026"),
    { ...job("01/07/2026"), status: "CANCELLED" },
  ], "day", scope);

  assert.equal(out.counted, 2, "a booked job that did not run is not volume");
  assert.equal(out.cancelled, 1);
  assert.equal(out.rows[0].total, 2);
});

test("an unreadable date is reported rather than dropped or guessed", () => {
  const out = byPeriod([job("01/07/2026"), job("soon")], "month", scope);
  assert.equal(out.counted, 1);
  assert.equal(out.undated, 1);
  // The rows and the reported total agree; the missing one is named separately.
  assert.equal(out.rows.reduce((sum, r) => sum + r.total, 0), out.counted);
});

test("the split follows the register rather than an assumed two", () => {
  // Counts deliberately unequal. The first version of this had EXPORT and
  // DELIVERY tied at one, so it was testing the alphabetical tiebreak while
  // claiming to test the ordering — and it would have passed whatever the
  // ordering rule was.
  const out = byPeriod([
    job("01/07/2026", "IMPORT"), job("02/07/2026", "IMPORT"), job("03/07/2026", "IMPORT"),
    job("01/07/2026", "EXPORT"), job("02/07/2026", "EXPORT"),
    job("01/07/2026", "DELIVERY"),
  ], "day", scope);

  // Domestic is worked under The Chemours; a "total" that silently left it out
  // would be wrong in exactly the way nobody checks.
  assert.deepEqual(out.cats, ["IMPORT", "EXPORT", "DELIVERY"], "commonest first");
  assert.equal(out.totals.DELIVERY, 1);
  assert.equal(out.counted, 6);
});

test("equal counts fall back to the name, so the order never wobbles", () => {
  // Two directions with the same count must not swap places between renders
  // depending on which job the register happened to return first.
  const out = byPeriod([job("01/07/2026", "EXPORT"), job("02/07/2026", "DELIVERY")], "day", scope);
  assert.deepEqual(out.cats, ["DELIVERY", "EXPORT"]);
});

test("the range is inclusive at both ends", () => {
  const jobs = ["30/06/2026", "01/07/2026", "15/07/2026", "31/07/2026", "01/08/2026"].map((d) => job(d));
  assert.equal(byPeriod(jobs, "day", { ...scope, from: "01/07/2026", to: "31/07/2026" }).counted, 3);
});

test("out of range is out of scope, not a problem inside the period", () => {
  // A cancelled job in June must not appear in July's cancelled figure — that
  // number describes the period being reported on and nothing else.
  const out = byPeriod([
    { ...job("30/06/2026"), status: "CANCELLED" },
    job("05/07/2026"),
  ], "month", { ...scope, from: "01/07/2026", to: "31/07/2026" });

  assert.equal(out.cancelled, 0);
  assert.equal(out.counted, 1);
});

test("periods read newest first and the busiest is found", () => {
  const out = byPeriod([
    job("01/07/2026"), job("03/07/2026"), job("03/07/2026"), job("02/07/2026"),
  ], "day", scope);

  assert.deepEqual(out.rows.map((r) => r.label), ["03/07/2026", "02/07/2026", "01/07/2026"]);
  assert.equal(busiest(out).label, "03/07/2026");
  assert.equal(busiest(out).total, 2);
});

test("a breakdown ranks heaviest first and splits by direction", () => {
  const out = byField([
    job("01/07/2026", "IMPORT", { trucker: "WEALTHY" }),
    job("02/07/2026", "EXPORT", { trucker: "WEALTHY" }),
    job("03/07/2026", "IMPORT", { trucker: "WEALTHY" }),
    job("04/07/2026", "IMPORT", { trucker: "SANGJA" }),
  ], (j) => j.trucker, scope);

  assert.deepEqual(out.rows.map((r) => r.label), ["WEALTHY", "SANGJA"]);
  assert.equal(out.rows[0].total, 3);
  assert.equal(out.rows[0].byCat.IMPORT, 2);
  assert.equal(out.rows[0].byCat.EXPORT, 1);
});

test("a blank column is named and counted, not discarded", () => {
  // Dropping these would make the ranking add up to less than the total, which
  // is how a report loses somebody's trips without ever saying so.
  const out = byField([
    job("01/07/2026", "IMPORT", { plant: "" }),
    job("02/07/2026", "IMPORT", { plant: "ALLNEX" }),
  ], (j) => j.plant, scope);

  assert.equal(out.counted, 2);
  assert.equal(out.blank, 1);
  assert.equal(out.rows.find((r) => r.label === BLANK).total, 1);
  assert.equal(out.rows.reduce((sum, r) => sum + r.total, 0), out.counted);
});

test("one direction can be asked for on its own", () => {
  const jobs = [
    job("01/07/2026", "IMPORT", { cyYard: "JWD" }),
    job("02/07/2026", "EXPORT", { cyYard: "JWD" }),
    job("03/07/2026", "EXPORT", { cyYard: "KERRY" }),
  ];
  const exports = byField(jobs, (j) => j.cyYard, { ...scope, cat: "EXPORT" });

  assert.equal(exports.counted, 2);
  assert.deepEqual(exports.cats, ["EXPORT"]);
  assert.equal(exports.rows.every((r) => r.byCat.IMPORT === undefined), true);
});

test("every table on the page counts the same trips", () => {
  // The property the whole report rests on. The first version kept undated
  // jobs in the rankings and dropped them from the period table, so the page
  // showed 2,076 trips in one table and 2,104 in the next one down. Both were
  // arguable; the pair was unreadable.
  const jobs = [
    job("01/07/2026", "IMPORT", { customer: "SHPP", trucker: "WEALTHY" }),
    job("02/07/2026", "EXPORT", { customer: "LOTUS", trucker: "SANGJA" }),
    job("soon", "IMPORT", { customer: "SHPP", trucker: "WEALTHY" }),
    { ...job("03/07/2026", "IMPORT", { customer: "SHPP" }), status: "CANCELLED" },
  ];

  const tables = [
    byPeriod(jobs, "month", scope),
    byField(jobs, (j) => j.customer, scope),
    byField(jobs, (j) => j.trucker, scope),
  ];

  for (const table of tables) {
    assert.equal(table.counted, 2, "the same two trips are in scope everywhere");
    assert.equal(table.undated, 1);
    assert.equal(table.cancelled, 1);
    assert.equal(table.rows.reduce((sum, r) => sum + r.total, 0), table.counted);
  }
});

test("a cancelled job with an unreadable date is reported once, as cancelled", () => {
  // It is both, and counting it in each would make the exclusions add up to
  // more jobs than the register holds.
  const out = byPeriod([{ ...job("soon"), status: "CANCELLED" }], "day", scope);
  assert.equal(out.cancelled, 1);
  assert.equal(out.undated, 0);
  assert.equal(out.counted, 0);
});

test("nothing in scope reports nothing rather than a zero-filled month", () => {
  const out = byPeriod([], "month", scope);
  assert.deepEqual(out.rows, []);
  assert.equal(busiest(out), null);
  assert.equal(out.counted, 0);
});
