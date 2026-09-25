import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const service = read("server/Scmos.Api/Services/CarrierService.cs");
const access = read("server/Scmos.Api/Services/CarrierTenantContext.cs");
const documents = read("server/Scmos.Api/Services/DocumentService.cs");
const endpoints = read("server/Scmos.Api/Endpoints/CarrierEndpoints.cs");
const portal = read("app/scmos/screens/CarrierPortal.tsx");

test("Phase 3 reuses the registered carrier fleet and rejects foreign resources", () => {
  assert.match(service, /row\.SupplierId == company\.Id && row\.Status == "active"/);
  assert.match(service, /รถหรือคนขับไม่ได้อยู่ในทะเบียนที่ใช้งานของบริษัทนี้/);
  assert.match(service, /trailerId == truckId/);
});

test("truck and driver reassignment appends history instead of deleting evidence", () => {
  assert.match(service, /Kind = "carrier-resources"/);
  assert.match(service, /db\.WorkflowEvents\.Add\(new WorkflowEvent/);
  assert.match(service, /truck-driver/);
});

test("status updates use the existing job ladder, milestones and append-only history", () => {
  assert.match(service, /CarrierOperations\.Decide\(job\.Cat, job\.Status, type\)/);
  assert.match(service, /db\.ShipmentMilestones\.FirstOrDefaultAsync/);
  assert.match(service, /Kind = "carrier-status"/);
});

test("carrier operations endpoints do not accept a supplier id from the browser", () => {
  assert.match(endpoints, /\/{jobKey}\/resources/);
  assert.match(endpoints, /\/{jobKey}\/status/);
  assert.doesNotMatch(endpoints, /ResourcesBody\([^)]*SupplierId/);
});

test("known job ids are authorised from the confirmed assignment", () => {
  assert.match(access, /row\.Outcome == CarrierAssignment\.Confirmed/);
  assert.match(access, /CarrierAssignment\.BelongsTo/);
  assert.match(documents, /row\.Outcome == CarrierAssignment\.Confirmed/);
  assert.match(documents, /jobsWithHistory/);
});

test("the portal exposes fleet assignment, operational status, POD and history", () => {
  assert.match(portal, /บันทึกการจัดรถ/);
  assert.match(portal, /Delivery Complete/);
  assert.match(portal, /body\.append\("folder", "POD"\)/);
  assert.match(portal, /ประวัติการปฏิบัติงาน/);
});

test("the carrier schedule has the required operational views", () => {
  for (const view of ["today", "tomorrow", "week", "calendar", "unassigned", "active", "completed"])
    assert.match(portal, new RegExp(`scheduleView === "${view}"|"${view}",`));
  assert.match(portal, /jobDateKey\(job\.date\)/);
  assert.match(portal, /!job\.licence\.trim\(\)/);
});
