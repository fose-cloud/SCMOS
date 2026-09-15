import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { initialsOf, lastActiveLabel } from "../app/scmos/rotationPeople.ts";

/*
 * The people row on Job Rotation — the department's template: initials in a
 * tile, the address, what each person holds, and when they were last here.
 * The two readings that could mislead are pinned: the initials, and how a
 * time is said relative to today.
 */

test("initials come from two words, or two parts of an address, or the first two letters", () => {
  assert.equal(initialsOf("Maliwan Boonmee", "x@y"), "MB");
  assert.equal(initialsOf("Uthai", "uthai.yo@leschaco.com"), "UT");
  assert.equal(initialsOf("", "jiratchaya.ti@leschaco.com"), "JT");
  assert.equal(initialsOf("", "watsana@leschaco.com"), "WA");
});

test("when somebody was last here is said against today, and nothing is said for nobody", () => {
  const now = new Date(2026, 8, 15, 10, 30);
  assert.equal(lastActiveLabel(new Date(2026, 8, 15, 9, 14).toISOString(), now), "วันนี้ 09:14");
  assert.equal(lastActiveLabel(new Date(2026, 8, 14, 16, 40).toISOString(), now), "เมื่อวาน 16:40");
  assert.equal(lastActiveLabel(new Date(2026, 8, 5, 8, 45).toISOString(), now), "05/09/2026");
  assert.equal(lastActiveLabel("", now), "", "no entry, no time");
  assert.equal(lastActiveLabel("not a date", now), "");
});

test("the figures on the card are the register's, not the sheet's", () => {
  const service = readFileSync(new URL("../server/Scmos.Api/Services/RotationService.cs", import.meta.url), "utf8");
  // Open jobs only — a finished job is history, not a load.
  assert.match(service, /job\.Status != Rules\.JobStatus\.Completed\s*&& job\.Status != Rules\.JobStatus\.Cancelled/);
  // And the last action is the audit trail's, so it is a fact rather than a presence light.
  assert.match(service, /db\.AuditEvents\.AsNoTracking\(\)[\s\S]*?group\.Max\(entry => entry\.At\)/);
  const screen = readFileSync(new URL("../app/scmos/screens/JobRotation.tsx", import.meta.url), "utf8");
  assert.match(screen, /function PeopleRow\(/);
  assert.match(screen, /ยังไม่มีการใช้งานที่บันทึกไว้/);
});
