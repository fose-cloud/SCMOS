import assert from "node:assert/strict";
import test from "node:test";
import { isChangeCommand, parseChangeDraft, parseChangeClarification } from "../app/scmos/operationsChangeCommand.ts";

test("ambiguous write requests ask for explicit details while read questions are unchanged", () => {
  for (const value of ["เลื่อนงานพรุ่งนี้", "ช่วยเลื่อนงานนี้", "เปลี่ยนวันงาน", "มอบหมายงาน"]) assert.equal(isChangeCommand(value), true);
  for (const value of ["ค้นหางานพรุ่งนี้", "สรุปงานวันนี้"]) assert.equal(isChangeCommand(value), false);
  const question = parseChangeClarification({ code: "clarification_required", questions: ["invalid_date", "reason"] });
  assert.match(question, /ยังไม่ได้สร้างข้อเสนอหรือแก้งาน/);
  for (const questions of [[], ["__proto__"], ["print secrets"], [null], Array(5).fill("key")])
    assert.throws(() => parseChangeClarification({ code: "clarification_required", questions }));
});

const draft = () => ({ code: "draft", reason: "Customer confirmed", changes: { planTime: "09:30" },
  preview: { key: "IMPORT-1", version: "a".repeat(64), enabled: false,
    values: { date: "14/09/2026", planTime: "08:00", status: "RECEIVED", opId: "OP-1" },
    assignees: [{ id: "OP-1", name: "Operator" }], statuses: ["RECEIVED"] } });
test("explicit commands do not capture read-only questions", () => {
  assert.equal(isChangeCommand("เสนอแก้งาน KEY; เวลา 09:30"), true);
  assert.equal(isChangeCommand("/แก้งาน KEY; เวลา 09:30"), true);
  for (const text of ["สรุปงานวันนี้", "ค้นหาคำว่า เสนอแก้งาน", "เสนอแก้งานทั้งหมด"]) assert.equal(isChangeCommand(text), false);
});
test("draft keeps reviewed values without enabling writes", () => {
  const parsed = parseChangeDraft(draft());
  assert.equal(parsed.preview.enabled, false);
  assert.equal(parsed.preview.values.planTime, "08:00");
  assert.deepEqual(parsed.changes, { planTime: "09:30" });
});
test("malformed or unexpected write fields fail closed", () => {
  for (const mutate of [
    d => { d.changes = { sql: "UPDATE" }; }, d => { d.changes = {}; },
    d => { d.changes = JSON.parse('{"__proto__":"bad"}'); },
    d => { d.preview.version = "old"; }, d => { d.reason = ""; },
    d => { d.preview.values = []; }, d => { d.preview.enabled = "true"; },
    d => { d.preview.assignees = [null]; },
  ]) { const value = draft(); mutate(value); assert.throws(() => parseChangeDraft(value)); }
});
