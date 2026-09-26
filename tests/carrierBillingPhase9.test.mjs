import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const root = read("server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs");
const api = read("server/Scmos.Api/Endpoints/CarrierBillingApiEndpoints.cs");
const billing = read("server/Scmos.Api/Services/CarrierBillingService.cs");
const auth = read("server/Scmos.Api/Services/CarrierApiAuth.cs");

test("Phase 9 extends the authenticated rate-limited v1 carrier door", () => {
  assert.match(root, /MapGroup\("\/api\/carrier\/v1"\)[\s\S]*RequireRateLimiting\(RateLimitPolicy\)[\s\S]*AddEndpointFilter\(AuthenticateAsync\)/);
  assert.match(root, /CarrierBillingApiEndpoints\.Map\(v1\)/);
  assert.match(auth, /Supplier Company/);
});

test("Phase 9 covers the required POD and billing flow", () => {
  for (const route of [
    "/assignments/{jobKey}/pod",
    "/billing/eligible",
    "/billing/cases/{caseId:long}/draft",
    "/billing/invoices/{invoiceId:long}",
    "/billing/invoices/{invoiceId:long}/documents",
    "/billing/invoices/{invoiceId:long}/submit",
    "/billing/invoices/{invoiceId:long}/validation",
  ]) assert.ok(api.includes(route), `missing ${route}`);
});

test("all Phase 9 writes use the persistent idempotency ledger", () => {
  assert.match(api, /assignments\/\{jobKey\}\/pod[\s\S]*IdempotentAsync/);
  assert.match(api, /billing\/cases\/\{caseId:long\}\/draft[\s\S]*IdempotentAsync/);
  assert.match(api, /MapPut\("\/billing\/invoices\/\{invoiceId:long\}"[\s\S]*IdempotentAsync/);
  assert.match(api, /billing\/invoices\/\{invoiceId:long\}\/documents[\s\S]*IdempotentAsync/);
  assert.match(api, /billing\/invoices\/\{invoiceId:long\}\/submit[\s\S]*IdempotentAsync/);
  assert.match(api, /SHA256\.HashDataAsync/);
});

test("tenant identity comes only from the authenticated key and direct IDs are scoped", () => {
  assert.doesNotMatch(api, /record DraftBody\([^)]*SupplierId/);
  assert.match(api, /who\.Company\.Id/);
  assert.match(api, /OwnsHeldJobAsync\(who\.Company, jobKey/);
  assert.match(billing, /FindInvoiceForAsync\(int supplierId[\s\S]*row\.SupplierId == supplierId/);
  assert.match(billing, /CreateDraftForAsync[\s\S]*row\.Id == caseId && row\.SupplierId == supplierId/);
});

test("external document answers omit private blob coordinates and write TMS audit source", () => {
  const publicShape = api.slice(api.indexOf("private static object PublicDocument"));
  assert.doesNotMatch(publicShape, /ObjectKey|BlobUrl/);
  assert.match(api, /items = all[\s\S]*Select\(PublicBillingCase\)/);
  assert.match(api, /item = PublicBillingCase\(item\)/);
  assert.match(publicShape, /Documents = item\.Documents\.Select\(PublicDocument\)/);
  assert.match(api, /EventSource\.CarrierApi/);
  assert.match(billing, /AuditSourceOf\(user\)/);
});
