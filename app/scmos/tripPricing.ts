/**
 * What a Domestic trip is priced at, and where every part of the figure
 * came from — one rule for the grid's three money columns, the workbook
 * export and the haulier reconciliation, which had been reading the same
 * arithmetic from three places.
 *
 * The order is the one the department set: a rate keyed on the job is what
 * was actually agreed and wins; otherwise the customer's card is read at the
 * trip's diesel — the job's own figure first, then the average of the month
 * it ran in off the Oil Rate tab, then the screen-wide stand-in — and the
 * return leg is added at its share. Null is the honest answer whenever a
 * part is unknown; a zero would read as a free trip.
 *
 * Imported with the extension so the leaf loads under Node's test runner.
 */

import { dieselRate } from "./diesel.ts";
import { rateForJob, type DieselDay } from "./dieselMonth.ts";
import { rateTrip, type RateLaneLike, type RatedJob, type RatedTrip } from "./domesticRate.ts";
import { bandForDiesel, type FuelBand } from "./rates.ts";
import { returnKind, returnLoadCharge, tripCost, type ReturnKind } from "./returnLoad.ts";

/** As much of a job as pricing reads. */
export type PriceableJob = RatedJob & {
  date?: string;
  cost?: string;
  diesel?: string;
  returnLoad?: string;
  returnFinished?: string;
};

/** A card as the grid holds it: the lanes and the fuel clause. */
export type PricingCard = { lanes: readonly RateLaneLike[]; bands: readonly FuelBand[] };

export type TripPrice = {
  /** The trip's rate in baht, or null when nothing can say. */
  rate: number | null;
  /** Keyed on the job, read off the card, or nothing. */
  from: "keyed" | "card" | "none";
  /** The diesel the card was read at, and which figure that was. */
  diesel: number;
  dieselFrom: "job" | "month" | "default";
  /** MM/yyyy when the diesel is the month's average. */
  month: string;
  /** The band that diesel fell in on the card; -1 when none or no card. */
  band: number;
  /** The card's own reading, for the tooltip and for the reason when it could not price. */
  rated: RatedTrip | null;
  returnKind: ReturnKind;
  returnCharge: number | null;
  total: number | null;
};

/** The diesel a trip is read at: its own, its month's, or the stand-in. */
export function dieselFor(job: PriceableJob, dieselDays: readonly DieselDay[], fallback: number):
  { price: number; from: TripPrice["dieselFrom"]; month: string } {
  const own = dieselRate(job.diesel);
  if (own !== null) return { price: own, from: "job", month: "" };
  const monthly = rateForJob(dieselDays, job.date);
  if (monthly && monthly.average !== null) return { price: monthly.average, from: "month", month: monthly.month };
  return { price: fallback, from: "default", month: "" };
}

export function priceTrip(
  job: PriceableJob,
  card: PricingCard | null,
  dieselDays: readonly DieselDay[],
  fallback: number,
): TripPrice {
  const diesel = dieselFor(job, dieselDays, fallback);
  const kind = returnKind(job.returnLoad, job.returnFinished);
  const band = card ? bandForDiesel([...card.bands], diesel.price) : -1;

  const keyed = Number(String(job.cost ?? "").replace(/[,\s฿]/g, ""));
  let rate: number | null = null;
  let from: TripPrice["from"] = "none";
  let rated: RatedTrip | null = null;
  if (Number.isFinite(keyed) && keyed > 0) {
    rate = keyed;
    from = "keyed";
  } else if (card) {
    rated = rateTrip(job, card.lanes, band);
    if (rated.total !== null) { rate = rated.total; from = "card"; }
  }

  return {
    rate, from,
    diesel: diesel.price, dieselFrom: diesel.from, month: diesel.month, band, rated,
    returnKind: kind,
    returnCharge: kind === "none" ? null : returnLoadCharge(rate, kind),
    total: tripCost(rate, kind),
  };
}

/** The diesel, said the way the tooltip says it. */
export function dieselNote(price: TripPrice): string {
  return `ที่ดีเซล ${price.diesel.toFixed(2)}`
    + (price.dieselFrom === "month" ? ` (เฉลี่ยเดือน ${price.month})` : price.dieselFrom === "job" ? " (ของงานนี้)" : " (ค่าตั้งต้น)");
}
