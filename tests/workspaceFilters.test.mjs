import assert from "node:assert/strict";
import { globSync, readFileSync } from "node:fs";
import test from "node:test";

import { chosenIn, matchesChosen, pickLabel } from "../app/scmos/filterChoices.ts";

const workspace = readFileSync(new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");
const table = readFileSync(new URL("../app/scmos/DataTable.tsx", import.meta.url), "utf8");

test("multi-filter values preserve pipes and legacy empty sentinels", () => {
  assert.deepEqual(chosenIn("ALL"), []);
  assert.deepEqual(chosenIn("All Team"), []);
  assert.deepEqual(chosenIn("2025|2026"), ["2025", "2026"]);
  assert.equal(matchesChosen("2026", "2025|2026"), true);
  assert.equal(matchesChosen("2024", "2025|2026"), false);
});

test("closed multi-picker labels show the first formatted value and remaining count", () => {
  assert.equal(pickLabel("07|08", (month) => `month-${month}`), "month-07 +1");
  assert.equal(pickLabel("ALL"), "ALL");
});

test("workspace uses checkbox pickers for assignee, type and the three period filters", () => {
  for (const label of ["ASSIGNED", "TYPE", "ปี", "เดือน", "วัน"]) {
    assert.match(workspace, new RegExp(`<FilterPickMany label="${label}"`));
  }
  assert.doesNotMatch(workspace, />ช่วงวันที่</);
  assert.doesNotMatch(workspace, /ws\.(from|to)/);
});

test("period filters and their totals share the main filter bar", () => {
  assert.doesNotMatch(workspace, /const periodControls/);
  assert.match(workspace, /controls: controlBar/);
  assert.match(workspace, /จาก \{catBase\.length\} งานในหมวดนี้/);
  assert.match(workspace, /วันที่ใช้ไม่ได้ \{undated\}/);
});

test("table removes the horizontal helper bar and provides Excel-like zoom", () => {
  assert.doesNotMatch(table, /กลับคอลัมแรก|เลื่อนดูคอลัมทางขวา/);
  // The control itself moved to TableFrame when the plainer screens needed it
  // too. What this asserts is that the grid still shows one, and takes it from
  // there rather than keeping a second copy.
  assert.match(table, /<ZoomBar zoom=\{zoom\}/);
  assert.doesNotMatch(table, /type="range"/, "the grid must not carry its own slider");
});

test("every screen's zoom is the same control, remembered in one place", () => {
  // Written twice it drifts: two sliders with different limits, or two keys, and
  // the size of the type changes as you move between screens.
  const frame = readFileSync(new URL("../app/scmos/TableFrame.tsx", import.meta.url), "utf8");
  assert.match(frame, /type="range" min=\{LIMIT\.min\} max=\{LIMIT\.max\}/);
  assert.match(frame, /scmos\.table\.zoom/);

  // And nowhere else keeps a key or a slider of its own.
  const screens = globSync("app/scmos/screens/*.tsx").concat(["app/scmos/DataTable.tsx"]);
  for (const file of screens) {
    const source = readFileSync(file, "utf8");
    assert.doesNotMatch(source, /scmos\.table\.zoom/, `${file} keeps its own zoom key`);
    assert.doesNotMatch(source, /type="range" min="50"/, `${file} keeps its own slider`);
  }
});

test("fullscreen table covers app chrome without hiding global overlays", () => {
  const match = table.match(/position:fixed;inset:0;z-index:(\d+);/);
  assert.ok(match, "fullscreen layer should be declared");

  const layer = Number(match[1]);
  assert.ok(layer > 45, "fullscreen table must cover the app header and mobile rail");
  assert.ok(layer < 50, "global drawers, pickers and modals must stay above fullscreen");
});

test("the grid's own search belongs to full screen and nowhere else", () => {
  // The app's header carries a search everywhere except full screen, which
  // covers that header — that is what it is for. Two search boxes on one page
  // would be one too many, and each would mean something different.
  assert.match(table, /\{full && model\.search &&/);

  // Escape is also how full screen is left. A search with something in it takes
  // the first press, so clearing a filter does not throw away the screen it was
  // typed on; an empty box lets it through.
  assert.match(table, /if \(draft\.length === 0\) return;/);
  assert.match(table, /event\.stopPropagation\(\)/);
});
