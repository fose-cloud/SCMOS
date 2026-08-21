import assert from "node:assert/strict";
import test from "node:test";

import {
  calendarMonthGrid,
  calendarMonthOf,
  shiftCalendarMonth,
} from "../app/scmos/calendar.ts";

test("builds a stable six-week calendar starting on Monday", () => {
  const days = calendarMonthGrid({ year: 2026, month: 8 });
  assert.equal(days.length, 42);
  assert.deepEqual(days[0], {
    year: 2026,
    month: 7,
    day: 27,
    date: "27/07/2026",
    inMonth: false,
  });
  assert.equal(days[5].date, "01/08/2026");
  assert.equal(days.at(-1).date, "06/09/2026");
});

test("includes leap day and marks it as part of the displayed month", () => {
  const leapDay = calendarMonthGrid({ year: 2024, month: 2 })
    .find((day) => day.date === "29/02/2024");
  assert.equal(leapDay?.inMonth, true);
});

test("moves the calendar cursor across year boundaries", () => {
  assert.deepEqual(shiftCalendarMonth({ year: 2026, month: 12 }, 1), { year: 2027, month: 1 });
  assert.deepEqual(shiftCalendarMonth({ year: 2026, month: 1 }, -1), { year: 2025, month: 12 });
});

test("reads the month from a valid operation date", () => {
  assert.deepEqual(calendarMonthOf("21/07/2026"), { year: 2026, month: 7 });
  assert.equal(calendarMonthOf("2026-07-21"), null);
});
