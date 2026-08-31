import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const screen = readFileSync(new URL("../app/scmos/screens/JobRotation.tsx", import.meta.url), "utf8");
const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
const endpoints = readFileSync(
  new URL("../server/Scmos.Api/Endpoints/RotationEndpoints.cs", import.meta.url), "utf8");
const service = readFileSync(
  new URL("../server/Scmos.Api/Services/RotationService.cs", import.meta.url), "utf8");

test("Job Rotation exposes row CRUD only through the supervisor capability", () => {
  assert.match(app, /canManage=\{able\("AssignJobs"\)\}/);
  assert.match(endpoints, /MapPost\("",/);
  assert.match(endpoints, /MapPut\("\/{id:long}"/);
  assert.match(endpoints, /MapDelete\("\/{id:long}"/);
  assert.match(endpoints, /CanManage\(AppUser user\) => user\.Can\(Capability\.AssignJobs\)/);
  assert.match(screen, /canManage \? \(/);
});

test("manual rotation choices come from the staff and subcontractor masters", () => {
  assert.match(endpoints, /MapGet\("\/options"/);
  assert.match(service, /db\.Staff\.AsNoTracking\(\)/);
  assert.match(service, /db\.Suppliers\.AsNoTracking\(\)/);
  assert.match(service, /ไม่พบผู้ขนส่งที่เลือกใน Subcontractor Master/);
  assert.match(screen, /ผู้รับผิดชอบมาจาก Staff Directory/);
  assert.match(screen, /เลือกจาก Subcontractor Master/);
});

test("the table offers add, edit and inline-confirmed delete controls", () => {
  assert.match(screen, /\+ เพิ่มลูกค้า/);
  assert.match(screen, />แก้ไข<\/button>/);
  assert.match(screen, />ยืนยันลบ<\/button>/);
  assert.match(screen, /deleteRotation\(id\)/);
});
