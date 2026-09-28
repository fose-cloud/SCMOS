import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const administration = read("app/scmos/screens/Administration.tsx");
const endpoints = read("server/Scmos.Api/Endpoints/StaffEndpoints.cs");
const staff = read("server/Scmos.Api/Services/StaffService.cs");
const tenant = read("server/Scmos.Api/Services/CarrierTenantContext.cs");

test("Administration requires a carrier company when creating or editing a Subcontractor", () => {
  assert.match(administration, /Field label="บริษัทขนส่ง \*"/);
  assert.match(administration, /form\.role === "Subcontractor" && !form\.supplierId/);
  assert.match(administration, /draft\.role === "Subcontractor" && !draft\.supplierId/);
  assert.match(administration, /supplierId: form\.role === "Subcontractor" \? Number\(form\.supplierId\) : null/);
  assert.match(administration, /supplierId: draft\.role === "Subcontractor" \? Number\(draft\.supplierId\) : null/);
  assert.match(administration, /ยังไม่ได้ผูกบริษัท/);
});

test("the staff API supplies only carrier choices and accepts stable supplier identity", () => {
  assert.match(endpoints, /CreateBody[\s\S]*int\? SupplierId/);
  assert.match(endpoints, /UpdateBody[\s\S]*int\? SupplierId/);
  assert.match(endpoints, /carriers = await staff\.CarrierOptionsAsync\(token\)/);
  assert.match(staff, /Where\(supplier => supplier\.IsCarrier\)/);
  assert.match(staff, /บทบาท Subcontractor ต้องเลือกบริษัทขนส่ง/);
  assert.match(staff, /supplier\.Id == supplierId && supplier\.IsCarrier/);
});

test("the saved staff supplier remains the server-owned Carrier Portal boundary", () => {
  assert.match(staff, /SupplierId = supplierId/);
  assert.match(tenant, /Human accounts use Staff\.SupplierId/);
  assert.match(tenant, /person\?\.SupplierId is not \{ \} supplierId/);
  assert.match(tenant, /row\.Id == supplierId && row\.IsCarrier/);
});
