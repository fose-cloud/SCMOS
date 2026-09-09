import assert from "node:assert/strict";
import test from "node:test";

import { COPY_FONT_CSS, copyBlockPayload } from "../app/scmos/pasteBlock.ts";

/*
 * What leaves on the clipboard, not what is on the screen.
 *
 * A table copied out of a grid is pasted into Word or Excel, where the
 * department's documents are written in Angsana New at 18 point. The payload
 * used to say Segoe UI at 13px, so every pasted table had to be selected and
 * restyled by hand. The grid itself is untouched — a table on a screen and a
 * table in a letter are not the same object.
 */
test("a copied table is set in Angsana New at 18 point", () => {
  const { html } = copyBlockPayload([["TEMU0404097", "3"]], ["Container", "Qty"]);
  assert.match(html, /<table style="[^"]*Angsana New[^"]*font-size:18pt/);
});

test("the size is in points, because Word and Excel ignore a pixel one", () => {
  // 18 has to mean 18 in the document somebody pastes into.
  assert.match(COPY_FONT_CSS, /font-size:18pt/);
  assert.doesNotMatch(COPY_FONT_CSS, /font-size:\d+px/);
});

test("the face falls back to something that can still draw Thai", () => {
  // A machine without Angsana New must not land on a face with no Thai glyphs.
  assert.match(COPY_FONT_CSS, /^font-family:'Angsana New'/);
  assert.match(COPY_FONT_CSS, /AngsanaUPC/);
  assert.match(COPY_FONT_CSS, /Cordia New/);
  assert.match(COPY_FONT_CSS, /serif;/);
});

test("the face is set once, on the table, not on every cell", () => {
  // Word keeps a table-level style and drops much of what is repeated across a
  // thousand cells, so putting it on the table is both smaller and more likely
  // to survive the paste.
  const { html } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);
  assert.equal((html.match(/Angsana New/g) ?? []).length, 1);
});

test("and the plain-text half carries no styling at all", () => {
  // Tabs and newlines are what Excel reads when a cell is selected rather than
  // a document. A font has no business there.
  const { text } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);
  assert.equal(text, "Name\tQty\nA\t1\nB\t2");
  assert.doesNotMatch(text, /Angsana|font|style/);
});

test("the heading row keeps its colours as well as the face", () => {
  // The navy heading was asked for so a pasted table reads as a heading in a
  // mail rather than as a first row that happens to be bold. Changing the font
  // must not have cost that.
  const { html } = copyBlockPayload([["A", "1"]], ["Name", "Qty"]);
  assert.match(html, /bgcolor="#0A2240"/);
  assert.match(html, /<font color="/);
});
