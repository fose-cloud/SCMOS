/**
 * What a Domestic trip costs, read off the rate card by postcode.
 *
 * The two columns on the grid — เรทน้ำมัน and TRANSPORTATION RATE — were blank
 * because nothing joined a job to the card. The join is the **destination
 * postcode**: the register's ZIP CODE column against the card's own postcode,
 * which the card's entity comment already calls the identifier for these
 * routes.
 *
 * Not the destination name. The two sides spell places differently — "Chonburi,
 * Amatanakorn" against "Chonburi, Amatanakorn (MCP)" — and matching on text
 * would miss most of them and match the wrong one occasionally, which is worse.
 *
 * A trip can send more than one truck. The card prices one truck of one size,
 * so the rate is the sum over the sizes the job actually sent: a job with one
 * 6-wheel and one 10-wheel costs both, and one with two 10-wheels costs twice.
 *
 * No imports on purpose. The arithmetic that decides what a trip costs should be
 * checkable without a browser or a rate card loaded.
 */

/** As much of a Domestic job as the lookup reads. */
export type RatedJob = {
  /** The destination postcode. The join key, and the only one. */
  zip?: string;
  /** The warehouse the run leaves from, when the register recorded one. */
  wh?: string;
  /** How many of each size went. */
  v4?: string;
  v6?: string;
  v10?: string;
  vtl?: string;
};

/** One priced route off the card, in the shape this module needs. */
export type RateLaneLike = {
  from: string;
  /** The destination postcode. */
  county: string;
  /** Price by vehicle, one entry per band. */
  prices: Record<string, (number | null)[]>;
};

/**
 * The job's vehicle counts, and which of the card's prices each is read at.
 *
 * A tail lift is priced as a 6-wheel. Neither card quotes one — the selling
 * card offers 4W, 6W, 10W and a 20'/40'GP it never priced, and the cost card is
 * six sheets of the three wheel sizes — so there is no tail-lift rate to read,
 * and the account team's answer is to use the 6-wheel's.
 *
 * `label` is kept apart from `vehicle` so the tooltip can say which column a
 * figure came from. A trip billed at the 6W rate because it sent a tail lift
 * should say so, not appear to have sent a 6-wheel.
 */
export const SIZES: { label: string; vehicle: string; field: keyof RatedJob }[] = [
  { label: "4W", vehicle: "4W", field: "v4" },
  { label: "6W", vehicle: "6W", field: "v6" },
  { label: "10W", vehicle: "10W", field: "v10" },
  { label: "TAIL LIFT", vehicle: "6W", field: "vtl" },
];

/**
 * How many of a size went, which is not always a whole number.
 *
 * 0.5 in a column means half the trip's rate for that size — a load shared with
 * somebody else's, which the operators write as a half. So the figure is kept
 * as typed rather than rounded: rounding turned every 0.5 into a whole truck
 * and doubled what the trip was billed.
 */
const count = (value: string | undefined): number => {
  const number = Number(String(value ?? "").replace(/[,\s]/g, ""));
  return Number.isFinite(number) && number > 0 ? number : 0;
};

/** A postcode as the join reads it: digits only, so " 21140" and "21140" meet. */
export function zipKey(value: string | undefined | null): string {
  return String(value ?? "").replace(/\D/g, "");
}

/**
 * Whether a lane's origin is the one this job left from.
 *
 * Loose on purpose, and only used to break a tie. The register writes "JWD" and
 * "UNITHAI"; the card writes "SCGJWD Warehouse (LCH)" and "Unithai (Bangna KM.
 * 23)". There is no agreed mapping between the two, so this asks whether either
 * contains the other and nothing more.
 *
 * A job whose warehouse matches nothing is not refused — the postcode is the
 * join, and the warehouse only decides which of two lanes to that postcode
 * applies.
 */
export function sameOrigin(warehouse: string | undefined, laneFrom: string): boolean {
  const a = String(warehouse ?? "").trim().toUpperCase();
  const b = String(laneFrom ?? "").trim().toUpperCase();
  if (!a || !b) return false;
  return a.includes(b) || b.includes(a);
}

/**
 * The lane a job is priced against, or null.
 *
 * The postcode first, because that is the join. Where a postcode has more than
 * one lane — the card quotes 20000 twice, from two warehouses — the job's own
 * warehouse breaks the tie. Where it cannot, the answer is null rather than the
 * first row: two prices for one trip is a question, not a rate.
 */
export function laneFor(
  job: RatedJob,
  lanes: readonly RateLaneLike[],
): { lane: RateLaneLike | null; reason: "" | "no-zip" | "no-lane" | "ambiguous" } {
  const zip = zipKey(job.zip);
  if (!zip) return { lane: null, reason: "no-zip" };

  const matches = lanes.filter((lane) => zipKey(lane.county) === zip);
  if (matches.length === 0) return { lane: null, reason: "no-lane" };
  if (matches.length === 1) return { lane: matches[0], reason: "" };

  const mine = matches.filter((lane) => sameOrigin(job.wh, lane.from));
  if (mine.length === 1) return { lane: mine[0], reason: "" };

  // More than one lane and no way to choose. The card really does quote one
  // postcode twice at the same price, so check that before giving up —
  // identical prices are one lane written twice, not a choice.
  const first = JSON.stringify(matches[0].prices);
  const allSame = matches.every((lane) => JSON.stringify(lane.prices) === first);
  return allSame ? { lane: matches[0], reason: "" } : { lane: null, reason: "ambiguous" };
}

export type RatedTrip = {
  /** The total for the trip, in baht. Null when it could not be worked out. */
  total: number | null;
  /** What each size contributed, for a tooltip that explains the number. */
  parts: { vehicle: string; trucks: number; each: number }[];
  /** Why there is no total, when there is none. */
  reason: "" | "no-zip" | "no-lane" | "ambiguous" | "no-trucks" | "no-band" | "not-quoted";
};

/**
 * What the trip costs: every truck it sent, at the band the diesel figure picks.
 *
 * Null rather than zero whenever any part of that is unknown — a lane that is
 * not on the card, a band the diesel does not reach, a size the card does not
 * quote. A zero in this column would read as a free trip, and it is the column
 * an invoice gets checked against.
 *
 * A tail lift counts as a vehicle here, priced at the 6-wheel rate — see SIZES.
 * In thirty-seven rows of the August register a tail lift and a wheel size never
 * appear on the same job, so the two are alternatives in practice rather than a
 * truck and an accessory.
 */
export function rateTrip(
  job: RatedJob,
  lanes: readonly RateLaneLike[],
  band: number,
): RatedTrip {
  const { lane, reason } = laneFor(job, lanes);
  if (!lane) return { total: null, parts: [], reason };
  if (band < 0) return { total: null, parts: [], reason: "no-band" };

  const wanted = SIZES
    .map((size) => ({
      label: size.label, vehicle: size.vehicle,
      trucks: count(job[size.field] as string | undefined),
    }))
    .filter((one) => one.trucks > 0);
  if (wanted.length === 0) return { total: null, parts: [], reason: "no-trucks" };

  const parts: { vehicle: string; trucks: number; each: number }[] = [];
  for (const one of wanted) {
    const row = lane.prices[one.vehicle];
    const each = row?.[band];
    // A size the card does not quote is not worth zero. The whole trip goes
    // unpriced rather than being billed for the trucks that happen to be on it.
    if (each === null || each === undefined) return { total: null, parts: [], reason: "not-quoted" };
    // Labelled by the column it was counted in, priced by the card's own key.
    parts.push({ vehicle: one.label, trucks: one.trucks, each });
  }

  return {
    // Whole baht at the end, not per part. A half of an odd price gives satang,
    // and rounding each line separately would drift from the sum of the lines.
    total: Math.round(parts.reduce((sum, part) => sum + part.trucks * part.each, 0)),
    parts,
    reason: "",
  };
}

/** Why a trip has no rate, in words a person reading the grid can act on. */
export function explain(reason: RatedTrip["reason"], warehouse?: string): string {
  switch (reason) {
    case "no-zip": return "งานนี้ยังไม่มี ZIP CODE — เป็นตัวที่ใช้จับคู่กับตารางค่าขนส่ง";
    case "no-lane": return "ไม่มีเส้นทางนี้ในการ์ดราคา";
    case "ambiguous": return warehouse
      ? `รหัสไปรษณีย์นี้มีราคาจากสองคลัง และ ${warehouse} ไม่ตรงกับคลังไหนในการ์ด`
      // 15 of the 36 postcodes on the selling card are quoted from both
      // warehouses at different prices. Without the job's W/H there is no way
      // to choose, and picking one would be inventing a price.
      : "รหัสไปรษณีย์นี้มีราคาจากสองคลัง — งานนี้ยังไม่ได้ระบุ W/H จึงเลือกไม่ได้";
    case "no-trucks": return "ยังไม่ได้ระบุจำนวนรถ";
    case "no-band": return "ยังไม่มีเรทน้ำมันของเดือนนี้";
    case "not-quoted": return "การ์ดราคาไม่ได้เสนอราคารถขนาดนี้ในเส้นทางนี้";
    default: return "";
  }
}
