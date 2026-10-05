import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const loreal = readFileSync("app/scmos/screens/Loreal.tsx", "utf8");

// 5 Oct 2026: the screen showed the typed loading times and the workbook read
// `column.read`, which is empty for the five movement columns — the customer's
// file went out with "Truck loading completed" blank.
test("L'OREAL workbook carries what the screen shows, movement times included", () => {
  const download = loreal.slice(loreal.indexOf("function downloadWorkbook"));
  assert.match(download, /COLUMNS\.map\(\(column\) => valueOf\(job, column, times\)\)/);
  assert.doesNotMatch(download, /column\.read\(/);
  assert.match(loreal, /function show\(job: Job, column: Column\): string \{\s*return valueOf\(job, column, times\);/);
  assert.match(loreal, /downloadWorkbook\(rows, times, periodLabel\(period\), onToast\)/);
  // The only place a movement column's value is read out of the milestones.
  assert.equal((loreal.match(/times\[job\.key\]\?\.\[MOVEMENT_STAGE/g) ?? []).length, 1);
});
