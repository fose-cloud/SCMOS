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
  ["app/scmos/screens/Workspace.tsx", "an HTML string built for the clipboard, never rendered"],
  // Wrapped where they are used rather than where they are written: each is a
  // helper whose caller puts a ZoomBox around it. Text alone cannot see that.
  ["app/scmos/screens/Kpi.tsx", "rendered inside Panel, which wraps its children"],
  ["app/scmos/screens/MonitorBoard.tsx", "rendered inside Card, which wraps its children"],
  // Dialogs size themselves and are read for a moment, not worked in.
  ["app/scmos/overlays/DataOverlays.tsx", "a modal with its own height"],
  ["app/scmos/overlays/ExcelOverlays.tsx", "a modal with its own height"],
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
