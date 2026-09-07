import assert from "node:assert/strict";
import test from "node:test";

import { DIESEL, DIESEL_DEFAULT, dieselRate, looksLikeDiesel } from "../app/scmos/diesel.ts";

/*
 * The diesel price is what chooses a rate.
 *
 * Every lane on the Chemours cards is eleven prices, one per band of the fuel
 * clause. The figure is half of how a trip's cost was arrived at, not a note
 * beside it.
 */

test("the default carries its source and the date it was read", () => {
  // A rate that decides money should say how old it is. The figure sat at
  // 32.94 long enough to be two bands low, and nothing on screen said so.
  assert.ok(DIESEL.price > 0);
  assert.match(DIESEL.effective, /^\d{2}-\d{2}-\d{4} \d{2}:\d{2}$/);
  assert.match(DIESEL.source, /pttor/i);
  assert.equal(DIESEL_DEFAULT, String(DIESEL.price));
});

test("the default is a plausible pump price, whatever it is updated to", () => {
  // The one thing this test can usefully pin about a number that will change:
  // that somebody editing it has not left a stray digit behind.
  assert.ok(looksLikeDiesel(DIESEL.price), String(DIESEL.price));
  assert.ok(looksLikeDiesel(DIESEL.b20), String(DIESEL.b20));
});

test("a rate is read past the commas and the baht sign", () => {
  assert.equal(dieselRate("39.14"), 39.14);
  assert.equal(dieselRate("฿ 39.14"), 39.14);
  assert.equal(dieselRate(39.14), 39.14);
});

test("no diesel rate is unknown, not free fuel", () => {
  // Zero would sort below every band of the clause and price the trip at the
  // cheapest one — the opposite of the safe direction.
  assert.equal(dieselRate(""), null);
  assert.equal(dieselRate(undefined), null);
  assert.equal(dieselRate("0"), null);
  assert.equal(dieselRate("-5"), null);
  assert.equal(dieselRate("ยังไม่ทราบ"), null);
});

test("a transport cost typed into the diesel column does not pass for one", () => {
  // This is what the check is for. 3,480 and 10160 are the two values most
  // likely to land here by mistake — a trip cost and a postcode.
  assert.equal(looksLikeDiesel("3480"), false);
  assert.equal(looksLikeDiesel("10160"), false);
  assert.equal(looksLikeDiesel("3,480"), false);
});

test("the check is wide enough not to argue with the market", () => {
  // Thai retail diesel has spent the last decade between about 20 and 35. The
  // range is here to catch a wrong column, not to hold an opinion about price.
  for (const rate of ["19.99", "25", "32.94", "39.14", "55", "99.9"]) {
    assert.equal(looksLikeDiesel(rate), true, rate);
  }
  assert.equal(looksLikeDiesel("9.99"), false);
  assert.equal(looksLikeDiesel("100"), false);
});
