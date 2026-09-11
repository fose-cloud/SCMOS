import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const api = readFileSync("server/Scmos.Api/Endpoints/SupplierEndpoints.cs", "utf8");
const ui = readFileSync("app/scmos/screens/Suppliers.tsx", "utf8");
const service = readFileSync("server/Scmos.Api/Services/SupplierService.cs", "utf8");

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
