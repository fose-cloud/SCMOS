import { canonicalCarrier, priceFor, type RateBook, type RateLane } from "./rates";
import { dnum } from "./util";
import type { Job } from "./ops";

/**
 * Truck booking.
 *
 * A job arrives from the plan with a customer, a date and a container type, and
 * has to leave with a carrier, a plate and a driver against it. That gap is the
 * booking work, and the register measures it exactly: on the July plan, 1,178
 * jobs name a carrier but no truck.
 *
 * The stages below are read off the job rather than tracked separately. There is
 * no booking record to fall out of step with the plan, and a job that gets its
 * plate keyed in the workspace leaves the queue without anybody telling the
 * booking screen about it.
 */

export type Stage = "no-carrier" | "no-plate" | "no-driver" | "ready" | "done";

export const STAGES: { id: Stage; en: string; th: string; tone: string }[] = [
  { id: "no-carrier", en: "No carrier", th: "ยังไม่มีผู้ขนส่ง", tone: "#B42318" },
  { id: "no-plate", en: "No plate", th: "รอทะเบียนรถ", tone: "#B45309" },
  { id: "no-driver", en: "No driver", th: "รอคนขับ", tone: "#1D5FA8" },
  { id: "ready", en: "Ready", th: "พร้อมปฏิบัติงาน", tone: "#16794C" },
  { id: "done", en: "Completed", th: "เสร็จสิ้น", tone: "#5A6B7D" },
];

const BLANK = /^(-|—|–|n\/a|none|null|ไม่มี)$/i;

/** True when a field actually carries a value rather than a dash or an N/A. */
export function filled(value: string | undefined): boolean {
  const text = (value ?? "").trim();
  return text.length > 0 && !BLANK.test(text);
}

/**
 * Where a job stands. The order matters: a job with no carrier cannot be
 * waiting for a plate, so the first gap found is the one that needs work.
 */
export function stageOf(job: Job): Stage {
  if (/complet|delivered|gate-in/i.test(job.status)) return "done";
  if (!filled(job.trucker)) return "no-carrier";
  if (!filled(job.licence)) return "no-plate";
  if (!filled(job.driver) || !filled(job.contact)) return "no-driver";
  return "ready";
}

export function bookingStats(jobs: Job[]): Record<Stage, Job[]> {
  const out: Record<Stage, Job[]> = {
    "no-carrier": [], "no-plate": [], "no-driver": [], ready: [], done: [],
  };
  for (const job of jobs) out[stageOf(job)].push(job);
  return out;
}

/** What is still missing on a job, named, for the queue to show. */
export function missing(job: Job): string[] {
  const gaps: string[] = [];
  if (!filled(job.trucker)) gaps.push("ผู้ขนส่ง");
  if (!filled(job.licence)) gaps.push("ทะเบียนรถ");
  if (!filled(job.driver)) gaps.push("คนขับ");
  if (!filled(job.contact)) gaps.push("เบอร์คนขับ");
  return gaps;
}

/* ----------------------------------------------------------- vehicle types */

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

/* ------------------------------------------------------------ carrier picks */

export type Candidate = {
  carrier: string;
  price: number | null;
  lane: RateLane | null;
  /** How the lane was matched, so a suggestion can be judged rather than trusted. */
  match: "lane" | "carrier-only";
};

const STOP = /\b(co|ltd|company|limited|thailand|th|inc|plc|จำกัด|บริษัท|มหาชน)\b/gi;

function tokens(value: string): string[] {
  return (value ?? "")
    .toLowerCase()
    .replace(STOP, " ")
    .replace(/[^\p{L}\p{N}]+/gu, " ")
    .split(" ")
    .filter((word) => word.length > 2);
}

/** Token overlap, 0–1. Used to suggest a lane, never to pick one silently. */
function overlap(a: string[], b: string[]): number {
  if (!a.length || !b.length) return 0;
  const set = new Set(b);
  const shared = a.filter((word) => set.has(word)).length;
  return shared / Math.min(a.length, b.length);
}

/**
 * Which carriers could take this job, and what each would charge.
 *
 * The lane is matched on the customer and the destination the job names against
 * the ones the carrier quoted. Nothing is auto-selected: the match quality and
 * the quoted lane travel with the price so an operator can see that the 4,120
 * belongs to the right journey before booking it.
 */
export function candidatesFor(job: Job, book: RateBook | null, diesel: number): Candidate[] {
  if (!book) return [];

  const vehicle = vehicleForType(job.type);
  if (!vehicle) return [];

  const wanted = tokens(`${job.customer} ${job.destination || job.plant || ""}`);
  const best = new Map<string, Candidate>();

  for (const lane of book.lanes) {
    const price = priceFor(lane, vehicle, book.bands, diesel);
    if (price === null) continue;

    const score = Math.max(
      overlap(wanted, tokens(`${lane.customer} ${lane.to}`)),
      overlap(wanted, tokens(`${lane.customer} ${lane.from}`)),
    );
    if (score < 0.5) continue;

    const held = best.get(lane.carrier);
    if (!held || (held.price ?? Infinity) > price) {
      best.set(lane.carrier, { carrier: lane.carrier, price, lane, match: "lane" });
    }
  }

  // Carriers who have a rate card but nothing matching this lane still belong in
  // the list — they are approved and can be asked. They are just not priced.
  const priced = new Set(best.keys());
  for (const lane of book.lanes) {
    if (priced.has(lane.carrier) || best.has(lane.carrier)) continue;
    best.set(lane.carrier, { carrier: lane.carrier, price: null, lane: null, match: "carrier-only" });
  }

  return [...best.values()].sort((a, b) => {
    if (a.price === null && b.price === null) return a.carrier.localeCompare(b.carrier);
    if (a.price === null) return 1;
    if (b.price === null) return -1;
    return a.price - b.price;
  });
}

/** The carrier already on the job, as the rate cards spell it. */
export function carrierOf(job: Job): string {
  return filled(job.trucker) ? canonicalCarrier(job.trucker) : "";
}

/** Loading date order, with undated jobs last — the same rule the grid uses. */
export function byLoadingDate(a: Job, b: Job): number {
  const da = dnum(a.date);
  const db = dnum(b.date);
  if (!da && !db) return 0;
  if (!da) return 1;
  if (!db) return -1;
  return da - db;
}
