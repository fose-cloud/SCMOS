import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import test from "node:test";

import {
  EXTERNAL_SYSTEMS, probeStateFor, probeSystem, probeTone, systemById,
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

function deferred() {
  let resolve, reject;
  const promise = new Promise((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

test("a probe passes its cancellation signal through the same-origin request", async () => {
  const controller = new AbortController();
  const answer = await probeSystem("/api/ccs/status", async (path, init) => {
    assert.equal(path, "/api/ccs/status");
    assert.equal(init.signal, controller.signal);
    assert.equal(init.headers.accept, "application/json");
    return { status: 404 };
  }, controller.signal);
  assert.equal(answer.state, "absent");
  assert.match(answer.detail, /endpoint/);
});

test("a cancelled probe never starts a request", async () => {
  const controller = new AbortController();
  controller.abort();
  const answer = await probeSystem("/api/abs/status", async () => {
    assert.fail("cancelled probes must not call the API");
  }, controller.signal);
  assert.equal(answer, null);
});

test("a late success from the menu we left cannot overwrite the new menu", async () => {
  const previous = new AbortController();
  const pending = deferred();
  const old = probeSystem("/api/abs/status", () => pending.promise, previous.signal);
  previous.abort();
  const current = await probeSystem("/api/ccs/status", async () => ({ status: 403 }), new AbortController().signal);
  pending.resolve({ status: 200 });
  assert.equal(current.state, "denied");
  assert.match(current.detail, /403/);
  assert.equal(await old, null);
});

test("a late failure from the menu we left is ignored too", async () => {
  const controller = new AbortController();
  const pending = deferred();
  const answer = probeSystem("/api/abs/status", () => pending.promise, controller.signal);
  controller.abort();
  pending.reject(new Error("old connection failed"));
  assert.equal(await answer, null);
});

test("a real network failure is still reported for the active menu", async () => {
  const answer = await probeSystem("/api/abs/status", async () => {
    throw new Error("network unavailable");
  }, new AbortController().signal);
  assert.deepEqual(answer, { state: "error", detail: "network unavailable" });
});

test("each new retry uses its own response", async () => {
  const answers = [];
  for (const status of [500, 200, 404]) {
    answers.push((await probeSystem("/api/abs/status", async () => ({ status }), new AbortController().signal)).state);
  }
  assert.deepEqual(answers, ["error", "ready", "absent"]);
});

test("the screen cancels on cleanup and resets its state when the endpoint changes", () => {
  const screen = readFileSync(new URL("../app/scmos/screens/ExternalSystem.tsx", import.meta.url), "utf8");
  assert.match(screen, /key=\{system.endpoint\}/);
  assert.match(screen, /return \(\) => controller.abort\(\)/);
  assert.match(screen, /\[endpoint, attempt\]/);
  assert.match(screen, /answer && !controller.signal.aborted/);
});

test("every document a system points at actually exists", () => {
  // A pointer to a file that was renamed is worse than no pointer: it sends
  // somebody looking for a plan that reads as though it were never written.
  for (const system of EXTERNAL_SYSTEMS) {
    for (const path of system.docs ?? []) {
      assert.ok(existsSync(new URL("../" + path, import.meta.url)),
        `${system.id} points at ${path}, which is not there`);
    }
  }
});

test("the two systems with a written plan point at it", () => {
  // LINE and Outlook arrived with full specifications. The plan documents are
  // where the disagreements between those specs and this repository are
  // recorded, and a screen that did not mention them would let somebody start
  // building against the wrong architecture.
  for (const id of ["line", "outlook"]) {
    const docs = systemById(id).docs ?? [];
    assert.ok(docs.some((path) => /IMPLEMENTATION_PLAN\.md$/.test(path)), id);
  }
});
