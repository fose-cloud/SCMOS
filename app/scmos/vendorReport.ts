/**
 * What each carrier ran, for whom, and how much of it arrived on time.
 *
 * The question is a supervisor's and a buyer's at once: this haulier moves work
 * for six of our customers — how much, and how well for each of them. The
 * carrier scorecard grades a haulier as one number, and the delay report cuts
 * one customer's work by month; neither of them crosses the two, which is where
 * "SANGJA is fine except on HENKEL" lives.
 *
 * <b>The lateness rule is passed in, not written here.</b> `lateMinutes` from
 * util is the one reading of plan against arrival in the browser, the same one
 * the delay report and the dashboard use, and a second copy of those four lines
 * would agree with it exactly until the first time somebody adjusted one of
 * them. This module has no imports at all so it can be run on its own, and the
 * rules it needs arrive as arguments.
 *
 * Two things are deliberate, and both are the same thing said twice:
 *
 * A trip with no arrival recorded is never on time and never late. It is not
 * assessable, it has its own column, and the percentage is taken over what
 * could be measured rather than over everything. On the register this was built
 * against, two of every three live jobs are in that state — a report that
 * quietly counted them as successes would hand somebody a number to take into a
 * carrier meeting that the records cannot defend.
 *
 * And a percentage never travels without the count it was taken over. Ninety
 * per cent of ten and ninety per cent of two hundred are not the same claim.
 */

/**
 * The only two fields this report reads for itself.
 *
 * Generic over whatever else the caller's row carries, because the lateness of
 * a trip is not read here — it arrives already worked out. That keeps the whole
 * module ignorant of how a date is spelled and of which columns hold the plan,
 * which is what lets one reading of lateness serve every screen.
 */
export type Trip = {
  trucker?: string;
  customer?: string;
};

/** What a set of trips came to. */
export type Counts = {
  /** Every trip in scope, measurable or not. */
  trips: number;
  /** Trips where plan and arrival were both recorded well enough to compare. */
  measured: number;
  onTime: number;
  late: number;
  /** In scope, but nobody wrote down enough to say. Reported, never folded in. */
  notAssessable: number;
  /**
   * On time as a percentage of what could be measured, to one decimal place.
   *
   * Null — not zero — when nothing could be measured. Zero is a carrier that
   * was late every time; null is a carrier nobody recorded, and a report that
   * spells the second as the first has accused somebody of the first.
   */
  otd: number | null;
};

export type Line = Counts & { customer: string };
export type Vendor = Counts & { vendor: string; customers: Line[] };

export type Report = {
  vendors: Vendor[];
  total: Counts;
  /**
   * Trips in scope with no carrier named.
   *
   * Counted and said out loud rather than dropped. They are real work, they
   * are missing from every row below, and the difference between the total and
   * the rows is otherwise unexplained.
   */
  unnamed: number;
};

/**
 * How few measured trips make a percentage worth reading as performance.
 *
 * Five, matching what the carrier scorecard refuses to grade below. Nothing is
 * hidden or suppressed on this count — the row is shown with its figures, and
 * the screen marks it — because a carrier with two trips has two trips, and
 * dropping the row would leave the totals not adding up.
 */
export const THIN = 5;

const named = (value: string | undefined, fallback: string): string =>
  (value ?? "").trim() || fallback;

/** An empty tally, so every vendor and customer starts from the same shape. */
const empty = (): Counts =>
  ({ trips: 0, measured: 0, onTime: 0, late: 0, notAssessable: 0, otd: null });

function add(into: Counts, late: number | null, grace: number): void {
  into.trips += 1;
  if (late === null) { into.notAssessable += 1; return; }
  into.measured += 1;
  // Early is on time. The grace period is the customer's service level, not
  // this report's opinion — the caller passes whatever the contract allows.
  if (late > grace) into.late += 1;
  else into.onTime += 1;
}

/** The percentage, worked out only once every trip has been counted. */
function settle(counts: Counts): void {
  counts.otd = counts.measured
    ? Math.round((counts.onTime / counts.measured) * 1000) / 10
    : null;
}

/**
 * Counts trips into carriers, and each carrier into the customers it ran for.
 *
 * Takes the trips the screen is already showing rather than filtering again, so
 * the table, the totals and the workbook cannot disagree about what was in
 * scope — the same arrangement the delay report uses, and for the same reason.
 */
export function byVendor<T extends Trip>(trips: T[], options: {
  /** Minutes a trip may run over its plan and still count as on time. */
  grace: number;
  /** `lateMinutes` from util. Null when the trip cannot be measured. */
  lateOf: (trip: T) => number | null;
  /** What to call a customer that the row does not name. */
  unnamedCustomer?: string;
}): Report {
  const { grace, lateOf, unnamedCustomer = "ไม่ระบุลูกค้า" } = options;

  const held = new Map<string, { counts: Counts; customers: Map<string, Counts> }>();
  const total = empty();
  let unnamed = 0;

  for (const trip of trips) {
    const late = lateOf(trip);
    add(total, late, grace);

    const carrier = (trip.trucker ?? "").trim();
    // A trip nobody assigned belongs to no carrier's record. It still happened,
    // so it stays in the total and is counted where the screen can say so.
    if (!carrier) { unnamed += 1; continue; }

    let vendor = held.get(carrier);
    if (!vendor) {
      vendor = { counts: empty(), customers: new Map() };
      held.set(carrier, vendor);
    }
    add(vendor.counts, late, grace);

    const customer = named(trip.customer, unnamedCustomer);
    let line = vendor.customers.get(customer);
    if (!line) { line = empty(); vendor.customers.set(customer, line); }
    add(line, late, grace);
  }

  settle(total);

  const vendors = [...held.entries()]
    .map(([vendor, one]) => {
      settle(one.counts);
      const customers = [...one.customers.entries()]
        .map(([customer, counts]) => { settle(counts); return { customer, ...counts }; })
        // Most work first, and a name to settle ties so the order does not
        // depend on which row the register happened to hold first.
        .sort((a, b) => b.trips - a.trips || a.customer.localeCompare(b.customer));
      return { vendor, ...one.counts, customers };
    })
    // The busiest carrier first. Not the worst: this is a report on who ran
    // what, and sorting by failure would make it read as a ranking of blame.
    .sort((a, b) => b.trips - a.trips || a.vendor.localeCompare(b.vendor));

  return { vendors, total, unnamed };
}

/** "93.3%", or the reason there is no percentage. */
export function otdLabel(counts: Counts): string {
  return counts.otd === null ? "วัดไม่ได้" : `${counts.otd.toFixed(1)}%`;
}
