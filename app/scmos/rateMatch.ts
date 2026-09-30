/**
 * How a job finds its lane in a carrier's rate book: the vehicle its container wording means, and how well
 * a lane's names fit the job's. Moved out of booking.ts on 30 Sep 2026 so it has no imports — the Billing
 * Case's contract rate is worked out on the server with the same rules (Rules/RateMatch.cs), and
 * tests/fixtures/billing-parity.json holds the two to one answer, which needs this file loadable in a test.
 */

/**
 * The plan's container wording onto the rate cards' vocabulary.
 *
 * The workbooks write the same truck eight ways — 1X6WH', 1X6W, 1x6 WH,
 * 6 WHEEL — because five operators typed them by hand over a month. The rate
 * cards call all of those 6W, and a booking cannot be priced until the two
 * agree.
 */
export function vehicleForType(type: string): string {
  const value = (type ?? "").toUpperCase().replace(/\s+/g, " ").trim();
  if (!value) return "";

  const dg = /\bDG\b/.test(value);
  // No word boundary before the digits: the plan writes 1X20', 1X6WH', 1x40 HQ,
  // so the number that matters always follows a letter, and \b never fires there.
  const wheels = /(\d{1,2})\s*W(?:H|HEELS?)?\b/.exec(value);

  let base = "";
  if (/TK|TANK|ISO/.test(value)) base = "ISO TANK";
  else if (/REEFER|RF\b/.test(value)) base = /40/.test(value) ? "40RF" : "20RF";
  else if (wheels) base = `${Number(wheels[1])}W`;
  else if (/40/.test(value)) base = "40F";
  else if (/20/.test(value)) base = "20F";

  if (!base) return "";
  // A tank or a reefer is quoted as one thing; the DG split only exists on dry
  // boxes and flatbeds.
  if (base === "ISO TANK") return base;
  return dg ? `${base} DG` : base;
}

/* ------------------------------------------------------------ lane fit */

const STOP = /\b(co|ltd|company|limited|thailand|th|inc|plc|จำกัด|บริษัท|มหาชน)\b/gi;

export function tokens(value: string): string[] {
  return (value ?? "")
    .toLowerCase()
    .replace(STOP, " ")
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .split(" ")
    .filter((word) => word.length > 2);
}

/** Token overlap, 0–1. Used to suggest a lane, never to pick one silently. */
export function overlap(a: string[], b: string[]): number {
  if (!a.length || !b.length) return 0;
  const set = new Set(b);
  const shared = a.filter((word) => set.has(word)).length;
  return shared / Math.min(a.length, b.length);
}

/**
 * How well a lane fits a job, 0–1: the job's customer and destination against the lane's customer and
 * either end. Booking's suggestion and a Billing Case's contract rate (Rules/RateMatch.cs, held to this by
 * tests/fixtures/billing-parity.json) both read a lane this way.
 */
export function laneScore(
  job: { customer: string; destination?: string; plant?: string },
  lane: { customer: string; from: string; to: string },
): number {
  const wanted = tokens(`${job.customer} ${job.destination || job.plant || ""}`);
  return Math.max(
    overlap(wanted, tokens(`${lane.customer} ${lane.to}`)),
    overlap(wanted, tokens(`${lane.customer} ${lane.from}`)),
  );
}
