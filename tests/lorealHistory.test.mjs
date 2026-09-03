import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const loreal = readFileSync("app/scmos/screens/Loreal.tsx", "utf8");
const app = readFileSync("app/SCMOSApp.tsx", "utf8");

test("L'OREAL history owns both register cells and movement milestones", () => {
  assert.match(loreal, /source: "register" \| "movement"/);
  assert.match(loreal, /onSetField\(job, field, typed, false\)/,
    "register edits must enter the report history without duplicating My Job history");
  assert.match(loreal, /const written = await writeMovement\(job, column, typed, true\)/,
    "movement edits must be saved before they enter history");
  assert.match(loreal, /source: "movement"[\s\S]*before: was, after: written\.saved/,
    "movement history must keep both directions");
  assert.match(app, /function setField\([^)]*recordHistory = true\): string/,
    "the shared register writer must return the stored value and allow report-owned history");
});

test("L'OREAL exposes the same undo and forward controls as My Job", () => {
  assert.match(loreal, /label: "↶ ย้อนกลับ"/);
  assert.match(loreal, /label: "↷ ถัดไป"/);
  assert.match(loreal, /editHistoryShortcut\(event\)/,
    "the report must reuse the tested Ctrl+Z and redo shortcut contract");
  assert.match(loreal, /if \(command === "undo"\) void undo\(\);[\s\S]*else void redo\(\);/);
});
