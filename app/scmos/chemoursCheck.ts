/**
 * Checking the Domestic runs against the tables: did each trip go where the
 * card prices, in a truck the card prices, and what does the card say the
 * haulier may charge for it.
 *
 * The account team check a haulier's invoice line by line against two
 * things — the Domestic grid, which says what ran, and the cost card, which
 * says what a run costs — and asked on 15 September 2026 for SCMOS to do the
 * looking. So every Domestic job is read against the haulier's own lanes on
 * the cost card, at the diesel band of the month it ran in, and comes back
 * either priced or with the one thing that stops it being priced.
 *
 * Nothing here decides that an invoice is right. What it decides is what the
 * table says the trip should cost, and whether the job as keyed fits the
 * table at all; the invoice is compared with that by a person.
 *
 * No React and no workbook reader, so the arithmetic can be checked with a
 * hand-keyed row and no browser.
 */

// With the extension, which is how Node loads a leaf under test: the rules
// this joins — the lane lookup, the band, the return leg, the month's diesel
// — each live in one module already, and are read from there rather than
// written a second time here.
import { laneFor, rateTrip, sameOrigin, zipKey, SIZES, type RateLaneLike, type RatedTrip } from "./domesticRate.ts";
import { dieselRate } from "./diesel.ts";
import { averageFor, expand, monthKey, monthOf, type DieselChange, type DieselDay } from "./dieselMonth.ts";
import { bandForDiesel, type FuelBand } from "./rates.ts";
import { returnKind, tripCost, type ReturnKind } from "./returnLoad.ts";

/** As much of a Domestic job as the check reads. */
export type CheckableJob = {
  key: string;
  date: string;
  trucker: string;
  customer: string;
  wh?: string;
  zip?: string;
  province?: string;
  destination?: string;
  jobCode?: string;
  dCode?: string;
  v4?: string; v6?: string; v10?: string; vtl?: string;
  diesel?: string;
  cost?: string;
  returnLoad?: string;
  returnFinished?: string;
  status?: string;
};

/** A cost-card lane, which knows whose it is. */
export type CarrierLane = RateLaneLike & { carrier: string; to: string };

/**
 * Why a trip could not be priced off the table, or "" when it could.
 *
 * `carrier` is the one the trip rater cannot produce: the card has nobody by
 * that name, so there are no lanes to look in. The rest are the rater's own.
 */
export type Verdict = "" | "carrier" | RatedTrip["reason"] | "no-diesel";

export type CheckedTrip = {
  job: CheckableJob;
  /** MM/yyyy the trip ran in, "" when the date is unreadable. */
  month: string;
  /** The haulier the card calls what the register calls this job's trucker. */
  carrier: string;
  /** The diesel the trip is read at, and where it came from. */
  diesel: number | null;
  dieselFrom: "job" | "month" | "none";
  /** Which band that diesel falls in, and its label off the card. */
  band: number;
  bandLabel: string;
  /** The lane the trip was priced against, when one was found. */
  lane: CarrierLane | null;
  /** The trucks the job sent, as the card names them. */
  trucks: string;
  /** What the card says the run costs, and the pieces it is made of. */
  cost: RatedTrip;
  /** The cost with the return leg, when one is ticked. */
  returnKind: ReturnKind;
  total: number | null;
  verdict: Verdict;
  /**
   * Priced, but worth a look: the only lane at this postcode leaves from a
   * warehouse other than the job's. The postcode is the join and the trip is
   * priced off it; the note says which origin the price assumes.
   */
  originNote: string;
};

/** What the lane assumes that the job does not say, or "" when they agree. */
function originNoteFor(job: CheckableJob, lane: CarrierLane | null): string {
  const wh = String(job.wh ?? "").trim();
  if (!lane || !wh || sameOrigin(wh, lane.from)) return "";
  return `W/H ของงานคือ ${wh} แต่ตารางคิดจาก ${lane.from}`;
}

/**
 * Whether the register's trucker and the card's carrier are the same company.
 *
 * Loose the way sameOrigin is: the card is filed under the name typed when it
 * was loaded — "THAI KOT" — and the register writes whatever the operator
 * wrote, so either containing the other is the most that can be asked.
 */
export function sameCarrier(trucker: string | undefined, carrier: string): boolean {
  const a = String(trucker ?? "").trim().toUpperCase().replace(/\s+/g, " ");
  const b = String(carrier ?? "").trim().toUpperCase().replace(/\s+/g, " ");
  if (!a || !b) return false;
  return a === b || a.includes(b) || b.includes(a);
}

/** The carrier the card knows this job's trucker as, or "" when it knows none. */
export function carrierOnCard(trucker: string | undefined, carriers: readonly string[]): string {
  // An exact name first, so "THAI KOT" is not handed to "THAI KOT (JWD)" when
  // both are on the card; then the loose match.
  const exact = carriers.find((one) => one.trim().toUpperCase() === String(trucker ?? "").trim().toUpperCase());
  if (exact) return exact;
  return carriers.find((one) => sameCarrier(trucker, one)) ?? "";
}

/** The trucks a job sent, as "1×6W · 2×10W", or "" when it says none. */
export function trucksOf(job: CheckableJob): string {
  return SIZES
    .map((size) => ({ label: size.label, n: Number(String(job[size.field as keyof CheckableJob] ?? "").replace(/[,\s]/g, "")) }))
    .filter((one) => Number.isFinite(one.n) && one.n > 0)
    .map((one) => `${one.n}×${one.label}`)
    .join(" · ");
}

/**
 * The card's lanes with every truck size on one row.
 *
 * A haulier card is read one sheet per truck size, so the register holds
 * "Unithai → Bangkae 10160" three times — once with the 4W price, once with
 * the 6W, once with the 10W. The trip rater wants the selling card's shape,
 * one row per route with a price per size, because a trip that sent a 6W and
 * a 10W is priced off one lane. Folded here, and the fold is by route: the
 * same carrier, origin, destination and postcode.
 */
export function mergeLanes(lanes: readonly CarrierLane[]): CarrierLane[] {
  const byRoute = new Map<string, CarrierLane>();
  for (const lane of lanes) {
    const key = [lane.carrier, lane.from, lane.to, zipKey(lane.county)].map((part) => String(part ?? "").trim().toUpperCase()).join("|");
    const held = byRoute.get(key);
    if (!held) { byRoute.set(key, { ...lane, prices: { ...lane.prices } }); continue; }
    for (const [vehicle, row] of Object.entries(lane.prices)) {
      // A size quoted twice for one route is one price written on two
      // sheets; the first read stands, so nothing is quietly overwritten.
      if (!(vehicle in held.prices)) held.prices[vehicle] = row;
    }
  }
  return [...byRoute.values()];
}

/**
 * Every Domestic job read against the card.
 *
 * The diesel is the job's own figure when one was keyed — that is what the
 * trip was actually charged against — and otherwise the average of the month
 * it ran in, off the Oil Rate tab. Neither known, the trip cannot be banded
 * and says so rather than being read at today's pump price.
 */
export function checkTrips(
  jobs: readonly CheckableJob[],
  lanes: readonly CarrierLane[],
  bands: readonly FuelBand[],
  changes: readonly DieselChange[],
): CheckedTrip[] {
  const merged = mergeLanes(lanes);
  const carriers = [...new Set(merged.map((lane) => lane.carrier).filter(Boolean))];
  const byCarrier = new Map(carriers.map((carrier) => [carrier, merged.filter((lane) => lane.carrier === carrier)]));
  const days = dieselDays(changes, jobs);

  return jobs.map((job) => {
    const month = monthOf(job.date);
    const carrier = carrierOnCard(job.trucker, carriers);
    const keyed = dieselRate(job.diesel);
    const monthly = month ? averageFor(days, month).average : null;
    const diesel = keyed ?? monthly;
    const dieselFrom: CheckedTrip["dieselFrom"] = keyed !== null ? "job" : monthly !== null ? "month" : "none";
    const band = diesel === null ? -1 : bandForDiesel([...bands], diesel);
    const bandLabel = band >= 0 ? bands[band].label : "";
    const kind = returnKind(job.returnLoad, job.returnFinished);
    const trucks = trucksOf(job);

    const empty: RatedTrip = { total: null, parts: [], reason: "" };
    if (!carrier) {
      return { job, month, carrier, diesel, dieselFrom, band, bandLabel, lane: null, trucks,
        cost: empty, returnKind: kind, total: null, verdict: "carrier", originNote: "" };
    }
    const mine = byCarrier.get(carrier) ?? [];
    const found = laneFor(job, mine).lane as CarrierLane | null;
    if (diesel === null) {
      // The lane is still looked for, so a trip with no diesel and no lane
      // reports the lane — the thing somebody can fix on the job.
      const reason = laneFor(job, mine).reason;
      return { job, month, carrier, diesel, dieselFrom, band, bandLabel, lane: found, trucks,
        cost: empty, returnKind: kind, total: null, verdict: reason || "no-diesel", originNote: originNoteFor(job, found) };
    }
    const cost = rateTrip(job, mine, band);
    return {
      job, month, carrier, diesel, dieselFrom, band, bandLabel, lane: found, trucks,
      cost, returnKind: kind,
      total: cost.total === null ? null : tripCost(cost.total, kind),
      verdict: cost.reason,
      originNote: originNoteFor(job, found),
    };
  });
}

/**
 * The pump price on every day the jobs could fall on, expanded from the
 * published changes month by month — see dieselMonth for why a month's
 * opening price comes from a change before it.
 */
function dieselDays(changes: readonly DieselChange[], jobs: readonly CheckableJob[]): DieselDay[] {
  if (changes.length === 0) return [];
  const months = [...new Set(jobs.map((job) => monthOf(job.date)).filter(Boolean))];
  return months.flatMap((month) => expand(changes, month));
}

/** What a verdict means to the person reading the row. */
export function verdictText(trip: CheckedTrip): string {
  switch (trip.verdict) {
    case "": return "ตรงตามตาราง";
    case "carrier": return `ผู้ขนส่ง "${trip.job.trucker || "(ไม่ระบุ)"}" ไม่มีการ์ดต้นทุน`;
    case "no-zip": return "งานนี้ยังไม่มี ZIP CODE — ใช้จับคู่ปลายทางกับตาราง";
    case "no-lane": return `ปลายทาง ${trip.job.zip || ""} ไม่อยู่ในตารางของ ${trip.carrier}`;
    case "ambiguous": return "รหัสไปรษณีย์นี้มีราคาจากสองคลัง และ W/H ของงานไม่ตรงกับคลังไหน";
    case "no-trucks": return "ยังไม่ได้ระบุประเภท/จำนวนรถ";
    case "not-quoted": return "ตารางไม่มีราคารถประเภทนี้ในเส้นทางนี้";
    case "no-diesel": return `ยังไม่มีเรทน้ำมันของเดือน ${trip.month || "นี้"} — บันทึกในแท็บ Oil Rate`;
    case "no-band": return "เรทน้ำมันอยู่นอกช่วงของตาราง";
    default: return trip.verdict;
  }
}

/** What a trip's verdict is about, so a row can say which column to look at. */
export function verdictField(verdict: Verdict): "carrier" | "destination" | "vehicle" | "diesel" | "" {
  switch (verdict) {
    case "carrier": return "carrier";
    case "no-zip": case "no-lane": case "ambiguous": return "destination";
    case "no-trucks": case "not-quoted": return "vehicle";
    case "no-diesel": case "no-band": return "diesel";
    default: return "";
  }
}

export type CarrierTally = {
  carrier: string;
  trips: number;
  priced: number;
  flagged: number;
  /** The sum of what the card says, over the trips it could price. */
  cost: number;
};

/**
 * One line per haulier: how many trips, how many the table could price, and
 * what those come to — the figure an invoice total is held against.
 *
 * Trips the card could not price are counted and not summed, and the tally
 * says so, because a total that quietly leaves out three trips is the wrong
 * number to argue an invoice with.
 */
export function carrierTallies(trips: readonly CheckedTrip[]): CarrierTally[] {
  const groups = new Map<string, CheckedTrip[]>();
  for (const trip of trips) {
    const name = trip.carrier || trip.job.trucker || "(ไม่ระบุผู้ขนส่ง)";
    groups.set(name, [...(groups.get(name) ?? []), trip]);
  }
  return [...groups.entries()]
    .map(([carrier, mine]) => ({
      carrier,
      trips: mine.length,
      priced: mine.filter((trip) => trip.total !== null).length,
      flagged: mine.filter((trip) => trip.verdict !== "").length,
      cost: mine.reduce((sum, trip) => sum + (trip.total ?? 0), 0),
    }))
    .sort((a, b) => b.trips - a.trips || a.carrier.localeCompare(b.carrier));
}

/** The months the jobs fall in, newest first, for the picker. */
export function monthsOf(jobs: readonly CheckableJob[]): string[] {
  return [...new Set(jobs.map((job) => monthOf(job.date)).filter(Boolean))]
    .sort((a, b) => monthKey(b).localeCompare(monthKey(a)));
}

/** The destination as a person names it: the town and the postcode. */
export function placeOf(job: CheckableJob): string {
  const town = (job.destination || job.province || "").trim();
  const zip = zipKey(job.zip);
  return [town, zip].filter(Boolean).join(" ");
}
