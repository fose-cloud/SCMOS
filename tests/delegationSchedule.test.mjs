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
  assert.match(app, /\[signedInAs, identityAttempt, identityRefresh\]/);
});
