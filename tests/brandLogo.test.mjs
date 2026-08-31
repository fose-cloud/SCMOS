import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const chrome = readFileSync(new URL("../app/scmos/Chrome.tsx", import.meta.url), "utf8");

test("the Leschaco wordmark has no white wrapper", () => {
  const mark = chrome.indexOf('<img src="/cargo-logo.png"');
  assert.notEqual(mark, -1, "the brand band should render the supplied wordmark");

  const wrapper = chrome.slice(Math.max(0, mark - 240), mark);
  assert.doesNotMatch(wrapper, /background:#fff|padding:5px 8px/);
});
