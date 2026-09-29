/**
 * The Booking Agent's drafts as the add-job form reads them (Agent Platform,
 * 28 Sep 2026): a pasted booking text read by `POST /api/ai/booking-draft`, or
 * a mail draft from the decision log. Only fields the server verified against
 * their own words fill the form; the rest are shown with the reason, for the
 * person to key. Parsing is strict — a reply that does not match is not used.
 */

import type { Decision } from "./aiFindings";

export type BookingReading = {
  field: string; proposed: string; quote: string; verified: boolean; value: string; reason: string;
};
export type BookingDraftAnswer = {
  category: string; status: string; fields: Record<string, string>; readings: BookingReading[]; missing: string[];
};

export const BOOKING_TYPE = "booking_draft";
export const CATEGORIES = ["IMPORT", "EXPORT", "DELIVERY"] as const;
export const MAX_TEXT = 8000;

/** The form's own words for its fields, for the lines under the paste box. */
export const FIELD_LABEL: Record<string, string> = {
  customer: "ลูกค้า", date: "วันที่", planTime: "เวลา", type: "ประเภทรถ", destination: "ปลายทาง", plant: "โรงงาน",
  container: "ตู้", seal: "ซีล", booking: "Booking", abs: "ABS", jobCode: "Job", product: "สินค้า", weight: "น้ำหนัก",
  cyYard: "CY", emptyReturn: "คืนตู้", returnLoc: "คืนตู้", fclLcl: "FCL/LCL", closingDate: "วันปิด", closingTime: "เวลาปิด",
  wh: "คลัง", jobNo: "Job", sid: "SID", province: "จังหวัด", zip: "รหัสไปรษณีย์", pallet: "พาเลท", kgs: "กก.", remark: "หมายเหตุ",
};
export const labelOf = (field: string) => FIELD_LABEL[field] ?? field;

type Obj = Record<string, unknown>;
const obj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const str = (v: unknown, max = 400) => typeof v === "string" && v.length <= max;
const FIELD = /^[a-zA-Z]{1,20}$/;

function reading(v: unknown): v is BookingReading {
  return obj(v) && str(v.field, 20) && FIELD.test(String(v.field)) && str(v.proposed) && str(v.quote)
    && typeof v.verified === "boolean" && str(v.value) && str(v.reason, 120)
    // Accepted means a value and the words it came from; not accepted means no value goes in.
    && (v.verified ? String(v.value).length > 0 && String(v.quote).length > 0 : v.value === "");
}

export function parseBookingDraft(v: unknown): BookingDraftAnswer {
  if (!(obj(v) && (CATEGORIES as readonly string[]).includes(String(v.category))
    && (v.status === "COMPLETE" || v.status === "NEEDS_INFORMATION")
    && obj(v.fields) && Object.entries(v.fields).length <= 20
    && Object.entries(v.fields).every(([key, value]) => FIELD.test(key) && str(value) && String(value).length > 0)
    && Array.isArray(v.readings) && v.readings.length <= 20 && v.readings.every(reading)
    && Array.isArray(v.missing) && v.missing.every(one => str(one, 20) && FIELD.test(String(one)))
    && (v.status === "COMPLETE") === (v.missing.length === 0)
    // Every field the form will take is one the readings accepted, with the same value.
    && Object.entries(v.fields).every(([key, value]) =>
      (v.readings as BookingReading[]).some(one => one.field === key && one.verified && one.value === value))))
    throw new Error("invalid_response");
  return v as unknown as BookingDraftAnswer;
}

export const bookingDraftBody = (category: string, text: string) => ({ category, text: text.trim() });

/**
 * A mail draft as the form takes it. The server stores each accepted field as a fact whose text is the
 * value and whose source is `email:{id}.text#{field}`, its words as an observation `quote#{field}`,
 * and the category as `Category:…` (BookingMailPass).
 */
export function draftFromDecision(decision: Pick<Decision, "decisionType" | "findings" | "ruleReferences">):
  { category: string; fields: Record<string, string>; quotes: Record<string, string> } | null {
  if (decision.decisionType !== BOOKING_TYPE) return null;
  const category = decision.ruleReferences.find(one => one.startsWith("Category:"))?.slice("Category:".length) ?? "";
  if (!(CATEGORIES as readonly string[]).includes(category)) return null;
  const fields: Record<string, string> = {};
  for (const fact of decision.findings.facts) {
    const match = /^email:\d+\.text#([a-zA-Z]{1,20})$/.exec(fact.source ?? "");
    if (match) fields[match[1]] = fact.text;
  }
  const quotes: Record<string, string> = {};
  for (const note of decision.findings.observations) {
    const match = /^quote#([a-zA-Z]{1,20})$/.exec(note.source ?? "");
    if (match) quotes[match[1]] = note.text;
  }
  return { category, fields, quotes };
}
