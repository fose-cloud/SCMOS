import assert from "node:assert/strict";
import test from "node:test";

import {
  chemoursLaneKey, chemoursLayout, chemoursMargin, chemoursSellIndex,
  chemoursSellLayout, chemoursSellVehicle, parseChemoursSellSheet, parseChemoursSheet,
} from "../app/scmos/rates.ts";

/*
 * The selling card: what LESCHACO bills the customer, as against the haulier
 * card that says what the trip costs us.
 *
 * Every number here is invented. The real card is a negotiated price for one
 * named account and does not belong in a repository — what is being pinned is
 * the shape of the sheet and the reading of it, and invented numbers pin those
 * exactly as well as real ones would.
 */

const BANDS = ["28.01-30.00", "30.01-32.00", "32.01-34.00"];

/** The heading the RFP answer carries, and the cost card does not. */
const HEAD = ["Origin City", "Origin Postal From", "Destination City", " Dest Postal From",
  "Cargo type", "Truck type", ...BANDS, "Note"];

const lane = (to, zip, cargo, truck, prices, note = "") =>
  ["Unithai (Bangna KM. 23)", 10540, to, zip, cargo, truck, ...prices, note];

const sheet = (rows) => ({
  carrier: "LESCHACO", fileName: "selling.xlsx", sheetName: "Unithai", rows: [HEAD, ...rows],
});

const read = (rows) => {
  const bands = [];
  const issues = [];
  const out = parseChemoursSellSheet(sheet(rows), bands, issues);
  return { out, bands, issues };
};

test("the two cards are told apart by the Truck type column, not by the file they came from", () => {
  // This is the guard that matters most on this screen. The cost card and the
  // selling card describe the same lanes; reading one as the other would file
  // what we charge as what we pay, and nobody would see it until the margin
  // came out wrong.
  assert.ok(chemoursSellLayout([HEAD]), "a Truck type column is the selling layout");

  const costHead = ["Origin City", "Origin\nPostal From", "Destination City", "Destination\nPostal From",
    "Transportation Rate per Trip for a 4-Wheel Truck (Based on Fuel Price Range)"];
  const costRows = [costHead, ["", "", "", "", ...BANDS], ["Unithai (Bangna KM. 23)", 10540, "Bangkok", 10160, 2400, 2430, 2450]];
  assert.equal(chemoursSellLayout(costRows), null, "the cost card is not a selling card");
  assert.ok(chemoursLayout(costRows).bandRow >= 0, "and it is still read by its own parser");
});

test("the cost parser refuses a selling sheet rather than misreading its columns", () => {
  // The converse. The selling sheet has no "Transportation Rate per Trip"
  // heading, so the cost parser gives up — which it must, because its bands
  // start two columns to the left of where this sheet keeps them.
  const issues = [];
  assert.equal(parseChemoursSheet(sheet([lane("Bangkok", 10160, "Non-DG", "4W", [2400, 2472, 2546])]), [], issues), null);
});

test("four truck sizes on one lane are one row with the sizes beside each other", () => {
  const { out, bands } = read([
    lane("Samutprakarn", 10130, "Non-DG", "4W", [2420, 2492.6, 2567.378]),
    lane("Samutprakarn", 10130, "Non-DG", "6W", [3570, 3677.1, 3787.413]),
    lane("Samutprakarn", 10130, "Non-DG", "10W", [4370, 4501.1, 4636.133]),
    lane("Samutprakarn", 10130, "Non-DG", "20'/40'GP", ["", "", ""]),
  ]);
  assert.equal(out.lanes.length, 1, "one lane, not four");
  assert.equal(bands.length, 3);
  assert.deepEqual(Object.keys(out.lanes[0].prices).sort(), ["10W", "4W", "6W"]);
  assert.equal(out.lanes[0].county, "10130");
});

test("a row the customer asked for and we never priced is kept as the question it is", () => {
  // 20'/40'GP is on every lane of the real card and priced on none of them —
  // 51 rows, noted "Truck with container". A lane in the price table with no
  // price would read as a trip worth nothing, so it is not one; but dropping
  // the row entirely threw away the fact that the customer had asked.
  const { out } = read([
    lane("Samutprakarn", 10130, "Non-DG", "4W", [2420, 2492.6, 2567.378]),
    lane("Samutprakarn", 10130, "Non-DG", "20'/40'GP", ["", "", ""], "Truck with container"),
  ]);
  assert.equal(out.lanes.length, 1, "not a priced lane");
  assert.equal(out.lanes[0].prices["20'/40'GP"], undefined);
  assert.equal(out.source.skipped, 1);

  assert.equal(out.unpriced.length, 1, "and not thrown away either");
  assert.deepEqual(out.unpriced[0], {
    from: "Unithai (Bangna KM. 23)", to: "Samutprakarn", county: "10130",
    vehicle: "20'/40'GP", note: "Truck with container",
  });
});

test("the unpriced line names its own lane, so the list can be sent to a haulier", () => {
  // A count says 51 and nothing else. Which lanes is the whole question.
  const { out } = read([
    lane("Bangkok", 10160, "Non-DG", "4W", [3080, 3172, 3268]),
    lane("Bangkok", 10160, "Non-DG", "20'/40'GP", ["", "", ""], "Truck with container"),
    lane("Rayong", 21140, "Non-DG", "6W", [6400, 6592, 6790]),
    lane("Rayong", 21140, "Non-DG", "20'/40'GP", ["", "", ""], "Truck with container"),
  ]);
  assert.deepEqual(out.unpriced.map((line) => `${line.to} ${line.county}`),
    ["Bangkok 10160", "Rayong 21140"]);
});

test("a sheet that priced nothing at all is still returned, not given up on", () => {
  // A whole warehouse left unquoted is the loudest version of this, and the
  // no-lanes shortcut was the one case that would have hidden it.
  const { out } = read([
    lane("Samutprakarn", 10130, "Non-DG", "20'/40'GP", ["", "", ""], "Truck with container"),
  ]);
  assert.equal(out.lanes.length, 0);
  assert.equal(out.unpriced.length, 1);
});

test("fractional baht round to whole baht, because an invoice is written in baht", () => {
  // The workbook computes each band as the one before it times 1.03 and leaves
  // the arithmetic in the cell — 2567.378 is not a price anybody agreed to.
  const { out } = read([lane("Samutprakarn", 10130, "Non-DG", "4W", [2420, 2492.6, 2567.378])]);
  assert.deepEqual(out.lanes[0].prices["4W"], [2420, 2493, 2567]);
});

test("DG rides on the lane, because it prices the same postcode quite differently", () => {
  const { out } = read([
    lane("Rayong", 21150, "DG", "6W", [7030, 7240.9, 7458.127]),
    lane("Bangkok", 10160, "Non-DG", "6W", [4910, 5057.3, 5209.019]),
  ]);
  assert.equal(out.lanes.find((one) => one.county === "21150").remark, "DG");
  assert.equal(out.lanes.find((one) => one.county === "10160").remark, "Non-DG");
});

test("the Note column is free text and is never read as a diesel band", () => {
  const layout = chemoursSellLayout([HEAD]);
  assert.equal(layout.bands.length, BANDS.length);
  assert.ok(!layout.bands.some((band) => band.column === layout.note));

  const { out } = read([lane("Samutprakarn", 10130, "Non-DG", "4W", [2420, 2493, 2567], "Truck with container")]);
  assert.deepEqual(out.lanes[0].prices["4W"], [2420, 2493, 2567], "the note did not become a fourth price");
});

test("a band whose ceiling is below its floor is refused, not sorted to the bottom", () => {
  // Taken at face value such a band sorts below every real one, so a lorry
  // running at 30 baht diesel would be billed at the top-band rate.
  const bands = [];
  const issues = [];
  const rows = [
    ["Origin City", "Origin Postal From", "Destination City", " Dest Postal From", "Cargo type", "Truck type",
      "28.01-30.00", "36.31-29.94", "Note"],
    ["Unithai (Bangna KM. 23)", 10540, "Bangkok", 10160, "Non-DG", "4W", 2420, 2493, ""],
  ];
  const out = parseChemoursSellSheet({ carrier: "LESCHACO", fileName: "selling.xlsx", sheetName: "Unithai", rows }, bands, issues);
  assert.equal(bands.length, 1, "only the sound band was taken");
  assert.equal(out.lanes[0].prices["4W"].length, 1);
  assert.equal(issues.length, 1);
  assert.match(issues[0].value, /36\.31-29\.94/);
});

test("the same lane quoted twice for one truck size keeps the first and says so", () => {
  const { out, issues } = read([
    lane("Samutprakarn", 10130, "Non-DG", "4W", [2420, 2493, 2567]),
    lane("Samutprakarn", 10130, "Non-DG", "4W", [9990, 9990, 9990]),
  ]);
  assert.deepEqual(out.lanes[0].prices["4W"], [2420, 2493, 2567]);
  assert.equal(issues.length, 1);
});

test("truck sizes are spelled the way the cost card spells them, so the two meet", () => {
  // The whole point of loading this card is one row carrying both prices. That
  // only works if "4W" here is the "4W" the cost card's heading produced.
  assert.equal(chemoursSellVehicle("4W"), "4W");
  assert.equal(chemoursSellVehicle(" 10w "), "10W");
  assert.equal(chemoursSellVehicle("06W"), "6W");
  // Kept as written rather than dropped — see the note on the function.
  assert.equal(chemoursSellVehicle("20'/40'GP"), "20'/40'GP");
});

/* ------------------------------------------------- cost against what we bill */

test("a lane is matched by origin and postcode, because the two cards name places differently", () => {
  assert.equal(chemoursLaneKey("Unithai (Bangna KM. 23)", "10130"),
    chemoursLaneKey(" unithai (bangna km. 23) ", " 10130 "));
  assert.notEqual(chemoursLaneKey("Unithai (Bangna KM. 23)", "10130"),
    chemoursLaneKey("SCGJWD Warehouse (LCH)", "10130"));
});

test("one lane written twice on the cost card finds the one selling price", () => {
  // The real card quotes 20000 as both "Chonburi, Amatanakorn" and "Chonburi,
  // Amatanakorn (MCP)" at the same price. Both are the same lane and both
  // should show the same margin rather than one of them showing none.
  const sell = [{ id: "s", carrier: "LESCHACO", service: "DELIVERY", customer: "CHEMOURS",
    from: "SCGJWD Warehouse (LCH)", to: "Chonburi, Amatanakorn", county: "20000", remark: "DG",
    prices: { "4W": [3500] } }];
  const index = chemoursSellIndex(sell);
  for (const name of ["Chonburi, Amatanakorn", "Chonburi, Amatanakorn (MCP)"]) {
    const found = index.get(chemoursLaneKey("SCGJWD Warehouse (LCH)", "20000"));
    assert.equal(found.prices["4W"][0], 3500, name);
  }
});

test("two selling prices for one lane keeps the first and reports it", () => {
  const lane = (price) => ({ id: String(price), carrier: "LESCHACO", service: "DELIVERY",
    customer: "CHEMOURS", from: "Unithai (Bangna KM. 23)", to: "Bangkok", county: "10160",
    remark: "Non-DG", prices: { "4W": [price] } });
  const issues = [];
  const index = chemoursSellIndex([lane(3080), lane(9999)], issues);
  assert.equal(index.size, 1);
  assert.equal(index.get(chemoursLaneKey("Unithai (Bangna KM. 23)", "10160")).prices["4W"][0], 3080);
  assert.equal(issues.length, 1);
});

test("margin is a share of the price we bill, not a mark-up on what we pay", () => {
  // 4,000 out and 5,000 in is 20% of the invoice and 25% on cost. They are
  // different numbers and the screen must not let them be read as one.
  assert.equal(chemoursMargin(4000, 5000), 20);
  assert.notEqual(chemoursMargin(4000, 5000), 25);
});

test("a missing cost is not a free trip, and a missing sell is not a giveaway", () => {
  assert.equal(chemoursMargin(null, 5000), null);
  assert.equal(chemoursMargin(4000, null), null);
  assert.equal(chemoursMargin(4000, 0), null);
});

test("a lane billed below cost gives a negative margin rather than hiding it", () => {
  assert.equal(chemoursMargin(5000, 4000), -25);
});
