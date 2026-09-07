import assert from "node:assert/strict";
import test from "node:test";

import {
  receiptChoices, receiptDestination, receiptHead, receiptLabel, receiptLine,
} from "../app/scmos/cargoReceipt.ts";

/*
 * The receipt is filled from the job it is for.
 *
 * The shapes here are the Domestic summary sheet's own columns — TRUCK, W/H,
 * Customer List, ZIP CODE, PALLET, KGS., DELIVER NO., SAP ORDER — under the
 * names the register gives them.
 */

const job = (over = {}) => ({
  key: "DEL1", cat: "DELIVERY", date: "02/08/2026",
  customer: "AMPACET", destination: "", province: "Rayong", zip: "21140",
  wh: "UNITHAI", trucker: "SSL", licence: "70-1234 กรุงเทพ",
  sapOrder: "8505076096", deliverNo: "6317309804",
  pallet: "10", kgs: "10000", remark: "", ...over,
});

test("the heading block is copied off the job, not typed again", () => {
  const head = receiptHead(job());
  assert.equal(head.customer, "AMPACET");
  assert.equal(head.date, "02/08/2026");
  assert.equal(head.truckNo, "70-1234 กรุงเทพ");
  assert.equal(head.invoiceNo, "6317309804", "the delivery note is what the gate checks");
  assert.equal(head.blNo, "8505076096", "and the SAP order is the customer's own reference");
});

test("a lorry leaving Bangna has no vessel and no ETA, so neither is guessed", () => {
  // The form carries those lines because the same document is used for import
  // work. Filling them from a Domestic job would put a ship on a road trip.
  const head = receiptHead(job());
  assert.equal(head.vessel, "");
  assert.equal(head.eta, "");
});

test("the consignee signs, not the account we bill", () => {
  // The Chemours is the account. AMPACET is who the goods go to and who signs
  // for them — the summary sheet heads that column "Customer List".
  assert.equal(receiptHead(job({ customer: "AMPACET" })).customer, "AMPACET");
});

test("the truck's times are left for whoever is standing at the gate", () => {
  // A receipt that arrived with them filled in would be a record of what was
  // planned, signed as what happened.
  const line = receiptLine(job());
  assert.equal(line.truckIn, "");
  assert.equal(line.truckOut, "");
});

test("pallets and kilos land in the two columns that hold them", () => {
  const line = receiptLine(job({ pallet: "10", kgs: "10000" }));
  assert.equal(line.packages, "10");
  assert.equal(line.weight, "10000");
});

test("kilos keyed in the general grid's column still fill the form", () => {
  // The Domestic import writes them to both; a job entered by hand elsewhere
  // has only `weight`. Either way the receipt should not go out empty.
  assert.equal(receiptLine(job({ kgs: "", weight: "8400" })).weight, "8400");
  assert.equal(receiptLine(job({ kgs: "10000", weight: "8400" })).weight, "10000",
    "the Domestic column wins when both are there");
});

test("where the goods are going falls back to the province and postcode", () => {
  // Most Domestic rows name no destination at all — the sheet is keyed on ZIP
  // CODE. A blank "Delivery to" is a receipt somebody has to ask about.
  assert.equal(receiptDestination(job({ destination: "" })), "Rayong 21140");
  assert.equal(receiptDestination(job({ destination: "W/H AMPACET" })), "W/H AMPACET Rayong 21140");
  assert.equal(receiptDestination(job({ destination: "", province: "", zip: "" })), "");
});

test("a destination that already carries the postcode does not carry it twice", () => {
  assert.equal(receiptDestination(job({ destination: "Amatanakorn 21140", province: "", zip: "21140" })),
    "Amatanakorn 21140");
});

test("only Domestic jobs are offered, because the form has no room for a vessel", () => {
  const rows = [job(), job({ key: "IMP1", cat: "IMPORT" }), job({ key: "EXP1", cat: "EXPORT" })];
  const found = receiptChoices(rows);
  assert.equal(found.length, 1);
  assert.equal(found[0].key, "DEL1");
});

test("a job with neither consignee nor destination is not offered", () => {
  // It would show as a date and nothing else, and picking it would blank a form
  // somebody had already started.
  const rows = [job({ key: "BLANK", customer: "", destination: "", province: "", zip: "" }), job()];
  assert.deepEqual(receiptChoices(rows).map((one) => one.key), ["DEL1"]);
});

test("the newest run is at the top, and December does not come before February", () => {
  const rows = [
    job({ key: "FEB", date: "10/02/2026" }),
    job({ key: "DEC", date: "10/12/2026" }),
    job({ key: "AUG", date: "02/08/2026" }),
  ];
  assert.deepEqual(receiptChoices(rows).map((one) => one.key), ["DEC", "AUG", "FEB"]);
});

test("a job is named the way somebody looking for it holds it in their head", () => {
  assert.equal(receiptLabel(job({ destination: "W/H AMPACET" })),
    "02/08/2026 · AMPACET · W/H AMPACET · 6317309804");
  // Without a consignee it says so rather than leaving a gap in the line.
  assert.match(receiptLabel(job({ customer: "" })), /ไม่ระบุผู้รับ/);
});
