import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { DG_SURCHARGE, describeFill, dgFills, priceValue } from "../app/scmos/dgSurcharge.ts";

/*
 * The DG price a NON-DG price implies on the rate sheet: filled when the DG
 * cell is empty, kept in step while it is still the plain rate plus the
 * surcharge, left alone once somebody typed their own figure.
 */

const rowWith = (prices) => (laneId, vehicle) => priceValue(prices[`${laneId}:${vehicle}`] ?? null);
const edit = (laneId, vehicle, value) => ({ laneId, field: `price:${vehicle}`, value });

test("the five surcharges are the department's: 4W +300, 6W +500, 10W +800, 20' +500, 40' +500", () => {
  assert.deepEqual(DG_SURCHARGE.map((one) => [one.from, one.to, one.add]), [
    ["4W", "4W DG", 300], ["6W", "6W DG", 500], ["10W", "10W DG", 800], ["20F", "20F DG", 500], ["40F", "40F DG", 500],
  ]);
});

test("an empty DG cell is filled from the NON-DG price, on every size that has a surcharge", () => {
  const fills = dgFills([edit(1, "4W", "1,000"), edit(1, "6W", "1500"), edit(1, "10W", "2200"), edit(1, "20F", "7000"), edit(1, "40F", "9000")], rowWith({}));
  assert.deepEqual(fills, [
    edit(1, "4W DG", "1300"), edit(1, "6W DG", "2000"), edit(1, "10W DG", "3000"), edit(1, "20F DG", "7500"), edit(1, "40F DG", "9500"),
  ]);
});

test("a DG figure somebody typed is left alone; a derived one follows the plain rate", () => {
  // 4W DG was keyed by hand at 1,450 — not 1,000 + 300 — so it stays.
  const hand = dgFills([edit(1, "4W", "1200")], rowWith({ "1:4W": 1000, "1:4W DG": 1450 }));
  assert.deepEqual(hand, []);
  // 6W DG reads 2,000 = 1,500 + 500, the surcharge's own figure, so it moves with the plain rate.
  const derived = dgFills([edit(1, "6W", "1600")], rowWith({ "1:6W": 1500, "1:6W DG": 2000 }));
  assert.deepEqual(derived, [edit(1, "6W DG", "2100")]);
});

test("clearing the plain rate clears a derived DG figure and keeps a hand-keyed one", () => {
  assert.deepEqual(dgFills([edit(1, "4W", "")], rowWith({ "1:4W": 1000, "1:4W DG": 1300 })), [edit(1, "4W DG", "")]);
  assert.deepEqual(dgFills([edit(1, "4W", "")], rowWith({ "1:4W": 1000, "1:4W DG": 1450 })), []);
  assert.deepEqual(dgFills([edit(1, "4W", "")], rowWith({})), [], "nothing to clear");
});

test("a batch that writes the DG cell itself is not second-guessed, and other sizes are untouched", () => {
  const fills = dgFills([edit(1, "4W", "1000"), edit(1, "4W DG", "1234"), edit(2, "4W RF", "3000"), edit(2, "6W", "abc")], rowWith({}));
  assert.deepEqual(fills, [], "the pasted DG wins; a reefer has no surcharge; a non-price fills nothing");
});

test("a fill describes itself for the toast, and prices read through commas and baht signs", () => {
  assert.equal(describeFill(edit(1, "4W DG", "1300")), "4W DG = ฿1,300 (+300)");
  assert.equal(describeFill(edit(1, "4W DG", "")), "4W DG ล้างแล้ว");
  assert.equal(priceValue("฿1,300 "), 1300);
  assert.equal(priceValue("0"), null);
  assert.equal(priceValue(""), null);
});

test("the sheet fills on a typed cell, a draft cell and a pasted block, and undo carries the fill", () => {
  const screen = readFileSync(new URL("../app/scmos/screens/RateSheet.tsx", import.meta.url), "utf8");
  assert.match(screen, /const fills = fillsFor\(\[\{ laneId: row\.laneId, field, value \}\], \[row\]\);[\s\S]*?remember\(`แก้ \$\{column\.head\}`/);
  assert.match(screen, /if \(fills\.length\) \{\s*const reply = await writeCells\(\[\{ laneId: row\.laneId, field, value \}, \.\.\.fills\]\);/);
  assert.match(screen, /const draftFills = fillsFor\(/);
  assert.match(screen, /\.concat\(fills\);\s*const rowOf/);
});
