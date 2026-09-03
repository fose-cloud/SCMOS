import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const source = readFileSync("app/scmos/screens/Loreal.tsx", "utf8");

test("L'OREAL Estimated Delivery comes from My Job DATE and PLAN LOADING TIME", () => {
  assert.match(source,
    /head: "Estimated Delivery Time"[^\n]*read: \(j\) => joinDateTime\(j\.date, j\.planTime\)/);
  assert.doesNotMatch(source,
    /head: "Estimated Delivery Time"[^\n]*(?:j\.arrDate|j\.arrTime)/,
    "arrival fields are actuals and must not be printed as the planned delivery");
});

test("L'OREAL period filters use the same DATE shown in Estimated Delivery", () => {
  assert.match(source, /mine\.map\(\(job\) => \(\{ job, keyed: job \}\)\)/);
  assert.doesNotMatch(source, /date: job\.arrDate \|\| job\.date/);
});
