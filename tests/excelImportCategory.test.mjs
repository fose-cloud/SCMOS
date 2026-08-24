import assert from "node:assert/strict";
import test from "node:test";

import {
  declaredImportCategory,
  inferImportCategory,
  schemaImportCategory,
  sheetImportCategory,
} from "../app/scmos/excelImportCategory.ts";

test("recognises export worksheet names in English abbreviations and Thai", () => {
  for (const name of ["Export Aug 2026", "EXP PLAN", "Outbound", "แผนส่งออก", "งานขาออก"]) {
    assert.equal(sheetImportCategory(name), "EXPORT", name);
  }
});

test("recognises category cells used by operator workbooks", () => {
  assert.equal(declaredImportCategory("EXP"), "EXPORT");
  assert.equal(declaredImportCategory("ส่งออก"), "EXPORT");
  assert.equal(declaredImportCategory("ขาเข้า"), "IMPORT");
  assert.equal(declaredImportCategory("จัดส่ง"), "DELIVERY");
});

test("infers export from ABS or from booking and closing fields", () => {
  assert.equal(schemaImportCategory(["date", "customer", "abs"]), "EXPORT");
  assert.equal(schemaImportCategory(["date", "customer", "booking", "closingDate"]), "EXPORT");
});

test("a declared row category overrides the worksheet name", () => {
  assert.equal(inferImportCategory("Export", "Import", ["jobCode"]), "EXPORT");
  assert.equal(inferImportCategory("Import", "Export", ["abs"]), "IMPORT");
});

test("an unknown ordinary plan keeps the safe Import default", () => {
  assert.equal(inferImportCategory("", "Plan", ["date", "customer", "jobCode"]), "IMPORT");
});
