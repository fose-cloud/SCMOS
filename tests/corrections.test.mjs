import assert from "node:assert/strict";
import test from "node:test";
import { correctionsByKey, correctionText, mergeMarks } from "../app/scmos/corrections.ts";

/**
 * The proposed corrections as the workspace folds them (22 Sep 2026): by
 * job for the drawer, into the row's mark beside the hauliers' messages.
 */
const items = [
  { id: 1, jobKey: "J-1", jobCode: "260900760079", field: "type", label: "ประเภทรถ/ตู้", from: "1X40 REEFER", to: "1X40' RF", reason: "ประเภทรถ/ตู้สะกดตามรายการ", proposedAt: "2026-09-22T07:00:00Z" },
  { id: 2, jobKey: "J-1", jobCode: "260900760079", field: "trucker", label: "ผู้ขนส่ง", from: "SJ", to: "Sangja Transport Co., Ltd.", reason: "ชื่อผู้ขนส่งตามทะเบียนผู้รับเหมา (เดิมสะกด SJ)", proposedAt: "2026-09-22T07:00:00Z" },
  { id: 3, jobKey: "J-2", jobCode: "", field: "customer", label: "ลูกค้า", from: "lotus asia", to: "LOTUS ASIA", reason: "ชื่อลูกค้าตาม Job Rotation", proposedAt: "2026-09-22T07:00:00Z" },
  { id: 4, jobKey: " ", jobCode: "", field: "cat", label: "หมวด", from: "Import", to: "IMPORT", reason: "", proposedAt: "" },
];

test("proposals fold by job; one without a job is nobody's", () => {
  const byKey = correctionsByKey(items);
  assert.deepEqual(Object.keys(byKey), ["J-1", "J-2"]);
  assert.deepEqual(byKey["J-1"].map((one) => one.id), [1, 2]);
  assert.deepEqual(correctionsByKey(null), {});
});

test("the row's mark folds the proposals in after the hauliers' messages", () => {
  const marks = mergeMarks({ "J-1": { count: 1, badge: "LINE 1" }, "J-9": { count: 2, badge: "LINE 1 · TMS 1" } }, items);
  assert.deepEqual(marks["J-1"], { count: 3, badge: "LINE 1 · AI 2" });
  assert.deepEqual(marks["J-2"], { count: 1, badge: "AI 1" });
  assert.deepEqual(marks["J-9"], { count: 2, badge: "LINE 1 · TMS 1" });
  assert.deepEqual(mergeMarks({}, []), {});
});

test("a proposal reads as the column, the value as typed and the list's spelling", () => {
  assert.equal(correctionText(items[1]), "ผู้ขนส่ง: SJ → Sangja Transport Co., Ltd.");
  assert.equal(correctionText({ ...items[0], from: "" }), "ประเภทรถ/ตู้: — → 1X40' RF");
});
