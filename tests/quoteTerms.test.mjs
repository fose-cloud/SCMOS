import assert from "node:assert/strict";
import test from "node:test";
import { QUOTE_TERMS, chargeText } from "../app/scmos/quoteTerms.ts";

/**
 * The quotation's conditions, checked as numbers rather than as prose.
 *
 * These end up in front of a customer, so the failure that matters is not a
 * typo in the wording — it is a number that is read the wrong way. The workbook
 * this came from holds exactly that mistake: it stores the cancellation charges
 * as 1 and 0.8, which are the percentages divided by a hundred, and a screen
 * that printed them would quote 1% of the truck rate for a booking cancelled on
 * the loading day.
 */

const every = QUOTE_TERMS.flatMap((block) => block.charges.map((charge) => ({ block, charge })));

test("both halves of the schedule are here", () => {
  assert.deepEqual(QUOTE_TERMS.map((block) => block.key), ["LCL", "FCL"]);
  for (const block of QUOTE_TERMS) {
    assert.ok(block.charges.length > 0, `${block.key} has no charges`);
    assert.ok(block.heading.trim().length > 0);
    assert.ok(block.thai.trim().length > 0);
  }
});

test("a percentage is a percentage and not a fraction of one", () => {
  const percents = every.filter(({ charge }) => charge.basis === "percent");
  assert.ok(percents.length >= 4, "the cancellation charges are percentages");
  for (const { block, charge } of percents) {
    assert.ok(
      typeof charge.amount === "number" && charge.amount > 1 && charge.amount <= 100,
      `${block.key} · ${charge.what} — ${charge.amount} is not a percentage`,
    );
  }
});

test("cancelling on the loading day costs the whole rate", () => {
  // The two the workbook has wrong, named rather than left to the range check:
  // 1 and 0.8 both sit inside "greater than zero", and the range test above
  // would not have caught 0.8 if the rule had been stated any more loosely.
  for (const block of QUOTE_TERMS) {
    const onTheDay = block.charges.find((one) => /Cancellation booking on loading date/.test(one.what));
    const before = block.charges.find((one) => /before loading date/.test(one.what));
    assert.equal(onTheDay?.amount, 100, `${block.key} cancellation on the day`);
    assert.equal(before?.amount, 80, `${block.key} cancellation the day before`);
  }
});

test("a charge in baht is a positive number, and free is not a number at all", () => {
  for (const { block, charge } of every) {
    const where = `${block.key} · ${charge.what}`;
    if (charge.basis === "free") {
      assert.equal(charge.amount, null, `${where} is free and should carry no amount`);
    } else {
      assert.ok(typeof charge.amount === "number" && charge.amount > 0, `${where} has no amount`);
      // A charge nobody can round is a charge that gets argued about on an
      // invoice. Every figure on this schedule is a whole baht.
      assert.equal(charge.amount, Math.round(charge.amount), `${where} is not a whole number`);
    }
  }
});

test("every charge says what it is charged against", () => {
  for (const { block, charge } of every) {
    assert.ok(charge.per.trim().length > 0, `${block.key} · ${charge.what} — no unit`);
    assert.ok(charge.what.trim().length > 0);
  }
});

test("no condition is written twice inside one block", () => {
  for (const block of QUOTE_TERMS) {
    const seen = new Set();
    for (const charge of block.charges) {
      assert.ok(!seen.has(charge.what), `${block.key} repeats “${charge.what}”`);
      seen.add(charge.what);
    }
  }
});

test("the overnight charges cover the three truck sizes", () => {
  const lcl = QUOTE_TERMS.find((block) => block.key === "LCL");
  const nights = lcl.charges.filter((one) => /Truck head overnight/.test(one.what));
  assert.deepEqual(nights.map((one) => one.amount), [500, 1000, 1500]);
  assert.deepEqual(
    nights.map((one) => one.what.split(", ")[1]),
    ["4WH", "6WH", "10WH"],
  );
});

test("an amount is written one way, wherever it is shown", () => {
  assert.equal(chargeText({ what: "", amount: 1500, basis: "baht", per: "" }), "THB 1,500");
  assert.equal(chargeText({ what: "", amount: 80, basis: "percent", per: "" }), "80%");
  assert.equal(chargeText({ what: "", amount: null, basis: "free", per: "" }), "FREE SERVICE");
});

test("the diesel adjustment is recorded as a note, not lost", () => {
  const fcl = QUOTE_TERMS.find((block) => block.key === "FCL");
  const diesel = fcl.notes.filter((note) => /diesel/i.test(note));
  assert.equal(diesel.length, 2, "the BKK and LCB routes adjust differently");
  assert.ok(diesel.some((note) => /150 baht/.test(note) && /BKK/.test(note)));
  assert.ok(diesel.some((note) => /300 baht/.test(note) && /LCB/.test(note)));
});

test("labour is excluded on both halves", () => {
  for (const block of QUOTE_TERMS) {
    assert.ok(block.notes.some((note) => /Not labor to support/.test(note)),
      `${block.key} does not say labour is excluded`);
  }
});

test("the file's five disagreements are settled the team's way", () => {
  // The ruling of 3 September 2026: what the team supplied is the schedule and
  // the workbook is corrected to match. Written as tests because the file is
  // the older artefact and looks like the source — somebody reconciling the
  // two later would reasonably assume the spreadsheet won.
  const fcl = QUOTE_TERMS.find((block) => block.key === "FCL");
  const lcl = QUOTE_TERMS.find((block) => block.key === "LCL");

  const axles = fcl.charges.find((one) => /หาง 3 เพลา/.test(one.what));
  assert.match(axles.what, /25 ตัน/, "the file says 23 tonnes; 25 is the rule");

  const bmt = fcl.charges.find((one) => /BMT/.test(one.what));
  assert.match(bmt.what, /Siam River/, "the file's return list is missing Siam River");

  const overnight = fcl.charges.find((one) => one.what === "Trailer head overnight charge");
  assert.equal(overnight.basis, "percent", "the file writes this as 1 /NIGHT/TRIP");
  assert.equal(overnight.amount, 100);

  // Two the file does not carry at all.
  assert.ok(fcl.charges.some((one) => /Cargo handling alongside vessel/.test(one.what)));
  assert.ok(fcl.charges.some((one) => /reefer genset/i.test(one.what)));

  // And a block it does not carry at all.
  assert.equal(lcl.charges.length, 10, "the file has no LCL block; these ten live only here");
});
