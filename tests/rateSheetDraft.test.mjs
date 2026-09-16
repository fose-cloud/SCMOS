import test from "node:test";
import { readFileSync } from "node:fs";
import assert from "node:assert/strict";
import { editRateDraft, missingForLane } from "../app/scmos/rateSheetColumns.ts";
import { gridTabTarget } from "../app/scmos/gridEditKey.ts";
const row = { laneId: -1, customer: "", fromPlace: "", toPlace: "", prices: {}, fcl: true };
test("pasted draft fields and prices stay local and preserve the original", () => {
  let next = editRateDraft(row, "customer", "Customer");
  next = editRateDraft(next, "fromPlace", "Bangkok");
  next = editRateDraft(next, "toPlace", "Rayong");
  next = editRateDraft(next, "price:20F", "1,200");
  assert.deepEqual(missingForLane(next), []);
  assert.equal(next.prices["20F"], 1200);
  assert.deepEqual(row.prices, {});
  assert.equal(row.customer, "");
  assert.deepEqual(editRateDraft(next, "price:20F", "").prices, {});
});
test("invalid prices and generated fields are rejected, never silently dropped", () => {
  for (const value of ["abc", "-1", "1.5", "2147483648"]) {
    assert.throws(() => editRateDraft(row, "price:20F", value));
  }
  for (const field of ["no", "requestor", "price:unknown"]) {
    assert.throws(() => editRateDraft(row, field, "1"));
  }
});
test("Tab moves right and Shift+Tab left on the rate sheet, skips controls, and stops at edges", () => {
  const fields = [undefined, "customer", undefined, "fromPlace", "toPlace"];
  // The sheet's own direction, since 16 Sep 2026 — the way a spreadsheet does it.
  assert.deepEqual(gridTabTarget({ key: "Tab" }, { row: 0, column: 1 }, 1, fields, "right"), { row: 0, column: 3 });
  assert.deepEqual(gridTabTarget({ key: "Tab", shiftKey: true }, { row: 0, column: 3 }, 1, fields, "right"), { row: 0, column: 1 });
  assert.deepEqual(gridTabTarget({ key: "Tab" }, { row: 0, column: 4 }, 1, fields, "right"), { row: 0, column: 4 });
  assert.equal(gridTabTarget({ key: "Tab", ctrlKey: true }, { row: 0, column: 3 }, 1, fields, "right"), null);
  // The rule still knows the other way round, for a grid that asks for it.
  assert.deepEqual(gridTabTarget({ key: "Tab" }, { row: 0, column: 3 }, 1, fields, "left"), { row: 0, column: 1 });
  const sheet = readFileSync(new URL("../app/scmos/screens/RateSheet.tsx", import.meta.url), "utf8");
  assert.match(sheet, /tabDirection: "right"/);
  assert.doesNotMatch(sheet, /"left"\)/);
});
