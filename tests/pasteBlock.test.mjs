import assert from "node:assert/strict";
import test from "node:test";
import { isSingleValue, planPaste, readClipboardGrid } from "../app/scmos/pasteBlock.ts";

/**
 * Pasting a block copied out of Excel.
 *
 * The gesture is the one the team already has: select a range in the workbook,
 * copy, click the cell it should start at here, paste. Everything below is
 * about landing that where a spreadsheet would land it.
 */

/** A grid five columns wide where column 2 is a tick box with no field. */
const FIELDS = ["customer", "trucker", undefined, "product", "destination"];
const fieldAt = (column) => FIELDS[column];
const SIZE = { rows: 4, columns: 5 };
const at = (row, column) => ({ row, column });

test("the clipboard is read as tab-separated rows", () => {
  assert.deepEqual(readClipboardGrid("a\tb\nc\td"), [["a", "b"], ["c", "d"]]);
  // Windows line endings, and the trailing newline a spreadsheet adds — that
  // is punctuation, not an empty row.
  assert.deepEqual(readClipboardGrid("a\tb\r\nc\td\r\n"), [["a", "b"], ["c", "d"]]);
  assert.deepEqual(readClipboardGrid("solo"), [["solo"]]);
});

test("one cell selected takes the whole block, spreading down and right", () => {
  // The reason this exists. Clamped to the selection, a 2x3 block pasted onto
  // one cell put one value in and dropped the other five without a word.
  const block = readClipboardGrid("ALLNEX\tSSL\nBERICAP\tSANGJA");
  const plan = planPaste(block, at(1, 0), at(1, 0), SIZE, fieldAt);

  assert.deepEqual(plan.cells.map((c) => [c.row, c.column, c.field, c.value]), [
    [1, 0, "customer", "ALLNEX"],
    [1, 1, "trucker", "SSL"],
    [2, 0, "customer", "BERICAP"],
    [2, 1, "trucker", "SANGJA"],
  ]);
  assert.equal(plan.rowsClipped, 0);
  assert.equal(plan.columnsClipped, 0);
});

test("a column with nothing behind it consumes its value rather than shifting the rest", () => {
  // The bug this replaced: the unwritable column was filtered out before values
  // were matched to columns, so everything after it moved one column left and a
  // destination was written into the product column, silently.
  const block = readClipboardGrid("ALLNEX\tSSL\tx\tSTEEL\tRAYONG");
  const plan = planPaste(block, at(0, 0), at(0, 0), SIZE, fieldAt);

  assert.deepEqual(plan.cells.map((c) => [c.column, c.field, c.value]), [
    [0, "customer", "ALLNEX"],
    [1, "trucker", "SSL"],
    [3, "product", "STEEL"],
    [4, "destination", "RAYONG"],
  ], "STEEL must land on product, not on the tick box's neighbour");
  assert.equal(plan.cellsUnwritable, 1, "the value over the tick box is counted, not moved");
});

test("what runs off the edge is counted, not silently lost", () => {
  // Sixty rows onto a page of fifty looks exactly like a paste that worked.
  const block = readClipboardGrid("a\tb\tc\nd\te\tf\ng\th\ti");
  const plan = planPaste(block, at(2, 3), at(2, 3), SIZE, fieldAt);

  assert.equal(plan.rowsClipped, 1, "three rows from row 2 of four");
  assert.equal(plan.columnsClipped, 1, "three columns from column 3 of five");
  // Only what fits is written.
  assert.deepEqual(plan.cells.map((c) => [c.row, c.column]), [[2, 3], [2, 4], [3, 3], [3, 4]]);
});

test("one value fills the selection instead of one cell", () => {
  // Putting one carrier on forty rows is most of what this is used for, and
  // asking for forty copies of it on the clipboard would be the wrong answer.
  const block = readClipboardGrid("SSL");
  assert.ok(isSingleValue(block));
  const plan = planPaste(block, at(0, 1), at(2, 1), SIZE, fieldAt);

  assert.deepEqual(plan.cells.map((c) => [c.row, c.column, c.value]), [
    [0, 1, "SSL"], [1, 1, "SSL"], [2, 1, "SSL"],
  ]);
});

test("a single value never runs past what is loaded", () => {
  const plan = planPaste(readClipboardGrid("SSL"), at(0, 1), at(99, 1), SIZE, fieldAt);
  assert.equal(plan.cells.length, SIZE.rows, "one per loaded row, and no more");
});

test("a short line in the block ends there rather than emptying the rest", () => {
  // The second row of the copied range had two cells, not three. The cells past
  // it keep what they had — a paste that never mentioned them should not clear
  // them.
  const block = [["a", "b", "c"], ["d", "e"]];
  const plan = planPaste(block, at(0, 0), at(0, 0), { rows: 4, columns: 5 },
    (c) => ["f0", "f1", "f2", "f3", "f4"][c]);

  assert.deepEqual(plan.cells.map((c) => [c.row, c.column, c.value]), [
    [0, 0, "a"], [0, 1, "b"], [0, 2, "c"],
    [1, 0, "d"], [1, 1, "e"],
  ]);
});

test("values are trimmed, because a spreadsheet's are not", () => {
  const plan = planPaste(readClipboardGrid("  ALLNEX \t SSL  "), at(0, 0), at(0, 0), SIZE, fieldAt);
  assert.deepEqual(plan.cells.map((c) => c.value), ["ALLNEX", "SSL"]);
});

test("pasting onto the last cell writes exactly one value", () => {
  const plan = planPaste(readClipboardGrid("a\tb\nc\td"), at(3, 4), at(3, 4), SIZE, fieldAt);
  assert.deepEqual(plan.cells.map((c) => [c.row, c.column, c.value]), [[3, 4, "a"]]);
  assert.equal(plan.rowsClipped, 1);
  assert.equal(plan.columnsClipped, 1);
});
