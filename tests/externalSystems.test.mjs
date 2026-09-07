import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import {
  EXTERNAL_SYSTEMS, probeStateFor, probeTone, systemById,
} from "../app/scmos/externalSystems.ts";

/*
 * The systems SCMOS is meant to talk to, and has not been connected to yet.
 *
 * One screen behind all four. These tests pin the two things that would break
 * quietly: a menu entry with no definition behind it, and a probe that cries
 * failure when it should be reporting the ordinary state of not-built-yet.
 */

test("every system says where SCMOS will call, and it is always SCMOS's own API", () => {
  // The browser must never hold somebody else's API key, and the permission
  // check belongs in the same place as every other menu's. A definition
  // pointing at graph.microsoft.com would be both mistakes at once.
  for (const system of EXTERNAL_SYSTEMS) {
    assert.match(system.endpoint, /^\/api\//, system.id);
    assert.ok(!/^https?:/.test(system.endpoint), system.id);
  }
});

test("the four systems asked for are all here and none is defined twice", () => {
  assert.deepEqual(EXTERNAL_SYSTEMS.map((one) => one.id).sort(), ["abs", "ccs", "line", "outlook"]);
  assert.equal(new Set(EXTERNAL_SYSTEMS.map((one) => one.endpoint)).size, EXTERNAL_SYSTEMS.length);
});

test("every system says what it is for and what is still unknown", () => {
  // The unknowns are the useful half of these pages. A system with none listed
  // is a page that looks finished and tells the next person nothing.
  for (const system of EXTERNAL_SYSTEMS) {
    assert.ok(system.name.length > 0, system.id);
    assert.ok(system.purpose.length > 10, system.id);
    assert.ok(system.unknowns.length >= 3, `${system.id} lists only ${system.unknowns.length}`);
  }
});

test("an endpoint that does not exist yet is the ordinary answer, not a failure", () => {
  // None of these are built. A red panel on every visit would train people to
  // ignore the one that eventually matters.
  assert.equal(probeStateFor(404), "absent");
  assert.equal(probeTone("absent"), "idle");
});

test("the probe tells apart not-built, not-allowed, and broken", () => {
  assert.equal(probeStateFor(200), "ready");
  assert.equal(probeStateFor(204), "ready");
  assert.equal(probeStateFor(401), "denied");
  assert.equal(probeStateFor(403), "denied");
  assert.equal(probeStateFor(500), "error");
  assert.equal(probeStateFor(502), "error");

  assert.equal(probeTone("ready"), "ok");
  assert.equal(probeTone("denied"), "warn");
  assert.equal(probeTone("error"), "warn");
  assert.equal(probeTone("checking"), "idle");
});

test("a screen id with no definition resolves to nothing, not to the first system", () => {
  // The app renders on `systemById(screen)`. Falling back to a default would
  // put the ABS page under the Dashboard menu.
  assert.equal(systemById("dashboard"), undefined);
  assert.equal(systemById(""), undefined);
  assert.equal(systemById("abs")?.name, "ABS");
});

test("every system in the menu has a definition, and every definition is in the menu", () => {
  // The two lists live in different files and drift the moment one is edited
  // alone: a menu entry with no definition renders a blank page, a definition
  // with no menu entry is unreachable code that looks done.
  const nav = readFileSync(new URL("../app/scmos/nav.ts", import.meta.url), "utf8");
  // To the line that closes the array, not to the first "]" — that one is
  // inside the first entry's icon rectangles.
  const from = nav.indexOf("  integrations: [");
  const block = nav.slice(from, nav.indexOf("\n  ],", from));
  const inMenu = [...block.matchAll(/\["(\w+)",/g)].map((m) => m[1]).sort();
  assert.deepEqual(inMenu, EXTERNAL_SYSTEMS.map((one) => one.id).sort());

  // And each is a member of the Screen union, or the app cannot route to it.
  const union = nav.slice(nav.indexOf("export type Screen ="), nav.indexOf(";", nav.indexOf("export type Screen =")));
  for (const system of EXTERNAL_SYSTEMS) {
    assert.ok(union.includes(`"${system.id}"`), `${system.id} is not a Screen`);
  }
});

test("no integration screen offers the header's fallback Export Excel", () => {
  // That button only raises a toast and exports nothing, which on a screen with
  // no data at all is a button that lies twice over. It was suppressed for ABS
  // by naming it in a hand-kept list; CCS, LINE and Outlook were added beside
  // it and got the button. The suppression reads the definitions now.
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.match(app, /NOT_BUILT\[screen\] \|\| OWN_SCREEN\[screen\] \|\| systemById\(screen\)/);

  const own = app.slice(app.indexOf("const OWN_SCREEN"), app.indexOf("};", app.indexOf("const OWN_SCREEN")));
  for (const system of EXTERNAL_SYSTEMS) {
    assert.ok(!own.includes(`${system.id}: true`),
      `${system.id} is named in OWN_SCREEN as well — one list or the other, not both`);
  }
});
