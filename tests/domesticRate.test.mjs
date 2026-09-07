import assert from "node:assert/strict";
import test from "node:test";

import { explain, rateTrip, sameOrigin, zipKey } from "../app/scmos/domesticRate.ts";

/*
 * What a Domestic trip costs, joined to the rate card by destination postcode.
 *
 * The postcode is the join because the two sides spell places differently —
 * "Chonburi, Amatanakorn" against "Chonburi, Amatanakorn (MCP)".
 */

const lane = (from, county, prices) => ({ from, county, prices });
const card = [
  lane("Unithai (Bangna KM. 23)", "21140", { "4W": [2000, 2060], "6W": [3000, 3080], "10W": [4000, 4100] }),
  lane("SCGJWD Warehouse (LCH)", "20230", { "4W": [2500, 2575], "6W": [3500, 3600] }),
];

test("a trip is priced by its postcode, not by the place name", () => {
  const got = rateTrip({ zip: "21140", v10: "1" }, card, 0);
  assert.equal(got.total, 4000);
  assert.deepEqual(got.parts, [{ vehicle: "10W", trucks: 1, each: 4000 }]);
});

test("the diesel band chooses which of the card's columns applies", () => {
  assert.equal(rateTrip({ zip: "21140", v10: "1" }, card, 0).total, 4000);
  assert.equal(rateTrip({ zip: "21140", v10: "1" }, card, 1).total, 4100);
});

test("more than one truck costs more than one truck", () => {
  // The card prices one lorry of one size. Four 10-wheels is four times.
  assert.equal(rateTrip({ zip: "21140", v10: "4" }, card, 0).total, 16000);
});

test("a trip sending two sizes pays for both", () => {
  // "1X6WH , 1X10WH" is a real vehicle line off the receipts.
  const got = rateTrip({ zip: "21140", v6: "1", v10: "1" }, card, 0);
  assert.equal(got.total, 7000);
  assert.deepEqual(got.parts.map((p) => p.vehicle), ["6W", "10W"]);
});

test("a tail lift is priced as a 6-wheel, because no card quotes one", () => {
  // The selling card offers 4W, 6W, 10W and a container line it never priced;
  // the cost card is six sheets of the three wheel sizes. Neither has a
  // tail-lift rate, and the account team's answer is to use the 6-wheel's.
  const got = rateTrip({ zip: "21140", vtl: "1" }, card, 0);
  assert.equal(got.total, 3000, "the 6W price");
  assert.deepEqual(got.parts, [{ vehicle: "TAIL LIFT", trucks: 1, each: 3000 }],
    "labelled by the column it came from, not as a 6-wheel it did not send");
});

test("a half in a column is half the trip, not a whole truck", () => {
  // A load shared with somebody else's. Rounding turned every 0.5 into a whole
  // truck and doubled what the trip was billed.
  assert.equal(rateTrip({ zip: "21140", v10: "0.5" }, card, 0).total, 2000);
  assert.equal(rateTrip({ zip: "21140", v6: "1.5" }, card, 0).total, 4500);
});

test("a half of an odd price is rounded once, at the end", () => {
  // Rounding each line separately drifts from the sum of the lines.
  const odd = [lane("Unithai (Bangna KM. 23)", "21140", { "4W": [2001], "6W": [3001] })];
  assert.equal(rateTrip({ zip: "21140", v4: "0.5", v6: "0.5" }, odd, 0).total, 2501);
});

/* ------------------------------------------- what must not be priced */

test("a job with no postcode is not priced, and says the postcode is why", () => {
  const got = rateTrip({ v10: "1" }, card, 0);
  assert.equal(got.total, null);
  assert.equal(got.reason, "no-zip");
  assert.match(explain(got.reason), /ZIP CODE/);
});

test("a postcode the card does not carry is not priced", () => {
  const got = rateTrip({ zip: "15220", v6: "1" }, card, 0);
  assert.equal(got.total, null);
  assert.equal(got.reason, "no-lane");
});

test("a size the card does not quote takes the whole trip down, not part of it", () => {
  // 20230 has no 10-wheel price. Billing the trip for only the trucks that
  // happen to be quoted would understate it and look like a real figure.
  const got = rateTrip({ zip: "20230", v6: "1", v10: "1" }, card, 0);
  assert.equal(got.total, null);
  assert.equal(got.reason, "not-quoted");
});

test("no diesel band means no price, because the band chooses the column", () => {
  const got = rateTrip({ zip: "21140", v10: "1" }, card, -1);
  assert.equal(got.total, null);
  assert.equal(got.reason, "no-band");
  assert.match(explain(got.reason), /เรทน้ำมัน/);
});

test("zero is never the answer when something is unknown", () => {
  // This column gets checked against an invoice. A zero reads as a free trip.
  for (const job of [{ v10: "1" }, { zip: "99999", v10: "1" }, { zip: "21140" }]) {
    assert.equal(rateTrip(job, card, 0).total, null);
  }
});

/* ----------------------------------------------- one postcode, two lanes */

test("a postcode quoted from two warehouses is settled by the job's own", () => {
  const two = [
    lane("Unithai (Bangna KM. 23)", "20000", { "4W": [2000] }),
    lane("SCGJWD Warehouse (LCH)", "20000", { "4W": [3000] }),
  ];
  assert.equal(rateTrip({ zip: "20000", wh: "JWD", v4: "1" }, two, 0).total, 3000);
  assert.equal(rateTrip({ zip: "20000", wh: "UNITHAI", v4: "1" }, two, 0).total, 2000);
});

test("two lanes and no way to choose is a question, not the first row", () => {
  const two = [
    lane("Unithai (Bangna KM. 23)", "20000", { "4W": [2000] }),
    lane("SCGJWD Warehouse (LCH)", "20000", { "4W": [3000] }),
  ];
  const got = rateTrip({ zip: "20000", v4: "1" }, two, 0);
  assert.equal(got.total, null);
  assert.equal(got.reason, "ambiguous");
});

test("one lane written twice at the same price is not ambiguous", () => {
  // The real card quotes 20000 as both "Chonburi, Amatanakorn" and "Chonburi,
  // Amatanakorn (MCP)" at identical prices. That is one lane, not a choice.
  const twice = [
    lane("SCGJWD Warehouse (LCH)", "20000", { "4W": [2080] }),
    lane("SCGJWD Warehouse (LCH)", "20000", { "4W": [2080] }),
  ];
  assert.equal(rateTrip({ zip: "20000", v4: "1" }, twice, 0).total, 2080);
});

/* --------------------------------------------------------- the odds and ends */

test("a postcode matches whatever spacing it was typed with", () => {
  assert.equal(zipKey(" 21140 "), "21140");
  assert.equal(zipKey("21140"), zipKey("21140 "));
  assert.equal(zipKey(""), "");
  assert.equal(zipKey(undefined), "");
});

test("the warehouse match is loose in both directions, because the two spell it differently", () => {
  assert.equal(sameOrigin("JWD", "SCGJWD Warehouse (LCH)"), true);
  assert.equal(sameOrigin("UNITHAI", "Unithai (Bangna KM. 23)"), true);
  assert.equal(sameOrigin("UNITHAI", "SCGJWD Warehouse (LCH)"), false);
  assert.equal(sameOrigin("", "Unithai"), false);
  assert.equal(sameOrigin(undefined, "Unithai"), false);
});
