import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
const login = readFileSync(
  new URL("../app/scmos/overlays/Login.tsx", import.meta.url), "utf8");
const deploy = readFileSync(
  new URL("../.github/workflows/web.yml", import.meta.url), "utf8");

/**
 * What the sign-in page may do before anybody has signed in.
 *
 * App Service used to redirect every anonymous request to Entra, so this screen
 * was unreachable and the question never arose. Once it is reachable — which is
 * what lets the designed page be seen at all — the page is served to whoever
 * asks, and everything it does on load is done for them.
 *
 * Measured before the gates went in: seven requests for the job register on the
 * first paint, and one for the identity every minute after. The API refuses all
 * of them, which is what it is for. But a screen that asks is a screen relying
 * on the far side to say no, and the far side saying no should be the last line
 * of the defence rather than the only one.
 */

test("nothing is fetched until somebody is signed in", () => {
  // The register read, the paged job read, and the identity read. Each one
  // named, because a guard removed from any of them puts the requests back and
  // nothing else would notice.
  assert.match(app, /if \(!isSignedIn \|\| !registerNeeded/,
    "the register load runs before sign-in");
  assert.match(app, /if \(!isSignedIn \|\| !isWorkspace\) return;/,
    "the paged job read runs before sign-in");
  assert.match(app, /if \(!isSignedIn\) return;/,
    "the identity read runs before sign-in");

  // The guard has to mean "is there a session", not "whose settings do I load".
  // `signedInAs` falls back to the first demo account and so is never empty; a
  // guard written on it would never fire.
  assert.match(app, /const isSignedIn = auth !== null;/);
  assert.match(app, /const signedInAs = \(auth \?\? ACCOUNTS\[0\]\)\.user;/);
});

test("the sign-in page does not accuse the visitor of a failure they have not had", () => {
  // It carried "the system did not receive your details from Microsoft". That
  // was right while arriving here without an identity meant something had gone
  // wrong; it is now simply what a visitor sees before signing in.
  assert.doesNotMatch(app, /ระบบยังไม่ได้รับข้อมูลผู้ใช้จาก Microsoft/);
});

test("the button says whose sign-in it is about to open", () => {
  // A button labelled only "sign in" that hands off to another company's page
  // is how somebody decides they have been phished.
  assert.match(login, /เข้าสู่ระบบด้วยบัญชี Microsoft/);
  assert.match(app, /\/\.auth\/login\/aad\?post_login_redirect_uri=/);
});

test("nothing sensitive is served to an anonymous visitor as a static file", () => {
  // Both of these are stripped from the package rather than trusted to the
  // front door being shut, because the front door is now open on purpose.
  assert.match(deploy, /rm -f \.next\/standalone\/public\/data\/ops\.json/,
    "the delivered register would be customer names, drivers and phone numbers");
  assert.match(deploy, /rm -f \.next\/standalone\/public\/Signature\.png/,
    "an unreferenced signature image would be downloadable by anyone");
});
