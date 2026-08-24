import * as XLSX from "xlsx";
import { fromSerial } from "./excel";
import type { NewIssue } from "./issues";
import { pad } from "./util";

/**
 * Reads the issue log the team already keeps.
 *
 * Column for column from the บันทึกปัญหา sheet of `Subcontract_Issue.xlsx`, so
 * a log they have been filling in for months comes across without anybody
 * re-typing it, and the codes it already issued come with it.
 *
 * Dates and times in that file are stored as real Excel values, not text, which
 * is what makes this exact — `02-Jul-2026` is displayed but `46205` is stored,
 * and the same `fromSerial` the plan importer uses turns it into the dd/MM/yyyy
 * the register writes. Reading the displayed text instead would mean guessing
 * whether `03-07` is March or July, which is the guess that has bitten this
 * importer before.
 */

/** The sheet that holds the log. Others in the file are its dashboard and lists. */
const LOG_SHEET = /บันทึกปัญหา|issue\s*log/i;

/** Headings, in the order that sheet has them. */
const COLUMNS: (keyof NewIssue | null)[] = [
  "code", "foundOn", "foundAt", "source", "reporter", "jobRef", "detail",
  "category", "severity", "impact", "channel", "owner", "dueOn", "status",
];

/** A cell the sheet uses to mean "none": a dash, or nothing. */
const NONE = /^[-–—]+$/;

function text(value: unknown): string {
  const raw = String(value ?? "").replace(/\s+/g, " ").trim();
  return NONE.test(raw) ? "" : raw;
}

/**
 * A date cell as dd/MM/yyyy, and a time cell as HH:mm.
 *
 * The stored number wins when there is one. A row typed by hand keeps whatever
 * the person wrote, because a value this cannot read is still worth carrying —
 * the log is what somebody recorded, not what this file can parse.
 */
function when(formatted: unknown, raw: unknown, wantTime: boolean): string {
  if (typeof raw === "number") {
    const parts = fromSerial(raw);
    if (parts) {
      const date = `${pad(parts.d)}/${pad(parts.m)}/${parts.y}`;
      const clock = `${pad(parts.H)}:${pad(parts.M)}`;
      // A bare time is stored as a fraction of a day, so its date half is
      // 1899 and meaningless; a due date carries both and keeps both.
      if (wantTime) return raw < 1 ? clock : `${date} ${clock}`;
      return date;
    }
  }
  return text(formatted);
}

export type IssueImport = {
  issues: NewIssue[];
  /** Rows the sheet held that carried no detail, so were not issues. */
  skipped: number;
  /** Which sheet it read, so the person can see it picked the right one. */
  sheet: string;
};

export async function parseIssueWorkbook(file: File): Promise<IssueImport> {
  const book = XLSX.read(await file.arrayBuffer(), { type: "array" });

  const sheetName = book.SheetNames.find((name) => LOG_SHEET.test(name)) ?? book.SheetNames[0] ?? "";
  if (!sheetName) return { issues: [], skipped: 0, sheet: "" };

  const shape = { header: 1, defval: null, blankrows: false } as const;
  const matrix = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], { ...shape, raw: false });
  const stored = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], { ...shape, raw: true });
  // Consulted only while both readings line up row for row; if they ever did
  // not, the formatted one stands alone rather than pairing rows wrongly.
  const raws = stored.length === matrix.length ? stored : [];

  // The heading row is the first that names the issue code and the reference.
  // Found rather than assumed, because the sheet carries a title above it and
  // somebody will eventually add another line there.
  const headerRow = matrix.findIndex((row) => {
    const cells = (row || []).map((cell) => String(cell ?? "").trim());
    return cells.some((cell) => /รหัสปัญหา/.test(cell))
      && cells.some((cell) => /Job\s*\/\s*Shipment/i.test(cell));
  });
  if (headerRow < 0) return { issues: [], skipped: 0, sheet: sheetName };

  const issues: NewIssue[] = [];
  let skipped = 0;

  for (let r = headerRow + 1; r < matrix.length; r++) {
    const cells = matrix[r] || [];
    const rawCells = raws[r] || [];

    const issue = { detail: "" } as NewIssue;
    COLUMNS.forEach((field, index) => {
      if (!field) return;
      const value = field === "foundOn" ? when(cells[index], rawCells[index], false)
        : field === "foundAt" || field === "dueOn" ? when(cells[index], rawCells[index], true)
        : text(cells[index]);
      if (value) (issue as Record<string, string>)[field] = value;
    });

    // An issue is its description. A row with a code and nothing else is a
    // blank line somebody left ready, not a problem that happened.
    if (!issue.detail) { if (text(cells[0])) skipped++; continue; }
    issues.push(issue);
  }

  return { issues, skipped, sheet: sheetName };
}
