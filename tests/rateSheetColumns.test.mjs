import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import { SHEET_COLUMNS, SHEET_VEHICLES } from "../app/scmos/rateSheetColumns.ts";

/** The vehicle codes the register actually prices, read out of the C#. */
function registerCodes() {
  const source = readFileSync("server/Scmos.Api/Rules/RateVehicles.cs", "utf8");
  return [...source.matchAll(/new\("([^"]+)"/g)].map(([, code]) => code);
}

test("every price column names a vehicle the register knows", () => {
  // A column writing a code the API refuses is a column that looks editable and
  // answers 400 on the first keystroke.
  const known = new Set(registerCodes());
  for (const code of SHEET_VEHICLES) {
    assert.ok(known.has(code), `the sheet prices "${code}", which the register does not`);
  }
});

test("every vehicle the register prices has somewhere to be typed", () => {
  // The other direction, which is how a rate becomes unquotable: the register
  // carries the code, the sheet has no column, and nobody can enter the price.
  const onSheet = new Set(SHEET_VEHICLES);
  for (const code of registerCodes()) {
    assert.ok(onSheet.has(code), `the register prices "${code}", which the sheet cannot enter`);
  }
});

test("no vehicle is priced by two columns", () => {
  // The workbook writes "Side Curtain truck NON-DG" twice. Two columns writing
  // one price would disagree the moment somebody used the second.
  assert.equal(new Set(SHEET_VEHICLES).size, SHEET_VEHICLES.length);
});

test("the columns before the prices are the workbook's own, in its order", () => {
  const heads = SHEET_COLUMNS.slice(0, 11).map((one) => one.head);
  assert.deepEqual(heads, [
    "Date", "No.", "Requestor", "Customer", "From", "To",
    "County", "Subcon", "FCL", "LCL", "Domestic",
  ]);
  assert.equal(SHEET_COLUMNS[SHEET_COLUMNS.length - 1].head, "Remark");
});

test("every column says what it writes", () => {
  for (const column of SHEET_COLUMNS) {
    if (column.kind === "price") assert.ok(column.vehicle, `${column.head} prices nothing`);
    else assert.ok(column.field, `${column.head} writes no field`);
  }
});
