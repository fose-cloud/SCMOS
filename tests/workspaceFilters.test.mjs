import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
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

test("workspace uses checkbox pickers for assignee and the three period filters", () => {
  for (const label of ["ASSIGNED", "ปี", "เดือน", "วัน"]) {
    assert.match(workspace, new RegExp(`<FilterPickMany label="${label}"`));
  }
  assert.doesNotMatch(workspace, />ช่วงวันที่</);
  assert.doesNotMatch(workspace, /ws\.(from|to)/);
});

test("table removes the horizontal helper bar and provides Excel-like zoom", () => {
  assert.doesNotMatch(table, /กลับคอลัมแรก|เลื่อนดูคอลัมทางขวา/);
  assert.match(table, /type="range" min="50" max="150"/);
  assert.match(table, /scmos\.table\.zoom/);
});
