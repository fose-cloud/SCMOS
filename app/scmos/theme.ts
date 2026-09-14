import type { CSSProperties } from "react";

/**
 * The mockup is authored as CSS declaration strings that are recomputed from
 * state on every render. Rather than hand-translating several thousand of them
 * into React style objects, we keep the strings and parse them once per unique
 * value. Splitting respects parentheses so gradients and data: URLs survive.
 */
const cache = new Map<string, CSSProperties>();
const rawCache = new Map<string, CSSProperties>();

/* ------------------------------------------------------------- the skin */

/**
 * One theme on every screen — the control tower's navy — asked for by the
 * department on 14 September 2026.
 *
 * Fifty screens style themselves through this function with the light palette
 * they were drawn in: white grounds, navy ink, pale rules. Rewriting every
 * literal would be the same rule restated several thousand times, so the
 * translation lives here, once, and reads the property it is translating.
 * A dark ink becomes a light one, a white ground becomes a navy panel, a pale
 * rule becomes a lit hairline; the mid-tone blues, ambers, reds and greens
 * that carry meaning are left alone because they read on either ground, and
 * the navy bars and blue buttons that were already dark stay as they are.
 *
 * `cssRaw` is the way out, for the paper: the sign-in card, and the documents
 * that print — a cargo receipt on navy is a receipt nobody can sign.
 */
type Family = "ink" | "ground" | "line" | "other";

function familyOf(prop: string): Family {
  if (prop === "color" || prop === "fill" || prop === "stroke" || prop === "caret-color") return "ink";
  if (prop === "background" || prop === "background-color" || prop === "background-image") return "ground";
  if (prop.startsWith("border") || prop.startsWith("outline") || prop === "column-rule") return "line";
  return "other";
}

/** The four tokens the screens lean on most, mapped exactly to the tower's own. */
const EXACT: Record<Family, Record<string, string>> = {
  ink: { "#0A2240": "#EAF4FC", "#16232F": "#EAF4FC", "#0E2B4D": "#EAF4FC", "#000000": "#EAF4FC" },
  ground: { "#FFFFFF": "#0C2338", "#EEF2F6": "#06152A", "#F8FAFC": "#0F2C48", "#F1F5F9": "#0F2C48", "#F4F7FA": "#0F2C48", "#E9EFF5": "#143354" },
  line: { "#D8E0E8": "rgba(74,148,214,.22)", "#E9EFF5": "rgba(74,148,214,.14)", "#E2E8F0": "rgba(74,148,214,.16)", "#C9D6E2": "rgba(74,148,214,.3)" },
  other: {},
};

function hsl(hex: string): [number, number, number] {
  const r = parseInt(hex.slice(1, 3), 16) / 255;
  const g = parseInt(hex.slice(3, 5), 16) / 255;
  const b = parseInt(hex.slice(5, 7), 16) / 255;
  const max = Math.max(r, g, b), min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return [0, 0, l];
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  const h = max === r ? ((g - b) / d + (g < b ? 6 : 0)) : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [h * 60, s, l];
}

const out = (h: number, s: number, l: number) => `hsl(${h.toFixed(0)},${(s * 100).toFixed(0)}%,${(l * 100).toFixed(0)}%)`;

function translate(family: Family, hex: string): string {
  const exact = EXACT[family][hex];
  if (exact) return exact;
  const [h, s, l] = hsl(hex);
  if (family === "ink") {
    // Dark ink on white becomes light ink on navy; a mid or pale ink already reads.
    if (l >= 0.5) return hex;
    if (s < 0.2) return out(h, s, l < 0.3 ? 0.9 : 0.78);
    return out(h, Math.min(s, 0.7), 0.7);
  }
  if (family === "ground") {
    // A pale ground becomes a navy panel; a tinted pale ground keeps its tint, deep.
    if (l <= 0.55) return hex;
    if (s < 0.35) return l >= 0.97 ? "#0C2338" : l >= 0.9 ? "#0F2C48" : "#143354";
    return out(h, Math.min(s, 0.45), 0.17);
  }
  // A pale rule becomes a lit hairline; a tinted one stays tinted.
  if (l <= 0.55) return hex;
  if (s < 0.35) return "rgba(74,148,214,.22)";
  return out(h, 0.5, 0.34);
}

const HEX = /#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\b|\bwhite\b/g;

/*
 * Paper on the navy.
 *
 * The tables people work in all day — the plan, the rate sheet, the supplier
 * register, the training lists — were asked back to white on 14 September,
 * the same day the navy went on: a light grid is easier on the eyes for a
 * shift of keying, and it is where the screens spend their time. The skin
 * cannot see where a colour will land, so every colour it translates goes
 * out as a CSS variable with the navy value as its fallback, and the light
 * value is written into a stylesheet under `table` and `.paper`. Inside a
 * table the variable resolves to the colour the screen was drawn with;
 * everywhere else the fallback is the navy. One rule per colour, added the
 * first time that colour is translated, so nothing here has to know the
 * palette in advance.
 */
const paper = new Map<string, string>();
let paperSheet: CSSStyleSheet | null = null;

function paperVar(family: Family, hex: string, dark: string): string {
  const name = `--sk-${family}-${hex.slice(1)}`;
  if (!paper.has(name)) {
    paper.set(name, hex);
    if (typeof document !== "undefined") {
      if (!paperSheet) {
        const element = document.createElement("style");
        element.id = "scmos-paper";
        document.head.appendChild(element);
        paperSheet = element.sheet;
      }
      paperSheet?.insertRule(`table,.paper{${name}:${hex}}`, paperSheet.cssRules.length);
    }
  }
  return `var(${name},${dark})`;
}

/** The light values every translated colour falls back to on paper — for tests. */
export function paperPalette(): ReadonlyMap<string, string> { return paper; }

function skin(prop: string, value: string): string {
  const family = familyOf(prop);
  if (family === "other") return value;
  return value.replace(HEX, (token) => {
    let hex = token.toLowerCase() === "white" ? "#FFFFFF" : token.toUpperCase();
    if (hex.length === 4) hex = "#" + hex[1] + hex[1] + hex[2] + hex[2] + hex[3] + hex[3];
    const dark = translate(family, hex);
    return dark === hex ? hex : paperVar(family, hex, dark);
  });
}

function parse(declarations: string, skinned: boolean): CSSProperties {
  const style: Record<string, string> = {};
  let depth = 0;
  let start = 0;
  for (let i = 0; i <= declarations.length; i++) {
    const ch = declarations[i];
    if (ch === "(") depth++;
    else if (ch === ")") depth--;
    if (i !== declarations.length && !(ch === ";" && depth === 0)) continue;

    const decl = declarations.slice(start, i).trim();
    start = i + 1;
    if (!decl) continue;
    const split = decl.indexOf(":");
    if (split < 0) continue;
    const prop = decl.slice(0, split).trim();
    const value = decl.slice(split + 1).trim();
    if (!prop || !value) continue;
    const key = prop.startsWith("--")
      ? prop
      : prop.replace(/-([a-z])/g, (_, c: string) => c.toUpperCase());
    style[key] = skinned ? skin(prop, value) : value;
  }
  return style as CSSProperties;
}

export function css(declarations: string | undefined | null): CSSProperties {
  if (!declarations) return {};
  const hit = cache.get(declarations);
  if (hit) return hit;
  const frozen = parse(declarations, true);
  cache.set(declarations, frozen);
  return frozen;
}

/** The declarations as written, for paper: the sign-in card and what prints. */
export function cssRaw(declarations: string | undefined | null): CSSProperties {
  if (!declarations) return {};
  const hit = rawCache.get(declarations);
  if (hit) return hit;
  const frozen = parse(declarations, false);
  rawCache.set(declarations, frozen);
  return frozen;
}

export const NAVY = "#0A2240";
export const NAVY_DEEP = "#071A31";
export const BLUE = "#2E7DD1";
export const LINE = "#D8E0E8";
export const CANVAS = "#EEF2F6";
export const MONO = "'IBM Plex Mono',monospace";

export type Tone = "green" | "amber" | "red" | "blue" | "gray" | "dark" | "teal";

const TONES: Record<Tone, [string, string]> = {
  green: ["#16794C", "#E3F4EB"],
  amber: ["#B45309", "#FDF2DF"],
  red: ["#B42318", "#FCE9E7"],
  blue: ["#1D5FA8", "#E7F0FA"],
  gray: ["#475569", "#EDF1F5"],
  dark: ["#0A2240", "#DDE5EE"],
  teal: ["#0A6E8A", "#E2F2F7"],
};

export function badge(_text: string, tone?: Tone | null): string {
  const c = TONES[(tone ?? "gray") as Tone] || TONES.gray;
  return (
    "display:inline-block;padding:1px 7px;border-radius:3px;font-size:10.5px;" +
    // Ends with the semicolon it used to leave off. Anything appended to a
    // badge landed inside the colour it finished on — "color:#1D5FA8border:1px
    // …" — which the browser drops whole, so the addition vanished and so did
    // the colour, with nothing to say either had happened.
    "font-weight:600;letter-spacing:.02em;background:" + c[1] + ";color:" + c[0] + ";"
  );
}

/** Shipment status -> [text colour, background, Thai label] */
export const STATUS: Record<string, [string, string, string]> = {
  "Waiting Truck": ["#475569", "#EDF1F5", "รอรถ"],
  "Truck Assigned": ["#1D5FA8", "#E7F0FA", "จัดรถแล้ว"],
  "Driver Assigned": ["#1D5FA8", "#E7F0FA", "จัดคนขับแล้ว"],
  "Plate Received": ["#1D5FA8", "#E7F0FA", "ได้ทะเบียนแล้ว"],
  "Ready for Pickup": ["#1D5FA8", "#E7F0FA", "พร้อมรับสินค้า"],
  "In Transit": ["#0A6E8A", "#E2F2F7", "กำลังขนส่ง"],
  "Arrived Customer": ["#0A6E8A", "#E2F2F7", "ถึงลูกค้า"],
  "Loading / Delivery": ["#B45309", "#FDF2DF", "กำลังขน/ส่ง"],
  Completed: ["#16794C", "#E3F4EB", "เสร็จสิ้น"],
  Delayed: ["#B42318", "#FCE9E7", "ล่าช้า"],
  Cancelled: ["#334155", "#E2E8F0", "ยกเลิก"],
};

export function stTone(status: string): Tone {
  const map: Record<string, Tone> = {
    Completed: "green",
    Delayed: "red",
    Cancelled: "dark",
    "In Transit": "teal",
    "Arrived Customer": "teal",
    "Loading / Delivery": "amber",
    "Waiting Truck": "gray",
  };
  return map[status] || "blue";
}

/** Tone for the free-form operational statuses used by the workspace grid. */
export function opTone(status: string): Tone {
  if (/delay/i.test(status)) return "red";
  if (/cancel/i.test(status)) return "dark";
  if (STATUS_RE.done.test(status)) return "green";
  if (/arrived|loading|pickup|transit|departed/i.test(status)) return "teal";
  if (/confirmed|assigned/i.test(status)) return "blue";
  return "gray";
}

export const ALL_STATUS = [
  "New Booking", "Waiting Information", "Ready for Booking", "Waiting Truck",
  "Truck Assigned", "Driver Assigned", "Plate Received", "Ready for Pickup",
  "Ready for Operation", "Truck Departed", "Arrived Pickup", "Loading / Delivery",
  "Loading Completed", "In Transit", "Arrived Customer", "Delivery Completed",
  "Waiting Empty Return", "Empty Returned", "Completed", "Delayed", "Cancelled",
];

/** Per-category status ladders used by the workspace grid. */
/**
 * The controlled status set, mirrored from server/Scmos.Api/Rules/JobStatus.cs.
 *
 * The codes are shared; the ladders are not. An import collects a laden
 * container and delivers it; an export collects an empty one and loads it at
 * the plant. LOADING is a real step for one and meaningless for the other, so
 * it is only offered where it means something.
 *
 * The .NET side is the authority — it validates every write — and this copy
 * exists so the dropdown can offer the right choices without a round trip.
 */
const BOOKING = [
  "DRAFT", "RECEIVED", "VALIDATING", "WAITING_CS", "READY_FOR_BOOKING",
  "WAITING_SUPPLIER", "SUPPLIER_CONFIRMED", "TRUCK_ASSIGNED", "PRE_RUN", "READY", "DISPATCHED",
];
const CLOSING = ["DOCUMENT_PENDING", "BILLING_PENDING", "COMPLETED"];
const EXITS = ["CANCELLED", "HOLD"];

export const STATUS_LADDER: Record<string, string[]> = {
  IMPORT: [...BOOKING, "PICKED_UP", "IN_TRANSIT", "DELIVERED", "CONTAINER_RETURNED", ...CLOSING, ...EXITS],
  EXPORT: [...BOOKING, "PICKED_UP", "LOADING", "IN_TRANSIT", "DELIVERED", "CONTAINER_RETURNED", ...CLOSING, ...EXITS],
  DELIVERY: [...BOOKING, "PICKED_UP", "IN_TRANSIT", "DELIVERED", ...CLOSING, ...EXITS],
};

/**
 * Thai labels for the ladder, carried over from the Import/Export process
 * screens so the workspace can show the same stage names those screens did.
 */
export const STATUS_TH: Record<string, string> = {
  DRAFT: "ร่าง",
  RECEIVED: "รับงานแล้ว",
  VALIDATING: "กำลังตรวจสอบ",
  WAITING_CS: "รอ CS",
  READY_FOR_BOOKING: "พร้อมจองรถ",
  WAITING_SUPPLIER: "รอผู้ขนส่งยืนยัน",
  SUPPLIER_CONFIRMED: "ผู้ขนส่งยืนยันแล้ว",
  TRUCK_ASSIGNED: "จัดรถแล้ว",
  PRE_RUN: "ตรวจก่อนออกงาน",
  READY: "พร้อมออกงาน",
  DISPATCHED: "จ่ายงานแล้ว",
  PICKED_UP: "รับตู้แล้ว",
  LOADING: "กำลังบรรจุ",
  IN_TRANSIT: "กำลังขนส่ง",
  DELIVERED: "ส่งถึงแล้ว",
  CONTAINER_RETURNED: "คืนตู้แล้ว",
  DOCUMENT_PENDING: "รอเอกสาร",
  BILLING_PENDING: "รอวางบิล",
  COMPLETED: "เสร็จสิ้น",
  CANCELLED: "ยกเลิก",
  HOLD: "พักงาน",
};

/**
 * How a status maps onto the buckets every summary counts by.
 *
 * The controlled codes are matched exactly; the old free-text spellings are
 * still recognised so a workbook imported before the move is not silently
 * uncounted. Mirrored from JobStatus.cs, which is the authority.
 *
 * DELIVERED is running, not done. It means the goods reached the destination —
 * the documents and the invoice still have to follow, and calling it finished
 * is how a job disappears from the queue while somebody is still owed a POD.
 */
const CODES = {
  waiting: ["DRAFT", "RECEIVED", "VALIDATING", "WAITING_CS", "READY_FOR_BOOKING", "WAITING_SUPPLIER"],
  confirmed: ["SUPPLIER_CONFIRMED", "TRUCK_ASSIGNED", "PRE_RUN", "READY"],
  running: ["DISPATCHED", "PICKED_UP", "LOADING", "IN_TRANSIT", "DELIVERED", "CONTAINER_RETURNED"],
  delayed: ["HOLD"],
  done: ["COMPLETED"],
};

const LEGACY = {
  waiting: /^(waiting truck|waiting information|new|scheduled)$/i,
  confirmed: /^(truck confirmed|driver assigned|truck assigned)$/i,
  running: /transit|arrived|loading|pickup|departed|gate|empty return/i,
  delayed: /delay/i,
  done: /^(completed|delivery completed|delivered)$/i,
};

/** Every controlled code, so a known code is never re-read as free text. */
const CONTROLLED = new Set(Object.values(CODES).flat());

const bucket = (kind: keyof typeof CODES) => ({
  test: (status: string) => {
    const value = (status ?? "").trim().toUpperCase();
    // A controlled code answers from the code lists alone. Falling through to
    // the legacy patterns would let DELIVERED — which means the goods arrived
    // and the paperwork has not — match the old free-text "Delivered" that
    // meant finished, and 228 running jobs would report as complete.
    if (CONTROLLED.has(value)) return CODES[kind].includes(value);
    return LEGACY[kind].test(status ?? "");
  },
});

export const STATUS_RE = {
  waiting: bucket("waiting"),
  confirmed: bucket("confirmed"),
  running: bucket("running"),
  delayed: bucket("delayed"),
  done: bucket("done"),
};


export const BTN_PRIMARY =
  "height:34px;padding:0 15px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:500;cursor:pointer";
export const BTN_SECONDARY =
  "height:34px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer";
