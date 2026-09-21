import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { CUSTOMER_TERMS, customerTerm, graceMinutes } from "../app/scmos/customerTerms.ts";

/*
 * 21 Sep 2026: the department lead set Lotus's on-time term — an arrival up to
 * thirty minutes after plan is not counted late in the KPI or on the dashboard.
 * The API applies it in JobRules.IsOnTime through Rules/CustomerTerms.cs; the
 * dashboard's own count applies it through customerTerms.ts. Two copies of one
 * rule, so this file holds them to the same table.
 */

test("the web's table of customer terms is the API's", () => {
  const source = readFileSync(new URL("../server/Scmos.Api/Rules/CustomerTerms.cs", import.meta.url), "utf8");
  const api = [...source.matchAll(/new\("([A-Z' ]+)", (\d+), "(\d{2}\/\d{2}\/\d{4})", "/g)]
    .map((m) => ({ customer: m[1], graceMinutes: Number(m[2]), since: m[3] }));
  assert.ok(api.length > 0, "the API's table was found");
  assert.deepEqual(CUSTOMER_TERMS, api);
  // The API matches a name the same way: equal to the term, or starting with it and a space.
  assert.match(source, /name == term\.Customer \|\| name\.StartsWith\(term\.Customer \+ " ", StringComparison\.Ordinal\)/);
  assert.match(readFileSync(new URL("../server/Scmos.Api/Rules/JobRules.cs", import.meta.url), "utf8"),
    /late <= CustomerTerms\.GraceMinutes\(job\.Customer\)/);
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

test("the dashboard's own count reads the same table — a Lotus arrival inside the grace is on time, everyone else's is late the minute it is late", () => {
  // ops.ts imports the app's modules without extensions, which Node's test runner cannot load;
  // the count is pinned by its source, and its arithmetic repeated here over the same helper.
  const ops = readFileSync(new URL("../app/scmos/ops.ts", import.meta.url), "utf8");
  assert.match(ops, /import \{ graceMinutes \} from "\.\/customerTerms";/);
  assert.match(ops, /const onTime = measurable\.filter\(\(j\) => \(lateMinutes\(j\) \?\? 1\) <= graceMinutes\(j\.customer\)\);/);
  const lateMinutes = (planTime, arrTime) => {
    const m = (t) => Number(t.slice(0, 2)) * 60 + Number(t.slice(3, 5));
    return m(arrTime) - m(planTime);
  };
  const onTime = (customer, arrTime) => lateMinutes("09:00", arrTime) <= graceMinutes(customer);
  assert.equal(onTime("LOTUS ASIA", "09:20"), true);
  assert.equal(onTime("LOTUS ASIA", "09:30"), true);
  assert.equal(onTime("LOTUS ASIA", "09:31"), false);
  assert.equal(onTime("L'OREAL", "09:01"), false);
  assert.equal(onTime("L'OREAL", "09:00"), true);
});
