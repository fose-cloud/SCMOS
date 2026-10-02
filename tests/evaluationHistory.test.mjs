import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { CERTIFICATE_STATE, HISTORY_SOURCE, latestCertificates, newestFirst } from "../app/scmos/annualEvaluation.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("periods run newest first by their year, campaigns and imported years together", () => {
  assert.deepEqual(newestFirst(["2025", "AE-2026", "2024", "2026"]), ["AE-2026", "2026", "2025", "2024"]);
  assert.deepEqual(newestFirst([]), []);
});

test("a carrier shows each certificate's newest record", () => {
  const rows = [
    { id: 1, type: "q-mark", year: 2025, state: "declared" }, { id: 2, type: "q-mark", year: 2026, state: "valid" },
    { id: 3, type: "iso-9001", year: 2025, state: "not-held" },
  ];
  assert.deepEqual(latestCertificates(rows).map((one) => one.id).sort(), [2, 3]);
});

test("every certificate state and history source the API can send has words on screen", () => {
  const certificates = read("server/Scmos.Api/Rules/SupplierCertificates.cs");
  const compliance = read("server/Scmos.Api/Rules/SupplierCompliance.cs");
  const states = [
    certificates.match(/public const string Declared = "([a-z-]+)";/)[1], certificates.match(/public const string NotHeld = "([a-z-]+)";/)[1],
    ...["Valid", "Expiring", "Expired", "NoExpiry"].map((name) => compliance.match(new RegExp(`public const string ${name} = "([a-z-]+)";`))[1]),
  ];
  for (const state of states) assert.ok(CERTIFICATE_STATE[state], state);
  assert.equal(CERTIFICATE_STATE.declared.label.includes("ยังไม่ยืนยัน"), true, "a declaration never reads as a valid certificate");
  const entities = read("server/Scmos.Api/Data/SupplierEntities.cs");
  for (const name of ["ScmosSource", "LegacyImport", "CampaignSource"]) {
    const source = entities.match(new RegExp(`public const string ${name} = "([a-z-]+)";`))[1];
    assert.ok(HISTORY_SOURCE[source], source);
  }
});

test("the certificate editor offers exactly the API's certificate types, in its order", () => {
  const server = [...read("server/Scmos.Api/Rules/SupplierCertificates.cs").matchAll(/\("([a-z0-9-]+)", "([^"]+)"\)/g)].map((match) => [match[1], match[2]]);
  const screen = read("app/scmos/screens/EvaluationHistory.tsx");
  const offered = [...screen.match(/const CERTIFICATE_TYPES = \[(.+?)\] as const;/)[1].matchAll(/\["([a-z0-9-]+)", "([^"]+)"\]/g)].map((match) => [match[1], match[2]]);
  assert.ok(server.length >= 5);
  assert.deepEqual(offered, server);
});

test("an import sends the workbook itself again, never the figures shown in the preview", () => {
  const screen = read("app/scmos/screens/EvaluationHistory.tsx");
  assert.match(screen, /form\.append\("file", file\);/);
  assert.match(screen, /form\.append\("chosen", JSON\.stringify\(picks\)\);/);
  assert.match(screen, /`\/api\/annual-evaluations\/history\/\$\{mode\}`/);
  assert.doesNotMatch(screen, /form\.append\("rows"|finalPercent: row/);
  const endpoints = read("server/Scmos.Api/Endpoints/AnnualEvaluationEndpoints.cs");
  for (const route of ['MapGet("/history"', 'MapPost("/history/preview"', 'MapPost("/history/import"', 'MapPost("/history/certificates"',
    'MapPut("/history/certificates/{certificate:long}"']) assert.ok(endpoints.includes(route), route);
  assert.match(read("app/scmos/screens/AnnualEvaluation.tsx"), /<EvaluationHistory canManage=\{canManage\} onToast=\{onToast\} \/>/);
});
