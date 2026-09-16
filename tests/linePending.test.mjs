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
