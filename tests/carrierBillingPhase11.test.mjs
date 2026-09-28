import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const entity = read("server/Scmos.Api/Data/CarrierBillingFinanceEntities.cs");
const service = read("server/Scmos.Api/Services/FinanceIntegration.cs");
const worker = read("server/Scmos.Api/Services/FinanceOutboxDispatcher.cs");
const endpoint = read("server/Scmos.Api/Endpoints/CarrierBillingEndpoints.cs");
const control = read("app/scmos/screens/BillingControl.tsx");
const settings = JSON.parse(read("server/Scmos.Api/appsettings.json"));

test("Phase 11 uses the approved internal Finance queue and restricted business roles", () => {
  assert.equal(settings.CarrierBilling.FinanceIntegration.Enabled, true);
  assert.equal(settings.CarrierBilling.FinanceIntegration.Adapter, "SCMOS_INTERNAL");
  assert.deepEqual(settings.CarrierBilling.FinanceIntegration.ReleaseRoles,
    ["Administrator", "Manager", "Assistant Manager", "Operation Supervisor", "Operation User"]);
  assert.match(service, /interface IFinanceAdapter/);
  assert.match(service, /class InternalFinanceQueueAdapter/);
  assert.match(service, /SCMOS-FIN-/);
  assert.match(service, /class UnconfiguredFinanceAdapter/);
  assert.match(service, /environment\.IsDevelopment\(\).*environment\.IsEnvironment\("Test"\)/s);
});

test("canonical Finance snapshot and transactional outbox are durable", () => {
  for (const field of ["PayloadJson", "PayloadHash", "IdempotencyKey", "ExternalReference", "PaymentReference", "RowVersion"])
    assert.match(entity, new RegExp(`public .* ${field}`));
  assert.match(entity, /integration_outbox_idempotency_idx/);
  assert.match(service, /BeginTransactionAsync/);
  assert.match(service, /db\.IntegrationOutbox\.Add/);
  assert.match(service, /transaction\.CommitAsync/);
});

test("Finance release re-evaluates deterministic gates and is internal-only", () => {
  assert.match(service, /BillingFinanceReadiness\.Evaluate/);
  assert.match(service, /latestRun is null.*Blocking/s);
  assert.match(service, /CarrierTenantContext\.IsCarrier\(user\)/);
  assert.match(endpoint, /NeedsSecondFactor\(users, user, Capability\.ReviewBilling\)/);
  assert.match(endpoint, /\/finance\/records/);
  assert.match(service, /ListInternalAsync[\s\S]*CarrierTenantContext\.IsCarrier\(actor\)/);
  const sharedBillingProjection = read("server/Scmos.Api/Services/CarrierBillingService.cs");
  assert.doesNotMatch(sharedBillingProjection, /BillingFinanceRecords/);
  assert.match(sharedBillingProjection, /DescribeCarrierAsync/);
  assert.match(sharedBillingProjection, /CarrierBillingVisibility\.StorageStatuses/);
  assert.match(sharedBillingProjection, /CarrierBillingVisibility\.IsFinanceInternal/);
});

test("outbox retries with the same idempotency key and ends visibly", () => {
  assert.match(worker, /adapter\.SubmitAsync\(payload, record\.IdempotencyKey/);
  assert.match(worker, /record\.Adapter, adapter\.Name/);
  assert.match(worker, /FixedTimeEquals[\s\S]*record\.PayloadHash/);
  assert.match(worker, /FinanceRetry\.Next/);
  assert.match(worker, /OutboxStatus\.Dead/);
  assert.match(worker, /FinanceStatus\.Failed/);
});

test("Finance response and payment reconciliation are audited server-side", () => {
  assert.match(service, /ReconcileAsync/);
  assert.match(service, /ReconcileAsync[\s\S]*!Settings\.Enabled[\s\S]*DISABLED/);
  assert.match(service, /FinanceReconciliation\.CanApply/);
  assert.match(service, /audit\.Stage/);
  assert.match(worker, /audit\.Stage[\s\S]*integration/);
  assert.match(endpoint, /\/finance\/records\/\{recordId:long\}\/reconcile/);
});

test("Billing Control shows release, retry, status and reconciliation", () => {
  assert.match(control, /Phase 11 · Finance Integration/);
  assert.match(control, /ส่งเข้า Finance/);
  assert.match(control, /Retry Finance/);
  assert.match(control, /Finance response \/ reconciliation/);
});
