import assert from "node:assert/strict";
import test from "node:test";

import {
  VIEWS, confidenceLabel, linkLabel, linkSummary, linkTone, matchedOnLabel,
  sizeLabel, statusLabel, statusTone, whenLabel,
} from "../app/scmos/mailInbox.ts";

test("the inbox opens on what is waiting, because that is the reason to visit", () => {
  assert.equal(VIEWS[0].key, "WAITING");
  assert.deepEqual(VIEWS.map((one) => one.key), ["WAITING", "LINKED", "UNLINKED", "ALL"]);
  for (const one of VIEWS) assert.ok(one.th.length > 0, `${one.key} has no Thai label`);
});

test("how sure the machine was is said in the specification's own two bands", () => {
  // At or above 0.95 it attached without asking; from 0.70 it asked. Somebody
  // confirming a link should be told which of those happened — checking a
  // certainty and answering a question are different jobs.
  assert.match(confidenceLabel(0.96, "CONTAINER"), /มั่นใจสูง/);
  assert.match(confidenceLabel(0.95, "CONTAINER"), /มั่นใจสูง/);
  assert.match(confidenceLabel(0.9499, "CONTAINER"), /น่าจะใช่/);
  assert.match(confidenceLabel(0.70, "CONTAINER"), /น่าจะใช่/);
  assert.doesNotMatch(confidenceLabel(0.69, "CONTAINER"), /น่าจะใช่|มั่นใจสูง/);
});

test("a hand-made link says so rather than showing a percentage", () => {
  // Nobody computed anything. 0% would read as "certainly wrong", which is the
  // opposite of what a person deciding it meant.
  assert.equal(confidenceLabel(0, "PERSON"), "คนจับคู่เอง");
  assert.equal(confidenceLabel(0, "CONTAINER"), "—");
});

test("the percentage shown is the stored number, rounded only for display", () => {
  assert.match(confidenceLabel(0.9984, "CONTAINER"), /^100%/);
  assert.match(confidenceLabel(0.912, "CONTAINER"), /^91%/);
});

test("only what needs a person, and what went wrong, are coloured", () => {
  // A list where every row is coloured has no colour left to say "this one".
  assert.equal(statusTone("NEED_REVIEW"), "amber");
  assert.equal(statusTone("FAILED"), "red");
  for (const quiet of ["RECEIVED", "PROCESSING", "PROCESSED", ""]) {
    assert.equal(statusTone(quiet), "gray", quiet);
  }
});

test("every status the API can store has words on the screen", () => {
  // A screen showing NEED_REVIEW to an operator is a screen showing them a
  // database value.
  for (const status of ["RECEIVED", "PROCESSING", "PROCESSED", "NEED_REVIEW", "FAILED"]) {
    const label = statusLabel(status);
    assert.ok(label.length > 0 && label !== status, `${status} is shown as itself`);
  }
});

test("and so does every link status and every identifier kind", () => {
  for (const status of ["SUGGESTED", "CONFIRMED", "REJECTED"]) {
    assert.ok(linkLabel(status) !== status, `${status} is shown as itself`);
  }
  for (const kind of ["CONTAINER", "JOB_CODE", "BOOKING", "BL", "PERSON"]) {
    assert.ok(matchedOnLabel(kind) !== kind, `${kind} is shown as itself`);
  }
  assert.equal(linkTone("CONFIRMED"), "green");
  assert.equal(linkTone("REJECTED"), "gray");
  assert.equal(linkTone("SUGGESTED"), "amber");
});

test("a rejected link is not counted as an attachment", () => {
  // A rejection is a record that somebody looked and said no. Counting it would
  // put mail in front of people that they have already dealt with.
  assert.equal(linkSummary([{ jobKey: "J1", status: "REJECTED" }]), "ยังไม่จับคู่");
  assert.equal(linkSummary([
    { jobKey: "J1", status: "REJECTED" },
    { jobKey: "J2", status: "CONFIRMED" },
  ]), "J2");
});

test("the list names the job rather than only saying that there is one", () => {
  assert.equal(linkSummary([{ jobKey: "J1", status: "CONFIRMED" }]), "J1");
  assert.equal(linkSummary([
    { jobKey: "J1", status: "CONFIRMED" },
    { jobKey: "J2", status: "SUGGESTED" },
  ]), "J1 +1");
  assert.equal(linkSummary([]), "ยังไม่จับคู่");
});

test("arrival time is relative while that answers the question, and a date after", () => {
  const now = new Date("2026-09-09T12:00:00Z");
  assert.equal(whenLabel("2026-09-09T11:59:40Z", now), "เมื่อครู่");
  assert.equal(whenLabel("2026-09-09T11:30:00Z", now), "30 นาทีที่แล้ว");
  assert.equal(whenLabel("2026-09-09T09:00:00Z", now), "3 ชม.ที่แล้ว");
  // "37 ชม.ที่แล้ว" is not an answer anybody wanted.
  assert.doesNotMatch(whenLabel("2026-09-08T00:00:00Z", now), /ชม\.ที่แล้ว/);
  assert.equal(whenLabel("not a date", now), "—");
});

test("a size is readable, and nothing is not zero bytes", () => {
  assert.equal(sizeLabel(0), "—");
  assert.equal(sizeLabel(512), "512 B");
  assert.equal(sizeLabel(84213), "82 KB");
  assert.match(sizeLabel(5 * 1024 * 1024), /MB$/);
});
