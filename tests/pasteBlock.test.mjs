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

/* ----------------------------------------------------------- copying out */

import { copyBlockPayload } from "../app/scmos/pasteBlock.ts";

/**
 * The button is called "copy with headings" and it copies a table.
 *
 * My Job's version once preferred the selected rectangle when there was one,
 * which was harmless while a click opened an editor. Once a click left a
 * one-cell selection instead, the same rule turned the button into "copy this
 * cell" — reported twice, on two screens, for the same reason. The payload
 * builder is shared now; these pin what it must produce.
 */
test("the text flavour is what a spreadsheet reads", () => {
  const { text } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);
  assert.equal(text, "Name\tQty\nA\t1\nB\t2");
});

test("the html flavour carries a heading row, so a mail renders it as a table", () => {
  const { html } = copyBlockPayload([["A", "1"]], ["Name", "Qty"]);
  assert.match(html, /<thead>/);
  assert.match(html, /<th[^>]*>.*Name.*<\/th>/);
  // The value is inside the cell, wrapped rather than bare: Outlook renders
  // through Word, which will not carry a font down from the table into a cell's
  // text, so every value now sits in a span that names the face. What matters
  // here is that it is in a td at all.
  assert.match(html, /<td[^>]*>.*>1<.*<\/td>/);
});

test("a value that looks like markup is escaped, not rendered", () => {
  // A customer called "A & B <Ltd>" must not become a tag in somebody's inbox.
  const { html } = copyBlockPayload([["A & B <Ltd>"]], null);
  assert.match(html, /A &amp; B &lt;Ltd&gt;/);
  assert.doesNotMatch(html, /<Ltd>/);
});

test("an empty cell keeps its box rather than collapsing the row", () => {
  const { html } = copyBlockPayload([["", "x"]], null);
  assert.match(html, /&nbsp;/);
});

test("the heading colour is set three ways, because clients drop different ones", () => {
  // Outlook honours bgcolor, Excel the style, and a white word on a white cell
  // is worse than no colour at all.
  const { html } = copyBlockPayload([["a"]], ["H"]);
  assert.match(html, /bgcolor="#0A2240"/);
  assert.match(html, /background-color:#0A2240/);
  assert.match(html, /<font color="#FFFFFF">/);
});

test("no headings means no thead, not an empty one", () => {
  const { html } = copyBlockPayload([["a"]], null);
  assert.doesNotMatch(html, /<thead>/);
});

test("a value with a newline in it cannot break the row it is on", () => {
  // The rate register keeps addresses as typed — three lines and a maps link is
  // ordinary. Fifty of those once produced 121 lines for 50 rows, which puts
  // every row after the first long address one out in the spreadsheet.
  const { text } = copyBlockPayload([["A", "W/H SUZUYO\n136/103 Moo1\nชลบุรี"], ["B", "x"]], ["N", "Addr"]);
  assert.equal(text.split("\n").length, 3, "one heading and two rows, whatever is inside a cell");
  assert.match(text, /W\/H SUZUYO 136\/103 Moo1 ชลบุรี/);
});

test("a tab inside a value cannot open a column either", () => {
  const { text } = copyBlockPayload([["a\tb", "c"]], null);
  assert.equal(text.split("\t").length, 2, "two cells, not three");
});
