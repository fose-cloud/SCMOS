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

/** The vehicle sizes the card quotes, and the job field each is counted in. */
export const SIZES: { vehicle: string; field: keyof RatedJob }[] = [
  { vehicle: "4W", field: "v4" },
  { vehicle: "6W", field: "v6" },
  { vehicle: "10W", field: "v10" },
];

const count = (value: string | undefined): number => {
  const number = Number(String(value ?? "").replace(/[,\s]/g, ""));
  return Number.isFinite(number) && number > 0 ? Math.round(number) : 0;
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
 * A tail lift is not counted as a truck. It is a property of the lorry sent,
 * which is how the cargo receipt writes it too, and the card does not price one
 * separately.
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
    .map((size) => ({ vehicle: size.vehicle, trucks: count(job[size.field] as string | undefined) }))
    .filter((one) => one.trucks > 0);
  if (wanted.length === 0) return { total: null, parts: [], reason: "no-trucks" };

  const parts: { vehicle: string; trucks: number; each: number }[] = [];
  for (const one of wanted) {
    const row = lane.prices[one.vehicle];
    const each = row?.[band];
    // A size the card does not quote is not worth zero. The whole trip goes
    // unpriced rather than being billed for the trucks that happen to be on it.
    if (each === null || each === undefined) return { total: null, parts: [], reason: "not-quoted" };
    parts.push({ vehicle: one.vehicle, trucks: one.trucks, each });
  }

  return {
    total: parts.reduce((sum, part) => sum + part.trucks * part.each, 0),
    parts,
    reason: "",
  };
}

/** Why a trip has no rate, in words a person reading the grid can act on. */
export function explain(reason: RatedTrip["reason"], warehouse?: string): string {
  switch (reason) {
    case "no-zip": return "งานนี้ยังไม่มี ZIP CODE — เป็นตัวที่ใช้จับคู่กับตารางค่าขนส่ง";
    case "no-lane": return "ไม่มีเส้นทางนี้ในการ์ดราคา";
    case "ambiguous": return `รหัสไปรษณีย์นี้มีหลายเส้นทางในการ์ด และคลัง ${warehouse || "ของงานนี้"} ไม่ตรงกับอันไหน`;
    case "no-trucks": return "ยังไม่ได้ระบุจำนวนรถ";
    case "no-band": return "ยังไม่มีเรทน้ำมันของเดือนนี้";
    case "not-quoted": return "การ์ดราคาไม่ได้เสนอราคารถขนาดนี้ในเส้นทางนี้";
    default: return "";
  }
}
