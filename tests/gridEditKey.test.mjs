import assert from "node:assert/strict";
import test from "node:test";

import { gridArrowTarget, gridEditIntent } from "../app/scmos/gridEditKey.ts";

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

test("arrow keys move one cell and stop at the outer row edges", () => {
  const fields = [undefined, "customer", "trucker", "jobCode"];
  assert.deepEqual(gridArrowTarget({ key: "ArrowDown" }, { row: 1, column: 2 }, 3, fields), { row: 2, column: 2 });
  assert.deepEqual(gridArrowTarget({ key: "ArrowDown" }, { row: 2, column: 2 }, 3, fields), { row: 2, column: 2 });
  assert.deepEqual(gridArrowTarget({ key: "ArrowUp" }, { row: 0, column: 2 }, 3, fields), { row: 0, column: 2 });
});

test("horizontal arrows skip control columns and never wrap into another row", () => {
  const fields = [undefined, "customer", undefined, "jobCode"];
  assert.deepEqual(gridArrowTarget({ key: "ArrowRight" }, { row: 1, column: 1 }, 3, fields), { row: 1, column: 3 });
  assert.deepEqual(gridArrowTarget({ key: "ArrowLeft" }, { row: 1, column: 3 }, 3, fields), { row: 1, column: 1 });
  assert.deepEqual(gridArrowTarget({ key: "ArrowLeft" }, { row: 1, column: 1 }, 3, fields), { row: 1, column: 1 });
});

test("arrow navigation leaves browser shortcuts alone", () => {
  const fields = [undefined, "customer"];
  assert.equal(gridArrowTarget({ key: "ArrowRight", ctrlKey: true }, { row: 0, column: 1 }, 1, fields), null);
  assert.equal(gridArrowTarget({ key: "x" }, { row: 0, column: 1 }, 1, fields), null);
});
