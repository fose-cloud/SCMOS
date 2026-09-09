import assert from "node:assert/strict";
import test from "node:test";

import { COPY_FONT_CSS, copyBlockPayload } from "../app/scmos/pasteBlock.ts";

/*
 * What leaves on the clipboard, not what is on the screen.
 *
 * A table copied out of a grid is pasted into Word or Outlook, where the
 * department's documents are written in Angsana New at 18 point. The grid
 * itself is untouched — a table on a screen and a table in a letter are not the
 * same object.
 */
test("a copied table is set in Angsana New at 18 point", () => {
  const { html } = copyBlockPayload([["TEMU0404097", "3"]], ["Container", "Qty"]);
  assert.match(html, /<table style="[^"]*Angsana New[^"]*font-size:18pt/);
});

/*
 * This file's first version asserted the face appeared exactly once, on the
 * table, "because Word keeps a table-level style and drops what is repeated
 * across a thousand cells". That was wrong, and it passed while being wrong.
 *
 * Outlook renders HTML through Word, which does not carry a face down from the
 * <table> element into the text inside a cell. A table pasted into a new mail
 * came out in Calibri 11 — the message's own default — with the table-level
 * rule ignored. The check said the payload was right; the paste said it was
 * not, and the paste was the one that counted.
 */
test("every cell carries the face, because Word will not inherit it", () => {
  const { html } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);

  // The lookahead matters: without it `<thead>` counts as a `<th`.
  const cells = html.match(/<t[dh](?=[\s>])[^>]*>/g) ?? [];
  assert.equal(cells.length, 6, "two headings and four values");
  for (const cell of cells) {
    assert.match(cell, /Angsana New/, `a cell without the face: ${cell}`);
    assert.match(cell, /font-size:18pt/, `a cell without the size: ${cell}`);
  }
});

test("and so does a span around every value, which is what Word actually reads", () => {
  const { html } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);

  const spans = html.match(/<span style="[^"]*">/g) ?? [];
  assert.equal(spans.length, 6, "one around each heading and each value");
  for (const span of spans) {
    assert.match(span, /Angsana New/);
    assert.match(span, /font-size:18pt/);
  }
});

test("the size is in points, because Word and Excel ignore a pixel one", () => {
  // 18 has to mean 18 in the document somebody pastes into.
  assert.match(COPY_FONT_CSS, /font-size:18pt/);
  assert.doesNotMatch(COPY_FONT_CSS, /font-size:\d+px/);
  const { html } = copyBlockPayload([["A"]], null);
  assert.doesNotMatch(html, /font-size:\d+px/);
});

test("the face falls back to something that can still draw Thai", () => {
  // A machine without Angsana New must not land on a face with no Thai glyphs.
  assert.match(COPY_FONT_CSS, /^font-family:'Angsana New'/);
  assert.match(COPY_FONT_CSS, /AngsanaUPC/);
  assert.match(COPY_FONT_CSS, /Cordia New/);
  assert.match(COPY_FONT_CSS, /serif;/);
});

test("the heading row keeps its colours as well as the face", () => {
  // The navy heading was asked for so a pasted table reads as a heading in a
  // mail rather than as a first row that happens to be bold. Repeating the font
  // must not have cost that — and the <font> tag is there for the same reason
  // the font is repeated: Outlook drops one or other of the two ways of saying
  // it, and a white word on a white cell is worse than no colour at all.
  const { html } = copyBlockPayload([["A", "1"]], ["Name", "Qty"]);
  assert.match(html, /bgcolor="#0A2240"/);
  assert.match(html, /<font color="#FFFFFF">/);
});

test("and the plain-text half carries no styling at all", () => {
  // Tabs and newlines are what a spreadsheet reads when cells are selected
  // rather than a document. A font has no business there.
  const { text } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);
  assert.equal(text, "Name\tQty\nA\t1\nB\t2");
  assert.doesNotMatch(text, /Angsana|font|style|span/);
});

test("an empty cell still gets the face, so a blank does not reset the row", () => {
  // Word takes the message default for anything it is not told about, and a
  // stray Calibri cell in the middle of a row is the kind of thing that is
  // noticed after the mail is sent.
  const { html } = copyBlockPayload([["", "1"]], null);
  const cells = html.match(/<td[^>]*>.*?<\/td>/g) ?? [];
  assert.equal(cells.length, 2);
  for (const cell of cells) assert.match(cell, /Angsana New/);
});
