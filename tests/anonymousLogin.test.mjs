import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
const login = readFileSync(
  new URL("../app/scmos/overlays/Login.tsx", import.meta.url), "utf8");
const { BRAND_LOGO_DATA_URI: logo } = await import("../app/scmos/brandLogo.ts");
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

/**
 * The sign-in page is the only path App Service opens to anonymous visitors.
 *
 * Everything else — `/_next/static/...`, the images under `/public` — still
 * redirects to Entra, so the stylesheet and the bundle come back as a redirect
 * to Microsoft rather than as CSS and JavaScript. Rendered from its own HTML
 * and nothing else, the page still has to be right.
 *
 * Verified by serving the built HTML on its own with every other path missing:
 * no broken images, React never hydrated, and the page indistinguishable from
 * the full one.
 */

test("the page needs no stylesheet of its own", () => {
  // globals.css lives under /_next. The handful of rules that actually reach
  // this screen are carried in its own <style> instead.
  assert.match(login, /\* \{ box-sizing: border-box \}/);
  assert.match(login, /body \{ margin: 0/);
});

test("the wordmark travels with the page rather than being fetched", () => {
  // A request for /cargo-logo.png is answered with a redirect, and the mark
  // draws as a broken image on the first screen anybody sees.
  assert.match(login, /BRAND_LOGO_DATA_URI/);
  assert.doesNotMatch(login, /src="\/cargo-logo\.png"/);
  assert.match(logo, /^data:image\/png;base64,/,
    "the inlined logo is not a PNG data URI");
  assert.ok(logo.length > 2000, "the inlined logo is too small to be the wordmark");
});

test("nothing on it depends on React having hydrated", () => {
  // The way in is a link, and the one disclosure is a <details> the browser
  // opens by itself. A button holding useState would do nothing here.
  assert.match(login, /<details/);
  assert.doesNotMatch(login, /onClick=\{\(\) => setNotice/);
});
