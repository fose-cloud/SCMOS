import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const entity = read("server/Scmos.Api/Data/Entities.cs");
const model = read("server/Scmos.Api/Data/ScmosDbContext.cs");
const workflow = read("server/Scmos.Api/Services/WorkflowService.cs");
const carrier = read("server/Scmos.Api/Services/CarrierService.cs");
const workflowApi = read("server/Scmos.Api/Endpoints/WorkflowEndpoints.cs");
const carrierApi = read("server/Scmos.Api/Endpoints/CarrierEndpoints.cs");
const portal = read("app/scmos/screens/CarrierPortal.tsx");
const panel = read("app/scmos/screens/WorkflowPanel.tsx");
const migration = read("server/Scmos.Api/Data/Migrations/20260925031857_CarrierCollaborationPhase2.cs");

test("Phase 2 extends SupplierRequest as assignment history instead of creating another Job", () => {
  assert.match(entity, /class SupplierRequest[\s\S]*SupplierId[\s\S]*PreviousRequestId[\s\S]*RowVersion/);
  assert.doesNotMatch(entity, /class CarrierPortalJob|class BillingJob/);
});

test("the database and domain both enforce one active assignment", () => {
  assert.match(model, /supplier_requests_one_active_job_idx/);
  assert.match(model, /\[outcome\] IN \('pending','confirmed'\)/);
  assert.match(workflow, /CarrierAssignment\.Pending \|\| row\.Outcome == CarrierAssignment\.Confirmed/);
  assert.match(migration, /HAVING COUNT\(\*\) > 1[\s\S]*THROW 51002/);
});

test("reassignment preserves the old record and links the new offer", () => {
  assert.match(workflow, /active\.Outcome = CarrierAssignment\.Superseded/);
  assert.match(workflow, /PreviousRequestId = active\.Id/);
  assert.match(workflowApi, /\/\{jobKey\}\/reassign-carrier/);
  assert.match(panel, /เปลี่ยนผู้ขนส่ง/);
});

test("carrier answers name the exact assignment and stale assignments are refused", () => {
  assert.match(carrierApi, /AcceptBody\(long\? RequestId/);
  assert.match(carrierApi, /DeclineBody\(long\? RequestId/);
  assert.match(carrier, /assignment นี้ปิดหรือถูกแทนที่แล้ว/);
  assert.match(portal, /requestId: job\.requestId/);
  assert.match(workflow, /accept\/reject ต้องมาจาก Carrier/);
  assert.doesNotMatch(panel, /label="ยืนยันรับงาน"/);
  assert.match(carrierApi, /ResultCode\.NotOffered(?: or CarrierService\.ResultCode\.NotHeld)? => StatusCodes\.Status404NotFound/);
});

test("accept and reject replay without a second audit side effect", () => {
  assert.match(carrier, /AssignmentAnswerDecision\.Replay/);
  assert.match(carrierApi, /if \(!result\.Replayed\)/);
  assert.match(carrierApi, /replayed = result\.Replayed/);
});

test("stable supplier identity wins and aliases only support historical rows", () => {
  assert.match(carrier, /request\.SupplierId == company\.Id/);
  assert.match(carrier, /request\.SupplierId == null && spellings\.Contains/);
  assert.match(workflow, /ไม่พบ .* ใน Supplier Register/);
});

test("accepted work is a schedule projection of the original SCMOS Job", () => {
  assert.match(carrier, /IReadOnlyList<CarrierJob> Schedule/);
  assert.match(carrier, /schedule,\s*schedule,\s*trucks,\s*drivers\);/);
  assert.match(portal, /ตารางงาน/);
  assert.match(portal, /รอจัดรถและคนขับ/);
});

test("assignment changes use the existing audit and webhook infrastructure", () => {
  assert.match(workflow, /audit\.Stage\(user, AuditActions\.Update, "carrier-assignment"/);
  assert.match(workflow, /webhooks\.OfferedAsync/);
  assert.match(workflow, /webhooks\.CancelledAsync/);
});
