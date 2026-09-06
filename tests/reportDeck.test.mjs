import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const source = readFileSync("app/scmos/reportDeck.ts", "utf8");

/**
 * The module with its prose removed.
 *
 * The first version of this file failed on its own documentation: the comment
 * explaining why `addImage` is never called contains the word `addImage`. A
 * check that cannot tell a rule from a sentence about the rule is a check that
 * teaches people to delete the sentence.
 */
const deck = source
  .replace(/\/\*[\s\S]*?\*\//g, "")
  .replace(/^\s*\/\/.*$/gm, "");

/**
 * The deck writer, and the one thing it must never start doing.
 *
 * pptxgenjs reaches for `image-size` whenever a picture is added, and every
 * published version of that package carries a denial-of-service advisory in its
 * ICNS, JXL and HEIF parsers — with no patched release to move to and npm's own
 * suggested fix being a major downgrade of pptxgenjs itself.
 *
 * The reason that is acceptable here is that nothing in this module adds an
 * image: the slides are text, shapes, tables and native charts, so the parser is
 * never reached. That reasoning is only worth anything for as long as it stays
 * true, and "we happen not to use images" is a fact about today. This is the
 * fact about tomorrow.
 */
test("the deck never adds an image, which is what keeps image-size unreachable", () => {
  assert.doesNotMatch(deck, /\baddImage\b/,
    "an image in the deck reaches image-size, which has an unpatched DoS advisory");
  assert.doesNotMatch(deck, /\baddMedia\b/, "media goes through the same path");
  // The background option takes a picture too, by a different name.
  assert.doesNotMatch(deck, /background\s*:\s*\{[^}]*\b(path|data)\b/,
    "a slide background image is still an image");
});

/**
 * A rate is a claim about a sample, and the deck is where that claim travels
 * furthest from the person who could check it.
 */
test("no figure appears on a slide without the base it was measured over", () => {
  // Every tile that shows a rate also shows what it was measured over.
  assert.match(deck, /วัดได้ \$\{nf\(s\.measurable\)\} จาก \$\{nf\(s\.trips\)\}/,
    "the on-time tile must say what it was measured over");
  // And the methodology travels on every slide, because a slide gets separated
  // from the email that explained it.
  assert.match(source, /function footer\(/);
  assert.match(source, /แสดงจำนวนฐานกำกับ/);
});

test("an unmeasured figure is a dash, never a nought", () => {
  const rate = deck.match(/const rate = [^\n]*\n/)[0];
  assert.match(rate, /=== null \? "—"/,
    "a rate nobody could measure must not render as 0%");
});

test("the verdict is words as well as colour", () => {
  // The green and the red are 6.7 apart for a deuteranope, and a slide is read
  // from across a room.
  assert.match(deck, /text: "▲ ถึงเป้า"/);
  assert.match(deck, /text: "▼ ต่ำกว่าเป้า"/);
});

test("the chart leaves out samples too small to carry a rate, and says so", () => {
  assert.match(deck, /report\.vendors\.filter\(\(one\) => one\.otd !== null\)/);
  assert.match(deck, /อีก \$\{report\.vendors\.length - shown\.length\} รายไม่ถึง/);
});

/**
 * A megabyte of PowerPoint writer belongs in the browser of somebody who
 * pressed the button, not in the bundle of the other thirty-nine screens.
 */
test("pptxgenjs is loaded only when a deck is actually asked for", () => {
  assert.doesNotMatch(deck, /^import .*pptxgenjs/m,
    "a static import puts the library in the main bundle");
  assert.match(deck, /await import\("pptxgenjs"\)/);
});
