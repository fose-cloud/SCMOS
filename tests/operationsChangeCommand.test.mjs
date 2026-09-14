import assert from "node:assert/strict";
import test from "node:test";
import { isChangeCommand, parseChangeDraft } from "../app/scmos/operationsChangeCommand.ts";

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
