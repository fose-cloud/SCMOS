import assert from "node:assert/strict";
import { globSync, readFileSync } from "node:fs";
import test from "node:test";

/**
 * Every table a person reads scrolls sideways on its own and can be zoomed.
 *
 * The screens were written one at a time and each brought its own scroll box,
 * so twenty-five of twenty-six could be reached but not made smaller. They go
 * through ZoomBox now, and this is what stops the twenty-seventh being written
 * the old way — the check is cheap and the drift is otherwise invisible until
 * somebody is reading a twenty-column report on a laptop.
 */

/** Tables that are deliberately outside a wrapper, and why. */
const ALLOWED = new Map([
  ["app/scmos/DataTable.tsx", "the grid itself — it carries the same zoom already"],
  ["app/scmos/screens/CargoForm.tsx", "a printed sheet, not a grid: a zoom would change what comes out"],
  ["app/scmos/screens/ReportCentre.tsx", "the same: a report that is printed, and a zoom control would print with it — it scrolls on its own instead"],
  // Wrapped where they are used rather than where they are written: each is a
  // helper whose caller puts a ZoomBox around it. Text alone cannot see that.
  ["app/scmos/screens/Kpi.tsx", "rendered inside Panel, which wraps its children"],
  ["app/scmos/screens/MonitorBoard.tsx", "rendered inside Card, which wraps its children"],
  // Two narrow tables of three fixed columns each — a month and a number, and a
  // date and a number. Nothing to zoom out to see.
  ["app/scmos/screens/OilRate.tsx", "two three-column tables that fit any screen"],
  // Dialogs size themselves and are read for a moment, not worked in.
  ["app/scmos/overlays/DataOverlays.tsx", "a modal with its own height"],
  ["app/scmos/overlays/ExcelOverlays.tsx", "a modal with its own height"],
  // Zoom taken off at the department's request, the same as KPI and the
  // Supplier Register. It keeps its own overflow-x so a wide case list still
  // scrolls sideways — it is the zoom that went, not the scrolling.
  ["app/scmos/screens/Incidents.tsx", "zoom removed on request; scrolls on its own instead"],
]);

test("no screen grows a table outside the shared scroll box", () => {
  const bare = [];
  for (const file of globSync("app/**/*.tsx")) {
    const path = file.replaceAll("\\", "/");
    const source = readFileSync(file, "utf8");
    for (const found of source.matchAll(/<table\b/g)) {
      const before = source.slice(0, found.index);
      const open = (before.match(/<(?:ZoomBox|TableFrame)\b/g) ?? []).length;
      const shut = (before.match(/<\/(?:ZoomBox|TableFrame)>/g) ?? []).length;
      if (open - shut > 0) continue;
      if (ALLOWED.has(path)) continue;
      bare.push(`${path}:${before.split("\n").length}`);
    }
  }

  assert.deepEqual(bare, [],
    "these tables have no scroll box or zoom — wrap them in <ZoomBox>, "
    + "or add them to ALLOWED with the reason");
});

test("the list of exceptions is kept honest", () => {
  // An exception for a file that no longer has a bare table is an exception
  // nobody will question again. They are cheap to write and quietly outlive
  // their reason.
  for (const [path] of ALLOWED) {
    const source = readFileSync(path, "utf8");
    assert.match(source, /<table\b/, `${path} is excused but has no table any more`);
  }
});

test("the screens the department asked the zoom off stay without it", () => {
  // KPI and the Supplier Register earlier; Operational Issues on 15 Sep 2026.
  // Each keeps its ZoomBox — the sideways scroll and the fold — and drops
  // only the slider. Putting one back is a decision, not a refactor.
  for (const [path, pattern] of [
    ["app/scmos/screens/Kpi.tsx", /<ZoomBox zoomable=\{false\}/],
    ["app/scmos/screens/Suppliers.tsx", /<ZoomBox zoomable=\{false\}/],
    // Uncapped since 21 Sep 2026: fifty rows a page, the page scrolls.
    ["app/scmos/screens/OperationalIssues.tsx", /<ZoomBox capped=\{false\} zoomable=\{false\}>/],
    // The Shipment Monitor's cards, on 21 Sep 2026.
    ["app/scmos/screens/MonitorBoard.tsx", /<ZoomBox zoomable=\{false\}>\{children\}<\/ZoomBox>/],
    // LINE on 16 Sep 2026 — both its tables.
    ["app/scmos/screens/LineReview.tsx", /<ZoomBox zoomable=\{false\}>[\s\S]*<ZoomBox capped=\{false\} zoomable=\{false\}>/],
  ]) {
    assert.match(readFileSync(path, "utf8"), pattern, path);
  }
  assert.doesNotMatch(readFileSync("app/scmos/screens/OperationalIssues.tsx", "utf8"), /<ZoomBox>|<ZoomBox zoomable=\{false\}>/);
  assert.doesNotMatch(readFileSync("app/scmos/screens/MonitorBoard.tsx", "utf8"), /<ZoomBox>/);
  // Operational Issues exports what it shows, fifty rows a page; the header's fallback button — a toast and no file — is gone from it.
  const issues = readFileSync("app/scmos/screens/OperationalIssues.tsx", "utf8");
  assert.match(issues, /const ROWS_PER_PAGE = 50;/);
  assert.match(issues, /exportIssues\(rows, /);
  assert.match(issues, /paged\.slice\.map\(\(issue\)/);
  assert.match(readFileSync("app/SCMOSApp.tsx", "utf8"), /screen === "audit" \|\| screen === "issues"\) return \[\];/);
  assert.doesNotMatch(readFileSync("app/scmos/screens/LineReview.tsx", "utf8"), /<ZoomBox>|<ZoomBox capped=\{false\}>/);
  // The queue shows the room's own id when it is unbound, and offers to bind it from the row.
  const line = readFileSync("app/scmos/screens/LineReview.tsx", "utf8");
  assert.match(line, /\{event\.lineGroupId \|\| "\(ไม่มีรหัสกลุ่ม/);
  assert.match(line, /onBind=\{canMap \? \(\) => setBindGroupId\(one\.lineGroupId\) : undefined\}/);
});
