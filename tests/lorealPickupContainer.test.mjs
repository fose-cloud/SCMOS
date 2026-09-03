import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const loreal = readFileSync("app/scmos/screens/Loreal.tsx", "utf8");
const ops = readFileSync("app/scmos/ops.ts", "utf8");

test("L'OREAL Pick up container is an independently keyed report value", () => {
  assert.match(loreal,
    /head: "Pick up container", source: "register", field: "lorealPickupContainer"/);
  assert.match(loreal, /read: \(j\) => j\.lorealPickupContainer/);
  assert.doesNotMatch(loreal,
    /head: "Pick up container"[^}]+(?:pickupPlan|pickupTime)/,
    "the report-only value must not be read from or written to the My Job pickup plan");
});

test("old and newly imported jobs start the report-only field empty", () => {
  assert.match(ops, /lorealPickupContainer: string/);
  assert.match(ops, /lorealPickupContainer: ""/);
});
