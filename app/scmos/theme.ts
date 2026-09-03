import type { CSSProperties } from "react";

/**
 * The mockup is authored as CSS declaration strings that are recomputed from
 * state on every render. Rather than hand-translating several thousand of them
 * into React style objects, we keep the strings and parse them once per unique
 * value. Splitting respects parentheses so gradients and data: URLs survive.
 */
const cache = new Map<string, CSSProperties>();

export function css(declarations: string | undefined | null): CSSProperties {
  if (!declarations) return {};
  const hit = cache.get(declarations);
  if (hit) return hit;

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
    style[key] = value;
  }

  const frozen = style as CSSProperties;
  cache.set(declarations, frozen);
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
