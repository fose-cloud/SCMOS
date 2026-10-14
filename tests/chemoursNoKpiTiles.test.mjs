import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const workspace = readFileSync(new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");

test("The Chemours no longer draws the twelve summary tiles above its grid (14 Oct 2026)", () => {
  // The user: "ช่วยนำข้อมูลสรุปด้านบนออกจาก The Chemours ให้หน่อย ไม่จำเป็นต้องใช้งาน". My Job lost them earlier,
  // so neither screen draws them and their definitions are gone with them.
  assert.doesNotMatch(workspace, /KPI_DEFS/);
  assert.doesNotMatch(workspace, /function kpiCount/);
  assert.doesNotMatch(workspace, /p\.panels\.kpi/);
  // Its toolbar keeps the two panels nobody asked to remove.
  assert.match(workspace, /: \(\[\["process", "ขั้นตอนงาน"\], \["team", "ภาระทีม"\]\] as \[keyof PanelPrefs, string\]\[\]\)/);
  // The KPI filter itself stays: the KPI screen still opens the workspace on the jobs behind a figure.
  assert.match(workspace, /if \(ws\.kpi !== "All"\) activeFilters\.push\(\["KPI", ws\.kpi/);
});
