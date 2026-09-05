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

/**
 * And the third relationship, which is the one that was not being kept.
 *
 * The sheet and the register agreed. The calculator's card did not: it held the
 * eleven rates the team had written down, so seven columns of the sheet — side
 * curtain, flat-bed, open top, both Hiabs, the 6-wheel flatbed and the 40' tank
 * — were places to type a price and nothing the calculator would quote. The
 * card is now topped up from the register, and this says so.
 */
test("the calculator offers every vehicle the sheet can hold a price for", () => {
  const service = readFileSync(
    "server/Scmos.Api/Services/QuoteCardService.cs", "utf8");
  assert.match(service, /RateVehicles\.All[\s\S]{0,200}?!vehicle\.Dg && !known\.Contains\(vehicle\.Code\)/,
    "the card no longer fills itself from the register");
});

test("dangerous goods is a surcharge on a row, not a row of its own", () => {
  // The card carries one line per vehicle and the form carries a DG tick, so
  // "4W" plus the tick writes the "4W DG" column. A DG line on the card would
  // double the picker and give one journey two prices.
  const dgOnSheet = SHEET_VEHICLES.filter((code) => / DG$/.test(code));
  for (const code of dgOnSheet) {
    const base = code.replace(/ DG$/, "");
    assert.ok(SHEET_VEHICLES.includes(base),
      `the sheet prices "${code}" with no plain "${base}" for the tick to start from`);
  }
});
