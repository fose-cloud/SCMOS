import assert from "node:assert/strict";
import test from "node:test";

import {
  confidenceLabel, describe, isActionable, palette, photoLabel, referenceLabel, statusLabel, summarise, whenLabel,
} from "../app/scmos/lineReview.ts";

/*
 * How the LINE queue reads to the person working it.
 *
 * The colour is not decoration. It says who has to do something, and the one
 * mistake worth guarding against is a refusal that looks like work — a queue
 * that files "another haulier's job" under the same treatment as "waiting for
 * approval" teaches its reader to click through both.
 */

test("a code an operator can act on is separated from one they cannot", () => {
  // Waiting for approval, and one choice away from it.
  assert.equal(isActionable("ready-to-apply"), true);
  assert.equal(isActionable("many-jobs"), true);

  // Correctly refused. Nothing to fix, so nothing to offer.
  assert.equal(isActionable("not-your-job"), false);
  assert.equal(isActionable("group-not-vendor"), false);
  assert.equal(isActionable("job-closed"), false);

  // Nothing to do at all.
  assert.equal(isActionable("already-there"), false);
  assert.equal(isActionable("dismissed"), false);
});

test("only ready-to-apply is green", () => {
  assert.equal(describe("ready-to-apply").tone, "ready");
  for (const code of [
    "many-jobs", "not-your-job", "unknown-group", "job-closed",
    "already-there", "backwards", "no-such-job",
  ]) {
    assert.notEqual(describe(code).tone, "ready", `${code} must not read as ready`);
  }
});

test("a refusal is told apart from something that needs setting up", () => {
  // Nobody mapped the room: somebody has to go and do that.
  assert.equal(describe("unknown-group").tone, "setup");
  assert.equal(describe("group-inactive").tone, "setup");
  assert.ok(describe("unknown-group").next.length > 0);

  // The room is mapped and simply may not do this. There is nothing to fix.
  assert.equal(describe("group-not-vendor").tone, "refused");
  assert.equal(describe("group-not-vendor").next, "");
  assert.equal(describe("not-your-job").next, "");
});

test("a code the screen has not been taught shows the code, not a shrug", () => {
  const unknown = describe("some-new-code-from-the-api");
  assert.equal(unknown.label, "some-new-code-from-the-api");
  assert.equal(unknown.tone, "attention");
  assert.ok(unknown.next.includes("แจ้งผู้ดูแลระบบ"));
});

test("no code at all is a dash, not an error", () => {
  for (const empty of ["", "   ", null, undefined]) {
    assert.equal(describe(empty).label, "—");
    assert.equal(describe(empty).tone, "quiet");
  }
});

test("the summary counts bands and drops the empty ones", () => {
  const counted = summarise([
    "ready-to-apply", "ready-to-apply",
    "many-jobs",
    "not-your-job",
  ]);
  assert.deepEqual(counted, [
    { tone: "ready", label: "รออนุมัติ", count: 2 },
    { tone: "attention", label: "ต้องตรวจสอบ", count: 1 },
    { tone: "refused", label: "ปฏิเสธแล้ว", count: 1 },
  ]);

  // A row of zeroes reads as a system with problems everywhere.
  assert.deepEqual(summarise([]), []);
  assert.equal(summarise(["ready-to-apply"]).length, 1);
});

test("what to do first comes first", () => {
  const order = summarise([
    "already-there", "not-your-job", "unknown-group", "many-jobs", "ready-to-apply",
  ]).map((band) => band.tone);
  assert.deepEqual(order, ["ready", "attention", "setup", "refused", "quiet"]);
});

test("every band has its own colour", () => {
  const seen = new Set();
  for (const tone of ["ready", "setup", "attention", "refused", "quiet"]) {
    const { line, fill, ink } = palette(tone);
    assert.match(line, /^#[0-9A-F]{6}$/i);
    assert.match(fill, /^#[0-9A-F]{6}$/i);
    assert.match(ink, /^#[0-9A-F]{6}$/i);
    assert.ok(!seen.has(line), `${tone} reuses another band's colour`);
    seen.add(line);
  }
});

test("a confidence of zero shows as 0%, because that is the reading", () => {
  // Zero is what the parser says when it understood nothing, and treating it
  // as missing hides exactly the row a reviewer most needs to see.
  assert.equal(confidenceLabel(0), "0%");
  assert.equal(confidenceLabel(0.9), "90%");
  assert.equal(confidenceLabel(1), "100%");
  assert.equal(confidenceLabel(0.905), "91%");
});

test("a confidence that is not a number shows nothing at all", () => {
  assert.equal(confidenceLabel(undefined), "");
  assert.equal(confidenceLabel(null), "");
  assert.equal(confidenceLabel(Number.NaN), "");
});

test("the arrival time is read in Bangkok, whatever the browser is set to", () => {
  // 23:30 UTC on the 6th is 06:30 on the 7th in Bangkok. A screen showing the
  // 6th would have the operator looking for yesterday's message.
  const shown = whenLabel("2026-09-06T23:30:00Z");
  assert.ok(shown.includes("07"), `expected the 7th in Bangkok, got ${shown}`);
  assert.ok(shown.includes("06:30"), `expected 06:30 Bangkok, got ${shown}`);
});

test("a time that is not a time shows nothing rather than Invalid Date", () => {
  assert.equal(whenLabel(""), "");
  assert.equal(whenLabel(null), "");
  assert.equal(whenLabel("not a date"), "");
});

/*
 * Since 16 Sep 2026 a haulier's message has carried a container and a plate
 * and no job number — "L'Oréal / TEMU7592765 / สมใจ / 700-3232 / ถึงโรงงาน 05:00
 * / ลงเสร็จ". The queue has to say what such a message pointed at.
 */

test("the reference column shows the number, else the box, else the plates", () => {
  assert.equal(referenceLabel({ jobNumber: "260600800773", container: "TEMU7592765" }), "260600800773");
  assert.equal(referenceLabel({ jobNumber: "", container: "TEMU7592765", plates: ["700-3232"] }), "TEMU7592765");
  assert.equal(referenceLabel({ jobNumber: "", container: "", plates: ["75-4384", "75-4385"] }), "75-4384 / 75-4385");
  assert.equal(referenceLabel({ jobNumber: null, container: null, plates: null }), "");
});

test("every code the parser and the rule can now produce has words on the screen", () => {
  for (const code of ["no-reference", "many-containers", "many-job-numbers", "many-times", "nothing-understood"]) {
    const outcome = describe(code);
    assert.notEqual(outcome.label, code, `${code} has no label`);
    assert.equal(outcome.tone, "attention");
  }
  // The rows stored before the parser read containers keep their word.
  assert.notEqual(describe("no-job-number").label, "no-job-number");
  // A greeting or a "รับทราบ" is filed quietly, since 17 Sep 2026 — nothing for anybody to do.
  assert.notEqual(describe("not-about-a-job").label, "not-about-a-job");
  assert.equal(describe("not-about-a-job").tone, "quiet");
  assert.equal(isActionable("not-about-a-job"), false);
});

test("the one status the parser cannot settle alone is named as such", () => {
  assert.equal(statusLabel("ARRIVED"), "ถึงหน้างาน (ตามประเภทงาน)");
  assert.equal(statusLabel("DELIVERED"), "DELIVERED");
  assert.equal(statusLabel(""), "");
});

/*
 * A driver's photograph of a box door, read for its number (v2.7.22). A
 * misread and a photo of nothing are different rows: one is for a person,
 * the other is filed.
 */

test("a photo's outcomes have words, and a photo of nothing is quiet", () => {
  assert.equal(describe("no-open-job").tone, "attention");
  assert.equal(describe("container-check-digit").tone, "attention");
  assert.equal(describe("image-failed").tone, "attention");
  assert.equal(describe("no-container-in-photo").tone, "quiet");
  assert.equal(isActionable("no-container-in-photo"), false);
  assert.equal(isActionable("container-check-digit"), true);
});

test("a photo's row says what was read, or what the photo showed", () => {
  assert.equal(photoLabel({ imageReading: "TEMU5246902", imageNote: "a box" }), "รูปตู้ · TEMU5246902");
  assert.equal(photoLabel({ imageReading: "", imageNote: "a delivery note" }), "รูป · a delivery note");
  assert.equal(photoLabel({ imageReading: "", imageNote: "" }), "รูป");
});

test("the answer to the morning reminder reads as ready", () => {
  assert.equal(describe("truck-details").tone, "ready");
  assert.equal(isActionable("truck-details"), true);
});
