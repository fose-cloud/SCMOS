/**
 * How much work ran, over what period, and whose it was.
 *
 * One report where the catalogue used to carry six cards — volume by period,
 * import, export, by supplier, by truck type, by customer. They were six views
 * of one count over one set of filters, and splitting them meant six places for
 * the same period to be typed and six chances for two of them to disagree about
 * what a trip is.
 *
 * Every table here is the same shape: something down the side, something across
 * the top, a count in each cell. Period by direction, customer by direction,
 * vehicle type by the person who handled it. One function does all of them, so
 * a rule about what counts as a trip cannot hold in one table and not another.
 *
 * Three exclusions, all reported on the page rather than folded into a total.
 *
 * A cancelled job is not volume. It was booked and it did not run, and counting
 * it would inflate a month on the strength of work nobody did.
 *
 * A job whose plan date cannot be read belongs to no day, week or month. It is
 * not dropped and it is not guessed at — it is counted as undated and shown,
 * because a total that quietly loses rows is worse than one that admits them.
 *
 * The columns come from the register rather than from an assumption about what
 * is in it, with one exception: a caller may pin columns that must appear even
 * at zero. Domestic work is worked under The Chemours and a month with none of
 * it should say so rather than silently dropping the column, which reads as the
 * report having forgotten about it.
 *
 * No imports, so `tests/volumeReport.test.mjs` can run the rules without
 * dragging the register, the theme and the nav in behind them — the shape every
 * tested module in this folder has. What that costs is the cancellation rule,
 * which lives in ops and must not be written a second time here: it is passed
 * in, and the caller names it.
 */

export type Grain = "day" | "week" | "month";

/** As much of a job as counting it needs. `Job` satisfies this structurally. */
export type VolumeJob = {
  date?: string; cat?: string; status?: string;
  customer?: string; trucker?: string; type?: string;
  op?: string; opId?: string;
  cyYard?: string; destination?: string; plant?: string; returnLoc?: string;
};

/** One row: what it is, and how many trips fall in each column. */
export type Slice = {
  /** Sorts a period table; zero in the rankings, which order by size. */
  order: number;
  label: string;
  byCol: Record<string, number>;
  total: number;
};

export type Tally = {
  rows: Slice[];
  /** The columns, commonest first, with any pinned ones kept even at zero. */
  cols: string[];
  totals: Record<string, number>;
  counted: number;
  /** Booked and did not run. Excluded from every figure above. */
  cancelled: number;
  /** Carrying a date nothing can read. Excluded, and said so. */
  undated: number;
  /** Counted, but with nothing written in the column being grouped on. */
  blank: number;
};

const DATE = /^(\d{2})\/(\d{2})\/(\d{4})/;

/** What a row or column is called when the field behind it is empty. */
export const BLANK = "ไม่ระบุ";

/** dd/MM/yyyy to its parts, or null when it is not a date. */
function parts(date: string | undefined): { d: number; m: number; y: number } | null {
  const found = DATE.exec(date ?? "");
  if (!found) return null;
  const d = +found[1], m = +found[2], y = +found[3];
  // A real calendar day, not merely digits that fit the shape: 31/02 and 00/07
  // both match the pattern and neither is a date anybody planned for.
  const made = new Date(Date.UTC(y, m - 1, d));
  if (made.getUTCFullYear() !== y || made.getUTCMonth() !== m - 1 || made.getUTCDate() !== d) return null;
  return { d, m, y };
}

const pad = (n: number) => String(n).padStart(2, "0");

/**
 * The Monday of that date's week.
 *
 * Weeks are named by the Monday they start on rather than by a week number.
 * Week numbering has more than one definition — ISO's, the calendar's, and the
 * one whoever is reading assumes — and a report is not the place to discover
 * which one somebody had in mind. A date is a date.
 */
export function weekStart(date: string | undefined): { d: number; m: number; y: number } | null {
  const on = parts(date);
  if (!on) return null;
  const at = new Date(Date.UTC(on.y, on.m - 1, on.d));
  // getUTCDay: Sunday is 0, so Sunday belongs to the week that began six days
  // earlier rather than starting one of its own.
  at.setUTCDate(at.getUTCDate() - ((at.getUTCDay() + 6) % 7));
  return { d: at.getUTCDate(), m: at.getUTCMonth() + 1, y: at.getUTCFullYear() };
}

/** The period a job falls in, or null when its date cannot be read. */
export function bucket(date: string | undefined, grain: Grain): { order: number; label: string } | null {
  const on = parts(date);
  if (!on) return null;

  if (grain === "month") return { order: on.y * 10000 + on.m * 100, label: `${pad(on.m)}/${on.y}` };
  if (grain === "week") {
    const start = weekStart(date)!;
    return {
      order: start.y * 10000 + start.m * 100 + start.d,
      label: `สัปดาห์ ${pad(start.d)}/${pad(start.m)}/${start.y}`,
    };
  }
  return { order: on.y * 10000 + on.m * 100 + on.d, label: `${pad(on.d)}/${pad(on.m)}/${on.y}` };
}

/** dd/MM/yyyy as yyyymmdd for range comparison, or 0 when unreadable. */
export function dayOrder(date: string | undefined): number {
  const on = parts(date);
  return on ? on.y * 10000 + on.m * 100 + on.d : 0;
}

export type Scope = {
  /** Inclusive dd/MM/yyyy; blank for open-ended. */
  from?: string;
  to?: string;
  /** One direction only, for the import and export sections. */
  cat?: string;
  /** Columns that must appear even with nothing in them. */
  always?: string[];
  /** `isCancelled` from ops — passed rather than reimplemented. */
  cancelledRule: (job: VolumeJob) => boolean;
};

/** What names a row. `key` groups where it differs from what is displayed. */
type Named = { order: number; label: string; key?: string };

/**
 * Count trips into a table: `group` down the side, `split` across the top.
 *
 * The exclusions happen here, once, so every table on the page counts the same
 * trips. That matters more than it sounds: the first version let the rankings
 * keep the jobs whose dates could not be read, on the grounds that a job with a
 * bad date still has a customer — and the page then showed 2,076 trips in the
 * period table and 2,104 in the one directly beneath it. Both were defensible
 * and the pair was unreadable. A trip nobody can date is not known to be in the
 * period at all, so it is out of all of them and counted where it can be seen.
 */
function tally(
  jobs: VolumeJob[],
  scope: Scope,
  group: (job: VolumeJob) => Named,
  split: (job: VolumeJob) => string,
  /**
   * Columns to keep even at zero.
   *
   * Passed by the caller rather than read from the scope, because a pin only
   * means anything when the columns are the thing pinned. A table split by
   * person once carried five people followed by IMPORT, EXPORT and DELIVERY at
   * zero — the pins were for a set of columns that table does not have.
   */
  pins?: string[],
): Tally {
  const start = dayOrder(scope.from);
  const end = dayOrder(scope.to);
  const wanted = (scope.cat ?? "").trim().toUpperCase();

  const buckets = new Map<number | string, Slice>();
  const totals: Record<string, number> = {};
  const seen = new Map<string, number>();
  let counted = 0, cancelled = 0, undated = 0, blank = 0;

  for (const job of jobs) {
    if (wanted && (job.cat ?? "").trim().toUpperCase() !== wanted) continue;

    // Out of the range asked for is out of scope entirely — not counted as
    // cancelled or undated either, because those figures describe the period
    // being reported on and nothing else.
    const day = dayOrder(job.date);
    if (day) {
      if (start && day < start) continue;
      if (end && day > end) continue;
    }

    // Before the date test, so a cancelled job is reported as cancelled
    // whatever state its date is in. One job, one line to explain it.
    if (scope.cancelledRule(job)) { cancelled++; continue; }
    if (!day) { undated++; continue; }

    const at = group(job);
    if (at.label === BLANK) blank++;

    const col = split(job);
    seen.set(col, (seen.get(col) ?? 0) + 1);
    totals[col] = (totals[col] ?? 0) + 1;
    counted++;

    const key = at.key ?? (at.order || at.label);
    let row = buckets.get(key);
    if (!row) { row = { order: at.order, label: at.label, byCol: {}, total: 0 }; buckets.set(key, row); }
    row.byCol[col] = (row.byCol[col] ?? 0) + 1;
    row.total++;
  }

  // Commonest first so the column that matters is nearest the labels, then by
  // name so equal counts do not swap places between one render and the next.
  const cols = [...seen.entries()]
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .map(([col]) => col);

  // Pinned columns, in the order asked for, after the ones with work in them.
  // Skipped when a single direction was asked for: an import-only table with
  // empty EXPORT and DOMESTIC columns says nothing anybody needs.
  if (!wanted) {
    for (const col of pins ?? []) {
      if (!cols.includes(col)) { cols.push(col); totals[col] = totals[col] ?? 0; }
    }
  }

  return { rows: [...buckets.values()], cols, totals, counted, cancelled, undated, blank };
}

/** A job's direction, which is what most of these tables split on. */
const direction = (job: VolumeJob) => (job.cat ?? "").trim() || BLANK;

/**
 * Trips per period, newest first — the meeting is about this week, and last
 * March is scrolling.
 */
export function byPeriod(jobs: VolumeJob[], grain: Grain, scope: Scope): Tally {
  // Non-null: `tally` has already dropped anything `dayOrder` cannot read, and
  // both read the date through the same `parts`.
  const out = tally(jobs, scope, (job) => bucket(job.date, grain)!, direction, scope.always);
  out.rows.sort((a, b) => b.order - a.order);
  return out;
}

/**
 * Trips per value of one column — customer, haulier, vehicle type, a yard.
 *
 * Heaviest first: a ranking is read from the top and stopped at, so the order
 * is the point of it. Values are counted as the register spells them and are
 * not tidied on the way past; where two spellings of one carrier exist that is
 * the register's to fix, and a report that merged them would hide it.
 */
export function byField(
  jobs: VolumeJob[],
  field: (job: VolumeJob) => string | undefined,
  scope: Scope,
  split: (job: VolumeJob) => string = direction,
): Tally {
  const out = tally(
    jobs, scope,
    (job) => ({ order: 0, label: (field(job) ?? "").trim() || BLANK }),
    split,
    // Only when the columns are the directions. A table split by who handled
    // the work has people across the top, and pinning IMPORT there adds a
    // column that can never hold anything.
    split === direction ? scope.always : undefined);
  out.rows.sort((a, b) => b.total - a.total || a.label.localeCompare(b.label));
  return out;
}

/**
 * Trips per person, split by direction.
 *
 * Grouped on the operator id and labelled with the name — the rule the rest of
 * the app uses. Ownership by name has broken here before: two spellings of one
 * person become two people, and the register has no way of telling anybody. The
 * two agree today, and this does not depend on them continuing to.
 */
export function byOperator(jobs: VolumeJob[], scope: Scope): Tally {
  const out = tally(jobs, scope, (job) => {
    const id = (job.opId ?? "").trim();
    const name = (job.op ?? "").trim();
    return { order: 0, label: name || BLANK, key: id || name || BLANK };
  }, direction, scope.always);
  out.rows.sort((a, b) => b.total - a.total || a.label.localeCompare(b.label));
  return out;
}

/** The person a job sits with, for a table split by who handled it. */
export const owner = (job: VolumeJob) => (job.op ?? "").trim() || BLANK;

/** The busiest row, for the line above a table. */
export function busiest(tallied: Tally): Slice | null {
  return tallied.rows.reduce<Slice | null>(
    (best, row) => (best === null || row.total > best.total ? row : best), null);
}
