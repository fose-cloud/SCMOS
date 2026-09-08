import assert from "node:assert/strict";
import test from "node:test";

import {
  receiptChoices, receiptDestination, receiptHead, receiptItem, receiptLabel, receiptTruck,
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
  assert.equal(head.deliveryDate, "02/08/2026");
  assert.equal(head.truckNo, "SSL · 70-1234 กรุงเทพ", "the haulier and the plate");
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
  // planned, signed as what happened. On CCL-04-01 they are heading fields,
  // not columns of the item table.
  const head = receiptHead(job());
  assert.equal(head.truckIn, "");
  assert.equal(head.truckOut, "");
});

test("pallets and kilos land in the heading block that holds them", () => {
  const head = receiptHead(job({ pallet: "10", kgs: "10000" }));
  assert.equal(head.packages, "10");
  assert.equal(head.grossWeight, "10000");
});

test("kilos keyed in the general grid's column still fill the form", () => {
  // The Domestic import writes them to both; a job entered by hand elsewhere
  // has only `weight`. Either way the receipt should not go out empty.
  assert.equal(receiptHead(job({ kgs: "", weight: "8400" })).grossWeight, "8400");
  assert.equal(receiptHead(job({ kgs: "10000", weight: "8400" })).grossWeight, "10000",
    "the Domestic column wins when both are there");
});

test("the item table is filled per column, because the columns differ per customer", () => {
  // Which references a customer wants beside the product is the one thing that
  // varies between copies of this form, so the mapping answers per heading
  // rather than returning a fixed row.
  const one = job({ dCode: "D10445447", deliverNo: "6317309804", pallet: "10", kgs: "10000" });
  assert.equal(receiptItem(one, "D-Code"), "D10445447");
  assert.equal(receiptItem(one, "DELIVERY NO."), "6317309804");
  assert.equal(receiptItem(one, "No. of P'kg (s)"), "10");
  assert.equal(receiptItem(one, "QTY (KG)"), "10000");
  assert.equal(receiptItem(one, "NET WEIGHT (KGS)"), "10000");
});

test("what the register does not know is left blank, not invented", () => {
  // The UN class and the IM flag live in the customer's own paperwork. A screen
  // that guessed them would be writing a signed document out of nothing.
  const one = job();
  assert.equal(receiptItem(one, "UN NUMBER CLASS"), "");
  assert.equal(receiptItem(one, "IM"), "");
});

test("PO NO and PRODUCT NAME are filled from the Domestic grid", () => {
  /*
   * Asked for by the department. On their summary sheet the captions "SID
   * NUMBER" and "JOB NO." are swapped — ops.ts records it — so the column they
   * read as "JOB NO." holds the D-code, and that is what goes under PO NO.
   *
   * The fixture above carries neither, which is why this needs its own job:
   * asserting "blank" against a job with nothing on it proves nothing.
   */
  const one = job({ dCode: "D10092680", product: "R960 W38 EX55 25KG" });
  assert.equal(receiptItem(one, "PO NO", ["PO NO", "PRODUCT NAME"]), "D10092680");
  assert.equal(receiptItem(one, "PRODUCT NAME", ["PO NO", "PRODUCT NAME"]), "R960 W38 EX55 25KG");
  assert.equal(receiptItem(one, "PRODUCT"), "R960 W38 EX55 25KG", "PRODUCT alone is the same column");
});

test("a preset with its own D-code column does not print it twice", () => {
  // MERIT's receipt carries PO NO and Dcode side by side. Filling both from the
  // same value would put one number under two headings on a signed document.
  const one = job({ dCode: "D10092680" });
  const merit = ["PO NO", "Dcode", "PRODUCT NAME", "IM", "UN NUMBER CLASS", "NET WEIGHT (KGS)"];
  assert.equal(receiptItem(one, "Dcode", merit), "D10092680");
  assert.equal(receiptItem(one, "PO NO", merit), "");
});

test("the receipt's JOB NO. is the grid's SID NUMBER column", () => {
  // Same caption swap, the other way round: what they read as "SID NUMBER" is
  // the LSTH job number, and that is the job number the receipt carries.
  assert.equal(receiptHead(job({ jobCode: "260600800773" })).jobNo, "260600800773");
});

test("a vessel and a container are import lines and stay empty on a lorry", () => {
  // All seven real copies leave every one of these blank.
  const head = receiptHead(job());
  for (const field of ["vessel", "eta", "portOfDischarge", "containerNo", "agent", "receiverName"]) {
    assert.equal(head[field], "", field);
  }
});

test("the delivery note goes where the form asks for an invoice number", () => {
  // It is what the consignee's gate checks against; the SAP order underneath
  // is what the customer's own system calls the same movement.
  const head = receiptHead(job({ deliverNo: "6317309804", sapOrder: "8505076096" }));
  assert.equal(head.invoiceNo, "6317309804");
  assert.equal(head.blNo, "8505076096");
});

test("the vehicle line is composed onto the form from the job's truck counts", () => {
  assert.equal(receiptHead(job({ v6: "1", vtl: "1" })).vehicle, "1X6WH Tail Lift");
  assert.equal(receiptHead(job({ v6: "1", v10: "1" })).vehicle, "1X6WH , 1X10WH");
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
    "02/08/2026 · SSL · AMPACET · W/H AMPACET · 6317309804");
  // Without a consignee it says so rather than leaving a gap in the line.
  assert.match(receiptLabel(job({ customer: "" })), /ไม่ระบุผู้รับ/);
});

test("the haulier is in the picker, because two of them can run to one consignee", () => {
  // On a day like that the company is the only thing telling the rows apart.
  const ssl = receiptLabel(job({ trucker: "SSL" }));
  const kot = receiptLabel(job({ trucker: "THAI KOT" }));
  assert.notEqual(ssl, kot);
  assert.match(ssl, /SSL/);
});

test("the receipt names which truck came: the haulier and the plate", () => {
  // The form's second signature line is ลายมือชื่อผู้รับบรรทุก — the carrier's —
  // so the company belongs on the document. It goes into the one TRUCK NO.
  // field rather than into a caption the customer has never seen.
  assert.equal(receiptTruck(job({ trucker: "SSL", licence: "70-1234 กรุงเทพ" })), "SSL · 70-1234 กรุงเทพ");
});

test("a job with a haulier and no plate still names the haulier", () => {
  // Which is most Domestic jobs: the company was booked days before and the
  // plate is only known at the gate.
  assert.equal(receiptTruck(job({ trucker: "SSL", licence: "" })), "SSL");
  assert.equal(receiptHead(job({ trucker: "SSL", licence: "" })).truckNo, "SSL");
});

test("a plate with no haulier is still a plate", () => {
  assert.equal(receiptTruck(job({ trucker: "", licence: "70-1234 กรุงเทพ" })), "70-1234 กรุงเทพ");
  assert.equal(receiptTruck(job({ trucker: "", licence: "" })), "");
});
