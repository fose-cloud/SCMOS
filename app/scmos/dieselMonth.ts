/**
 * เรทน้ำมัน — the diesel rate a month is priced at.
 *
 * **The rate is a month's average, not a day's price.** The daily pump price
 * over the whole of August is averaged, and that one figure is the rate for
 * August; the same is done at the end of every month. Given by the account team
 * on 2026-09-07.
 *
 * That is a different thing from what this system held before, and the
 * difference is not small. A spot price picks a band on the fuel clause on the
 * day somebody happens to look; a monthly average picks the band the whole
 * month's work is billed at. The first moves under a trip that has already run.
 *
 * Two consequences worth stating out loud, because both are visible on screen:
 *
 * A month is not final until it has ended. A rate quoted mid-August is quoted
 * against a partial average, and that average will move. Everything here
 * reports how many days it was computed from and whether the month is closed,
 * so a figure nobody can reproduce later is never shown as though it were
 * settled.
 *
 * And a job belongs to the month it ran in. Pricing an August trip at
 * September's average would be re-pricing work that was already done.
 *
 * No imports on purpose: this is arithmetic over a table of numbers and should
 * be checkable without a browser.
 */

/** One day's published pump price. */
export type DieselDay = {
  /** dd/MM/yyyy, as every date in this system is written. */
  date: string;
  /** Baht per litre. */
  price: number;
};

/** What a month came to, and how much of it was known when the sum was taken. */
export type DieselAverage = {
  /** MM/yyyy. */
  month: string;
  /** The mean of the days held, to two places. Null when no day is held. */
  average: number | null;
  /** How many days went into it. */
  days: number;
  /**
   * Whether the month has ended and the average can no longer move.
   *
   * A closed month is a figure somebody can reconcile an invoice against. An
   * open one is an estimate, and saying so is the whole reason this field
   * exists.
   */
  closed: boolean;
};

/** The month a dd/MM/yyyy date belongs to, as MM/yyyy. Empty when unreadable. */
export function monthOf(date: string | undefined | null): string {
  const text = String(date ?? "").trim();
  return /^\d{2}\/\d{2}\/\d{4}$/.test(text) ? text.slice(3) : "";
}

/**
 * A month key sorted as yyyyMM, so December does not come before February.
 *
 * The same trap the report and the archive both hit. Kept here so a fourth
 * place does not sort "12/2026" before "02/2026" and call it history.
 */
export function monthKey(month: string): string {
  return /^\d{2}\/\d{4}$/.test(month) ? month.slice(3) + month.slice(0, 2) : "";
}

/** How many days a month has. Gregorian, and leap years included. */
export function daysInMonth(month: string): number {
  if (!/^\d{2}\/\d{4}$/.test(month)) return 0;
  const m = Number(month.slice(0, 2));
  const y = Number(month.slice(3));
  if (m < 1 || m > 12) return 0;
  return new Date(Date.UTC(y, m, 0)).getUTCDate();
}

/**
 * The average for one month, off the days held for it.
 *
 * Rounded to two places, which is how a pump price is written. Not rounded to
 * a whole baht: the average decides which band of the fuel clause applies, and
 * the bands are quoted to two places — 33.995 rounded to 34 would cross
 * 33.00–36.29 into the next step.
 */
export function averageFor(days: readonly DieselDay[], month: string): DieselAverage {
  const mine = days.filter((day) => monthOf(day.date) === month && Number.isFinite(day.price) && day.price > 0);
  const total = daysInMonth(month);
  // Closed when every day of the month is accounted for. Counting days rather
  // than comparing to today keeps this pure — and a month with a gap in the
  // middle is not finished either, whatever the calendar says.
  const closed = total > 0 && mine.length >= total;

  if (mine.length === 0) return { month, average: null, days: 0, closed: false };

  const mean = mine.reduce((sum, day) => sum + day.price, 0) / mine.length;
  return { month, average: Math.round(mean * 100) / 100, days: mine.length, closed };
}

/** Every month the days cover, newest first. */
export function averages(days: readonly DieselDay[]): DieselAverage[] {
  const months = [...new Set(days.map((day) => monthOf(day.date)).filter(Boolean))];
  return months
    .map((month) => averageFor(days, month))
    .sort((a, b) => monthKey(b.month).localeCompare(monthKey(a.month)));
}

/**
 * The rate a job is priced at: the average of the month the job ran in.
 *
 * Null when that month has no days recorded — which is the honest answer, and
 * the reason this returns a rate rather than a number. A trip in a month nobody
 * has entered prices for cannot be priced, and falling back to the newest month
 * available would quietly bill August's work at September's diesel.
 */
export function rateForJob(
  days: readonly DieselDay[],
  jobDate: string | undefined | null,
): DieselAverage | null {
  const month = monthOf(jobDate);
  if (!month) return null;
  const found = averageFor(days, month);
  return found.average === null ? null : found;
}

/**
 * How a rate should be described where somebody has to trust it.
 *
 * A closed month says nothing extra — it is simply the rate. An open one says
 * so, because a figure that will move before the month ends must not be read as
 * one that will not.
 */
export function describe(rate: DieselAverage | null): string {
  if (rate === null || rate.average === null) return "ยังไม่มีราคาน้ำมันของเดือนนี้";
  if (rate.closed) return `${rate.average} · เฉลี่ยทั้งเดือน ${rate.month}`;
  return `${rate.average} · เฉลี่ย ${rate.days} วันของ ${rate.month} — ยังไม่สิ้นเดือน ตัวเลขยังขยับได้`;
}
