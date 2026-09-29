import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { bookingDraftBody, draftFromDecision, labelOf, parseBookingDraft } from "../app/scmos/bookingDraft.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const reading = (over = {}) => ({
  field: "customer", proposed: "BASF", quote: "BASF ขอรถรับตู้", verified: true, value: "BASF", reason: "", ...over,
});
const answer = (over = {}) => ({
  category: "IMPORT", status: "COMPLETE", fields: { customer: "BASF" },
  readings: [reading(), reading({ field: "jobCode", proposed: "J-999", quote: "Job J-999", verified: false, value: "", reason: "ข้อความที่อ้างไม่อยู่ในต้นฉบับ" })],
  missing: [], ...over,
});
const rejects = (value) => assert.throws(() => parseBookingDraft(value), /invalid_response/);

test("a pasted booking's draft is read as the server sent it", () => {
  const draft = parseBookingDraft(answer());
  assert.deepEqual(draft.fields, { customer: "BASF" });
  assert.equal(draft.readings.length, 2);
});

test("a draft that would put an unverified value in the form is not used", () => {
  rejects(answer({ fields: { customer: "ACME" } }));                                   // a value the readings did not accept
  rejects(answer({ fields: { jobCode: "J-999" } }));                                   // a field the readings refused
  rejects(answer({ readings: [reading({ quote: "" })] }));                             // accepted with no words behind it
  rejects(answer({ readings: [reading({ verified: false, reason: "x" })] }));          // refused but still carrying a value
  rejects(answer({ status: "COMPLETE", missing: ["date"] }));                          // complete and missing disagree
  rejects(answer({ status: "NEEDS_INFORMATION", missing: [] }));
  rejects(answer({ category: "SHIPPING" }));
  rejects(answer({ fields: { "customer<script>": "BASF" } }));
  rejects(null);
});

test("the body is trimmed, the labels are the form's own", () => {
  assert.deepEqual(bookingDraftBody("EXPORT", "  ขอรถ  "), { category: "EXPORT", text: "ขอรถ" });
  assert.equal(labelOf("customer"), "ลูกค้า");
  assert.equal(labelOf("unknownField"), "unknownField");
});

test("a mail draft becomes the form's fields, its words and its category", () => {
  const decision = {
    decisionType: "booking_draft", ruleReferences: ["BookingVerification", "Category:EXPORT"],
    findings: {
      facts: [
        { text: "เรื่อง: ขอรถ", source: "email:41.subject" },
        { text: "BASF", source: "email:41.text#customer" },
        { text: "29/09/2026", source: "email:41.text#date" },
      ],
      ruleResults: [], inferences: [], recommendations: [], blockingIssues: [],
      observations: [{ text: "“BASF ขอรถ”", source: "quote#customer" }, { text: "ไม่รับ jobCode: J-9 — …", source: null }],
    },
  };
  assert.deepEqual(draftFromDecision(decision), {
    category: "EXPORT", fields: { customer: "BASF", date: "29/09/2026" }, quotes: { customer: "“BASF ขอรถ”" },
  });
  assert.equal(draftFromDecision({ ...decision, decisionType: "otd_risk" }), null);
  assert.equal(draftFromDecision({ ...decision, ruleReferences: ["Category:NONE"] }), null);
});

test("the add-job form reads pasted text through the control header; a draft opened from mail is answered when saved", () => {
  const app = read("app/SCMOSApp.tsx");
  const modal = read("app/scmos/overlays/WorkspaceOverlays.tsx");
  const panel = read("app/scmos/screens/AiFindingsPanel.tsx");
  assert.match(app, /apiFetch\("\/api\/ai\/booking-draft", \{\s*method: "POST", headers: \{ "content-type": "application\/json", "X-SCMOS-AI-Control": "1" \}/);
  assert.match(app, /parseBookingDraft\(body\)/);
  assert.match(app, /JSON\.stringify\(\{ outcome: "ACCEPTED", choice: key, reason: "" \}\)/);
  // Answered only after the job's save succeeded — never a draft pointing at a job that failed to save.
  assert.match(app, /void flushNow\(\)\.then\(async \(saved\) => \{\s*if \(!saved\.ok\) \{ setToast\("บันทึกงานไม่สำเร็จ — ร่าง AI ยังเปิดอยู่"\); return; \}/);
  assert.match(app, /\|\| addCat !== null;/);                                      // the form loads the register wherever it opens
  assert.match(app, /onDraftJob=\{openBookingDraft\}/);
  assert.match(modal, /data-testid="booking-paste"/);
  assert.match(modal, /aria-label="ข้อความ booking"/);
  for (const label of ["อ่านข้อความ", "ยังไม่มี: "]) assert.ok(modal.includes(label), label);
  for (const label of ["เปิดฟอร์มเพิ่มงาน", "สร้างงานแล้ว", "ไม่ใช่ booking"]) assert.ok(panel.includes(`>${label}<`), label);
  assert.match(panel, /"communication-agent", "booking-agent"\] as const/);
});
