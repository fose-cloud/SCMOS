import assert from "node:assert/strict";
import test from "node:test";

import { COPY_FONT_CSS, copyBlockPayload } from "../app/scmos/pasteBlock.ts";
import {
  TABLE_FONT_CLIPBOARD, TABLE_FONT_CSS, TABLE_FONT_FAMILY, TABLE_FONT_SIZE_PT,
} from "../app/scmos/tableFont.ts";

/*
 * The grid and the clipboard used to set themselves: IBM Plex on screen at
 * 12.5px, Segoe UI on the clipboard at 13px. A table pasted into a mail looked
 * nothing like the table it was copied from, and nobody could see that from
 * either file on its own.
 */
test("what is copied is set in the same face as what is on screen", () => {
  // pasteBlock cannot import this — it is checked by the node runner, where a
  // value import of a sibling needs a .ts extension the compiler refuses — so
  // the two strings are asserted equal instead. This is the assertion that
  // makes them one fact rather than two.
  assert.equal(COPY_FONT_CSS, TABLE_FONT_CLIPBOARD);
});

test("the department asked for Angsana New at 18, and that is what both say", () => {
  assert.match(TABLE_FONT_FAMILY, /^'Angsana New'/);
  assert.equal(TABLE_FONT_SIZE_PT, 18);
  assert.match(TABLE_FONT_CSS, /font-size:18pt/);
  assert.match(COPY_FONT_CSS, /Angsana New/);
  assert.match(COPY_FONT_CSS, /font-size:18pt/);
});

test("the size is in points, because Word and Excel ignore a pixel one", () => {
  // The copied table is pasted into documents, where 18 has to mean 18.
  assert.doesNotMatch(COPY_FONT_CSS, /font-size:\d+px/);
  assert.doesNotMatch(TABLE_FONT_CSS, /font-size:\d+px/);
});

test("the face falls back to something that can still draw Thai", () => {
  // A machine without Angsana New must not land on a face with no Thai glyphs.
  assert.match(TABLE_FONT_FAMILY, /AngsanaUPC/);
  assert.match(TABLE_FONT_FAMILY, /Cordia New/);
  assert.match(TABLE_FONT_FAMILY, /serif$/);
});

test("a copied table carries the face on the table element itself", () => {
  // On the table, not on every cell: Word keeps a table-level style and drops
  // most of what is repeated on a thousand cells.
  const { html } = copyBlockPayload([["TEMU0404097", "3"]], ["Container", "Qty"]);
  assert.match(html, /<table style="[^"]*Angsana New[^"]*font-size:18pt/);
  assert.equal((html.match(/Angsana New/g) ?? []).length, 1,
    "the face is set once, not on every cell");
});

test("and the plain-text half is untouched by any of it", () => {
  // Tabs and newlines are what Excel reads. A font has no business there.
  const { text } = copyBlockPayload([["A", "1"], ["B", "2"]], ["Name", "Qty"]);
  assert.equal(text, "Name\tQty\nA\t1\nB\t2");
  assert.doesNotMatch(text, /Angsana|font/);
});
