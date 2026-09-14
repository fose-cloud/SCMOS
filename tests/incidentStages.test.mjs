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
