import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { stripTypeScriptTypes } from "node:module";
import { DEFAULT_CARD, quote } from "../app/scmos/quoteRate.ts";

const source = await readFile(new URL("../app/scmos/quoteBatch.ts", import.meta.url), "utf8");
const resolved = source.replaceAll('"./quoteRate"', JSON.stringify(new URL("../app/scmos/quoteRate.ts", import.meta.url).href))
  .replaceAll('"./rateSheetColumns"', JSON.stringify(new URL("../app/scmos/rateSheetColumns.ts", import.meta.url).href));
const { quoteMany, quoteSheetVehicle } = await import(`data:text/javascript;base64,${Buffer.from(stripTypeScriptTypes(resolved)).toString("base64")}`);
const ask = (over = {}) => ({ vehicles: ["4W", "6W", "20F"], km: 119, dangerousGoods: false, marginPercent: 10, options: [], ...over });

test("several truck prices use the same journey, margin and extras independently", () => {
  const request = ask({ options: [{ id: "1", label: "Waiting", basis: "perHour", rate: 250, quantity: 2 }] });
  const batch = quoteMany(DEFAULT_CARD, request);
  assert.deepEqual(batch.refusals, []);
  assert.equal(batch.results.length, 3);
  for (const result of batch.results) {
    assert.deepEqual(result.quote, quote(DEFAULT_CARD, { ...request, vehicle: result.vehicle }));
    assert.equal(batch.prices[result.sheetVehicle], result.quote.total);
  }
});
test("all eleven current trucks can be quoted and saved together for non-DG", () => {
  const batch = quoteMany(DEFAULT_CARD, ask({ vehicles: DEFAULT_CARD.map((one) => one.code) }));
  assert.equal(Object.keys(batch.prices).length, 11);
  assert.deepEqual(batch.sheetRefusals, []);
});
test("selected duplicates do not create duplicate prices", () => {
  assert.equal(quoteMany(DEFAULT_CARD, ask({ vehicles: ["4W", "4W"] })).results.length, 1);
});
test("DG maps to DG price cells including refrigerated trucks and tanks", () => {
  for (const vehicle of ["4W", "4W RF", "6W RF", "10W", "20F", "40F", "20TK"]) {
    assert.equal(quoteSheetVehicle(vehicle, true), `${vehicle} DG`);
  }
});
test("container reefer DG is not silently saved in the non-DG column", () => {
  for (const vehicle of ["20RF", "40RF"]) {
    const result = quoteMany(DEFAULT_CARD, ask({ vehicles: ["4W", vehicle], dangerousGoods: true }));
    assert.equal(result.results[1].quote.refusals.length, 0);
    assert.ok(result.sheetRefusals.length);
    assert.deepEqual(result.prices, {});
  }
});
test("no selection, unknown truck, invalid distance or margin blocks whole save", () => {
  for (const over of [{ vehicles: [] }, { vehicles: ["4W", "unknown"] }, { km: 0 }, { km: -1 }, { km: Infinity }, { km: 3001 }, { marginPercent: NaN }, { marginPercent: -1 }, { marginPercent: 101 }]) {
    const batch = quoteMany(DEFAULT_CARD, ask(over));
    assert.ok(batch.refusals.length);
    assert.deepEqual(batch.prices, {});
  }
});
