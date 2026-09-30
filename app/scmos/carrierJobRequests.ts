/**
 * Jobs a carrier keys in itself, for Leschaco to confirm (29 Sep 2026). The
 * fields are the add-job form's own names, sent by the API with the layouts
 * (so the carrier's form and the department's cannot disagree); a request is
 * opened in the department as the add-job form, saved there, and only then
 * marked approved with the job it became.
 */

export type RequestStatus = "PENDING" | "APPROVED" | "REJECTED" | "WITHDRAWN";

export type CarrierJobRequest = {
  id: number; supplierId: number; supplierName: string; category: string; fields: Record<string, string>;
  note: string; status: RequestStatus; createdBy: string; createdAt: string;
  decidedBy: string; decidedAt: string | null; decisionNote: string; jobKey: string; revision: number;
};

export type RequestForm = { fields: Record<string, string[]>; essential: Record<string, string[]> };

export const REQUEST_STATUS_LABEL: Record<RequestStatus, string> = {
  PENDING: "รอ Leschaco ยืนยัน", APPROVED: "ยืนยันแล้ว", REJECTED: "ไม่รับ", WITHDRAWN: "ถอนแล้ว",
};

/** The add-job form's fields as a carrier reads them. */
export const FIELD_LABEL: Record<string, string> = {
  customer: "ลูกค้า", jobCode: "Job No.", product: "สินค้า", destination: "ปลายทาง", date: "วันที่ (dd/mm/yyyy)",
  planTime: "เวลา (HH:mm)", type: "ประเภทรถ", cyYard: "ลานรับตู้ (CY)", weight: "น้ำหนัก", container: "เลขตู้",
  emptyReturn: "คืนตู้เปล่า", booking: "Booking", abs: "ABS", fclLcl: "FCL / LCL", plant: "โรงงาน",
  returnLoc: "ที่คืนตู้", closingDate: "Closing date (dd/mm/yyyy)", closingTime: "Closing time (HH:mm)", seal: "ซีล",
  wh: "คลังสินค้า", jobNo: "Job No.", sid: "SID", province: "จังหวัด", zip: "รหัสไปรษณีย์", pallet: "พาเลท",
  kgs: "น้ำหนัก (kg)", remark: "หมายเหตุ",
};

export const CATEGORIES = ["IMPORT", "EXPORT", "DELIVERY"] as const;

type Obj = Record<string, unknown>;
const obj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const text = (v: unknown, max = 500) => typeof v === "string" && v.length <= max;
const count = (v: unknown) => typeof v === "number" && Number.isInteger(v) && v >= 0;
const names = (v: unknown) => obj(v) && Object.values(v).every(list => Array.isArray(list) && list.length <= 40 && list.every(n => text(n, 40)));

function request(v: unknown): v is CarrierJobRequest {
  return obj(v) && count(v.id) && count(v.supplierId) && text(v.supplierName, 200) && text(v.category, 20)
    && obj(v.fields) && Object.entries(v.fields).every(([k, value]) => k.length <= 40 && text(value, 120))
    && text(v.note) && Object.hasOwn(REQUEST_STATUS_LABEL, String(v.status)) && text(v.createdBy, 160) && text(v.createdAt, 40)
    && text(v.decidedBy, 160) && (v.decidedAt === null || text(v.decidedAt, 40)) && text(v.decisionNote) && text(v.jobKey, 100)
    && count(v.revision);
}

export function parseRequests(v: unknown): { items: CarrierJobRequest[]; form: RequestForm | null } {
  if (!(obj(v) && Array.isArray(v.items) && v.items.length <= 200 && v.items.every(request)
    && (v.form === undefined || obj(v.form) && names(v.form.fields) && names(v.form.essential)))) throw new Error("invalid_response");
  return { items: v.items as CarrierJobRequest[], form: (v.form as RequestForm | undefined) ?? null };
}

/** The fields as sent: the category's own names, each trimmed, blanks left out. */
export function requestBody(category: string, fields: Record<string, string>, allowed: string[], note: string) {
  const kept: Record<string, string> = {};
  for (const name of allowed) {
    const value = (fields[name] ?? "").trim();
    if (value) kept[name] = value;
  }
  return { category, fields: kept, note: note.trim() };
}

/** What still has to be keyed before a request can be sent. */
export function missingFields(category: string, fields: Record<string, string>, form: RequestForm): string[] {
  return (form.essential[category] ?? []).filter(name => !(fields[name] ?? "").trim());
}

/** One line for a list: customer, date and time, where to. */
export function requestSummary(r: Pick<CarrierJobRequest, "category" | "fields">): string {
  const f = r.fields;
  const where = f.destination || f.plant || f.wh || f.province || "";
  return [r.category, f.customer, [f.date, f.planTime].filter(Boolean).join(" "), f.type, where].filter(Boolean).join(" · ");
}
