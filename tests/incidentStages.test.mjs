import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import { STAGES, byStage, stageLabel } from "../app/scmos/incidentStages.ts";

test("CAR/PAR keeps horizontal scrolling without table zoom controls", () => {
  const source = readFileSync(new URL("../app/scmos/screens/Incidents.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(source, /ZoomBox/);
  assert.match(source, /overflowX: "auto"/);
});

test("every stage is reported, including the ones nothing is sitting at", () => {
  const counts = byStage([{ stage: "open" }, { stage: "open" }, { stage: "closed" }]);

  assert.equal(counts["เปิดเคส"], 2);
  assert.equal(counts["ปิดแล้ว"], 1);
  // "Nothing is waiting for approval" is a thing worth seeing. A row that
  // vanishes at zero makes a stalled pipeline look like a short one.
  assert.equal(counts["รออนุมัติ"], 0);
  assert.equal(Object.keys(counts).length, STAGES.length);
});

test("a stage the API grows later still counts, under its own name", () => {
  const counts = byStage([{ stage: "escalated" }]);
  assert.equal(counts.escalated, 1);
  assert.equal(stageLabel("escalated"), "escalated");
});

test("the dashboard reads the incident API rather than the demo bundle", () => {
  const dashboard = readFileSync(new URL("../app/scmos/screens/Dashboard.tsx", import.meta.url), "utf8");
  const tower = readFileSync(new URL("../app/scmos/screens/ControlTower.tsx", import.meta.url), "utf8");

  // The CAR/PAR card is the engine's own measure, counted from the incident
  // cases on the server — the same figure the KPI screen shows.
  assert.match(tower, /\/api\/kpi\/measures/, "the CAR/PAR card should read the measured report");
  assert.match(tower, /"CarPar"/, "and pick the CAR/PAR measure out of it");
  assert.doesNotMatch(dashboard + tower, /db\.carpar/, "no panel should count generated CAR/PAR data");
});

test("the stage vocabulary is defined once", () => {
  const incidents = readFileSync(new URL("../app/scmos/screens/Incidents.tsx", import.meta.url), "utf8");

  // The screen may use the names; it must not keep a second copy of them.
  assert.doesNotMatch(incidents, /const STAGES\s*=/);
  assert.doesNotMatch(incidents, /const STAGE_TH\s*[:=]/);
  assert.match(incidents, /from "\.\.\/incidentStages"/);
});

test("CAR/PAR is a status monitor: no entry form, a supervisor sets the stage or removes the case", () => {
  const screen = readFileSync(new URL("../app/scmos/screens/Incidents.tsx", import.meta.url), "utf8");
  // The eight-disciplines form and its gated "next step" are gone from the screen.
  assert.doesNotMatch(screen, /ขั้นตอนถัดไป/);
  assert.doesNotMatch(screen, /const SECTIONS/);
  assert.doesNotMatch(screen, /\/advance/);
  // A supervisor's two actions, each on its own route; closing asks why, deleting always does.
  assert.match(screen, /call\(`\/\$\{c\.id\}\/stage`, "POST", \{ stage, reason \}/);
  assert.match(screen, /if \(stage === "closed"\) \{[\s\S]*?askReason\(/);
  assert.match(screen, /call\(`\/\$\{c\.id\}\?reason=\$\{encodeURIComponent\(reason\)\}`, "DELETE"\)/);
  // Everyone else reads the stage; only canManage draws the select and the delete.
  assert.match(screen, /canManage \? \(\s*<select value=\{c\.stage\}/);
  assert.match(screen, /\{canManage && \([\s\S]*?remove\(c\)/);

  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.match(app, /canManage=\{able\("CloseCarPar"\)\}/);

  // The API side: both routes are supervisor-only in the service, and a
  // delete or a close without a reason is refused before anything is read.
  const service = readFileSync(new URL("../server/Scmos.Api/Services/IncidentService.cs", import.meta.url), "utf8");
  assert.match(service, /public async Task<IncidentResult> SetStageAsync\([\s\S]*?if \(!StaffDirectory\.IsSupervisor\(role\)\)/);
  assert.match(service, /public async Task<IncidentResult> DeleteAsync\([\s\S]*?if \(!StaffDirectory\.IsSupervisor\(role\)\)/);
  assert.match(service, /foreach \(var file in evidence\) file\.CaseId = null;/, "evidence is unlinked, not deleted");
  const routes = readFileSync(new URL("../server/Scmos.Api/Endpoints/SupplierEndpoints.cs", import.meta.url), "utf8");
  assert.match(routes, /incidents\.MapPost\("\/\{id:long\}\/stage"/);
  assert.match(routes, /incidents\.MapDelete\("\/\{id:long\}"/);
  assert.match(routes, /if \(closing && why\.Length < 4\)/);
  assert.match(routes, /if \(why\.Length < 4\)/);
  const actions = readFileSync(new URL("../server/Scmos.Api/Rules/AuditActions.cs", import.meta.url), "utf8");
  assert.match(actions, /\[CarrierChange, RateChange, Close, RetentionReview, BulkReplace, Delete\]/);
});
