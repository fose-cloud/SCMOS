import test from "node:test";
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
test("Tab moves left and Shift+Tab right, skips controls, and stops at edges", () => {
  const fields = [undefined, "customer", undefined, "fromPlace", "toPlace"];
  assert.deepEqual(gridTabTarget({ key: "Tab" }, { row: 0, column: 3 }, 1, fields, "left"), { row: 0, column: 1 });
  assert.deepEqual(gridTabTarget({ key: "Tab", shiftKey: true }, { row: 0, column: 3 }, 1, fields, "left"), { row: 0, column: 4 });
  assert.deepEqual(gridTabTarget({ key: "Tab" }, { row: 0, column: 1 }, 1, fields, "left"), { row: 0, column: 1 });
  assert.equal(gridTabTarget({ key: "Tab", ctrlKey: true }, { row: 0, column: 3 }, 1, fields, "left"), null);
});
