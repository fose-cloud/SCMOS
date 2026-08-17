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
    "display:inline-block;padding:3px 9px;border-radius:3px;font-size:11px;" +
    "font-weight:600;letter-spacing:.02em;background:" + c[1] + ";color:" + c[0]
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
  if (/complet|delivered|gate-in/i.test(status)) return "green";
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
export const STATUS_LADDER: Record<string, string[]> = {
  IMPORT: ["New", "Waiting Information", "Waiting Truck", "Truck Confirmed", "Driver Assigned", "Container Pickup", "Departed Port", "In Transit", "Arrived Customer", "Delivery Started", "Delivery Completed", "Empty Return Pending", "Empty Returned", "Completed", "Delayed", "Cancelled"],
  EXPORT: ["New", "Waiting Information", "Waiting Truck", "Truck Confirmed", "Empty Pickup", "Driver Assigned", "Arrived Plant", "Loading", "Loading Completed", "Departed Plant", "Port Return", "Gate-In Completed", "Completed", "Delayed", "Cancelled"],
  DELIVERY: ["Scheduled", "Truck Assigned", "Pickup", "In Transit", "Delivered", "Completed", "Delayed", "Cancelled"],
};

/**
 * Thai labels for the ladder, carried over from the Import/Export process
 * screens so the workspace can show the same stage names those screens did.
 */
export const STATUS_TH: Record<string, string> = {
  New: "งานใหม่",
  "Waiting Information": "รอข้อมูล",
  "Waiting Truck": "รอรถ",
  "Truck Confirmed": "ยืนยันรถ",
  "Truck Assigned": "จัดรถแล้ว",
  "Driver Assigned": "จัดคนขับ",
  "Container Pickup": "รับตู้สินค้า",
  "Departed Port": "ออกจากท่าเรือ",
  "In Transit": "กำลังขนส่ง",
  "Arrived Customer": "ถึงลูกค้า",
  "Delivery Started": "เริ่มส่งมอบ",
  "Delivery Completed": "ส่งมอบเสร็จ",
  "Empty Return Pending": "รอคืนตู้เปล่า",
  "Empty Returned": "คืนตู้เปล่าแล้ว",
  "Empty Pickup": "รับตู้เปล่า",
  "Arrived Plant": "ถึงโรงงาน",
  Loading: "กำลังบรรจุ",
  "Loading Completed": "บรรจุเสร็จ",
  "Departed Plant": "ออกจากโรงงาน",
  "Port Return": "ส่งกลับท่าเรือ",
  "Gate-In Completed": "ผ่านประตูท่าเรือ",
  Scheduled: "นัดหมายแล้ว",
  Pickup: "รับสินค้า",
  Delivered: "ส่งถึงแล้ว",
  Completed: "เสร็จสิ้น",
  Delayed: "ล่าช้า",
  Cancelled: "ยกเลิก",
};

export const BTN_PRIMARY =
  "height:34px;padding:0 15px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:500;cursor:pointer";
export const BTN_SECONDARY =
  "height:34px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12.5px;cursor:pointer";
