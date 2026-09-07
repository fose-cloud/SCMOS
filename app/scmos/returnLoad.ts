/**
 * งานรับกลับ — a return load, and what it does to the cost of a trip.
 *
 * THAI KOT's card carries the term under its SCGJWD sheets, in one line at the
 * bottom of all three: *กรณีมีงานรับกลับ วางบิลในครึ่งราคาของราคาเที่ยวนั้นๆ* — where
 * there is a return load, it is billed at half the rate of that trip. The
 * Unithai sheets of the same card say nothing of the kind.
 *
 * Two things follow from reading it closely, and both are load-bearing here.
 *
 * It is half of **that trip's** rate — *ราคาเที่ยวนั้นๆ* — so the figure is taken
 * from the job's own Transportation Rate rather than looked up again on the
 * card. That is not only simpler: the job may have been priced at a diesel band
 * that has since moved, and half of what was actually charged is the number an
 * invoice has to reconcile against.
 *
 * And it is on the **cost** card, which is the haulier quoting us. It reduces
 * what we pay for the return leg. It says nothing about what The Chemours is
 * billed, the selling card carries no matching term, and nothing here touches
 * the selling side — confirmed with the account team on 2026-09-07.
 *
 * No imports on purpose: this is arithmetic on a contract term and should be
 * testable without a browser or a register behind it.
 */

/**
 * What the register writes in a ticked box.
 *
 * "TRUE" and "FALSE", matching the operators' own CHACK column rather than
 * inventing a second spelling for yes. Their sheets round-trip through this
 * system and a job that came in as TRUE should go back out as TRUE.
 */
export const TICKED = "TRUE";
export const UNTICKED = "";

/**
 * Whether a job is marked as having carried a return load.
 *
 * Generous in what it accepts because these values arrive from spreadsheets as
 * well as from the tick box: Excel writes booleans as TRUE, some sheets carry
 * "Y", and a checked box exported to CSV can come back as "1". Anything else —
 * including the empty string, "FALSE" and "0" — is no.
 */
export function hasReturnLoad(value: string | undefined | null): boolean {
  const text = String(value ?? "").trim().toUpperCase();
  return text === "TRUE" || text === "YES" || text === "Y" || text === "1";
}

/**
 * A money value off a job, as a number.
 *
 * Null rather than zero when there is nothing readable there. The difference
 * matters everywhere below: zero is a trip that cost nothing, null is a trip
 * nobody has priced yet, and only one of them should be added into a total.
 */
export function amount(value: string | number | undefined | null): number | null {
  if (typeof value === "number") return Number.isFinite(value) && value >= 0 ? value : null;
  const text = String(value ?? "").replace(/[,\s฿]/g, "").trim();
  if (!text) return null;
  const number = Number(text);
  return Number.isFinite(number) && number >= 0 ? number : null;
}

/**
 * What the return leg adds to a trip: half its rate, to the nearest baht.
 *
 * Null when the trip has no rate on it. A job nobody has priced does not have a
 * free return load — it has an unknown one, and showing 0 would put a number
 * into an invoice reconciliation that no document supports.
 */
export function returnLoadCharge(rate: string | number | undefined | null): number | null {
  const trip = amount(rate);
  if (trip === null) return null;
  return Math.round(trip / 2);
}

/**
 * What the trip costs altogether — the rate, plus the return leg when there
 * was one.
 *
 * Null when the trip has no rate, for the same reason as above: a total built
 * out of an unknown is not a total.
 */
export function tripCost(
  rate: string | number | undefined | null,
  returnLoad: string | undefined | null,
): number | null {
  const trip = amount(rate);
  if (trip === null) return null;
  if (!hasReturnLoad(returnLoad)) return trip;
  return trip + (returnLoadCharge(trip) ?? 0);
}

/**
 * The origins whose card carries the term, as the card writes them.
 *
 * Used to warn, never to refuse. The term is written on THAI KOT's SCGJWD
 * sheets and nowhere else, so a return load ticked on a Unithai run is worth
 * questioning — but the warehouse vocabulary in the register ("JWD",
 * "UNITHAI") is whatever operators typed, there is no canonical mapping to the
 * card's own names, and a tick refused on a spelling nobody agreed is worse
 * than a tick queried.
 */
export const RETURN_LOAD_ORIGINS = ["SCGJWD", "JWD"];

/**
 * Whether the term is known to cover this warehouse.
 *
 * A substring match in both directions, because the register writes "JWD" and
 * the card writes "SCGJWD Warehouse (LCH)". Deliberately loose: the answer is
 * only ever used to decide whether to raise a question.
 */
export function termCoversOrigin(warehouse: string | undefined | null): boolean {
  const text = String(warehouse ?? "").trim().toUpperCase();
  if (!text) return false;
  return RETURN_LOAD_ORIGINS.some((known) => text.includes(known));
}
