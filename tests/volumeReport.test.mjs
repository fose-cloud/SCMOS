import assert from "node:assert/strict";
import test from "node:test";

import {
  BLANK, bucket, busiest, byField, byOperator, byPeriod, owner, weekStart,
} from "../app/scmos/volumeReport.ts";

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
  assert.deepEqual(out.cols, ["IMPORT", "EXPORT", "DELIVERY"], "commonest first");
  assert.equal(out.totals.DELIVERY, 1);
  assert.equal(out.counted, 6);
});

test("equal counts fall back to the name, so the order never wobbles", () => {
  // Two directions with the same count must not swap places between renders
  // depending on which job the register happened to return first.
  const out = byPeriod([job("01/07/2026", "EXPORT"), job("02/07/2026", "DELIVERY")], "day", scope);
  assert.deepEqual(out.cols, ["DELIVERY", "EXPORT"]);
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
  assert.equal(out.rows[0].byCol.IMPORT, 2);
  assert.equal(out.rows[0].byCol.EXPORT, 1);
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
  assert.deepEqual(exports.cols, ["EXPORT"]);
  assert.equal(exports.rows.every((r) => r.byCol.IMPORT === undefined), true);
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

test("a person's work is grouped on their id, not their name", () => {
  // Ownership by name has broken here before: two spellings of one person
  // become two people and nothing says so. The two agree in the register today
  // and this must not depend on them continuing to.
  const out = byOperator([
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", op: "Watsana", opId: "OP-01" },
    { date: "02/07/2026", cat: "EXPORT", status: "OPEN", op: "watsana ", opId: "OP-01" },
    { date: "03/07/2026", cat: "IMPORT", status: "OPEN", op: "Uthai", opId: "OP-02" },
  ], scope);

  assert.equal(out.rows.length, 2, "one person, one row, however their name was typed");
  assert.equal(out.rows[0].total, 2);
  assert.equal(out.rows[0].byCol.IMPORT, 1);
  assert.equal(out.rows[0].byCol.EXPORT, 1);
});

test("a job with nobody on it is still counted, under a name", () => {
  const out = byOperator([
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", op: "", opId: "" },
    { date: "02/07/2026", cat: "IMPORT", status: "OPEN", op: "Uthai", opId: "OP-02" },
  ], scope);

  assert.equal(out.counted, 2);
  assert.equal(out.rows.find((r) => r.label === BLANK).total, 1);
});

test("the three directions stay as columns even with no work in them", () => {
  // Domestic is worked under The Chemours. A month with none of it should say
  // so; a column that quietly disappears reads as the report forgetting it.
  const pinned = { ...scope, always: ["IMPORT", "EXPORT", "DELIVERY"] };
  const out = byOperator([
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", op: "Uthai", opId: "OP-02" },
  ], pinned);

  assert.deepEqual(out.cols, ["IMPORT", "EXPORT", "DELIVERY"]);
  assert.equal(out.totals.DELIVERY, 0);
  assert.equal(out.rows[0].byCol.DELIVERY, undefined, "no work is no cell, not a wrong one");
});

test("asking for one direction does not pin the other two", () => {
  // An import-only table with empty EXPORT and DOMESTIC columns says nothing
  // anybody needs and makes the table wider than the screen.
  const out = byField([
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", cyYard: "JWD" },
    { date: "02/07/2026", cat: "EXPORT", status: "OPEN", cyYard: "JWD" },
  ], (j) => j.cyYard, { ...scope, cat: "IMPORT", always: ["IMPORT", "EXPORT", "DELIVERY"] });

  assert.deepEqual(out.cols, ["IMPORT"]);
  assert.equal(out.counted, 1);
});

test("a table can be split by who handled it rather than by direction", () => {
  const out = byField([
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", type: "1X20'", op: "Uthai", opId: "OP-02" },
    { date: "02/07/2026", cat: "EXPORT", status: "OPEN", type: "1X20'", op: "Watsana", opId: "OP-01" },
    { date: "03/07/2026", cat: "IMPORT", status: "OPEN", type: "1X40'", op: "Uthai", opId: "OP-02" },
  ], (j) => j.type, scope, owner);

  assert.deepEqual(out.cols, ["Uthai", "Watsana"], "commonest first");
  const twenty = out.rows.find((r) => r.label === "1X20'");
  assert.equal(twenty.total, 2);
  assert.equal(twenty.byCol.Uthai, 1);
  assert.equal(twenty.byCol.Watsana, 1);
});

test("however a table is split, it counts the same trips", () => {
  const jobs = [
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", type: "1X20'", op: "Uthai", opId: "OP-02", customer: "SHPP" },
    { date: "02/07/2026", cat: "EXPORT", status: "OPEN", type: "1X40'", op: "Watsana", opId: "OP-01", customer: "LOTUS" },
    { date: "soon", cat: "IMPORT", status: "OPEN", type: "1X20'", op: "Uthai", opId: "OP-02", customer: "SHPP" },
    { date: "03/07/2026", cat: "IMPORT", status: "CANCELLED", type: "1X20'", op: "Uthai", opId: "OP-02", customer: "SHPP" },
  ];

  for (const table of [
    byPeriod(jobs, "month", scope),
    byOperator(jobs, scope),
    byField(jobs, (j) => j.type, scope, owner),
    byField(jobs, (j) => j.customer, scope),
  ]) {
    assert.equal(table.counted, 2);
    assert.equal(table.undated, 1);
    assert.equal(table.cancelled, 1);
    assert.equal(table.rows.reduce((sum, r) => sum + r.total, 0), table.counted);
  }
});

test("a table split by person does not pin the direction columns", () => {
  // Caught on screen: the vehicle-type-by-person table showed five people and
  // then IMPORT, EXPORT and DELIVERY at zero, because the pins were applied to
  // whatever the columns happened to be rather than to the directions.
  const out = byField([
    { date: "01/07/2026", cat: "IMPORT", status: "OPEN", type: "1X20'", op: "Uthai", opId: "OP-02" },
  ], (j) => j.type, { ...scope, always: ["IMPORT", "EXPORT", "DELIVERY"] }, owner);

  assert.deepEqual(out.cols, ["Uthai"], "people across the top, and only people");
});
