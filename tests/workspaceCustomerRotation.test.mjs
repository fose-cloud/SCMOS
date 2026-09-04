import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const workspace = readFileSync(
  new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");
const hook = readFileSync(
  new URL("../app/scmos/rotationCustomers.ts", import.meta.url), "utf8");
const endpoints = readFileSync(
  new URL("../server/Scmos.Api/Endpoints/RotationEndpoints.cs", import.meta.url), "utf8");
const service = readFileSync(
  new URL("../server/Scmos.Api/Services/RotationService.cs", import.meta.url), "utf8");

test("My Job customer choices come from the distinct Job Rotation customer master", () => {
  assert.match(endpoints, /MapGet\("\/customers"/);
  assert.match(endpoints, /rotation\.CustomersAsync\(token\)/);
  assert.match(service, /db\.RotationAssignments\.AsNoTracking\(\)/);
  assert.match(service, /\.Select\(row => row\.Customer\)\s*\.Distinct\(\)/);
  assert.match(hook, /apiFetch\("\/api\/rotation\/customers"/);
});

test("Import and Export customer cells are dropdowns and preserve off-master values", () => {
  assert.match(workspace, /useRotationCustomers\(\)/);
  // The cell still gets its options from the rotation master and still offers
  // the empty one — that list moved into `choicesFor`, which the dropdown and
  // a pasted value now both read, so the column cannot accept a name its own
  // dropdown does not show. Asserted through that function rather than as the
  // literal expression it used to be written as.
  assert.match(workspace, /edChoice\(j, "customer", choicesFor\("customer", j\)!/);
  assert.match(workspace, /case "customer": return \["", \.\.\.customers\.names\];/);
  assert.equal((workspace.match(/edCustomer\(j, \{ bold: true, w: 150 \}\)/g) ?? []).length, 2);
  assert.match(workspace, /ไม่มีใน Job Rotation/);
});
