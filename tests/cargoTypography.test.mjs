import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const css = readFileSync("app/globals.css", "utf8");
const form = readFileSync("app/scmos/screens/CargoForm.tsx", "utf8");

test("mobile large-input rule is screen-only, not applied to a printed receipt", () => {
  const mobile = css.indexOf("@media screen and (max-width: 900px)");
  const largeInput = css.indexOf("font-size: 16px !important", mobile);
  const nextMedia = css.indexOf("@media", mobile + 1);
  assert.ok(mobile >= 0 && largeInput > mobile && largeInput < nextMedia);
});

test("Cargo Receipt uses compact titles and readable 8pt printed values", () => {
  assert.match(form, /const BODY_PT = "8pt"/);
  assert.match(form, /const TITLE_PT = "14pt"/);
  assert.match(form, /const ITEM_PT = "9pt"/);
  const print = css.slice(css.indexOf("@media print"));
  assert.match(print, /\.cargo-page input, \.cargo-page textarea\s*\{[^}]*font-size: 8pt !important;[^}]*line-height: 1\.2 !important;/);
});
