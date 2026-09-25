import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const screen = read("app/scmos/screens/CarrierBillingFoundation.tsx");
const administration = read("app/scmos/screens/Administration.tsx");
const endpoints = read("server/Scmos.Api/Endpoints/CarrierBillingFoundationEndpoints.cs");
const documents = read("server/Scmos.Api/Services/DocumentService.cs");

test("Phase 1 administration reads and writes the real calendar and SLA endpoints", () => {
  assert.match(administration, /<CarrierBillingFoundation canManage=\{dir\.canManage\}/);
  assert.match(screen, /\/api\/carrier-billing\/foundation\/calendar\?from=/);
  assert.match(screen, /\/api\/carrier-billing\/foundation\/calendar\/\$\{day\.date\}/);
  assert.match(screen, /\/api\/carrier-billing\/foundation\/sla/);
  assert.match(screen, /method: "PUT" \| "DELETE"/);
});

test("the SLA editor makes Day 0 or Day 1 explicit and never supplies a hidden default", () => {
  assert.match(screen, /startDay: ""/);
  assert.match(screen, /<option value="">เลือกวันเริ่ม…<\/option>/);
  assert.match(screen, /<option value="day0">Day 0<\/option>/);
  assert.match(screen, /<option value="day1">Day 1<\/option>/);
  assert.match(screen, /!sla\.startDay/);
});

test("configuration writes are gated in both the screen and API with the existing MFA policy", () => {
  assert.match(screen, /if \(busy \|\| !canManage\) return false/);
  assert.match(endpoints, /user\.Can\(Capability\.AdministerData\)/);
  assert.match(endpoints, /NeedsSecondFactor\(users, user, Capability\.AdministerData\)/);
});

test("carrier document ownership is applied before the result limit", () => {
  const scoped = documents.indexOf("ListForCarrierAsync");
  const tenantWhere = documents.indexOf("row.SupplierId == carrier.SupplierId", scoped);
  const limit = documents.indexOf("Take(500)", tenantWhere);
  assert.ok(scoped >= 0 && tenantWhere > scoped && limit > tenantWhere);
  assert.match(documents, /row\.DriverId != null && driverIds\.Contains/);
  assert.match(documents, /row\.JobKey != "" && jobKeys\.Contains/);
});
