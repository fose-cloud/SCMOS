import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { spellingIndex, supplierKey } from "../app/scmos/supplierKey.ts";

const fixture = JSON.parse(readFileSync(new URL("./fixtures/supplier-keys.json", import.meta.url), "utf8"));

test("the screens reduce a company name to the key the API does — Thai letters kept (supplier-keys.json)", () => {
  for (const { input, key } of fixture.keys) assert.equal(supplierKey(input), key, JSON.stringify(input));
});

test("a spelling means its supplier by name, code, legal name or alias; one two share means neither", () => {
  const index = spellingIndex([
    { name: "ALPHA TRANSPORT", code: "ALP", legalName: "Alpha Transport Co., Ltd.", aliases: ["ALPHA", "SHARED"] },
    { name: "BRAVO LOGISTICS", code: "BRV", legalName: "Shared", aliases: [] },
    { name: "บริษัท ก.ไก่ ขนส่ง จำกัด", code: "", legalName: "", aliases: [] },
  ]);
  const of = (spelling) => index.get(supplierKey(spelling)) ?? null;
  assert.equal(of("alpha transport co., ltd."), "ALPHA TRANSPORT");
  assert.equal(of(" ALP "), "ALPHA TRANSPORT");
  assert.equal(of("Alpha"), "ALPHA TRANSPORT");
  assert.equal(of("BRAVO LOGISTICS"), "BRAVO LOGISTICS");
  assert.equal(of("SHARED"), null);
  assert.equal(of("บริษัท ก.ไก่ ขนส่ง จำกัด"), "บริษัท ก.ไก่ ขนส่ง จำกัด");
  // Before 30 Sep 2026 the screens kept A-Z and 0-9 only, so every Thai name was the empty key — one company.
  assert.equal(of("บริษัท ข.ไข่ จำกัด"), null);
  assert.equal(index.has(""), false);
});
