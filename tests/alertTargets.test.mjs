import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

import { ALL_NAV, HEADINGS } from "../app/scmos/nav.ts";

/**
 * Every alert has to open something.
 *
 * The alert list is written in C# and the menu in TypeScript, and until now
 * nothing joined them. So when Booking, Pre-Run and Document Verification were
 * taken out of the menu on 2026-08-31, five alerts went on pointing at them and
 * two more pointed at Workspace — a heading with nothing rendered behind it,
 * which opened a blank page. Neither failed anything. Both were found by
 * reading, which is not a method.
 *
 * This is the join. It reads the screen names straight out of the .NET source
 * rather than a copy, because a copy is the thing that drifts.
 */

const source = readFileSync(new URL("../server/Scmos.Api/Rules/Notifications.cs", import.meta.url), "utf8");

/** [kind, screen] for every alert defined in the .NET rules. */
const alerts = [...source.matchAll(/new\(AlertKind\.(\w+),([\s\S]*?)\),\n/g)]
  .map(([, kind, body]) => [kind, [...body.matchAll(/"([^"]*)"/g)].at(-1)?.[1] ?? ""]);

/** Screens a person can actually open, which is not the same as screens that exist. */
const reachable = new Set(ALL_NAV.map(([screen]) => screen).filter((screen) => !HEADINGS.includes(screen)));

test("the alert list was found at all", () => {
  // Guards the regex above: a parse that silently matched nothing would make
  // every assertion below pass by having nothing to check.
  assert.ok(alerts.length >= 16, `only found ${alerts.length} alerts`);
  assert.ok(alerts.every(([, screen]) => screen.length > 0), "every alert names a screen");
});

test("every alert opens a screen somebody can reach", () => {
  const stranded = alerts.filter(([, screen]) => !reachable.has(screen));
  assert.deepEqual(stranded, [], "these alerts point at screens that are not in the menu");
});

test("no alert points at a menu heading", () => {
  // A heading folds its children instead of navigating, so nothing renders for
  // it. An alert sent there opens an empty page, which reads as a broken system.
  const onHeading = alerts.filter(([, screen]) => HEADINGS.includes(screen));
  assert.deepEqual(onHeading, []);
});

test("the three screens taken out of the menu keep no alerts", () => {
  const removed = ["booking", "prerun", "docverify"];
  const pointing = alerts.filter(([, screen]) => removed.includes(screen));
  assert.deepEqual(pointing, [], "these screens were removed on 2026-08-31");
});
