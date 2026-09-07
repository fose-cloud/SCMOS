import assert from "node:assert/strict";
import test from "node:test";

import {
  RETURN_SHARE, TICKED, amount, hasReturnLoad, returnKind, returnLoadCharge,
  termCoversOrigin, tripCost,
} from "../app/scmos/returnLoad.ts";
import { parseChemoursSheet } from "../app/scmos/rates.ts";

/*
 * งานรับกลับ — the return load, at half the rate of that trip.
 *
 * The half-rate is on THAI KOT's cost card, under its SCGJWD sheets only:
 * "กรณีมีงานรับกลับ วางบิลในครึ่งราคาของราคาเที่ยวนั้นๆ". Finished goods coming back
 * are charged at 80% instead — from the account team, on no card at all.
 *
 * Both are what the haulier charges us. The selling card carries no matching
 * term and nothing here touches the selling side.
 */

test("a return load costs half the rate of the trip it came back from", () => {
  assert.equal(returnLoadCharge(4000), 2000);
  assert.equal(returnLoadCharge("8,700"), 4350);
  assert.equal(returnLoadCharge("฿ 2,080"), 1040);
});

test("half an odd baht goes to the nearest baht, like every other rate here", () => {
  assert.equal(returnLoadCharge(2085), 1043);
  assert.equal(returnLoadCharge(2083), 1042);
});

test("a trip nobody has priced does not get a free return load", () => {
  // Zero would be a number an invoice reconciliation could not support. The
  // honest answer is that the charge is unknown.
  assert.equal(returnLoadCharge(""), null);
  assert.equal(returnLoadCharge(undefined), null);
  assert.equal(returnLoadCharge("ยังไม่ได้ราคา"), null);
});

test("zero is a price and blank is the absence of one", () => {
  assert.equal(amount(0), 0);
  assert.equal(amount(""), null);
  assert.equal(returnLoadCharge(0), 0);
});

test("a negative rate is refused rather than halved", () => {
  assert.equal(amount(-500), null);
  assert.equal(returnLoadCharge("-500"), null);
});

test("the trip costs its rate, and half again when a load came back", () => {
  assert.equal(tripCost(4000, "none"), 4000);
  assert.equal(tripCost(4000, "standard"), 6000);
  assert.equal(tripCost("8,700", "standard"), 13050);
});

test("finished goods coming back cost 80% of the trip, not half", () => {
  assert.equal(returnLoadCharge(4000, "finished"), 3200);
  assert.equal(tripCost(4000, "finished"), 7200);
  assert.equal(returnLoadCharge(3480, "finished"), 2784);
});

test("the two rates are different numbers and never the same one", () => {
  assert.equal(RETURN_SHARE.standard, 0.5);
  assert.equal(RETURN_SHARE.finished, 0.8);
  assert.notEqual(returnLoadCharge(4000, "standard"), returnLoadCharge(4000, "finished"));
});

test("a trip with no return leg adds nothing, which is a real answer", () => {
  assert.equal(returnLoadCharge(4000, "none"), 0);
  assert.equal(tripCost(4000, "none"), 4000);
});

test("an unpriced trip has no total, ticked or not", () => {
  assert.equal(tripCost("", "standard"), null);
  assert.equal(tripCost("", "finished"), null);
  assert.equal(tripCost("", "none"), null);
});

test("a lorry comes back once, so the two kinds are never added together", () => {
  // 50% and 80% would be 130% of the outbound rate for the journey home. The
  // model has no way to express it: the pair is one kind, not two flags.
  assert.equal(returnKind(TICKED, TICKED), "finished");
  assert.equal(tripCost(4000, returnKind(TICKED, TICKED)), 7200);
  assert.notEqual(tripCost(4000, returnKind(TICKED, TICKED)), 4000 + 2000 + 3200);
});

test("finished goods wins when the register somehow holds both", () => {
  // It is the more specific description of the same leg. The grid cannot
  // produce the state — ticking either box clears the other — but an import
  // from a sheet with its own columns could.
  assert.equal(returnKind(TICKED, TICKED), "finished");
  assert.equal(returnKind(TICKED, ""), "standard");
  assert.equal(returnKind("", TICKED), "finished");
  assert.equal(returnKind("", ""), "none");
  assert.equal(returnKind(undefined, undefined), "none");
});

test("FALSE in either column is not a return leg", () => {
  assert.equal(returnKind("FALSE", "FALSE"), "none");
  assert.equal(returnKind("FALSE", TICKED), "finished");
});

test("the tick is read the way spreadsheets write it", () => {
  // These values arrive from the operators' own sheets as well as from the box.
  for (const yes of ["TRUE", "true", " True ", "YES", "y", "1"]) {
    assert.equal(hasReturnLoad(yes), true, yes);
  }
  for (const no of ["", "FALSE", "false", "0", "N", "-", undefined, null]) {
    assert.equal(hasReturnLoad(no), false, String(no));
  }
});

test("FALSE is not read as a tick, however Excel spells it", () => {
  // The obvious way to get this wrong is a truthiness check on the string:
  // "FALSE" is a non-empty string and would tick every unticked row.
  assert.equal(hasReturnLoad("FALSE"), false);
  assert.equal(tripCost(4000, returnKind("FALSE", "")), 4000);
});

test("the term is known to cover SCGJWD, whichever way the warehouse is spelled", () => {
  // The register writes "JWD"; the card writes "SCGJWD Warehouse (LCH)".
  assert.equal(termCoversOrigin("JWD"), true);
  assert.equal(termCoversOrigin("SCGJWD"), true);
  assert.equal(termCoversOrigin("SCGJWD Warehouse (LCH)"), true);
});

test("Unithai is not covered, because its sheets carry no such line", () => {
  assert.equal(termCoversOrigin("UNITHAI"), false);
  assert.equal(termCoversOrigin("Unithai (Bangna KM. 23)"), false);
  assert.equal(termCoversOrigin(""), false);
  assert.equal(termCoversOrigin(undefined), false);
});

/* ------------------------------------ the term as it comes off the card */

test("the condition at the foot of a sheet is carried, not walked past", () => {
  // It was being dropped. The loop skips rows with no destination, and that is
  // exactly the shape of a footnote — so a term worth half a trip's rate on
  // every backhaul lived only in a workbook nobody reopens.
  const issues = [];
  const rows = [
    ["Origin City", "Origin\nPostal From", "Destination City", "Destination\nPostal From",
      "Transportation Rate per Trip for a 6-Wheel Truck (Based on Fuel Price Range)"],
    ["", "", "", "", "28.01-30.00", "30.01-32.00"],
    ["SCGJWD Warehouse (LCH)", 20230, "Chonburi, Amatanakorn", 20000, 3760, 3810],
    [],
    ["หมายเหตุ"],
    ["กรณีมีงานรับกลับ วางบิลในครึ่งราคาของราคาเที่ยวนั้นๆ"],
  ];
  const out = parseChemoursSheet(
    { carrier: "THAIKOT", fileName: "cost.xlsx", sheetName: "SCGJWD (6W)", rows }, [], issues);

  assert.equal(out.lanes.length, 1, "the footnote did not become a lane");
  assert.deepEqual(out.source.notes, ["กรณีมีงานรับกลับ วางบิลในครึ่งราคาของราคาเที่ยวนั้นๆ"]);
});

test("the bare หมายเหตุ heading is not carried as a condition of its own", () => {
  const rows = [
    ["Origin City", "", "Destination City", "", "Transportation Rate per Trip for a 4-Wheel Truck"],
    // Two bands, because that is the threshold a fuel clause has to clear —
    // one stray number in a spacer row is not a contract term.
    ["", "", "", "", "28.01-30.00", "30.01-32.00"],
    ["SCGJWD Warehouse (LCH)", 20230, "Chonburi", 20000, 2080, 2110],
    ["หมายเหตุ"],
  ];
  const out = parseChemoursSheet(
    { carrier: "THAIKOT", fileName: "cost.xlsx", sheetName: "SCGJWD (4W)", rows }, [], []);
  assert.deepEqual(out.source.notes, [], "a heading over nothing says nothing");
});

test("a sheet with no conditions reports none, rather than borrowing another's", () => {
  // Unithai's sheets carry no return-load line. A card that showed the term
  // against them would be claiming a discount nobody agreed to.
  const rows = [
    ["Origin City", "", "Destination City", "", "Transportation Rate per Trip for a 4-Wheel Truck"],
    ["", "", "", "", "28.01-30.00", "30.01-32.00"],
    ["Unithai (Bangna KM. 23)", 10540, "Bangkok, Saimai", 10220, 2480, 2510],
  ];
  const out = parseChemoursSheet(
    { carrier: "THAIKOT", fileName: "cost.xlsx", sheetName: "Unithai (4W)", rows }, [], []);
  assert.deepEqual(out.source.notes, []);
});
