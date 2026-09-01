import assert from "node:assert/strict";
import test from "node:test";

import { MOVEMENT_STAGE, toInstant, toTyped } from "../app/scmos/truckTimes.ts";

test("a typed time carries the yard's offset, not the reader's", () => {
  // The whole reason this file exists. 08:30 at the yard is 01:30 UTC; sending
  // it without the offset lets the API read it as 08:30 UTC and the customer's
  // file gains seven hours. The same mistake was live in the scorecard.
  assert.equal(toInstant("01/07/2026 08:30"), "2026-07-01T08:30:00+07:00");
  assert.equal(new Date(toInstant("01/07/2026 08:30")).toISOString(), "2026-07-01T01:30:00.000Z");
});

test("a time reads back exactly as it was typed", () => {
  for (const typed of ["01/07/2026 08:30", "31/12/2026 23:59", "05/03/2026 00:00"]) {
    assert.equal(toTyped(toInstant(typed)), typed, typed);
  }
});

test("an instant reads in the yard's zone whoever opens it", () => {
  // 01:30 UTC is half past eight in the morning at the yard. A reader in London
  // must see the same row as a reader in Bangkok — it is the customer's file.
  assert.equal(toTyped("2026-07-01T01:30:00.000Z"), "01/07/2026 08:30");
  assert.equal(toTyped("2026-07-01T08:30:00+07:00"), "01/07/2026 08:30");
  // Late UTC that has already become tomorrow at the yard.
  assert.equal(toTyped("2026-07-01T18:00:00.000Z"), "02/07/2026 01:00");
});

test("a date with no time means the start of that day", () => {
  // Real rows in the workbook carry only a date. Refusing them would push
  // somebody into inventing a clock time to get the row saved.
  assert.equal(toInstant("03/07/2026"), "2026-07-03T00:00:00+07:00");
  assert.equal(toTyped(toInstant("03/07/2026")), "03/07/2026 00:00");
});

test("single-digit days and months are accepted and normalised", () => {
  assert.equal(toInstant("5/3/2026 9:05"), "2026-03-05T09:05:00+07:00");
});

test("what is not a time is refused rather than turned into midnight", () => {
  // Null is the refusal that lets the screen say what was wrong. Saving a zero
  // would put 01/01/1970 in a file that goes to the customer.
  for (const bad of [
    "", "   ", "รอรถเข้ารับ", "01-07-2026", "2026-07-01",
    "31/02/2026 08:00", "01/07/2026 24:00", "01/07/2026 08:60", "01/07/2026 8",
  ]) {
    assert.equal(toInstant(bad), null, `${bad} should be refused`);
  }
});

test("an empty instant reads as empty, not as an error", () => {
  // Every one of these columns is blank on almost every row today.
  assert.equal(toTyped(null), "");
  assert.equal(toTyped(undefined), "");
  assert.equal(toTyped(""), "");
  assert.equal(toTyped("not a date"), "");
});

test("every movement column names a stage, and no two share one", () => {
  const columns = [
    "Leave base", "Truck arrival", "Truck loading time",
    "Truck loading comp", "Truck departure", "Return container",
  ];
  assert.deepEqual(Object.keys(MOVEMENT_STAGE).sort(), [...columns].sort());

  // One stage holds one timestamp, so two columns sharing a stage would mean
  // the second silently overwriting the first.
  const stages = Object.values(MOVEMENT_STAGE);
  assert.equal(new Set(stages).size, stages.length, "each column has its own stage");
});
