import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");
const overlay = read("../app/scmos/overlays/Overlays.tsx");
const workspace = read("../app/scmos/screens/Workspace.tsx");
const app = read("../app/SCMOSApp.tsx");
const endpoints = read("../server/Scmos.Api/Endpoints/JobsEndpoints.cs");
const service = read("../server/Scmos.Api/Services/JobTransferService.cs");

/*
 * Moving somebody's jobs to a colleague rewrites who owns work in bulk. The
 * rules for which jobs go are checked in C# (--check-job-transfer); these pin
 * the shape of the feature around them — that nothing is written without
 * being counted first, that the authority is the one the API enforces, and
 * that the grid's own dropdown goes through the path that keeps the id and
 * the name together.
 */

test("the settings panel offers a transfer beside the grant, only to those who may assign", () => {
  assert.match(overlay, /โยกงาน · เปลี่ยนชื่อผู้รับผิดชอบ/);
  assert.match(overlay, /มอบสิทธิ์แก้ไข · ชื่อเจ้าของงานคงเดิม/);
  // The same signal the "แทน" field reads: an empty owners list means no
  // authority to assign, and the toggle does not appear at all.
  assert.match(overlay, /const mayTransfer = owners\.length > 0;/);
  assert.match(overlay, /\{mayTransfer && \(/);
});

test("a transfer is counted before it is written, and asks before writing", () => {
  const form = overlay.slice(overlay.indexOf("function TransferJobs("));
  assert.match(form, /apiFetch\("\/api\/jobs\/transfer\/people"/);
  assert.match(form, /apiFetch\("\/api\/jobs\/transfer"/);
  // The preview button sends apply:false; the apply button only exists once
  // a preview has been shown, and confirms with the count it is about to move.
  assert.match(form, /onClick=\{\(\) => void run\(false\)\}/);
  assert.match(form, /\{preview && \(/);
  assert.match(form, /if \(apply && preview\) \{\s*const receiver[\s\S]*?window\.confirm\(/);
  assert.match(form, /โยก \$\{preview\.moving\} งานให้/);
  // Any edit to the form throws the preview away: the count was for another week.
  assert.match(form, /const update = [\s\S]*?setPreview\(null\);/);
  // The rows it moved come back by key, and the app changes the same rows in memory.
  assert.match(form, /onMoved\(outcome\.keys/);
  assert.match(app, /function jobsMoved\(keys: string\[\], owner: string, ownerId: string\)/);
  assert.match(app, /onJobsMoved=\{jobsMoved\}/);
});

test("the API gates both transfer routes on AssignJobs, the same authority as assigning one job", () => {
  const people = endpoints.slice(endpoints.indexOf('MapGet("/transfer/people"'), endpoints.indexOf('MapPost("/transfer"'));
  const move = endpoints.slice(endpoints.indexOf('MapPost("/transfer"'), endpoints.indexOf('MapGet("/changed"'));
  assert.match(people, /user\.Can\(Capability\.AssignJobs\)/);
  assert.match(move, /user\.Can\(Capability\.AssignJobs\)/);
  // And the receiver is judged by the delegation's rule, not a second one.
  assert.match(service, /DelegationService\.CanReceive\(receiver, from\)/);
  // Nothing is written without a reason; the audit row is the only record of why.
  assert.match(service, /if \(apply && Formats\.Clean\(reason\)\.Length < 4\)/);
  // One audit row per job, in the same words the grid's reassignment uses.
  assert.match(service, /audit\.Stage\(actor, AuditActions\.Assign, "job", job\.Key, label,\s*"ผู้รับผิดชอบ", job\.Owner, receiver\.Name/);
});

test("the grid's Assigned To column is a dropdown for assigners and goes through the bulk path", () => {
  const cell = workspace.slice(workspace.indexOf("const opCell = "), workspace.indexOf("const stCell = "));
  assert.match(cell, /if \(!canAssign\) return \{ \.\.\.cell\(j\.op/);
  // Through onBulkAssign so op and opId change together and the audit row
  // reads "Assigned To", exactly as a reassignment from the bulk bar does.
  assert.match(cell, /p\.onBulkAssign\(\[j\.key\], e\.target\.value\)/);
  // Every layout draws the same cell — four grids, one decision.
  assert.equal((workspace.match(/opCell\(j, mine\)/g) ?? []).length, 4);
});
