import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { markFor, onDay, parsePlanPaste, parseTsv } from "../app/scmos/auditPlan.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const people = '"Nattikorn\nSalarnyou\nPunnarai"';
const months = "\t".repeat(12);
// The department's sheet as it pastes out of Excel (1 Oct 2026): title rows, the legend, the month header, the
// section row, then numbered rows with the auditors in one quoted cell and the date in Remark.
const sheet = [
  "Leschaco (Thailand) Co.,Ltd\t\t\t\t\t\t\t\t\tPrepared by\t\t\tReviewed by",
  "\t\t\t2026 AUDIT planning of EHS with Truck Sub-Contractor",
  "No\tSubject\t\t\tTargets\tPerson in charge\tSchedule",
  "\t\t\t\t\t\tJAN\tFEB\tMAR\tAPR\tMAY\tJUN\tJUL\tAUG\tSEP\tOCT\tNOV\tDEC\tRemark",
  "1\tRE-Audit EHS : Truck Sub-Contractor",
  `\t\t1\tDGT Cross Haul Co., Ltd.\tLCB\t${people}${months}\tAudit date: 13/02/2026\t\t`,
  `\t\t6\tPhanpong Logistics Limited Partnership\tBKK\t${people}${months}\tAudit date: 15/07/2026`,
  `\t\t11\tSeino Saha Logistics Co., Ltd.\tLCB\t${people}${months}\tNew audit date: 23/01/2026`,
  `\t\t12\tNo Date Transport Co., Ltd.\tLCB\t${people}${months}\t`,
].join("\n");

test("a pasted plan reads as the sheet's rows: number, company, site, auditors and the date in Remark", () => {
  const { rows, problems } = parsePlanPaste(sheet);
  assert.deepEqual(rows.map((row) => [row.sequence, row.company, row.target, row.auditDate, row.kind]), [
    [1, "DGT Cross Haul Co., Ltd.", "LCB", "13/02/2026", "re-audit"],
    [6, "Phanpong Logistics Limited Partnership", "BKK", "15/07/2026", "re-audit"],
    [11, "Seino Saha Logistics Co., Ltd.", "LCB", "23/01/2026", "re-audit"],
  ]);
  assert.equal(rows[0].personInCharge, "Nattikorn\nSalarnyou\nPunnarai");
  assert.equal(rows[2].remark, "New audit date: 23/01/2026");
  // A numbered row with no date is reported, never guessed into a month.
  assert.deepEqual(problems, ["No Date Transport Co., Ltd.: ไม่พบวันที่ Audit"]);
  // Quoted cells keep their line breaks and doubled quotes.
  assert.deepEqual(parseTsv('a\t"b ""c""\nd"\te'), [["a", 'b "c"\nd', "e"]]);
});

test("a section row sets the kind of the rows under it", () => {
  const { rows } = parsePlanPaste(`2\tNew Sub-Contractor Audit\n\t\t1\tNorth Star Transport Co., Ltd.\tLCB\tNattikorn${months}\tAudit date: 05/11/2026`);
  assert.deepEqual(rows.map((row) => [row.company, row.kind]), [["North Star Transport Co., Ltd.", "new"]]);
});

const item = (over) => ({ auditDate: "13/02/2026", nextDate: "", status: "planned", schedule: "fixed", notYetDone: false, ...over });

test("each month cell carries the legend's mark: the day, ○, ✓, X, → or ⇢ — and ○ where a move lands", () => {
  assert.deepEqual(markFor(item({}), 2026, 2).symbol, "13");
  assert.equal(markFor(item({}), 2026, 3), null);
  assert.equal(markFor(item({}), 2027, 2), null);
  assert.equal(markFor(item({ schedule: "tentative" }), 2026, 2).symbol, "○");
  assert.equal(markFor(item({ status: "done" }), 2026, 2).symbol, "✓");
  assert.equal(markFor(item({ notYetDone: true }), 2026, 2).symbol, "X");
  assert.equal(markFor(item({ status: "postponed", nextDate: "10/04/2026" }), 2026, 2).symbol, "⇢");
  assert.equal(markFor(item({ status: "postponed", nextDate: "10/04/2026" }), 2026, 4).symbol, "○");
  assert.equal(markFor(item({ status: "continue", nextDate: "10/04/2026" }), 2026, 2).symbol, "→");
  assert.equal(markFor(item({ status: "cancelled" }), 2026, 2).symbol, "—");
});

test("the calendar puts an audit on its day, a moved one on the day it moved to, and leaves cancelled ones off", () => {
  const items = [
    { id: 1, auditDate: "05/10/2026", nextDate: "", status: "planned" },
    { id: 2, auditDate: "05/10/2026", nextDate: "20/10/2026", status: "postponed" },
    { id: 3, auditDate: "05/10/2026", nextDate: "", status: "cancelled" },
  ];
  assert.deepEqual(onDay(items, 2026, 10, 5).map((one) => one.id), [1]);
  assert.deepEqual(onDay(items, 2026, 10, 20).map((one) => one.id), [2]);
});

test("Audit Planning is a Subcontractor menu entry, and Add New Vendor carries the checklist and the calendar", () => {
  const nav = read("app/scmos/nav.ts");
  assert.match(nav, /\["evaluation", "Annual Evaluation"[^\n]*\n[^\n]*\n\s*\["auditplan", "Audit Planning", "แผนการตรวจประเมิน"/);
  const vendor = read("app/scmos/screens/SupplierFlows.tsx");
  assert.match(vendor, /<OnboardingChecklist supplierId=\{row\.id\} canEdit=\{canRegister\} canUpload=\{canUpload\}/);
  assert.match(vendor, /<AuditCalendar refresh=\{calendar\} \/>/);
  // The checklist's files go through the supplier documents route, under the line's own kind.
  const checklist = read("app/scmos/screens/VendorOnboarding.tsx");
  assert.match(checklist, /body\.append\("kind", item\.kind\);/);
  assert.match(checklist, /apiFetch\("\/api\/documents", \{ method: "POST", body \}\)/);
});
