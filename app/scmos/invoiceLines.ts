/**
 * A carrier's invoice, laid out as the department's own paper one (30 Sep 2026, the department lead's sample:
 * a carrier's ใบแจ้งหนี้ to Leschaco). One job per invoice; section 1 is the transportation charge (1.1, priced
 * from the carrier's Rate and editable) and three charges on it, section 2 what the carrier paid out and is
 * reimbursed. Withholding tax is 1% of the transportation charge only.
 *
 * The server keeps the same list and arithmetic in Rules/InvoiceLines.cs; tests/fixtures/billing-parity.json
 * holds both to the sample's own figures. Money is added in satang, as integers, so the sums are exact.
 */

export type InvoiceSection = "TRANSPORT" | "REIMBURSEMENT";
export type InvoiceLineKind = { code: string; section: InvoiceSection; number: string; english: string; thai: string };

export const TRANSPORT_CHARGE = "TRANSPORT_CHARGE";

export const INVOICE_LINES: readonly InvoiceLineKind[] = [
  { code: TRANSPORT_CHARGE, section: "TRANSPORT", number: "1.1", english: "TRANSPORTATION CHARGE", thai: "ค่าขนส่ง" },
  { code: "KNOCK_DOOR", section: "TRANSPORT", number: "1.2", english: "KNOCK DOOR", thai: "ค่าล่วงเวลา" },
  { code: "CHASSIS_DETENTION", section: "TRANSPORT", number: "1.3", english: "CHASSIS DETENTION", thai: "ค่าค้างหาง" },
  { code: "WAITING_TIME", section: "TRANSPORT", number: "1.4", english: "WAITING TIME CHARGE", thai: "ค่าเสียเวลา" },
  { code: "GATE_FEE", section: "REIMBURSEMENT", number: "", english: "GATE FEE", thai: "ค่าผ่านท่า" },
  { code: "GATE_CHARGE", section: "REIMBURSEMENT", number: "", english: "GATE CHARGE", thai: "ค่าบริการประตูท่า" },
  { code: "CLEANING_CHARGE", section: "REIMBURSEMENT", number: "", english: "CLEANING CHARGE", thai: "ค่าล้างตู้" },
  { code: "REPAIR_CHARGE", section: "REIMBURSEMENT", number: "", english: "REPAIR CHARGE", thai: "ค่าซ่อมตู้" },
  { code: "LIFT_ON", section: "REIMBURSEMENT", number: "", english: "LIFT ON", thai: "ค่ายกตู้ขึ้น" },
  { code: "LIFT_OFF", section: "REIMBURSEMENT", number: "", english: "LIFT OFF", thai: "ค่ายกตู้ลง" },
];

/** Withholding tax on the transportation charge, in percent. */
export const WITHHOLDING_PERCENT = 1;

export type InvoiceLineInput = { code: string; quantity: number; unitPrice: number };
export type InvoiceTotals = { transport: number; reimbursement: number; total: number; withholding: number; net: number };

const cents = (value: number) => Math.round(value * 100);

/** A line's amount: quantity × unit price, to the satang. */
export function lineAmount(quantity: number, unitPrice: number): number {
  return cents((Number(quantity) || 0) * (Number(unitPrice) || 0)) / 100;
}

export function invoiceTotals(lines: readonly InvoiceLineInput[]): InvoiceTotals {
  let transport = 0;
  let reimbursement = 0;
  for (const line of lines) {
    const amount = cents((Number(line.quantity) || 0) * (Number(line.unitPrice) || 0));
    if (INVOICE_LINES.find((kind) => kind.code === line.code)?.section === "REIMBURSEMENT") reimbursement += amount;
    else transport += amount;
  }
  const withholding = Math.round((transport * WITHHOLDING_PERCENT) / 100);
  const total = transport + reimbursement;
  return { transport: transport / 100, reimbursement: reimbursement / 100, total: total / 100, withholding: withholding / 100, net: (total - withholding) / 100 };
}

/** Why a job has no price by its carrier's Rate, in the carrier's words; empty when it has one. */
export function rateReason(rate: { reason: string; vehicle: string; dieselMonth: string } | null | undefined): string {
  switch (rate?.reason ?? "") {
    case "": return "";
    case "no-vehicle": return "งานนี้ไม่มีประเภทรถหรือตู้ที่ Rate ใช้";
    case "no-diesel": return `ยังไม่มีราคาน้ำมันของเดือน ${rate?.dieselMonth || "ที่วิ่งงาน"}`;
    case "no-lane": return "ไม่พบเส้นทางใน Rate ที่ตรงกับลูกค้าและปลายทางของงานนี้";
    case "not-quoted": return `เส้นทางใน Rate ไม่มีราคาสำหรับรถ ${rate?.vehicle || ""}`.trim();
    case "many": return "มีหลายเส้นทางใน Rate ที่ตรงกับงานนี้และราคาไม่เท่ากัน";
    default: return "หาราคาจาก Rate ไม่ได้";
  }
}

/** Baht as the invoice writes it: 7,879.00. */
export function money(value: number | null | undefined): string {
  return value == null || !Number.isFinite(value) ? "—" : value.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

/** "2026-09-29" → "29 September 2026", as the invoice dates are written. */
export function longDate(iso: string | null | undefined): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(iso ?? ""));
  if (!parts) return "";
  const months = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];
  return `${Number(parts[3])} ${months[Number(parts[2]) - 1] ?? ""} ${parts[1]}`;
}

/** An ISO date plus some days, as ISO — the due date from the invoice date and the credit term. */
export function addDays(iso: string, days: number): string {
  const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  if (!parts || !Number.isFinite(days)) return "";
  const date = new Date(Date.UTC(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]) + Math.trunc(days)));
  return date.toISOString().slice(0, 10);
}

/** A register date (dd/MM/yyyy) as ISO; empty when unreadable. */
export function isoOf(date: string | null | undefined): string {
  const parts = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(String(date ?? "").trim());
  return parts ? `${parts[3]}-${parts[2]}-${parts[1]}` : "";
}
