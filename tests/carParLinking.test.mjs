import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { ORIGINAL_COLUMNS, importedColumns } from "../app/scmos/incidentNote.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("the scorecard offers every case not yet linked — all stages, the API's list, no cut-off", () => {
  const screen = read("app/scmos/screens/ScorecardIssues.tsx");
  assert.match(screen, /apiFetch\("\/api\/incidents\/linkable"/);
  assert.doesNotMatch(screen, /stage !== "closed"/);
  assert.doesNotMatch(screen, /\.slice\(0, 80\)/);
  // After a link or an unlink the list is read again, so a case just taken is no longer offered.
  assert.match(screen, /await Promise\.all\(\[load\(\), loadCases\(\)\]\)/);
  const service = read("server/Scmos.Api/Services/IncidentService.cs");
  assert.match(service, /Where\(c => !linked\.Contains\(c\.Id\)\)/);
  const issues = read("server/Scmos.Api/Services/OperationalIssueService.cs");
  assert.match(issues, /row\.CaseId == wanted && row\.Id != id/);
});

test("an imported case's source row reads as columns, not as JSON", () => {
  const note = `${ORIGINAL_COLUMNS}\n${JSON.stringify({ "No.": "15", "Responsible\nBy": "Nattikorn", "Requirement\nEMS": "-", "Blank": "", "Detail": "line one\nline two" }, null, 2)}`;
  const { rest, columns } = importedColumns(note);
  assert.equal(rest, "");
  assert.deepEqual(columns, [["No.", "15"], ["Responsible By", "Nattikorn"], ["Detail", "line one\nline two"]]);
  assert.deepEqual(importedColumns("plain note"), { rest: "plain note", columns: [] });
  assert.deepEqual(importedColumns(`kept\n${ORIGINAL_COLUMNS}\n{not json`), { rest: `kept\n${ORIGINAL_COLUMNS}\n{not json`, columns: [] });
  // The heading is the importer's own words, so the two cannot drift apart.
  assert.ok(read("server/Scmos.Api/Data/IncidentImporter.cs").includes(`"\\n${ORIGINAL_COLUMNS}\\n"`));
  const screen = read("app/scmos/screens/Incidents.tsx");
  assert.match(screen, /importedColumns\(/);
  assert.match(screen, /<details/);
});
