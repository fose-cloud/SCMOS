import assert from "node:assert/strict";
import test from "node:test";

import { pendingByKey, pendingMarks, pendingText, pendingWrites, sourceOf, sourcesOf } from "../app/scmos/linePending.ts";

/*
 * What a haulier's message is waiting to do to a job, as the workspace reads
 * it — the mark on the row and the line in the drawer the job's owner
 * approves from. From the LINE room or from the haulier's TMS: one queue, and
 * the mark names the door.
 */

const text = (over = {}) => ({
  id: 1, jobKey: "J1", receivedAt: "2026-09-16T05:10:00Z", errorCode: "ready-to-apply", detail: "",
  kind: "text", text: "TEMU5246902 ถึงโรงงาน 05:00 ลงเสร็จ", group: "SHORE",
  to: "DELIVERED", arrival: { date: "16/09/2026", time: "05:00" }, ready: true, ...over,
});

test("messages fold by job, and a message pinned to nothing is left out", () => {
  const byKey = pendingByKey([text(), text({ id: 2 }), text({ id: 3, jobKey: "J2" }), text({ id: 4, jobKey: "" })]);
  assert.deepEqual(Object.keys(byKey).sort(), ["J1", "J2"]);
  assert.equal(byKey.J1.length, 2);
  assert.deepEqual(pendingMarks([text(), text({ id: 2 }), text({ id: 3, jobKey: "J2" })]), { J1: { count: 2, badge: "LINE 2" }, J2: { count: 1, badge: "LINE 1" } });
  assert.deepEqual(pendingMarks(null), {});
});

test("the mark names the door a message came through — LINE, the haulier's TMS, or both", () => {
  const tms = (over = {}) => text({ kind: "tms", group: "Shore Trans Asia Co., Ltd. · SHORE TMS", text: "container_returned · 20/09/2026 18:38", ...over });
  assert.equal(sourceOf(text()), "LINE");
  assert.equal(sourceOf(tms()), "TMS");
  assert.deepEqual(sourcesOf([tms(), text()]), ["LINE", "TMS"]);
  assert.deepEqual(sourcesOf([tms()]), ["TMS"]);
  assert.deepEqual(pendingMarks([tms()]), { J1: { count: 1, badge: "TMS 1" } });
  assert.deepEqual(pendingMarks([text(), tms({ id: 2 }), text({ id: 3 })]), { J1: { count: 3, badge: "LINE 2 · TMS 1" } });
  // A kind the feed has never sent reads as LINE, the door that was there first.
  assert.equal(sourceOf(text({ kind: "" })), "LINE");
});

test("what approving writes is said in one line", () => {
  assert.equal(pendingWrites(text()), "สถานะ → DELIVERED · เวลาถึง 05:00 16/09/2026");
  assert.equal(pendingWrites(text({ arrival: { date: "", time: "" } })), "สถานะ → DELIVERED");
  assert.equal(pendingWrites(text({ to: "", arrival: { date: "", time: "" } })), "");
  // The answer to the morning reminder: no status, the truck's details.
  assert.equal(pendingWrites(text({ to: "", arrival: { date: "", time: "" }, details: "ทะเบียน 70-1234 · คนขับ สมชาย ใจดี · เบอร์ 081-2345678" })),
    "ทะเบียน 70-1234 · คนขับ สมชาย ใจดี · เบอร์ 081-2345678");
});

test("the message reads as typed", () => {
  assert.equal(pendingText(text()), "TEMU5246902 ถึงโรงงาน 05:00 ลงเสร็จ");
});

test("an estimate is said on the card and marked as not written; a send-time arrival says which it is", () => {
  const base = { id: 1, jobKey: "K", receivedAt: "", errorCode: "ready-to-apply", detail: "", kind: "text", text: "", group: "", ready: true };
  assert.equal(pendingWrites({ ...base, to: "IN_TRANSIT", arrival: { date: "", time: "" }, eta: "10:00" }), "สถานะ → IN_TRANSIT · คาดถึง 10:00 (ไม่บันทึก)");
  assert.equal(pendingWrites({ ...base, to: "DELIVERED", arrival: { date: "17/09/2026", time: "11:00", atSend: true } }), "สถานะ → DELIVERED · เวลาถึง 11:00 17/09/2026 (เวลาที่ส่งข้อความ)");
  // A clock the driver wrote wins over an estimate in the same message.
  assert.equal(pendingWrites({ ...base, to: "DELIVERED", arrival: { date: "17/09/2026", time: "12:40" }, eta: "10:00" }), "สถานะ → DELIVERED · เวลาถึง 12:40 17/09/2026");
});

test("a message about every row of a job number says so on each row's card", () => {
  const base = { id: 7, receivedAt: "", errorCode: "ready-to-apply", detail: "", kind: "text", text: "", group: "", ready: true, to: "DELIVERED", arrival: { date: "17/09/2026", time: "08:36", atSend: true } };
  const items = [{ ...base, jobKey: "C1", every: 3 }, { ...base, jobKey: "C2", every: 3 }, { ...base, jobKey: "C3", every: 3 }];
  assert.deepEqual(Object.keys(pendingByKey(items)), ["C1", "C2", "C3"]);
  assert.equal(pendingWrites(items[0]), "สถานะ → DELIVERED · เวลาถึง 08:36 17/09/2026 (เวลาที่ส่งข้อความ) · ทั้ง 3 รายการของเลขงานนี้");
});

