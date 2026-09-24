import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { CUSTOMER_TERMS, customerTerm, graceMinutes } from "../app/scmos/customerTerms.ts";

/*
 * Customer/job-specific on-time terms are deliberately kept in their own
 * contract test. The API applies them in JobRules.IsOnTime and the dashboard's
 * own count applies them through customerTerms.ts, so this file holds the two
 * copies to the same table and boundary behavior.
 */

test("the web's table of customer terms is the API's", () => {
  const source = readFileSync(new URL("../server/Scmos.Api/Rules/CustomerTerms.cs", import.meta.url), "utf8");
  const api = [...source.matchAll(/new\("([^"]+)", (\d+), "(\d{2}\/\d{2}\/\d{4})", "(all|tank)", "/g)]
    .map((m) => ({ customer: m[1], graceMinutes: Number(m[2]), since: m[3], scope: m[4] }));
  assert.ok(api.length > 0, "the API's table was found");
  assert.deepEqual(CUSTOMER_TERMS, api);
  // The API matches a name the same way: equal to the term, or starting with it and a space.
  assert.match(source, /name == term\.Customer \|\| name\.StartsWith\(term\.Customer \+ " ", StringComparison\.Ordinal\)/);
  assert.match(readFileSync(new URL("../server/Scmos.Api/Rules/JobRules.cs", import.meta.url), "utf8"),
    /late <= CustomerTerms\.GraceMinutes\(job\.Customer, job\.Type\)/);
});

test("Lotus is Lotus however the register spells it; a name that merely contains the word is not", () => {
  assert.equal(graceMinutes("LOTUS"), 30);
  assert.equal(graceMinutes("LOTUS ASIA"), 30);
  assert.equal(graceMinutes(" lotus asia "), 30);
  assert.equal(customerTerm("LOTUS ASIA")?.since, "21/09/2026");
  assert.equal(graceMinutes("L'OREAL"), 0);
  assert.equal(graceMinutes("BLUE LOTUS TRADING"), 0);
  assert.equal(graceMinutes("LOTUSLAND"), 0);
  assert.equal(graceMinutes(undefined), 0);
});

test("Allnex and Syensqo keep OTD through minute 30", () => {
  for (const customer of ["ALLNEX", "ALLNEX (THAILAND)", "SYENSQO", "SYENSQO THAILAND"]) {
    assert.equal(graceMinutes(customer), 30);
  }
});

test("EVONIK gets 180 minutes only for Tank jobs", () => {
  assert.equal(graceMinutes("EVONIK (THAILAND) LTD.", "1X20' TK"), 180);
  assert.equal(graceMinutes("EVONIK (THAILAND) LTD.", "ISO Tank"), 180);
  assert.equal(graceMinutes("EVONIK (THAILAND) LTD.", "1X20'"), 0);
  assert.equal(graceMinutes("EVONIK (THAILAND) LTD."), 0);
  assert.equal(graceMinutes("EVONIK OTHER COMPANY", "1X20' TK"), 0);
});

test("the dashboard's own count reads the same table and applies each boundary inclusively", () => {
  // ops.ts imports the app's modules without extensions, which Node's test runner cannot load;
  // the count is pinned by its source, and its arithmetic repeated here over the same helper.
  const ops = readFileSync(new URL("../app/scmos/ops.ts", import.meta.url), "utf8");
  assert.match(ops, /import \{ graceMinutes \} from "\.\/customerTerms";/);
  assert.match(ops, /const onTime = measurable\.filter\(\(j\) => \(lateMinutes\(j\) \?\? 1\) <= graceMinutes\(j\.customer, j\.type\)\);/);
  const lateMinutes = (planTime, arrTime) => {
    const m = (t) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));
    return m(arrTime) - m(planTime);
  };
  const onTime = (customer, type, arrTime) => lateMinutes("09:00", arrTime) <= graceMinutes(customer, type);
  assert.equal(onTime("LOTUS ASIA", "1X20'", "09:30"), true);
  assert.equal(onTime("ALLNEX", "1X20'", "09:30"), true);
  assert.equal(onTime("ALLNEX", "1X20'", "09:31"), false);
  assert.equal(onTime("SYENSQO", "1X20'", "09:30"), true);
  assert.equal(onTime("SYENSQO", "1X20'", "09:31"), false);
  assert.equal(onTime("EVONIK (THAILAND) LTD.", "1X20' TK", "12:00"), true);
  assert.equal(onTime("EVONIK (THAILAND) LTD.", "1X20' TK", "12:01"), false);
  assert.equal(onTime("EVONIK (THAILAND) LTD.", "1X20'", "09:01"), false);
  assert.equal(onTime("L'OREAL", "1X20'", "09:00"), true);
});
