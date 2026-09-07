import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_ITEM_COLUMNS, FORM_NO, ITEM_PRESETS, NOTE, SIGNATURES, TERMS, itemColumns,
} from "../app/scmos/cargoReceiptForm.ts";
// The vehicle line reads the job's own truck counts, so it lives with the
// job-to-receipt mapping rather than with the document's fixed text.
import { vehicleLine } from "../app/scmos/cargoReceipt.ts";

/*
 * ISO-FRM-TH-CCL-04-01, rebuilt from seven signed copies.
 *
 * The document's own text is pinned here because it is a controlled form: a
 * clause that drifts is a receipt the customer has not agreed to.
 */

test("the form says which form it is", () => {
  assert.equal(FORM_NO, "ISO-FRM-TH-CCL-04-01");
});

test("the conditions of carriage are the CCL wording, not the ADM one", () => {
  // The ADM form gave 24 hours to note damage and 7 days to send a claim
  // letter. This one gives 24 hours and no claim clause. Getting the two
  // mixed up would put terms on a receipt that nobody agreed to.
  assert.equal(TERMS.length, 2);
  assert.match(TERMS[0], /HAVE RECEIVED IN GOOD ORDER AND CONDITION/);
  assert.match(TERMS[1], /24 ชั่วโมง/);
  assert.match(TERMS[1], /WE CANNOT BE HELD RESPONSIBLE FOR LOSS OR DAMAGE/);
  assert.ok(!/7 วัน/.test(TERMS[1]), "the claim-letter clause belongs to the other form");
});

test("the ADR note is spelled as Thai, not as the PDF extractor read it", () => {
  // Extracting this text from the PDFs splits every combining vowel: ชำรุด
  // comes out as "ช ารุด" and สินค้า as "สินค ้า". Pasting that in would put
  // broken Thai on a signed document.
  assert.ok(!/ช ารุด|สินค ้า|ด าเนินการ|จ ากัด/.test(NOTE + TERMS.join("")));
  assert.match(NOTE, /ADR/);
  assert.match(NOTE, /1\.1\.4\.2\.1/);
});

test("three signature lines, and none of them carries a name", () => {
  // One of the seven copies has the officer's name typed in. Printing it on
  // every blank form would put somebody's signature under work they never saw.
  assert.equal(SIGNATURES.length, 3);
  assert.deepEqual(SIGNATURES.map((one) => one[1]), ["LESCHACO OFFICER", "TRUCK DRIVER", "CUSTOMER"]);
  assert.ok(!/Jiratchaya|Timrattana/.test(JSON.stringify(SIGNATURES)));
});

/* ------------------------------------------------------- the item table */

test("all four column shapes seen across the seven copies are here", () => {
  assert.equal(ITEM_PRESETS.length, 4);
  for (const preset of ITEM_PRESETS) {
    assert.ok(preset.columns.length >= 5, preset.id);
    assert.ok(preset.seen.length >= 1, preset.id);
  }
});

test("no preset carries the row number as a column", () => {
  // The table draws NO itself. A customer whose stored columns began with it
  // would get two of them.
  for (const preset of ITEM_PRESETS) {
    assert.ok(!preset.columns.includes("NO"), preset.id);
  }
});

test("a customer with stored columns gets theirs, not the default", () => {
  const theirs = ["D-Code", "PRODUCT", "QTY (KG)"];
  assert.deepEqual(itemColumns(theirs), theirs);
});

test("a customer with nothing stored gets the commonest shape, not an empty table", () => {
  assert.deepEqual(itemColumns(undefined), DEFAULT_ITEM_COLUMNS);
  assert.deepEqual(itemColumns([]), DEFAULT_ITEM_COLUMNS);
  assert.deepEqual(itemColumns(["", "  "]), DEFAULT_ITEM_COLUMNS, "blank entries are not columns");
});

/* ----------------------------------------------------- the vehicle line */

test("the vehicle line is composed from the counts the grid already carries", () => {
  // These are the exact strings the seven copies write.
  assert.equal(vehicleLine({ v6: "1" }), "1X6WH");
  assert.equal(vehicleLine({ v10: "2" }), "2X10WH");
  assert.equal(vehicleLine({ v6: "1", v10: "1" }), "1X6WH , 1X10WH");
});

test("a tail lift rides on the line rather than counting as its own truck", () => {
  // It is a property of the truck sent. All seven copies write it this way.
  assert.equal(vehicleLine({ v6: "1", vtl: "1" }), "1X6WH Tail Lift");
  assert.equal(vehicleLine({ vtl: "1" }), "", "a tail lift with no truck is not a vehicle line");
});

test("zero and blank are not a truck", () => {
  assert.equal(vehicleLine({}), "");
  assert.equal(vehicleLine({ v4: "0", v6: "", v10: undefined }), "");
  assert.equal(vehicleLine({ v4: "-1" }), "");
});

test("the trucks come out in size order, whatever order the fields are read in", () => {
  assert.equal(vehicleLine({ v10: "1", v4: "2", v6: "1" }), "2X4WH , 1X6WH , 1X10WH");
});
