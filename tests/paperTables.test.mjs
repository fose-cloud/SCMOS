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

test("every screen but the control tower is drawn on paper", () => {
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  const chrome = readFileSync(new URL("../app/scmos/Chrome.tsx", import.meta.url), "utf8");
  const sheet = readFileSync(new URL("../app/globals.css", import.meta.url), "utf8");
  assert.match(app, /canvas=\{screen === "dashboard" \? "dark" : "light"\}/);
  assert.match(chrome, /<main className=\{dark \? undefined : "paper"\}/);
  // The stylesheet's own retoned rules are put back under paper.
  for (const rule of [".paper .sc-card {", ".paper .ghost-btn:hover", ".paper .row-hover:hover", ".paper #scmos-screen-toolbar"]) {
    assert.ok(sheet.includes(rule), rule);
  }
});

/*
 * 21 Sep 2026: the settings modal's fields read black on navy. The skin turns
 * a field's white ground into a navy panel outside the paper but never touched
 * its ink, which is the browser's black unless the screen names one. The
 * stylesheet now gives every field the light ink by default and the dark one
 * on paper and in tables; the sign-in card, white and drawn raw, is paper.
 */
test("a field's default ink is light off the paper and dark on it", () => {
  const sheet = readFileSync(new URL("../app/globals.css", import.meta.url), "utf8");
  const login = readFileSync(new URL("../app/scmos/overlays/Login.tsx", import.meta.url), "utf8");
  assert.match(sheet, /^input, select, textarea \{ color: #EAF4FC; \}$/m);
  assert.match(sheet, /^input::placeholder, textarea::placeholder \{ color: #7FA5CC; \}$/m);
  assert.match(sheet, /^\.paper input, \.paper select, \.paper textarea,\ntable input, table select, table textarea \{ color: #16232F; \}$/m);
  assert.match(sheet, /^\.paper input::placeholder, \.paper textarea::placeholder,\ntable input::placeholder, table textarea::placeholder \{ color: #94A3B8; \}$/m);
  // The light rule comes first, so the paper's dark one wins by order as well as weight.
  assert.ok(sheet.indexOf("input, select, textarea { color: #EAF4FC; }") < sheet.indexOf(".paper input, .paper select"));
  assert.match(login, /<div className="paper" style=\{css\("position:fixed;inset:0;z-index:90/);
  // The settings modal's fields name no ink of their own: the default is what they get.
  const overlays = readFileSync(new URL("../app/scmos/overlays/Overlays.tsx", import.meta.url), "utf8");
  for (const field of overlays.match(/const (?:field|box) = "[^"]+"/g) ?? []) assert.doesNotMatch(field, /color:/);
});
