/**
 * The movement times on a customer truck report, typed and read back.
 *
 * The customer's workbook writes them as `dd/MM/yyyy HH:mm`, which is what an
 * operator types. The register stores an instant. Converting between the two is
 * the whole of this file, and the reason it is its own file is the offset.
 *
 * The yard is at +07:00 and the API runs on a machine set to UTC. A time typed
 * as 08:30 that is sent without saying which 08:30 it is arrives seven hours
 * out — this exact mistake was live in the carrier scorecard until August,
 * where a report filed four minutes after an accident scored as seven hours
 * before it. So the offset is written here, once, and never taken from the
 * browser: a laptop set to the wrong zone must not change what a time means.
 *
 * No imports, so the conversion can be tested on its own.
 */

/** Where the work happens. Not the reader's clock, and not the server's. */
export const ZONE = "+07:00";

const TYPED = /^(\d{1,2})\/(\d{1,2})\/(\d{4})(?:[\s,]+(\d{1,2}):(\d{2}))?$/;

const pad = (n: number) => String(n).padStart(2, "0");

/** Whether these parts name a day that exists — 31/02 does not. */
function real(d: number, m: number, y: number): boolean {
  const made = new Date(Date.UTC(y, m - 1, d));
  return made.getUTCFullYear() === y && made.getUTCMonth() === m - 1 && made.getUTCDate() === d;
}

/**
 * What the operator typed, as an instant the API will store.
 *
 * Null when it is not a time. Null is a refusal, not a zero: the caller shows
 * the person what was wrong rather than saving midnight on the first of January
 * and letting it reach the customer's file.
 *
 * A date with no time means the start of that day. That is a real answer for
 * "the container went back on the 3rd" and it is what the workbook itself
 * carries on rows where only the date was known.
 */
export function toInstant(typed: string): string | null {
  const found = TYPED.exec(typed.trim());
  if (!found) return null;

  const d = +found[1], m = +found[2], y = +found[3];
  if (!real(d, m, y)) return null;

  const hh = found[4] === undefined ? 0 : +found[4];
  const mm = found[5] === undefined ? 0 : +found[5];
  if (hh > 23 || mm > 59) return null;

  return `${y}-${pad(m)}-${pad(d)}T${pad(hh)}:${pad(mm)}:00${ZONE}`;
}

/**
 * An instant, back in the words the workbook uses.
 *
 * Read at +07:00 rather than in the reader's zone, so the same row says the
 * same thing to somebody opening it from another country — which is the point
 * of a report that gets sent to a customer.
 */
export function toTyped(instant: string | null | undefined): string {
  if (!instant) return "";
  const at = new Date(instant);
  if (Number.isNaN(at.getTime())) return "";

  // Shift into the yard's zone, then read the UTC parts of the shifted value.
  // Intl would do this too, and would also quietly localise the digits.
  const shifted = new Date(at.getTime() + 7 * 60 * 60 * 1000);
  return `${pad(shifted.getUTCDate())}/${pad(shifted.getUTCMonth() + 1)}/${shifted.getUTCFullYear()}`
    + ` ${pad(shifted.getUTCHours())}:${pad(shifted.getUTCMinutes())}`;
}

/**
 * The stage behind each movement column of the customer truck report.
 *
 * The report's own column names on the left, the register's stage on the right.
 * Kept here rather than in the screen so the report and anything else reading
 * these times name the same stage for the same column.
 */
export const MOVEMENT_STAGE: Record<string, string> = {
  "Leave base": "Dispatched",
  "Truck loading time": "Loading",
  // Added to the register's stage list for this report: one stage carries one
  // timestamp, so loading started and loading finished cannot be the same one.
  "Truck loading completed": "LoadingComplete",
  "Truck departure": "InTransit",
  "Return container": "ContainerReturned",
};
