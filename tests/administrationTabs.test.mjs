import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const screen = read("app/scmos/screens/Administration.tsx");
const billing = read("app/scmos/screens/CarrierBillingFoundation.tsx");

test("Administration is four tabs, and only an administrator gets the clear tab (14 Oct 2026)", () => {
  assert.match(screen, /type AdminTab = "people" \| "roles" \| "calendar" \| "clear";/);
  assert.match(screen, /\{ id: "people", label: "ผู้ใช้", count: dir\.people\.length \}/);
  assert.match(screen, /\{ id: "roles", label: "สิทธิ์ตามบทบาท", count: dir\.roles\.length \}/);
  assert.match(screen, /\{ id: "calendar", label: "ปฏิทินและ SLA" \}/);
  assert.match(screen, /\.\.\.\(dir\.canManage \? \[\{ id: "clear" as const, label: "ล้างข้อมูลงาน" \}\] : \[\]\)/);
  assert.match(screen, /role="tablist" aria-label="Administration"/);
  // Every pane stays mounted and is only hidden, so a half-filled form survives a look at another tab.
  assert.match(screen, /hidden: shown !== id/);
  for (const id of ["people", "roles", "calendar", "clear"]) assert.match(screen, new RegExp(`\\{\\.\\.\\.pane\\("${id}"\\)\\}`));
});

test("Administration has no zoom control and no paragraphs about itself", () => {
  // The user asked for the zoom in/out to go (14 Oct 2026); both tables are plain, uncapped boxes.
  assert.equal((screen.match(/<ZoomBox capped=\{false\} zoomable=\{false\}>/g) ?? []).length, 2);
  assert.doesNotMatch(screen, /<ZoomBox>/);
  assert.doesNotMatch(screen, /ซึ่งเป็นตัวเดียวกับที่ API ใช้บังคับจริง/);
  assert.doesNotMatch(screen, /ไม่มีปุ่มลบ — คนที่ลาออก/);
  assert.doesNotMatch(screen, /บทบาทที่ระบบไม่รู้จักจะได้สิทธิ์เท่า Viewer/);
  assert.doesNotMatch(billing, /กำหนดวันทำการและกฎ SLA ที่จะใช้เมื่อเริ่ม Billing ใน Phase ถัดไป/);
  assert.doesNotMatch(billing, /ต้องระบุ Day 0 หรือ Day 1 และ 3 หรือ 4 วันทำการอย่างชัดเจน/);
  // Tab styles use whole border values: a shorthand beside a changing longhand made React warn on every switch.
  assert.match(screen, /const TAB_ON = ";color:#1D5FA8;border-bottom:2px solid #1D5FA8";/);
  assert.doesNotMatch(screen, /border-bottom-color/);
});
