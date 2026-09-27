import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const root = read("server/Scmos.Api/Endpoints/CarrierApiEndpoints.cs");
const api = read("server/Scmos.Api/Endpoints/CarrierBillingApiEndpoints.cs");
const billing = read("server/Scmos.Api/Services/CarrierBillingService.cs");
const auth = read("server/Scmos.Api/Services/CarrierApiAuth.cs");
const ops = read("server/Scmos.Api/Endpoints/CarrierOperationsApiEndpoints.cs");
const carriers = read("server/Scmos.Api/Services/CarrierService.cs");
const rules = read("server/Scmos.Api/Rules/CarrierApi.cs");
const validation = read("server/Scmos.Api/Services/BillingValidationService.cs");
const workflow = read("server/Scmos.Api/Services/WorkflowService.cs");

test("Phase 9 extends the authenticated rate-limited v1 carrier door", () => {
  assert.match(root, /MapGroup\("\/api\/carrier\/v1"\)[\s\S]*RequireRateLimiting\(RateLimitPolicy\)[\s\S]*AddEndpointFilter\(AuthenticateAsync\)/);
  assert.match(root, /CarrierBillingApiEndpoints\.Map\(v1\)/);
  assert.match(root, /CarrierOperationsApiEndpoints\.Map\(v1\)/);
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
    "/billing/invoices/{invoiceId:long}/status",
    "/billing/invoices/{invoiceId:long}/original-package",
    "/billing/cases",
  ]) assert.ok(api.includes(route), `missing ${route}`);
  for (const route of ["/fleet", "/assignments/{jobKey}/resources", "/assignments/{jobKey}/status", "/assignments/{jobKey}/history"])
    assert.ok(ops.includes(route), `missing ${route}`);
});

test("all Phase 9 writes use the persistent idempotency ledger", () => {
  assert.match(api, /assignments\/\{jobKey\}\/pod[\s\S]*IdempotentAsync/);
  assert.match(api, /billing\/cases\/\{caseId:long\}\/draft[\s\S]*IdempotentAsync/);
  assert.match(api, /MapPut\("\/billing\/invoices\/\{invoiceId:long\}"[\s\S]*IdempotentAsync/);
  assert.match(api, /billing\/invoices\/\{invoiceId:long\}\/documents[\s\S]*IdempotentAsync/);
  assert.match(api, /billing\/invoices\/\{invoiceId:long\}\/submit[\s\S]*IdempotentAsync/);
  assert.match(api, /original-package[\s\S]*IdempotentAsync/);
  assert.match(ops, /assignments\/\{jobKey\}\/resources[\s\S]*IdempotentAsync/);
  assert.match(ops, /assignments\/\{jobKey\}\/status[\s\S]*IdempotentAsync/);
  assert.match(api, /SHA256\.HashDataAsync/);
});

test("tenant identity comes only from the authenticated key and direct IDs are scoped", () => {
  assert.doesNotMatch(api, /record DraftBody\([^)]*SupplierId/);
  assert.match(api, /who\.Company\.Id/);
  assert.match(api, /OwnsHeldJobAsync\(who\.Company, jobKey/);
  assert.match(billing, /FindInvoiceForAsync\(int supplierId[\s\S]*row\.SupplierId == supplierId/);
  assert.match(billing, /CreateDraftForAsync[\s\S]*row\.Id == caseId && row\.SupplierId == supplierId/);
});

test("external answers omit private blob coordinates and LESCHACO staff names, and write TMS audit source", () => {
  const document = api.slice(api.indexOf("internal static object PublicDocument"), api.indexOf("internal static object? PublicInvoice"));
  assert.doesNotMatch(document, /ObjectKey|BlobUrl/);
  const invoice = api.slice(api.indexOf("internal static object? PublicInvoice"), api.indexOf("private static object PublicBillingCase"));
  assert.doesNotMatch(invoice, /ActorName|ActorId|ReceivedByName|package\.SentBy/);
  assert.match(api, /items = items\.Select\(PublicBillingCase\)/);
  assert.match(api, /item = PublicBillingCase\(item\)/);
  assert.match(api, /Documents = item\.Documents\.Select\(PublicDocument\)/);
  assert.match(api, /Invoice = PublicInvoice\(item\.Invoice\)/);
  assert.match(api, /EventSource\.CarrierApi/);
  assert.match(billing, /AuditSourceOf\(user\)/);
});

test("BR-016: resources and status run the portal's own operations with the key's supplier", () => {
  assert.match(ops, /carriers\.AssignResourcesForAsync\(who\.AsUser\(\), who\.Company/);
  assert.match(ops, /carriers\.AdvanceForAsync\(who\.AsUser\(\), who\.Company/);
  assert.match(carriers, /AssignResourcesAsync\(AppUser user[\s\S]*return await AssignResourcesForAsync\(user, company/);
  assert.match(carriers, /AdvanceAsync\(AppUser user[\s\S]*return await AdvanceForAsync\(user, company/);
  assert.match(carriers, /EnsureForDeliveryAsync/);
  assert.doesNotMatch(ops, /record (ResourcesBody|StatusBody)\([^)]*(SupplierId|CarrierId)/);
});

test("direct writes need the department's existing trust; the governed routes stay for everyone", () => {
  assert.match(rules, /MayWriteDirectly\(string\? setting, bool keyMarked\) => AutoApplies\(setting, keyMarked\)/);
  assert.equal((ops.match(/CarrierApi\.MayWriteDirectly\(config\[CarrierApi\.AutoApplyKey\], who\.AutoApplyMarked\)/g) ?? []).length, 2);
  assert.match(root, /MapPost\("\/assignments\/\{jobKey\}\/events"/);
  assert.match(root, /MapPut\("\/assignments\/\{jobKey\}\/truck"/);
});

test("uploads are held to an allow-list whose bytes must agree", () => {
  assert.match(api, /CarrierApi\.UploadProblem\(file\.ContentType/);
  assert.match(api, /CarrierApi\.DocumentKind\(/);
  assert.doesNotMatch(rules.slice(rules.indexOf("UploadTypes")), /"text\/html"|"image\/svg\+xml"|"application\/octet-stream"/);
});

test("hand-opened transactions run inside the execution strategy (EnableRetryOnFailure refuses them otherwise)", () => {
  assert.match(validation, /CreateExecutionStrategy\(\)\.ExecuteAsync\(\(\) => SubmitOnceAsync/);
  assert.match(workflow, /CreateExecutionStrategy\(\)\.ExecuteAsync\(async \(\) =>\s*\{\s*await using var transaction/);
});
