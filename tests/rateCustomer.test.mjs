import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");
const screen = read("app/scmos/screens/Rates.tsx");
const rule = read("server/Scmos.Api/Rules/LaneCustomer.cs");
const billing = read("server/Scmos.Api/Services/ContractRates.cs");

test("Rate Management links a lane to a Job Rotation customer from the same list My job offers (1 Oct 2026)", () => {
  assert.match(screen, /const rotation = useRotationCustomers\(\);/);
  // Picked on the contracted tab only, by somebody who may change rates, once the list has answered.
  assert.match(screen, /editable=\{canEditRates && tab === "carrier" && rotation\.ready\}/);
  assert.match(screen, /apiFetch\(`\/api\/rates\/lanes\/\$\{lane\.id\}\/customer`, \{\s*method: "PUT"/);
  // "Back to the lane's own text" is null, "any customer" is empty, anything else a rotation name.
  assert.match(screen, /onPick\(e\.target\.value === BY_TEXT \? null : e\.target\.value\)/);
  assert.match(screen, /<option value="">ทุกลูกค้า<\/option>/);
  // The carrier's own wording is shown, never rewritten.
  assert.match(screen, /written && linkedTo && written\.toUpperCase\(\) !== linkedTo\.toUpperCase\(\)/);
  // And the lanes can be narrowed to one customer's, or to those no customer is linked to.
  assert.match(screen, /options=\{\["All", UNLINKED, \.\.\.linkedCustomers\]\}/);
});

test("the API decides a lane's customer in one place, and Billing prices only from the job's own or nobody's", () => {
  // Rotation's own comparison: trimmed, upper case — rotationCustomers.ts.
  assert.match(rule, /public static string Key\(string\? name\) => \(name \?\? ""\)\.Trim\(\)\.ToUpperInvariant\(\);/);
  assert.match(read("app/scmos/rotationCustomers.ts"), /const key = \(value: string\) => value\.trim\(\)\.toLocaleUpperCase\(\);/);
  // Picked wins; else the Customer text, then the From text (SANGJA write the customer there).
  assert.match(rule, /if \(picked is not null\) return/);
  assert.ok(rule.indexOf("Key(customerText)") < rule.indexOf("Key(fromText)"));
  assert.match(billing, /if \(!LaneCustomer\.Serves\(linked, job\.Customer\)\) continue;/);
  assert.match(billing, /RateMatch\.Score\("", facts\.Destination, "", lane\.FromPlace, lane\.ToPlace\)/);
  // The server never rewrites the carrier's text; the link is its own column.
  const service = read("server/Scmos.Api/Services/RateService.cs");
  const link = service.slice(service.indexOf("SetLaneCustomerAsync(long laneId"), service.indexOf("private async Task<(List<LaneView> Views"));
  assert.match(link, /lane\.RotationCustomer = stored;/);
  assert.doesNotMatch(link, /lane\.(Customer|FromPlace) = /);
});
