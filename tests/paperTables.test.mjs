import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { css, cssRaw, paperPalette } from "../app/scmos/theme.ts";

/*
 * The tables are paper on the navy. The skin translates every colour a screen
 * was drawn with into the tower's navy, and the tables — where people key for
 * a shift — were asked back to white. Rather than restyling forty screens,
 * every translated colour goes out as a CSS variable whose fallback is the
 * navy value, and `table`/`.paper` define the variables back to the light
 * ones. These pin that mechanism, since a colour that slips past it is a
 * navy cell in a white grid, or the other way round.
 */

test("a dark ink on a white ground goes out as a variable with the navy as its fallback", () => {
  const style = css("color:#0A2240;background:#fff;border:1px solid #D8E0E8");
  assert.equal(style.color, "var(--sk-ink-0A2240,#EAF4FC)");
  assert.equal(style.background, "var(--sk-ground-FFFFFF,#0C2338)");
  assert.equal(style.border, "1px solid var(--sk-line-D8E0E8,rgba(74,148,214,.22))");
});

test("a colour the skin leaves alone is written as it was, with no variable", () => {
  // A pale grey ink and a mid blue ground read on either ground.
  const style = css("color:#94A3B8;background:#2E7DD1");
  assert.equal(style.color, "#94A3B8");
  assert.equal(style.background, "#2E7DD1");
});

test("the paper palette remembers the light value for every variable it issued", () => {
  css("color:#16232F;background:#F8FAFC");
  const palette = paperPalette();
  assert.equal(palette.get("--sk-ink-16232F"), "#16232F");
  assert.equal(palette.get("--sk-ground-F8FAFC"), "#F8FAFC");
  assert.equal(palette.get("--sk-ink-0A2240"), "#0A2240");
});

test("cssRaw is untouched: the paper that prints keeps its literal colours", () => {
  assert.equal(cssRaw("color:#0A2240;background:#fff").color, "#0A2240");
});

test("the stylesheet puts a table's banding, hover and default ink back to the light ones", () => {
  const sheet = readFileSync(new URL("../app/globals.css", import.meta.url), "utf8");
  assert.match(sheet, /table \.row-hover:hover \{ background: #EAF2FB !important; \}/);
  assert.match(sheet, /table \.row-hover:nth-child\(even\) \{ background: #FAFCFE; \}/);
  assert.match(sheet, /table \{ color: #16232F; background: #FFFFFF; \}/);
});

test("the rail's mark says what the system is, and its globe turns", () => {
  const chrome = readFileSync(new URL("../app/scmos/Chrome.tsx", import.meta.url), "utf8");
  assert.match(chrome, /SUBCONTRACT MANAGEMENT<br \/>OPERATION SYSTEM/);
  assert.doesNotMatch(chrome, /SUPPLY CHAIN CONTROL TOWER/);
  // Six meridians sweeping in turn, and stilled for somebody who asked for less motion.
  assert.match(chrome, /const meridians = 6;/);
  assert.match(chrome, /<animate attributeName="rx" values=\{`\$\{R\};0;\$\{R\}`\}/);
  assert.match(chrome, /prefers-reduced-motion: reduce/);
});
