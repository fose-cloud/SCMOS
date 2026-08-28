import type { ChangeEvent, KeyboardEvent, MouseEvent } from "react";
import { badge, type Tone } from "./theme";

/** Deterministic PRNG so the demo data set is identical on server and client. */
export function rng(seed: number) {
  let t = seed >>> 0;
  return () => {
    t += 0x6d2b79f5;
    let x = Math.imul(t ^ (t >>> 15), 1 | t);
    x ^= x + Math.imul(x ^ (x >>> 7), 61 | x);
    return ((x ^ (x >>> 14)) >>> 0) / 4294967296;
  };
}

export const pad = (n: number) => (n < 10 ? "0" + n : "" + n);

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/**
 * Seeded demo timestamps are read in UTC so the server prerender and the client
 * hydration agree regardless of the viewer's timezone. Interaction timestamps
 * (see `nowHM`) are local, because those only ever run in the browser.
 */
export const fdate = (d: Date) => pad(d.getUTCDate()) + " " + MONTHS[d.getUTCMonth()];
export const ftime = (d: Date) => pad(d.getUTCHours()) + ":" + pad(d.getUTCMinutes());
export const nowHM = () => {
  const n = new Date();
  return pad(n.getHours()) + ":" + pad(n.getMinutes());
};
export const money = (n: number) => "฿" + Math.round(n).toLocaleString("en-US");

/**
 * A stored weight as the customer reports print it: grouped, two decimals.
 *
 * The register holds weights that came out of a spreadsheet division, so
 * `18459.335999999999` is a real value; printed as it stands it goes into the
 * customer's file exactly like that. Anything that will not parse is passed
 * through, because an operator's note in a weight column is still worth
 * carrying.
 */
export const kilos = (value: string | undefined) => {
  const raw = (value ?? "").trim();
  if (!raw) return "";
  const number = Number(raw.replace(/,/g, ""));
  return Number.isFinite(number)
    ? number.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    : raw;
};

/** DD/MM/YYYY -> sortable integer. */
export function dnum(d: string | undefined) {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})/.exec(d || "");
  return m ? +m[3] * 10000 + +m[2] * 100 + +m[1] : 0;
}

export function dowOf(d: string | undefined) {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})/.exec(d || "");
  if (!m) return "";
  return ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"][new Date(+m[3], +m[2] - 1, +m[1]).getDay()];
}

/** "HH:MM" -> minutes since midnight. */
export function tmin(t: string | undefined) {
  const m = /^(\d{1,2}):(\d{2})/.exec(t || "");
  return m ? +m[1] * 60 + +m[2] : null;
}

/** dd/MM/yyyy as a count of days, so two dates can be subtracted. */
function dayNumber(d: string | undefined): number | null {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})/.exec(d || "");
  return m ? Math.floor(Date.UTC(+m[3], +m[2] - 1, +m[1]) / 86_400_000) : null;
}

/**
 * Minutes between the plan and the arrival. Negative is early, null when
 * either half is missing.
 *
 * One calculation, because there is more than one threshold. The KPI on the
 * dashboard counts a truck late the minute it is late — that is the figure
 * reported upward. A customer's own service level may allow a grace period;
 * Syensqo's is thirty minutes. Both are real, and they are the same subtraction
 * judged against different numbers, so the subtraction lives here and the
 * threshold is the caller's.
 *
 * The day difference is a real difference in days, not two yyyymmdd numbers
 * subtracted: an arrival at 00:30 on the 1st against a plan at 23:00 on the
 * previous month's 31st is ninety minutes late, and the naive arithmetic makes
 * it seventy days.
 */
export function lateMinutes(job: {
  date?: string; planTime?: string; arrDate?: string; arrTime?: string;
}): number | null {
  const planDay = dayNumber(job.date);
  const arrDay = dayNumber(job.arrDate);
  const planAt = tmin(job.planTime);
  const arrAt = tmin(job.arrTime);
  if (planDay === null || arrDay === null || planAt === null || arrAt === null) return null;
  return (arrDay - planDay) * 1440 + (arrAt - planAt);
}

/** "1 ชม 53 นาที", the way the delay report writes it. */
export function lateLabel(minutes: number): string {
  const late = Math.abs(minutes);
  return `${Math.floor(late / 60)} ชม ${late % 60} นาที`;
}

export type CellOpts = {
  align?: "left" | "right" | "center";
  w?: number;
  mute?: boolean;
  mono?: boolean;
  bold?: boolean;
  tone?: Tone | null;
  color?: string | null;
};

export type Cell = {
  /** "combo" is a text box with a suggestion list: pick a known value or type a new one. */
  kind: "plain" | "select" | "input" | "check" | "combo";
  /**
   * The job field this cell shows, when it shows one.
   *
   * Set by the grid's editable-cell builder and left off everything else — the
   * tick box, the priority badge, the status pill. It is what lets a dragged
   * rectangle be turned back into "which value of which job", so copy and paste
   * work on the data rather than on the text that happens to be rendered.
   */
  field?: string;
  /** Inside the dragged rectangle, so it is drawn as selected. */
  sel?: boolean;
  /** Starts a drag-selection, and extends one already running. */
  onDown?: (e: MouseEvent<HTMLTableCellElement>) => void;
  onEnter?: (e: MouseEvent<HTMLTableCellElement>) => void;
  /** Which suggestion list a combo cell reads from, see TableModel.datalists. */
  listId?: string;
  v: string;
  td: string;
  sp: string;
  value?: string;
  options?: string[];
  selStyle?: string;
  inpStyle?: string;
  /** Tick-box cells: row selection for bulk actions. */
  checked?: boolean;
  disabled?: boolean;
  title?: string;
  onCheck?: () => void;
  onChange?: (e: ChangeEvent<HTMLSelectElement | HTMLInputElement>) => void;
  onBlur?: () => void;
  onKey?: (e: KeyboardEvent<HTMLInputElement>) => void;
  go?: (e: MouseEvent<HTMLTableCellElement>) => void;
  /**
   * Two clicks, for the gesture that changes something.
   *
   * Kept apart from `go` because one click and two clicks now mean different
   * things on the same cell: the first selects, the second edits.
   */
  onDouble?: (e: MouseEvent<HTMLTableCellElement>) => void;
};

export function cell(value: unknown, o: CellOpts = {}): Cell {
  // Six and twelve, not ten and fourteen. A row was forty-one pixels of which
  // about half was air; this brings it near thirty and puts a third more work
  // on the screen without making anything smaller than it reads at.
  let td =
    "padding:5px 11px;white-space:nowrap;border-bottom:1px solid #EDF1F5;font-size:12.5px;vertical-align:middle;";
  if (o.align) td += "text-align:" + o.align + ";";
  if (o.w) td += "min-width:" + o.w + "px;";

  let sp = "font-size:12.5px;color:" + (o.mute ? "#7C8B9B" : "#16232F") + ";";
  if (o.mono) sp += "font-family:'IBM Plex Mono',monospace;font-size:12px;";
  if (o.bold) sp += "font-weight:600;color:#0A2240;";
  if (o.tone) sp = badge(String(value), o.tone);
  if (o.color) sp += "color:" + o.color + ";font-weight:600;";

  return {
    kind: "plain",
    v: value === null || value === undefined || value === "" ? "—" : String(value),
    td,
    sp,
  };
}

export type Col = { label: string; style: string; sort: () => void };

export function cols(
  defs: [string, ("left" | "right" | "center")?][],
  onSort: (key: string) => void,
): Col[] {
  return defs.map((d) => ({
    label: d[0],
    style:
      // Centred unless the column says otherwise. A header sitting hard left
      // over a column two hundred pixels wide reads as belonging to the column
      // before it; centred, each label sits over its own space. An explicit
      // alignment still wins, so a number column's header stays with its
      // figures.
      "position:sticky;top:0;z-index:2;background:#F4F7FA;padding:6px 11px;text-align:" +
      (d[1] || "center") +
      ";font-size:10.5px;font-weight:600;color:#465A6E;letter-spacing:.05em;text-transform:uppercase;" +
      "white-space:nowrap;border-bottom:1px solid #D8E0E8;cursor:pointer;user-select:none",
    sort: () => onSort(d[0]),
  }));
}

export type Page<T> = { slice: T[]; total: number; pageCount: number; p: number; per: number };

export function paginate<T>(rows: T[], page: number, per = 25): Page<T> {
  const total = rows.length;
  const pageCount = Math.max(1, Math.ceil(total / per));
  const p = Math.min(page, pageCount);
  return { slice: rows.slice((p - 1) * per, p * per), total, pageCount, p, per };
}

export function searched<T>(list: T[], keys: (keyof T)[], q: string): T[] {
  const needle = (q || "").toLowerCase();
  if (!needle) return list;
  return list.filter(
    (o) => keys.map((k) => String(o[k])).join(" ").toLowerCase().indexOf(needle) >= 0,
  );
}
