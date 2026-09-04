import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
const overlay = readFileSync(
  new URL("../app/scmos/overlays/Overlays.tsx", import.meta.url), "utf8");
const service = readFileSync(
  new URL("../server/Scmos.Api/Services/DelegationService.cs", import.meta.url), "utf8");

test("a future delegation reports that it is scheduled, not already active", () => {
  assert.match(service, /start\.Value > today/);
  assert.match(service, /บันทึกการมอบสิทธิ์ล่วงหน้า/);
  assert.match(service, /สิทธิ์จะเริ่มอัตโนมัติวันที่/);
  assert.match(overlay, /สถานะจะเป็น “รอถึงกำหนด”/);
});

test("an open workspace refreshes delegated owners without requiring sign-out", () => {
  assert.match(app, /setInterval\(refreshVisible, 60_000\)/);
  assert.match(app, /addEventListener\("focus", refresh\)/);
  assert.match(app, /addEventListener\("visibilitychange", refreshVisible\)/);
  // Each trigger by name, rather than the dependency array as written.
  //
  // The list gained `isSignedIn` when the sign-in page became reachable without
  // a session: the identity fetch must not run before there is one. That kept
  // every refresh trigger this test guards and still failed it, because the
  // assertion was on the exact text of the array rather than on what has to be
  // in it.
  const deps = app.match(/\}, \[[^\]]*identityRefresh\]\);/g) ?? [];
  assert.equal(deps.length, 1, "the identity effect's dependency list");
  for (const trigger of ["signedInAs", "identityAttempt", "identityRefresh"]) {
    assert.ok(deps[0].includes(trigger),
      `the identity effect no longer refreshes on ${trigger}: ${deps[0]}`);
  }

  // And it does not run at all before somebody is signed in — an anonymous
  // visitor on the login page has no identity to fetch.
  assert.match(app, /if \(!isSignedIn\) return;/);
});
