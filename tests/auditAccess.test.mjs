import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const roles = readFileSync(
  new URL("../server/Scmos.Api/Rules/Roles.cs", import.meta.url), "utf8");
const staff = readFileSync(
  new URL("../server/Scmos.Api/Endpoints/StaffEndpoints.cs", import.meta.url), "utf8");
const documents = readFileSync(
  new URL("../server/Scmos.Api/Endpoints/DocumentEndpoints.cs", import.meta.url), "utf8");
const audit = readFileSync(
  new URL("../server/Scmos.Api/Endpoints/AuditEndpoints.cs", import.meta.url), "utf8");
const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");

/**
 * Who may read the audit trail, and what that does not carry with it.
 *
 * The department asked for the Audit Trail menu to be open to their operators.
 * One flag was gating three unrelated doors — the trail, the staff directory
 * and the document retention list — because it was the one nearest to hand, so
 * granting the first would have handed over the other two as well.
 */

test("an operator may read the audit trail", () => {
  assert.match(roles, /private const Capability OperationGrants =[\s\S]*?Capability\.ViewAudit;/,
    "Operation User no longer holds ViewAudit");
});

test("that did not hand them the user register or the retention list", () => {
  // These are the two doors ViewAudit was borrowed for. They ask for their own
  // capability now, which the supervisory roles hold and operators do not.
  assert.match(staff, /if \(!user\.Can\(Capability\.ViewDirectory\)\)/);
  assert.match(documents, /if \(!user\.Can\(Capability\.ViewDirectory\)\)/);
  assert.doesNotMatch(staff, /Capability\.ViewAudit/);
  assert.doesNotMatch(documents, /Capability\.ViewAudit/);

  assert.match(roles, /private const Capability SupervisorGrants =[\s\S]*?Capability\.ViewDirectory;/,
    "supervisors lost the directory they always had");
  const operationBlock = roles.match(
    /private const Capability OperationGrants =[\s\S]*?;/)[0];
  assert.doesNotMatch(operationBlock, /ViewDirectory/,
    "the directory must not travel with the audit trail");
});

test("the trail is still gated — a carrier is not part of the team whose history it is", () => {
  assert.match(audit, /if \(!user\.Can\(Capability\.ViewAudit\)\)/);
  // The Subcontractor role is the carrier's. It holds three capabilities and
  // none of them is this one.
  const carrier = roles.match(/new\(Subcontractor,[\s\S]*?\),/)[0];
  assert.doesNotMatch(carrier, /ViewAudit/);
  assert.match(carrier, /Capability\.ViewDashboard \| Capability\.EditOwnJobs \| Capability\.UploadDocuments/);
});

test("the screen asks the capability rather than a stand-in for seniority", () => {
  // It asked `isSupervisor`, which is ApproveAi wearing another name. Left
  // alone, the screen would have gone on refusing what the API had started
  // allowing.
  assert.match(app, /screen === "audit" && <Audit canView=\{able\("ViewAudit"\)\} \/>/);
});

/**
 * A row inserted into the grid starts empty.
 */
test("an inserted row carries no date", () => {
  const insert = app.match(/function insertRow\(\)[\s\S]*?\n  \}/)[0];
  assert.match(insert, /date: "",/, "the row is created with a date already in it");
  assert.doesNotMatch(insert, /const today = /,
    "today's date is still being worked out for the new row");
  assert.doesNotMatch(insert, /neu: today/);
});
