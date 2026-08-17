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
};

export function cell(value: unknown, o: CellOpts = {}): Cell {
  let td =
    "padding:10px 14px;white-space:nowrap;border-bottom:1px solid #EDF1F5;font-size:12.5px;vertical-align:middle;";
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
      "position:sticky;top:0;z-index:2;background:#F4F7FA;padding:9px 14px;text-align:" +
      (d[1] || "left") +
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
