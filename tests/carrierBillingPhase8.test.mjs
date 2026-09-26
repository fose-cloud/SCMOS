import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const rules = read("server/Scmos.Api/Rules/CarrierBillingControlTower.cs");
const service = read("server/Scmos.Api/Services/CarrierBillingControlTowerService.cs");
const endpoints = read("server/Scmos.Api/Endpoints/CarrierBillingEndpoints.cs");
const control = read("app/scmos/screens/BillingControl.tsx");
const portal = read("app/scmos/screens/CarrierPortal.tsx");

test("Phase 8 metrics are server rules over the reviewed calendar, not browser arithmetic", () => {
  assert.match(rules, /SubmissionLeadWorkingDays[\s\S]*BusinessCalendar\.IsWorkingDay/);
  assert.match(rules, /Within3WorkingDays[\s\S]*Within4WorkingDays[\s\S]*FirstTimeRight[\s\S]*ReturnRatePercent/);
  assert.match(service, /BusinessCalendarDays\.AsNoTracking/);
  assert.doesNotMatch(control, /function\s+SubmissionLeadWorkingDays|function\s+FirstTimeRight/);
});

test("carrier dashboard scope is decided from identity and ignores a browser supplier id", () => {
  assert.match(service, /CarrierTenantContext\.IsCarrier\(user\)[\s\S]*tenants\.ResolveAsync/);
  assert.match(service, /SupplierScope\(true, tenant\.SupplierId, requestedSupplierId\)/);
  assert.match(rules, /isCarrier \? tenantSupplierId : requestedSupplierId/);
});

test("control tower is bounded and returns the case ids behind every KPI", () => {
  assert.match(service, /to\.DayNumber - from\.DayNumber > 366/);
  assert.match(service, /Take\(5000\)/);
  assert.match(service, /record BillingControlTowerItem\(long CaseId/);
  assert.match(endpoints, /\/control-tower/);
  assert.match(control, /caseIds: metricItems\.filter/);
});

test("both internal and carrier screens use the real tenant-scoped control tower", () => {
  assert.match(control, /\/api\/carrier-billing\/control-tower/);
  assert.match(control, /Billing ≤3 Working Days/);
  assert.match(control, /Original Pending Aging/);
  assert.match(portal, /\/api\/carrier-billing\/control-tower/);
  assert.match(portal, /Carrier Acceptance/);
  assert.match(portal, /Truck Assignment Pending/);
});
