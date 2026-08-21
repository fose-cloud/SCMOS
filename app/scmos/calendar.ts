export type CalendarMonth = { year: number; month: number };

export type CalendarDay = CalendarMonth & {
  date: string;
  day: number;
  inMonth: boolean;
};

const OPERATION_DATE = /^([0-9]{2})\/([0-9]{2})\/([0-9]{4})$/;

/** Return the calendar month carried by an operation date. */
export function calendarMonthOf(date: string): CalendarMonth | null {
  const parts = OPERATION_DATE.exec(String(date ?? "").trim());
  return parts ? { year: Number(parts[3]), month: Number(parts[2]) } : null;
}

/** Move a calendar cursor across year boundaries without changing a filter. */
export function shiftCalendarMonth(value: CalendarMonth, delta: number): CalendarMonth {
  const shifted = new Date(Date.UTC(value.year, value.month - 1 + delta, 1));
  return { year: shifted.getUTCFullYear(), month: shifted.getUTCMonth() + 1 };
}

/**
 * Six stable Monday-first weeks for a monthly picker. Adjacent-month days are
 * included so the layout does not jump and a busy day at a month boundary is
 * still visible.
 */
export function calendarMonthGrid(value: CalendarMonth): CalendarDay[] {
  if (!Number.isInteger(value.year) || value.year < 1 || !Number.isInteger(value.month) || value.month < 1 || value.month > 12) {
    return [];
  }

  const first = new Date(Date.UTC(value.year, value.month - 1, 1));
  const mondayOffset = (first.getUTCDay() + 6) % 7;
  const start = new Date(Date.UTC(value.year, value.month - 1, 1 - mondayOffset));

  return Array.from({ length: 42 }, (_, index) => {
    const current = new Date(start);
    current.setUTCDate(start.getUTCDate() + index);
    const year = current.getUTCFullYear();
    const month = current.getUTCMonth() + 1;
    const day = current.getUTCDate();
    const two = (n: number) => String(n).padStart(2, "0");
    return {
      year,
      month,
      day,
      date: `${two(day)}/${two(month)}/${year}`,
      inMonth: year === value.year && month === value.month,
    };
  });
}
