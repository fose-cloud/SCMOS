/**
 * Audit Planning (1 Oct 2026): the year's EHS audit plan with the truck subcontractors, drawn as the
 * department's workbook is — a row per audit, a column per month, the legend's marks in the month cells.
 * The API keeps the rows (Services/AuditPlanService.cs); this file reads them onto the sheet's grid, and reads
 * rows pasted from that sheet back into rows.
 */

export type AuditPlanHeader = {
  year: number; title: string; preparedBy: string; reviewedBy: string; reviewedDate: string;
  secondReviewedBy: string; approvedBy: string; revision: string; updatedBy: string; updatedAt: string | null;
};
export type AuditPlanItem = {
  id: number; year: number; kind: string; sequence: number; supplierId: number | null; company: string; supplierStatus: string;
  target: string; personInCharge: string; auditDate: string; schedule: string; status: string; notYetDone: boolean;
  nextDate: string; remark: string; findingSentDate: string; reportSentDate: string; updatedBy: string; updatedAt: string;
};
export type AuditPlan = { plan: AuditPlanHeader; items: AuditPlanItem[]; years: number[]; sections: Record<string, string> };

export const MONTHS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];
/** The sheet's month header colours, as the workbook paints them. */
export const MONTH_COLOURS = ["#8EA9DB", "#FFFF00", "#FF0000", "#A9D08E", "#BFBFBF", "#808080", "#D9E1F2", "#FFC000", "#375623", "#8497B0", "#3A3838", "#FF7C80"];

export const STATUS_TH: Record<string, string> = {
  planned: "ตามแผน", done: "ตรวจแล้ว", postponed: "เลื่อน", continue: "ทำต่อ", cancelled: "ยกเลิก",
};

export type Mark = { symbol: string; tone: "fixed" | "tentative" | "done" | "late" | "moved" | "cancelled"; title: string };

/** dd/mm/yyyy into its parts, or null. */
export function dateParts(value: string): { day: number; month: number; year: number } | null {
  const match = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((value ?? "").trim());
  return match ? { day: Number(match[1]), month: Number(match[2]), year: Number(match[3]) } : null;
}

/**
 * What the sheet draws in one month's cell for one audit: the day for a fixed date, ○ tentative, ✓ done,
 * X not yet done (its date passed while still planned), → continues and ⇢ postponed — with ○ in the month it
 * moved to — or nothing.
 */
export function markFor(item: Pick<AuditPlanItem, "auditDate" | "nextDate" | "status" | "schedule" | "notYetDone">,
  year: number, month: number): Mark | null {
  const at = dateParts(item.auditDate);
  const next = dateParts(item.nextDate);
  if (at && at.year === year && at.month === month) {
    if (item.status === "done") return { symbol: "✓", tone: "done", title: `ตรวจแล้ว ${item.auditDate}` };
    if (item.status === "cancelled") return { symbol: "—", tone: "cancelled", title: "ยกเลิก" };
    if (item.status === "postponed") return { symbol: "⇢", tone: "moved", title: `เลื่อนไป ${item.nextDate}` };
    if (item.status === "continue") return { symbol: "→", tone: "moved", title: `ทำต่อ ${item.nextDate}` };
    if (item.notYetDone) return { symbol: "X", tone: "late", title: `ยังไม่ได้ตรวจ — กำหนด ${item.auditDate}` };
    if (item.schedule === "tentative") return { symbol: "○", tone: "tentative", title: `กำหนดการเบื้องต้น ${item.auditDate}` };
    return { symbol: String(at.day), tone: "fixed", title: `กำหนดการแน่นอน ${item.auditDate}` };
  }
  if (next && next.year === year && next.month === month && (item.status === "postponed" || item.status === "continue"))
    return { symbol: "○", tone: "tentative", title: `${item.status === "postponed" ? "เลื่อนมา" : "ทำต่อ"} ${item.nextDate}` };
  return null;
}

/** Cells of a tab-separated paste, quoted cells kept whole — the sheet's person-in-charge cells hold line breaks. */
export function parseTsv(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let quoted = false;
  const source = (text ?? "").replace(/\r\n?/g, "\n");
  for (let i = 0; i < source.length; i++) {
    const char = source[i];
    if (quoted) {
      if (char === '"' && source[i + 1] === '"') { cell += '"'; i++; }
      else if (char === '"') quoted = false;
      else cell += char;
    } else if (char === '"' && cell === "") quoted = true;
    else if (char === "\t") { row.push(cell); cell = ""; }
    else if (char === "\n") { row.push(cell); rows.push(row); row = []; cell = ""; }
    else cell += char;
  }
  if (cell !== "" || row.length) { row.push(cell); rows.push(row); }
  return rows;
}

export type PastedRow = {
  kind: string; sequence: number; company: string; target: string; personInCharge: string; auditDate: string; remark: string;
};

/**
 * Rows copied off the department's own plan sheet. A row is a number, the company after it, the site and the
 * auditors after that, and a date somewhere along it ("Audit date: 13/02/2026"); a section row ("RE-Audit …",
 * "New …") sets the kind for the rows under it. Anything else — the title, the legend, the month header, blank
 * rows — is passed over, and a numbered row with no date is reported rather than guessed.
 */
export function parsePlanPaste(text: string): { rows: PastedRow[]; problems: string[] } {
  const rows: PastedRow[] = [];
  const problems: string[] = [];
  let kind = "re-audit";
  for (const cells of parseTsv(text)) {
    const at = cells.findIndex((value, index) => /^\d{1,3}$/.test(value.trim()) && /\p{L}/u.test(cells[index + 1] ?? ""));
    if (at < 0) continue;
    const title = (cells[at + 1] ?? "").trim();
    if (/re-?audit/i.test(title)) { kind = "re-audit"; continue; }
    if (/^new\b/i.test(title) && !/co\.|ltd|limited|partnership/i.test(title)) { kind = "new"; continue; }
    const dated = cells.find((value) => /\d{1,2}[/.]\d{1,2}[/.]\d{4}/.test(value)) ?? "";
    const date = /(\d{1,2})[/.](\d{1,2})[/.](\d{4})/.exec(dated);
    if (!date) { problems.push(`${title}: ไม่พบวันที่ Audit`); continue; }
    const target = (cells[at + 2] ?? "").trim();
    rows.push({
      kind,
      sequence: Number(cells[at].trim()),
      company: title,
      target: /^[A-Za-z]{2,6}$/.test(target) ? target.toUpperCase() : "",
      personInCharge: /^[A-Za-z]{2,6}$/.test(target) ? (cells[at + 3] ?? "").trim() : target,
      auditDate: `${date[1].padStart(2, "0")}/${date[2].padStart(2, "0")}/${date[3]}`,
      remark: dated.replace(/\s+/g, " ").trim(),
    });
  }
  return { rows, problems };
}

/** The audits on one day of a month, for the vendor calendar. */
export function onDay(items: AuditPlanItem[], year: number, month: number, day: number): AuditPlanItem[] {
  return items.filter((item) => {
    const shown = item.status === "postponed" || item.status === "continue" ? dateParts(item.nextDate) : dateParts(item.auditDate);
    return shown !== null && shown.year === year && shown.month === month && shown.day === day && item.status !== "cancelled";
  });
}
