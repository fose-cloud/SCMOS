import assert from "node:assert/strict";
import test from "node:test";

import { gridEditIntent } from "../app/scmos/gridEditKey.ts";

test("F2 and Enter edit while preserving the selected cell value", () => {
  assert.deepEqual(gridEditIntent({ key: "F2" }), { mode: "keep" });
  assert.deepEqual(gridEditIntent({ key: "Enter" }), { mode: "keep" });
});

test("a printable key starts a replacement edit, including Thai and shifted keys", () => {
  assert.deepEqual(gridEditIntent({ key: "W" }), { mode: "replace", value: "W" });
  assert.deepEqual(gridEditIntent({ key: "ง" }), { mode: "replace", value: "ง" });
  assert.deepEqual(gridEditIntent({ key: "@", shiftKey: true }), { mode: "replace", value: "@" });
  assert.deepEqual(gridEditIntent({ key: " " }), { mode: "replace", value: " " });
});

test("shortcuts and navigation keys do not start editing", () => {
  assert.equal(gridEditIntent({ key: "c", ctrlKey: true }), null);
  assert.equal(gridEditIntent({ key: "v", metaKey: true }), null);
  assert.equal(gridEditIntent({ key: "ArrowLeft" }), null);
  assert.equal(gridEditIntent({ key: "Escape" }), null);
  assert.equal(gridEditIntent({ key: "Enter", shiftKey: true }), null);
});
