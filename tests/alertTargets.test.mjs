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

test("every alert kind has a definition, and the parse found them all", () => {
  // Guards the regex above — a parse that silently matched nothing would make
  // every assertion below pass by having nothing to check — and pins a real
  // property while it is at it: `Notifications.Of` throws on a kind with no
  // definition, so a kind added to the enum and forgotten in the list is a
  // crash waiting for the first alert run.
  //
  // Counted against the enum rather than a number written here. The first
  // version said "at least 16" and went red the moment two alerts were
  // deliberately removed, which is a test reporting its own staleness.
  const enumBlock = /public enum AlertKind\s*\{([\s\S]*?)\}/.exec(source)?.[1] ?? "";
  const kinds = [...enumBlock.matchAll(/^\s*(\w+),/gm)].map(([, name]) => name);

  assert.ok(kinds.length > 0, "the enum should parse");
  assert.deepEqual(alerts.map(([kind]) => kind).sort(), [...kinds].sort());
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

test("the screens taken out of the menu keep no alerts", () => {
  // Booking and Document Verification went on 2026-08-31 and have stayed out.
  // Pre-Run went with them, was deleted on 2026-09-01 and restored the same
  // day, so it is off this list — the reachability test above is what holds it
  // to being reachable now, rather than this one holding it to being gone.
  const removed = ["booking", "docverify"];
  const pointing = alerts.filter(([, screen]) => removed.includes(screen));
  assert.deepEqual(pointing, []);
});
