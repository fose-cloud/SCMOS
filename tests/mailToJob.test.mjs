import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { canOpenJobFrom, mailSource, SOURCE_BODY_MAX } from "../app/scmos/mailInbox.ts";

// 5 Oct 2026: customers send one email per job. The person who opens the job from it attaches the email to it — the
// job's owner for their own job, Supervisor+ for any (the user's decision).
const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("the form shows the message it was opened from, cut to a booking's length", () => {
  const source = mailSource({ id: 7, subject: "Booking 1x40HC", fromName: "KWE Booking", fromAddress: "booking@kwe.co.th", bodyText: "  pickup 9 Oct  " });
  assert.deepEqual(source, { id: 7, subject: "Booking 1x40HC", from: "KWE Booking <booking@kwe.co.th>", body: "pickup 9 Oct" });
  assert.equal(mailSource({ id: 1, subject: "", fromName: "", fromAddress: "a@b.co", bodyText: "" }).from, "a@b.co");
  const long = mailSource({ id: 1, subject: "", fromName: "", fromAddress: "", bodyText: "x".repeat(SOURCE_BODY_MAX + 50) });
  assert.equal(long.body.length, SOURCE_BODY_MAX + 2);
});

test("a message a person already confirmed is not offered as a new job", () => {
  assert.equal(canOpenJobFrom([]), true);
  assert.equal(canOpenJobFrom([{ status: "SUGGESTED" }, { status: "REJECTED" }]), true);
  assert.equal(canOpenJobFrom([{ status: "CONFIRMED" }]), false);
});

test("the job is saved first, then the message is attached to it", () => {
  const app = read("app/SCMOSApp.tsx");
  const save = app.slice(app.indexOf("function saveAddJob()"), app.indexOf("function saveAddJob()") + 9000);
  const block = save.slice(save.indexOf("if (fromEmail !== null)"));
  assert.ok(block.indexOf("flushNow()") > 0 && block.indexOf("flushNow()") < block.indexOf("/api/mail/${mail.id}/decide"));
  assert.match(block, /if \(!saved\.ok\) \{ setToast\("บันทึกงานไม่สำเร็จ — อีเมลยังไม่ได้ผูกกับงาน"\); return; \}/);
  assert.match(block, /JSON\.stringify\(\{ jobKey: key, status: "CONFIRMED" \}\)/);
  assert.match(app, /onClose=\{\(\) => \{[^}]*setFromEmail\(null\); \}\}/);
  assert.match(app, /onCreateJob=\{able\("EditOwnJobs"\) \? openFromEmail : undefined\}/);
  // The chooser goes through startAddJob, which must leave the message where it is.
  const start = app.slice(app.indexOf("const startAddJob = "), app.indexOf("const openBookingDraft"));
  assert.doesNotMatch(start, /setFromEmail/);
});

test("a job's owner may attach only a message they can see, only to their own job, and never reject", () => {
  const endpoints = read("server/Scmos.Api/Endpoints/MailEndpoints.cs");
  assert.match(endpoints, /if \(!user\.Can\(Capability\.EditAnyJob\)\s*&& !\(status == MailLink\.Confirmed && await MailVisibility\.MayAttachAsync\(db, user, id, jobKey, token\)\)\)/);
  const rule = read("server/Scmos.Api/Services/MailVisibility.cs");
  const may = rule.slice(rule.indexOf("public static async Task<bool> MayAttachAsync"), rule.indexOf("public static Task<bool> CanSeeAsync"));
  assert.match(may, /job\.OwnerId == me/);
  assert.match(may, /Capability\.EditOwnJobs/);
  assert.match(may, /Capability\.ViewMailbox/);
  assert.match(may, /CanSeeAsync\(db, user, emailId, token\)/);
});
