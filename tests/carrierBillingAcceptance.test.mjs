import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const check = read("server/Scmos.Api/Data/CarrierBillingFinalAcceptanceCheck.cs");
const program = read("server/Scmos.Api/Program.cs");
const workflow = read(".github/workflows/api.yml");

test("final acceptance composes the lifecycle from Carrier acceptance through Finance close", () => {
  for (const rule of [
    "CarrierAssignment.DecideAnswer",
    "CarrierOperations.Decide",
    "BillingEligibility.IsEligible",
    "BusinessCalendar.Calculate",
    "BillingValidationRules.Document",
    "BillingValidationRules.Rate",
    "BillingValidationRules.Charge",
    "BillingValidationRules.Tax",
    "BillingValidationRules.Duplicate",
    "BillingReviewTransitions.NextStatus",
    "BillingFinanceReadiness.Evaluate",
    "InternalFinanceQueueAdapter",
    "FinanceReconciliation.CanApply",
  ]) assert.match(check, new RegExp(rule.replaceAll(".", "\\.")));
});

test("final acceptance directly covers tenant isolation and financial privacy", () => {
  assert.match(check, /CarrierTenantPolicy\.OwnsSupplier/);
  assert.match(check, /CarrierAssignment\.BelongsTo/);
  assert.match(check, /!CarrierTenantPolicy\.OwnsSupplier\(carrierA, 18\)/);
  assert.match(check, /CarrierBillingVisibility\.PublicStatus/);
});

test("final acceptance proves the auditable Job-to-payment relationship in the EF model", () => {
  for (const entity of [
    "OperationJob", "SupplierRequest", "BillingCase", "BillingInvoice",
    "BillingValidationRun", "BillingReviewEvent", "OriginalDocumentPackage",
    "BillingFinanceRecord", "IntegrationOutboxEvent",
  ]) assert.match(check, new RegExp(`typeof\\(${entity}\\)`));
  assert.match(check, /PaymentReference/);
  assert.match(check, /ReconciledBy/);
});

test("final acceptance is a mandatory offline API quality gate", () => {
  assert.match(program, /CarrierBillingFinalAcceptanceCheck\.Run\(args\)/);
  assert.match(workflow, /carrier-billing-phase11 carrier-billing-acceptance audit-revert/);
  assert.match(check, /no database, network, Production data, credential or external Finance system is used/);
});
