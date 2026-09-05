import assert from "node:assert/strict";
import test from "node:test";
import { THIN, byVendor, otdLabel } from "../app/scmos/vendorReport.ts";

/**
 * Each carrier's work, cut by the customer it was run for.
 *
 * The figures here end up in front of a haulier in a review meeting, so what
 * this file mostly checks is what the report refuses to say: that a trip nobody
 * recorded arrived on time, that a carrier with no records is perfect, and that
 * a percentage means anything without the count it was taken over.
 */

/** A trip, with lateness stated outright so the fixture needs no clock. */
const trip = (trucker, customer, late) => ({ trucker, customer, late });
const lateOf = (one) => one.late;
const run = (trips, grace = 30) => byVendor(trips, { grace, lateOf });

test("each carrier is counted against every customer it ran for", () => {
  const report = run([
    trip("SANGJA", "HENKEL", 0),
    trip("SANGJA", "HENKEL", 90),
    trip("SANGJA", "ALLNEX", 10),
    trip("WEALTHY", "HENKEL", 5),
  ]);

  assert.deepEqual(report.vendors.map((one) => [one.vendor, one.trips]),
    [["SANGJA", 3], ["WEALTHY", 1]], "busiest carrier first");

  const sangja = report.vendors[0];
  assert.deepEqual(sangja.customers.map((one) => [one.customer, one.trips, one.onTime, one.late]),
    [["HENKEL", 2, 1, 1], ["ALLNEX", 1, 1, 0]]);
});

test("the grace period is the customer's service level, not this report's", () => {
  // Ten minutes over is on time at thirty and late at zero. Both readings are
  // correct; which one applies is a contract, so the caller passes it.
  const trips = [trip("SANGJA", "HENKEL", 10)];
  assert.equal(run(trips, 30).vendors[0].onTime, 1);
  assert.equal(run(trips, 0).vendors[0].late, 1);
});

test("early is on time", () => {
  assert.equal(run([trip("SANGJA", "HENKEL", -45)]).vendors[0].onTime, 1);
});

test("a trip nobody recorded is neither on time nor late", () => {
  // The whole point. Two of every three live jobs on the register are in this
  // state, and counting them as successes would put a number in front of a
  // carrier that the records cannot defend.
  const report = run([
    trip("SANGJA", "HENKEL", 0),
    trip("SANGJA", "HENKEL", null),
    trip("SANGJA", "HENKEL", null),
  ]);
  const sangja = report.vendors[0];

  assert.equal(sangja.trips, 3, "it still ran three trips");
  assert.equal(sangja.measured, 1);
  assert.equal(sangja.notAssessable, 2);
  assert.equal(sangja.onTime, 1);
  assert.equal(sangja.late, 0);
  assert.equal(sangja.otd, 100, "over what could be measured, not over everything");
});

test("a carrier nobody recorded at all has no percentage, not a perfect one", () => {
  // Absence of evidence is not performance. Scoring this as 100% put the
  // least-known carriers at the top of a list meant to find the worst.
  const report = run([trip("DGT", "SHPP", null), trip("DGT", "SHPP", null)]);
  const dgt = report.vendors[0];

  assert.equal(dgt.otd, null);
  assert.notEqual(dgt.otd, 0, "nor a failing one — nobody said either way");
  assert.equal(otdLabel(dgt), "วัดไม่ได้");
  assert.equal(dgt.trips, 2, "the trips are still there to be seen");
});

test("a percentage is only ever as good as its base", () => {
  const one = run([trip("SJ", "ALLNEX", 0)]).vendors[0];
  assert.equal(one.otd, 100);
  assert.ok(one.measured < THIN,
    "one trip is below the count the scorecard will grade on, and the screen says so");
  assert.equal(THIN, 5, "the same floor the carrier scorecard refuses to grade below");
});

test("the percentage keeps one decimal rather than rounding a near miss to target", () => {
  // 29 of 30 is 96.7. Rounded to a whole number it is 97, and a service level
  // written as 97% would be reported as met by a report that rounded it there.
  const trips = Array.from({ length: 30 }, (_, at) => trip("PK", "SHPP", at === 0 ? 200 : 0));
  assert.equal(run(trips).vendors[0].otd, 96.7);
});

test("a trip with no carrier is counted somewhere rather than dropped", () => {
  // It is real work. Left out silently, the totals stop matching the rows and
  // nothing on the screen explains the gap.
  const report = run([trip("", "HENKEL", 0), trip("SANGJA", "HENKEL", 0)]);

  assert.equal(report.unnamed, 1);
  assert.equal(report.total.trips, 2, "the total counts it");
  assert.equal(report.vendors.length, 1, "no carrier row is invented for it");
  assert.equal(report.vendors.reduce((sum, one) => sum + one.trips, 0), 1);
});

test("a trip with no customer named is kept under the carrier that ran it", () => {
  const report = run([trip("SANGJA", "", 0)]);
  assert.deepEqual(report.vendors[0].customers.map((one) => one.customer), ["ไม่ระบุลูกค้า"]);
});

test("the totals are the whole scope, counted once", () => {
  const report = run([
    trip("SANGJA", "HENKEL", 0), trip("SANGJA", "ALLNEX", 90),
    trip("WEALTHY", "HENKEL", null), trip("", "SHPP", 0),
  ]);

  assert.equal(report.total.trips, 4);
  assert.equal(report.total.measured, 3);
  assert.equal(report.total.onTime, 2);
  assert.equal(report.total.late, 1);
  assert.equal(report.total.notAssessable, 1);
  // Not the average of the carriers' percentages: a carrier with one trip and
  // one with two hundred would then weigh the same.
  assert.equal(report.total.otd, 66.7);
});

test("nothing in scope is an empty report, not a zero score", () => {
  const report = run([]);
  assert.deepEqual(report.vendors, []);
  assert.equal(report.total.trips, 0);
  assert.equal(report.total.otd, null);
});

test("order does not depend on which row the register happened to hold first", () => {
  const report = run([
    trip("BBB", "X", 0), trip("AAA", "X", 0),
    trip("SANGJA", "B", 0), trip("SANGJA", "A", 0),
  ]);
  assert.deepEqual(report.vendors.map((one) => one.vendor), ["SANGJA", "AAA", "BBB"],
    "most trips first, then by name");
  assert.deepEqual(report.vendors[0].customers.map((one) => one.customer), ["A", "B"]);
});
