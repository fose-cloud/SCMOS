import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { mailboxKindLabel, ownerChoice } from "../app/scmos/mailInbox.ts";

// The SQL checks prove the rule against LocalDB, not in CI; these keep the order of the reads in CI (4 Oct 2026).
const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

test("a personal mailbox's message is judged on its sender before anything else of it is fetched", () => {
  const worker = read("server/Scmos.Api/Services/MailWorker.cs");
  const fill = worker.slice(worker.indexOf("private async Task<bool> FillAsync"), worker.indexOf("private async Task<bool?> SettleAsync"));
  assert.ok(fill.indexOf("reader.SenderAsync(") > 0, "the drain asks for the sender");
  assert.ok(fill.indexOf("reader.SenderAsync(") < fill.indexOf("reader.MessageAsync("), "and asks before the message itself");
  assert.match(fill, /if \(!MailSenders\.Reads\(mailbox\.OwnerOperatorId, sender\.Value, allowed\)\)\s*\{\s*db\.Emails\.Remove\(row\);/);
  // The catch-up lists which message, when and from whom — never a subject or a body.
  assert.match(worker, /PageAsync\(mailbox\.Address, from, stopping, fields: GraphMessages\.ListFields\)/);
  const messages = read("server/Scmos.Api/Rules/GraphMessages.cs");
  const list = messages.match(/ListFields = "([^"]+)"/)[1].split(",");
  const sender = messages.match(/SenderFields = "([^"]+)"/)[1].split(",");
  assert.deepEqual(list.sort(), ["from", "id", "receivedDateTime"]);
  assert.deepEqual(sender.sort(), ["from", "id"]);
});

test("every reader of mail goes through the same rule", () => {
  const endpoints = read("server/Scmos.Api/Endpoints/MailEndpoints.cs");
  assert.equal((endpoints.match(/\.VisibleTo\(db, user\)/g) ?? []).length, 3, "the list, its waiting count and one message");
  assert.match(read("server/Scmos.Api/Endpoints/DocumentEndpoints.cs"), /MailVisibility\.HidesDocumentAsync\(db, user, document, token\)/);
  assert.match(read("server/Scmos.Api/Ai/AiDecisionLog.cs"), /var query = WithoutOthersMail\(/);
  assert.match(read("server/Scmos.Api/Ai/AiTasks.cs"), /AiDecisionLog\.WithoutOthersMail\(/);
  assert.match(read("server/Scmos.Api/Ai/Communication/CommunicationSource.cs"), /box\.OwnerOperatorId == ""/);
  assert.match(read("server/Scmos.Api/Ai/Booking/BookingMailPass.cs"), /decisions\.Stage\(result, runId, mail\.Owner,/);
});

test("the settings offer a person's own mailbox as theirs, never as shared by default", () => {
  assert.equal(ownerChoice({ owner: "", staffOwner: "OP-1" }), "OP-1");
  assert.equal(ownerChoice({ owner: "OP-2", staffOwner: "OP-1" }), "OP-2");
  assert.equal(ownerChoice({ owner: "", staffOwner: "" }), "");
  assert.match(mailboxKindLabel(""), /กล่องกลาง/);
  assert.match(mailboxKindLabel("Watsana"), /ส่วนตัว · Watsana · อ่านเฉพาะผู้ส่งที่กำหนด/);
  assert.match(read("app/SCMOSApp.tsx"), /<Outlook canDecide=\{able\("EditAnyJob"\)\} canMailboxes=\{able\("AdministerMailbox"\)\}/);
});
