import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("each scorecard count asks for its issues by the API's own column name", () => {
  const rules = read("server/Scmos.Api/Rules/ScorecardColumn.cs");
  const server = [...rules.matchAll(/public const string (TransportMajor|TransportMinor|LoadingAccident|Complaint|Breakdown) = "([^"]+)";/g)]
    .map((match) => match[2]);
  assert.equal(server.length, 5);
  const kpi = read("app/scmos/screens/Kpi.tsx");
  const block = kpi.slice(kpi.indexOf("const TALLY_COLUMNS"), kpi.indexOf("];", kpi.indexOf("const TALLY_COLUMNS")));
  const asked = [...block.matchAll(/, "([^"]+)"\],/g)].map((match) => match[1]);
  // The heading says "external", the API "External": the request must carry the API's spelling.
  assert.deepEqual([...asked].sort(), [...server].sort());
});

test("a count above nought opens the issues behind it; the ungraded note opens them too", () => {
  const kpi = read("app/scmos/screens/Kpi.tsx");
  assert.match(kpi, /value > 0 \? \(\s*<button type="button" onClick=\{\(\) => setDrill\(\{ carrier: row\.carrier, column, label: head \}\)\}/);
  assert.match(kpi, /setDrill\(\{ carrier: row\.carrier, column: UNGRADED/);
  assert.match(kpi, /<ScorecardIssues period=\{period\}/);
  // Re-tagging re-reads the scorecard.
  assert.match(kpi, /onChanged=\{\(\) => setVersion\(\(n\) => n \+ 1\)\}/);
  assert.match(kpi, /\}, \[period, setEngine, setReport, version\]\);/);
});

test("the panel reads, re-tags and links through the API's routes", () => {
  const panel = read("app/scmos/screens/ScorecardIssues.tsx");
  assert.match(panel, /apiFetch\(`\/api\/kpi\/scorecard\/issues\?\$\{query\}`/);
  assert.match(panel, /send\(`\/api\/issues\/\$\{row\.id\}`, "PATCH", \{ scorecardColumn: event\.target\.value \}/);
  assert.match(panel, /send\(`\/api\/issues\/\$\{row\.id\}\/case`, "PUT", \{ caseId: Number\(event\.target\.value\) \}/);
  assert.match(panel, /send\(`\/api\/issues\/\$\{row\.id\}\/case`, "PUT", \{ caseId: null \}/);
  assert.match(read("server/Scmos.Api/Endpoints/KpiEndpoints.cs"), /routes\.MapGet\("\/api\/kpi\/scorecard\/issues"/);
  assert.match(read("server/Scmos.Api/Endpoints/OperationalIssueEndpoints.cs"), /group\.MapPut\("\/\{id:long\}\/case"/);
});

test("a CAR/PAR opened from an issue carries the issue's id, from either screen", () => {
  const app = read("app/SCMOSApp.tsx");
  assert.equal(app.match(/issueId: issue\.id,/g)?.length, 2);
  assert.match(app, /onOpenCase=\{\(id\) => \{ setCaseFocus\(id\); go\("incident"\); \}\}/);
  const incidents = read("app/scmos/screens/Incidents.tsx");
  assert.match(incidents, /\.\.\.\(fromIssueId \? \{ issueId: fromIssueId \} : \{\}\)/);
  assert.match(read("server/Scmos.Api/Endpoints/SupplierEndpoints.cs"), /body\.Who \?\? "", body\.IssueId\)/);
  // Each side shows the other.
  assert.match(incidents, /Operational Issues · \{case_\.issues!\.length\}/);
  assert.match(read("app/scmos/screens/OperationalIssues.tsx"), /\{issue\.caseReference\} →/);
});

test("the dashboard's Accident and CAR/PAR cards are read over the chosen period", () => {
  const engine = read("server/Scmos.Api/Services/KpiEngine.cs");
  assert.match(engine, /cases = CasesIn\(period, rows\.Select\(row => row\.Key\)/);
  assert.ok(engine.indexOf("cases = CasesIn(") < engine.indexOf("Accident(cases)"));
});
