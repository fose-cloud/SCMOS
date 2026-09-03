import type { Job } from "./ops";

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

/**
 * The year picker's value for "no usable date".
 *
 * Undated jobs were counted and named — "วันที่ใช้ไม่ได้ 28" — and there was no
 * way to look at them. They are the ones that need looking at: a job whose date
 * will not parse is a job nobody can plan, and choosing any period hides it,
 * which is exactly when somebody would want the list.
 *
 * Carried in the year rather than as a fourth field because it is the same
 * question the year asks — which period is this in — with "none" as an answer.
 * The month and day are meaningless beside it and are cleared when it is chosen.
 *
 * The API knows this word too, for the workspace grid it pages server-side.
 * `tests/noDateFilter.test.mjs` fails if the two ever stop agreeing.
 */
export const NO_DATE = "NONE";

const DATE = /^(\d{2})\/(\d{2})\/(\d{4})$/;

/** Decode the period pickers' pipe-separated any-of value. */
function chosenPeriodValues(value: string): string[] {
  return !value || value === "ALL" ? [] : value.split("|").filter(Boolean);
}

export function partsOf(date: string): { d: string; m: string; y: string } | null {
  const m = DATE.exec(String(date ?? "").trim());
  return m ? { d: m[1], m: m[2], y: m[3] } : null;
}

/**
 * The workspace's any-of period filter.
 *
 * Its pickers may carry several pipe-separated years, months or dates. Keeping
 * this beside the single-period rule prevents the fast API page and the full
 * register that replaces it from interpreting an invalid date differently.
 */
export function inChosenPeriod(date: string, period: Period): boolean {
  const years = chosenPeriodValues(period.year);
  const months = chosenPeriodValues(period.month);
  const days = chosenPeriodValues(period.day);
  const parts = partsOf(date);

  // "No date" is one of the year choices. It can be selected by itself or
  // alongside a real year; stale month/day values never hide the undated rows.
  if (!parts) return years.includes(NO_DATE)
    || (!years.length && !months.length && !days.length);

  if (years.length && !years.includes(parts.y)) return false;
  if (months.length && !months.includes(parts.m)) return false;
  // The compact period bar stores a day-of-month ("15"), while Workspace's
  // any-of picker stores the full date ("15/07/2026"). Accept both shapes.
  if (days.length && !days.includes(date) && !days.includes(parts.d)) return false;
  return true;
}

export function inPeriod(job: Job, period: Period): boolean {
  return inChosenPeriod(job.date, period);
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
  if (period.year === NO_DATE) return "ไม่มีวันที่";
  if (period.year === "ALL" && period.month === "ALL" && period.day === "ALL") return "ทั้งแผน";
  const parts: string[] = [];
  if (period.day !== "ALL") parts.push(period.day);
  if (period.month !== "ALL") parts.push(monthLabel(period.month));
  if (period.year !== "ALL") parts.push(period.year);
  return parts.join(" ");
}

/**
 * The most recent day that carries work, for the "latest day" shortcut.
 *
 * Ranked through this file's own parser rather than through `dnum` next door.
 * It was already discarding anything `partsOf` refused before asking `dnum` to
 * order what was left, so the two agreed by construction and only one of them
 * was needed — and without that import nothing outside this file is reached,
 * which is what lets the period rules be tested on their own.
 */
export function latestDay(jobs: Job[]): Period | null {
  let best: { d: string; m: string; y: string } | null = null;
  let highest = -1;

  for (const job of jobs) {
    const parts = partsOf(job.date);
    if (!parts) continue;
    const rank = Number(parts.y) * 10000 + Number(parts.m) * 100 + Number(parts.d);
    if (rank > highest) { highest = rank; best = parts; }
  }

  return best ? { year: best.y, month: best.m, day: best.d } : null;
}
