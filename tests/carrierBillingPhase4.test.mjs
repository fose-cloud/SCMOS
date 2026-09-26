import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const entities = read("server/Scmos.Api/Data/CarrierBillingEntities.cs");
const service = read("server/Scmos.Api/Services/CarrierBillingService.cs");
const carrier = read("server/Scmos.Api/Services/CarrierService.cs");
const endpoints = read("server/Scmos.Api/Endpoints/CarrierBillingEndpoints.cs");
const documents = read("server/Scmos.Api/Services/DocumentService.cs");
const migration = read("server/Scmos.Api/Data/Migrations/20260925135353_CarrierBillingPhase4.cs");
const portal = read("app/scmos/screens/CarrierPortal.tsx");
const control = read("app/scmos/screens/BillingControl.tsx");
const shell = read("app/SCMOSApp.tsx");

test("Delivery Complete automatically and idempotently opens one Billing Case", () => {
  assert.match(carrier, /CarrierOperations\.IsDeliveryComplete\(type\)[\s\S]*EnsureForDeliveryAsync/);
  assert.match(service, /row\.JobKey == job\.Key/);
  assert.match(entities, /HasIndex\(row => row\.JobKey\)\.IsUnique/);
  assert.match(migration, /billing_cases_job_idx[\s\S]*unique: true/);
});

test("billing eligibility reuses the original Job and confirmed carrier assignment", () => {
  assert.match(entities, /class BillingCase[\s\S]*string JobKey[\s\S]*int SupplierId[\s\S]*long AssignmentId/);
  assert.match(service, /row\.Outcome == CarrierAssignment\.Confirmed/);
  assert.match(service, /if \(assignment is null\) return \(null, false\)/);
  assert.doesNotMatch(entities, /class BillingJob|class BillingCarrier/);
});

test("SLA is snapshotted from the configured working-day calendar, never guessed", () => {
  assert.match(service, /calendar\.CalculateAsync\(DefaultSlaRuleCode, deliveredOn/);
  assert.match(service, /record\.SlaIssueCode = resolved\.Code/);
  assert.match(entities, /SlaStartDay[\s\S]*SlaTargetWorkingDays[\s\S]*SlaStartDate[\s\S]*SlaDueDate/);
});

test("carrier billing commands are tenant scoped and browser cannot select supplier", () => {
  assert.match(service, /CreateDraftForAsync\(user, tenant\.SupplierId, caseId/);
  assert.match(service, /row\.Id == caseId && row\.SupplierId == supplierId/);
  assert.match(service, /UpdateDraftForAsync\(user, tenant\.SupplierId, invoiceId/);
  assert.match(service, /row\.Id == invoiceId && row\.SupplierId == supplierId/);
  assert.doesNotMatch(endpoints, /DraftInput\([^)]*SupplierId/);
});

test("Invoice Draft links explicitly to eligible jobs and prevents duplicate case links", () => {
  assert.match(entities, /class BillingInvoiceJobLink/);
  assert.match(entities, /HasIndex\(row => row\.BillingCaseId\)\.IsUnique/);
  assert.match(service, /FirstOrDefaultAsync\(row => row\.BillingCaseId == caseId/);
});

test("billing documents reuse Blob Storage metadata and retain case and invoice identity", () => {
  assert.match(documents, /AddToBillingAsync/);
  assert.match(documents, /document\.BillingCaseId = billingCase\.Id/);
  assert.match(documents, /document\.BillingInvoiceId = invoice\.Id/);
  assert.match(portal, /\+ เพิ่มเอกสาร/);
});

test("internal and carrier screens read the real billing API with no demo warning", () => {
  assert.match(control, /\/api\/carrier-billing\/cases/);
  assert.match(portal, /สร้าง Invoice Draft/);
  assert.match(portal, /\/api\/carrier-billing\/invoices\/\$\{invoiceId\}/);
  assert.match(shell, /<BillingControl onToast=\{setToast\}/);
  assert.doesNotMatch(shell, /DEMO DATA[\s\S]*BillingAging/);
});
