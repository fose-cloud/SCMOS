import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const api = readFileSync("server/Scmos.Api/Endpoints/SupplierEndpoints.cs", "utf8");
const ui = readFileSync("app/scmos/screens/Suppliers.tsx", "utf8");
const service = readFileSync("server/Scmos.Api/Services/SupplierService.cs", "utf8");
const compliance = readFileSync("app/scmos/supplierCompliance.ts", "utf8");
const complianceRules = readFileSync("server/Scmos.Api/Rules/SupplierCompliance.cs", "utf8");
const notifications = readFileSync("server/Scmos.Api/Services/NotificationService.cs", "utf8");
const dashboard = readFileSync("server/Scmos.Api/Services/DashboardService.cs", "utf8");

test("supplier creation and details have a separate permission from approval and deletion", () => {
  assert.match(api, /suppliers.MapPost\("",[\s\S]*?Capability.EditSuppliers/);
  assert.match(api, /body.Status is null \? Capability.EditSuppliers : Capability.ManageSuppliers/);
  for (const route of ['suppliers.MapDelete("/{id:int}"', 'suppliers.MapPost("/{id:int}/status"', 'suppliers.MapPost("/merge"']) {
    const section = api.slice(api.indexOf(route));
    assert.match(section.slice(0, section.indexOf('));') + 3), /Capability.ManageSuppliers/);
  }
});

test("main supplier grid has selection, confirms deletion, and retains linked-record refusal", () => {
  assert.match(ui, /aria-label="เลือกผู้ขนส่งที่ลบได้ในผลค้นหาทั้งหมด"/);
  assert.match(ui, /onClick=\{e => e.stopPropagation\(\)\}/);
  assert.match(ui, /disabled=\{busy \|\| row.attached > 0\}/);
  const remove = ui.slice(ui.indexOf("async function removeChosen()"), ui.indexOf("async function remove(row"));
  assert.ok(remove.indexOf("window.confirm") < remove.indexOf('method: "DELETE"'));
  assert.match(remove, /!canManage/);
  const serverRemove = service.slice(service.indexOf("public async Task<SupplierResult> RemoveAsync"));
  assert.ok(serverRemove.indexOf("holdings.Total > 0") < serverRemove.indexOf("db.Suppliers.Remove"));
});

test("the details form offers every ASL/BSL column the table shows, and the API takes them", () => {
  // Asked for on 17 Sep 2026: the table showed fourteen procurement columns and
  // the form offered eight of them.
  for (const field of ["absNo", "listType", "creditTerm", "servicesRequired", "mainSpType", "typeOfService"]) {
    assert.ok(ui.includes(`text("${field}"`) || ui.includes(`value("${field}")`), `${field} is not on the form`);
  }
  assert.match(api, /string\? AbsNo = null, string\? ListType = null, string\? CreditTerm = null/);
  assert.match(api, /body.AbsNo, body.ListType, body.CreditTerm, body.ServicesRequired, body.MainSpType, body.TypeOfService/);
  // The ABS number is the join to procurement: one company per number.
  assert.match(service, /row.Id != id && row.AbsNo == absNo/);
  assert.match(service, /listType is not \("" or "ASL" or "BSL"\)/);
});

test("the company affidavit uploads without an expiry and can opt into the 60-day warning", () => {
  assert.match(compliance, /code: "affidavit"[\s\S]*?expires: true, expiryOptional: true/);
  assert.match(ui, /แจ้งเตือนเมื่อเอกสารจะหมดอายุภายใน \{WARNING_DAYS\} วัน/);
  assert.match(ui, /const ready = !asksExpiry \|\| wellFormed/);
  assert.match(ui, /onAttach\(files, asksExpiry \? expiry\.trim\(\) : ""\)/);
  assert.match(complianceRules, /need\.ExpiryOptional[\s\S]*?OrderByDescending\(pair => id\(pair\.Doc\)\)/);
  assert.match(service, /SupplierCompliance\.CurrentSupplierDocuments/);
  assert.match(notifications, /SupplierCompliance\.CurrentSupplierDocuments/);
  assert.match(dashboard, /SupplierCompliance\.CurrentSupplierDocuments/);
  assert.match(service, /SupplierCompliance\.MonitorsExpiry\(need, document\?\.ExpiryDate\)/);
});

test("documents with a future expiry show their remaining lifetime in days", () => {
  assert.match(compliance,
    /\(state === "valid" \|\| state === "expiring"\) && daysLeft !== null[\s\S]*?เหลือ \$\{daysLeft\} วัน/);
});

test("supplier documents can upload several selected files in one action", () => {
  assert.match(ui, /async function upload\(supplierId: number, files: File\[\]/);
  assert.match(ui, /for \(const file of files\)/);
  assert.equal(ui.match(/<input type="file" multiple/g)?.length, 2);
  assert.equal(ui.match(/const files = Array\.from\(e\.target\.files \?\? \[\]\)/g)?.length, 2);
  assert.match(ui, /onAttach\(files, asksExpiry \? expiry\.trim\(\) : ""\)/);
  assert.match(ui, /อัปโหลดเอกสารสำเร็จ \$\{uploaded\} ฉบับ/);
});
