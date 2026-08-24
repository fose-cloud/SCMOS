import type { Job } from "./ops";
import { dnum } from "./util";

/**
 * The dashboard's period filter.
 *
 * The plan was one month when the screen was built; it is already 2,102 jobs
 * and will keep growing, so every figure needs a stated period rather than
 * "everything ever loaded". Options are derived from the jobs themselves — no
 * month is offered that has no work in it — and each level narrows the next.
 */

export type Period = { year: string; month: string; day: string };

export const ALL_PERIOD: Period = { year: "ALL", month: "ALL", day: "ALL" };

const DATE = /^(\d{2})\/(\d{2})\/(\d{4})$/;

export function partsOf(date: string): { d: string; m: string; y: string } | null {
  const m = DATE.exec(String(date ?? "").trim());
  return m ? { d: m[1], m: m[2], y: m[3] } : null;
}

export function inPeriod(job: Job, period: Period): boolean {
  if (period.year === "ALL" && period.month === "ALL" && period.day === "ALL") return true;
  const parts = partsOf(job.date);
  // A job whose date will not parse cannot belong to a period. It stays visible
  // only while no period is chosen, so a filtered view never quietly counts it.
  if (!parts) return false;
  if (period.year !== "ALL" && parts.y !== period.year) return false;
  if (period.month !== "ALL" && parts.m !== period.month) return false;
  if (period.day !== "ALL" && parts.d !== period.day) return false;
  return true;
}

export function filterPeriod(jobs: Job[], period: Period): Job[] {
  return jobs.filter((job) => inPeriod(job, period));
}

const THAI_MONTHS = ["", "ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.", "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."];

export function monthLabel(mm: string): string {
  return THAI_MONTHS[Number(mm)] ?? mm;
}

const ENGLISH_MONTHS = ["", "January", "February", "March", "April", "May", "June",
  "July", "August", "September", "October", "November", "December"];

/**
 * The month in English, for a document a customer or head office reads.
 *
 * Beside the Thai names rather than in whichever screen needed it, because a
 * second list of twelve month names is exactly the kind of thing this codebase
 * has had to gather back together before. `short` gives Apr rather than April,
 * which is what a column heading has room for.
 */
export function monthNameEn(mm: string, short = false): string {
  const name = ENGLISH_MONTHS[Number(mm)] ?? mm;
  return short ? name.slice(0, 3) : name;
}

/**
 * The YYYY-MM a dd/MM/yyyy date belongs to, for grouping a month at a time.
 *
 * The customer reports each grew their own copy of this and of the Thai month
 * names below it — the L'OREAL report and the Chemours report had the same
 * twelve strings written out twice more, three copies in a codebase whose
 * recurring failure is a rule written twice drifting apart.
 */
export function monthKey(date: string | undefined): string {
  const parts = partsOf(date ?? "");
  return parts ? `${parts.y}-${parts.m}` : "";
}

/** "2026-07" as "ก.ค. 2026". */
export function monthKeyLabel(key: string): string {
  const [year, month] = key.split("-");
  return year ? `${monthLabel(month)} ${year}` : key;
}

/** What the pickers may offer, each level narrowed by the one above it. */
export function periodOptions(jobs: Job[], period: Period) {
  const years = new Set<string>();
  const months = new Set<string>();
  const days = new Set<string>();
  let undated = 0;

  for (const job of jobs) {
    const parts = partsOf(job.date);
    if (!parts) { undated++; continue; }
    years.add(parts.y);
    if (period.year === "ALL" || parts.y === period.year) months.add(parts.m);
    if ((period.year === "ALL" || parts.y === period.year) && (period.month === "ALL" || parts.m === period.month)) {
      days.add(parts.d);
    }
  }

  const sorted = (set: Set<string>) => [...set].sort((a, b) => Number(a) - Number(b));
  return { years: sorted(years), months: sorted(months), days: sorted(days), undated };
}

export function periodLabel(period: Period): string {
  if (period.year === "ALL" && period.month === "ALL" && period.day === "ALL") return "ทั้งแผน";
  const parts: string[] = [];
  if (period.day !== "ALL") parts.push(period.day);
  if (period.month !== "ALL") parts.push(monthLabel(period.month));
  if (period.year !== "ALL") parts.push(period.year);
  return parts.join(" ");
}

/** The most recent day that carries work, for the "latest day" shortcut. */
export function latestDay(jobs: Job[]): Period | null {
  let best = "";
  for (const job of jobs) {
    if (!partsOf(job.date)) continue;
    if (!best || dnum(job.date) > dnum(best)) best = job.date;
  }
  const parts = best ? partsOf(best) : null;
  return parts ? { year: parts.y, month: parts.m, day: parts.d } : null;
}
