import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const bar = read("app/scmos/TabBar.tsx");
const screen = read("app/scmos/screens/Administration.tsx");
const billing = read("app/scmos/screens/CarrierBillingFoundation.tsx");
const kpi = read("app/scmos/screens/Kpi.tsx");

test("one tab bar for the screens split into tabs (14 Oct 2026)", () => {
  assert.match(bar, /role="tablist" aria-label=\{name\}/);
  assert.match(bar, /role="tab" id=\{`\$\{prefix\}-tab-\$\{one\.id\}`\} aria-controls=\{`\$\{prefix\}-pane-\$\{one\.id\}`\}/);
  // Hidden rather than unmounted, so a half-filled form survives a look at another tab.
  assert.match(bar, /hidden: shown !== id/);
  // Whole border values only: a shorthand beside a changing longhand made React warn on every switch.
  assert.match(bar, /const TAB_ON = ";color:#1D5FA8;border-bottom:2px solid #1D5FA8";/);
  assert.doesNotMatch(bar, /border-bottom-color/);
  for (const file of [screen, kpi]) {
    assert.match(file, /import \{ paneOf, shownTab, TabBar, type TabSpec \} from "\.\.\/TabBar";/);
    assert.doesNotMatch(file, /role="tablist"/, "the bar is the shared one, not a copy");
    // A display set on the pane itself wins over `hidden` and shows the pane on every tab (found in the KPI preview).
    assert.doesNotMatch(file, /\{\.\.\.pane\("[a-z]+"\)\} style=/);
  }
});

test("Administration is four tabs, and only an administrator gets the clear tab", () => {
  assert.match(screen, /type AdminTab = "people" \| "roles" \| "calendar" \| "clear";/);
  assert.match(screen, /\{ id: "people", label: "ผู้ใช้", count: dir\.people\.length \}/);
  assert.match(screen, /\{ id: "roles", label: "สิทธิ์ตามบทบาท", count: dir\.roles\.length \}/);
  assert.match(screen, /\{ id: "calendar", label: "ปฏิทินและ SLA" \}/);
  assert.match(screen, /\.\.\.\(dir\.canManage \? \[\{ id: "clear" as const, label: "ล้างข้อมูลงาน" \}\] : \[\]\)/);
  assert.match(screen, /<TabBar name="Administration" prefix="admin"/);
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
});

test("KPI keeps the period and six figures on top, then four tabs", () => {
  assert.match(kpi, /type KpiTab = "measures" \| "scorecard" \| "carriers" \| "work";/);
  // The measures and the scorecard are offered only when /api/kpi/measures answered.
  assert.match(kpi, /\.\.\.\(engine \? \[\{ id: "measures" as const, label: "ตัวชี้วัดหลัก", count: engine\.measures\.length \}\] : \[\]\)/);
  assert.match(kpi, /\.\.\.\(engine\?\.scorecard\?\.length \? \[\{ id: "scorecard" as const, label: "คะแนนผู้ขนส่ง"/);
  assert.match(kpi, /\{ id: "carriers", label: "ตรงเวลาแยกผู้ขนส่ง", count: measuredCarriers\.length \}/);
  assert.match(kpi, /\{ id: "work", label: "ภาระงานและสถานะ" \}/);
  for (const id of ["measures", "scorecard", "carriers", "work"]) assert.match(kpi, new RegExp(`\\{\\.\\.\\.pane\\("${id}"\\)\\}`));
  // The tiles come before the bar; the Excel download sits at the bar's end.
  assert.ok(kpi.indexOf('label="เสี่ยงตกเรือ (Export)"') < kpi.indexOf('<TabBar name="KPI" prefix="kpi"'));
  assert.match(kpi, /<TabBar name="KPI"[^>]*>\s*<button\s+onClick=\{\(\) => void download\(period, setExporting\)\}/);
  // The scorecard's long method note folds under one line; its text is kept.
  assert.match(kpi, /<summary[^>]*>วิธีนับและวิธีคิดคะแนน<\/summary>\s*<div[^>]*>\{SCORECARD_FORMULA\}<\/div>/);
  assert.doesNotMatch(kpi, /ตัวไหนยังไม่มีข้อมูลจะบอกว่า “ยังวัดไม่ได้”/);
});
