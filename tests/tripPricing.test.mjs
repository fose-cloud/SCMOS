import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { dieselFor, dieselNote, priceTrip } from "../app/scmos/tripPricing.ts";

/*
 * One rule for what a Domestic trip is priced at: a keyed rate wins, else
 * the card at the trip's diesel — its own, its month's, the stand-in — and
 * the return leg on top.
 */

const card = {
  bands: [{ label: "28.01-30.00", min: 28.01, max: 30 }, { label: "30.01-32.00", min: 30.01, max: 32 }, { label: "32.01-34.00", min: 32.01, max: 34 }],
  lanes: [{ from: "SCGJWD Warehouse (LCH)", county: "20000", prices: { "6W": [1500, 1545, 1591], "10W": [2200, 2266, 2334] } }],
};
const days = [
  ...Array.from({ length: 15 }, (_, i) => ({ date: `${String(i + 1).padStart(2, "0")}/09/2026`, price: i < 9 ? 31 : 33.2 })),
];
const job = (over) => ({ date: "12/09/2026", zip: "20000", wh: "JWD", v6: "1", ...over });

test("the diesel is the job's own, else the month's average, else the stand-in", () => {
  assert.deepEqual(dieselFor(job({ diesel: "29.5" }), days, 39.14), { price: 29.5, from: "job", month: "" });
  const monthly = dieselFor(job({}), days, 39.14);
  assert.equal(monthly.from, "month");
  assert.equal(monthly.month, "09/2026");
  assert.equal(monthly.price, Math.round(((31 * 9 + 33.2 * 6) / 15) * 100) / 100);
  assert.deepEqual(dieselFor(job({ date: "12/06/2026" }), days, 39.14), { price: 39.14, from: "default", month: "" });
});

test("a keyed rate wins; otherwise the card is read at the month's band, and the return leg is added", () => {
  const keyed = priceTrip(job({ cost: "9,999", returnLoad: "TRUE" }), card, days, 39.14);
  assert.equal(keyed.from, "keyed");
  assert.equal(keyed.rate, 9999);
  assert.equal(keyed.returnCharge, Math.round(9999 / 2));
  assert.equal(keyed.total, 9999 + Math.round(9999 / 2));

  const read = priceTrip(job({ v6: "1", v10: "1" }), card, days, 39.14);
  assert.equal(read.from, "card");
  assert.equal(read.band, 1, "31.88 average falls in 30.01–32.00");
  assert.equal(read.rate, 1545 + 2266);
  assert.equal(read.returnKind, "none");
  assert.equal(read.returnCharge, null);
  assert.equal(read.total, 3811);
  assert.match(dieselNote(read), /เฉลี่ยเดือน 09\/2026/);
});

test("nothing is invented: no card, no lane, or no trucks leaves the rate null and the reason on the reading", () => {
  assert.equal(priceTrip(job({}), null, days, 39.14).rate, null);
  const noLane = priceTrip(job({ zip: "99999" }), card, days, 39.14);
  assert.equal(noLane.rate, null);
  assert.equal(noLane.rated.reason, "no-lane");
  assert.equal(noLane.total, null);
  assert.equal(priceTrip(job({ v6: "" }), card, days, 39.14).rated.reason, "no-trucks");
});

test("the Domestic export writes the priced figures as numbers and sums them with a formula", () => {
  const excel = readFileSync(new URL("../app/scmos/excel.ts", import.meta.url), "utf8");
  assert.match(excel, /export function exportJobs\(jobs: Job\[\], layout: string, scopeLabel: string, pricing\?: \(job: Job\) => TripPrice\)/);
  assert.match(excel, /row\["Transport Cost"\] = price\.rate \?\? "";/);
  assert.match(excel, /row\["Total Transport Cost"\] = price\.total \?\? "";/);
  assert.match(excel, /\{ t: "n", v: sum, f: `SUM\(\$\{letter\}2:\$\{letter\}\$\{rows\.length \+ 1\}\)` \}/);
  assert.match(excel, /const MONEY = \["Transport Cost", "Return Load Cost", "Total Transport Cost"\];/);
});
