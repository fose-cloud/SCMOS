import assert from "node:assert/strict";
import test from "node:test";

import { pendingByKey, pendingCounts, pendingText, pendingWrites } from "../app/scmos/linePending.ts";

/*
 * What a haulier's LINE message is waiting to do to a job, as the workspace
 * reads it — the mark on the row and the line in the drawer the job's owner
 * approves from.
 */

const text = (over = {}) => ({
  id: 1, jobKey: "J1", receivedAt: "2026-09-16T05:10:00Z", errorCode: "ready-to-apply", detail: "",
  kind: "text", text: "TEMU5246902 ถึงโรงงาน 05:00 ลงเสร็จ", reading: "", hasImage: false, group: "SHORE",
  to: "DELIVERED", arrival: { date: "16/09/2026", time: "05:00" }, ready: true, ...over,
});

test("messages fold by job, and a message pinned to nothing is left out", () => {
  const byKey = pendingByKey([text(), text({ id: 2 }), text({ id: 3, jobKey: "J2" }), text({ id: 4, jobKey: "" })]);
  assert.deepEqual(Object.keys(byKey).sort(), ["J1", "J2"]);
  assert.equal(byKey.J1.length, 2);
  assert.deepEqual(pendingCounts([text(), text({ id: 2 }), text({ id: 3, jobKey: "J2" })]), { J1: 2, J2: 1 });
  assert.deepEqual(pendingCounts(null), {});
});

test("what approving writes is said in one line", () => {
  assert.equal(pendingWrites(text()), "สถานะ → DELIVERED · เวลาถึง 05:00 16/09/2026");
  assert.equal(pendingWrites(text({ arrival: { date: "", time: "" } })), "สถานะ → DELIVERED");
  assert.equal(pendingWrites(text({ kind: "image", to: "TEMU5246902" })), "เลขตู้ TEMU5246902");
  assert.equal(pendingWrites(text({ to: "", arrival: { date: "", time: "" } })), "");
  // The answer to the morning reminder: no status, the truck's details.
  assert.equal(pendingWrites(text({ to: "", arrival: { date: "", time: "" }, details: "ทะเบียน 70-1234 · คนขับ สมชาย ใจดี · เบอร์ 081-2345678" })),
    "ทะเบียน 70-1234 · คนขับ สมชาย ใจดี · เบอร์ 081-2345678");
});

test("a photo reads as what was seen on it", () => {
  assert.equal(pendingText(text()), "TEMU5246902 ถึงโรงงาน 05:00 ลงเสร็จ");
  assert.equal(pendingText(text({ kind: "image", reading: "TEMU5246902" })), "รูปตู้ · TEMU5246902");
  assert.equal(pendingText(text({ kind: "image", reading: "" })), "รูป");
});

test("an estimate is said on the card and marked as not written; a send-time arrival says which it is", () => {
  const base = { id: 1, jobKey: "K", receivedAt: "", errorCode: "ready-to-apply", detail: "", kind: "text", text: "", reading: "", hasImage: false, group: "", ready: true };
  assert.equal(pendingWrites({ ...base, to: "IN_TRANSIT", arrival: { date: "", time: "" }, eta: "10:00" }), "สถานะ → IN_TRANSIT · คาดถึง 10:00 (ไม่บันทึก)");
  assert.equal(pendingWrites({ ...base, to: "DELIVERED", arrival: { date: "17/09/2026", time: "11:00", atSend: true } }), "สถานะ → DELIVERED · เวลาถึง 11:00 17/09/2026 (เวลาที่ส่งข้อความ)");
  // A clock the driver wrote wins over an estimate in the same message.
  assert.equal(pendingWrites({ ...base, to: "DELIVERED", arrival: { date: "17/09/2026", time: "12:40" }, eta: "10:00" }), "สถานะ → DELIVERED · เวลาถึง 12:40 17/09/2026");
});

test("a message about every row of a job number says so on each row's card", () => {
  const base = { id: 7, receivedAt: "", errorCode: "ready-to-apply", detail: "", kind: "text", text: "", reading: "", hasImage: false, group: "", ready: true, to: "DELIVERED", arrival: { date: "17/09/2026", time: "08:36", atSend: true } };
  const items = [{ ...base, jobKey: "C1", every: 3 }, { ...base, jobKey: "C2", every: 3 }, { ...base, jobKey: "C3", every: 3 }];
  assert.deepEqual(Object.keys(pendingByKey(items)), ["C1", "C2", "C3"]);
  assert.equal(pendingWrites(items[0]), "สถานะ → DELIVERED · เวลาถึง 08:36 17/09/2026 (เวลาที่ส่งข้อความ) · ทั้ง 3 รายการของเลขงานนี้");
});

test("a photo of a box the job already carries is the truck at the site, at the time the photo was sent", () => {
  const item = { id: 9, jobKey: "K", receivedAt: "", errorCode: "ready-to-apply", detail: "", kind: "image", arrivalPhoto: true, text: "", reading: "BSIU8065659", hasImage: true, group: "", ready: true, to: "DELIVERED", arrival: { date: "17/09/2026", time: "10:23", atSend: true } };
  assert.equal(pendingText(item), "รูปตู้ BSIU8065659 — รถถึงหน้างาน (ตามเวลาที่ผู้ขนส่งส่งรูป)");
  assert.equal(pendingWrites(item), "สถานะ → DELIVERED · เวลาถึง 10:23 17/09/2026 (เวลาที่ส่งรูป)");
  // A photo that gives a job its number still says so.
  assert.equal(pendingWrites({ ...item, arrivalPhoto: false, to: "BSIU8065659", arrival: { date: "", time: "" } }), "เลขตู้ BSIU8065659");
});
