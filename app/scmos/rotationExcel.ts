import * as XLSX from "xlsx";
import type { NewRotationRow } from "./rotation";

/**
 * Reads the rotation workbook.
 *
 * One sheet per operator — "JIRATCHAYA -IMPORT", "MALIWAN-EXPORT" — and a
 * customer on every row, with the modes that operator handles for them, the two
 * people who cover, the carriers used, and the customer service contact at LCB.
 *
 * Every sheet is read, not just the first: the sheets are the rotation. Reading
 * one would produce a table that looks complete and quietly omits four
 * operators' customers.
 */

/** The heading each sheet carries, and the field it fills. */
const HEADINGS: [RegExp, keyof NewRotationRow][] = [
  [/^customer$/i, "customer"],
  [/^import$/i, "import"],
  [/^export$/i, "export"],
  [/^fcl$/i, "fcl"],
  [/^lcl$/i, "lcl"],
  [/^domestis|^domestic/i, "domestic"],
  [/subcon.*primary|^primary$/i, "primaryContact"],
  // Back up 2 before Back up: the looser pattern would swallow both.
  [/^back\s*up\s*2$/i, "backup2Contact"],
  [/^back\s*up$/i, "backupContact"],
  [/^sub\s*fcl$/i, "subFcl"],
  [/^sub\s*lcl$/i, "subLcl"],
  [/^cs\s*lcb$/i, "csLcb"],
];

/** The mode columns, which the sheet ticks with a "P". */
const MODES = new Set(["import", "export", "fcl", "lcl", "domestic"]);

const EMAIL = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/;

function text(value: unknown): string {
  return String(value ?? "").replace(/\s+/g, " ").trim();
}

/** The email inside a contact cell, lowercased. Empty when the cell has none. */
function emailIn(contact: string): string {
  const found = EMAIL.exec(contact);
  return found ? found[0].toLowerCase() : "";
}

export type RotationImport = {
  rows: NewRotationRow[];
  /** Sheets read, so somebody can see it did not stop at the first. */
  sheets: string[];
  /** Rows that named no customer, so were headings or spacers. */
  skipped: number;
};

export async function parseRotationWorkbook(file: File): Promise<RotationImport> {
  const book = XLSX.read(await file.arrayBuffer(), { type: "array" });
  const rows: NewRotationRow[] = [];
  const sheets: string[] = [];
  let skipped = 0;

  for (const sheetName of book.SheetNames) {
    const matrix = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName],
      { header: 1, defval: null, blankrows: false, raw: false });
    if (!matrix.length) continue;

    // The heading row is the one naming the customer column and a mode beside
    // it. Found rather than assumed at row zero, because a sheet may grow a
    // title line and a rotation read one row off is worse than one not read.
    const headerRow = matrix.findIndex((row) => {
      const cells = (row || []).map(text);
      return cells.some((cell) => /^customer$/i.test(cell))
        && cells.some((cell) => /^import$|^export$/i.test(cell));
    });
    if (headerRow < 0) continue;

    const fields = (matrix[headerRow] || []).map((cell) => {
      const label = text(cell);
      return HEADINGS.find(([pattern]) => pattern.test(label))?.[1] ?? null;
    });
    if (!fields.includes("customer")) continue;

    sheets.push(sheetName);

    for (let r = headerRow + 1; r < matrix.length; r++) {
      const cells = matrix[r] || [];
      const row: NewRotationRow = {
        customer: "", sheet: sheetName,
        import: false, export: false, fcl: false, lcl: false, domestic: false,
        primaryContact: "", primaryEmail: "",
        backupContact: "", backupEmail: "",
        backup2Contact: "", backup2Email: "",
        subFcl: "", subLcl: "", csLcb: "",
      };

      fields.forEach((field, index) => {
        if (!field) return;
        const value = text(cells[index]);
        if (MODES.has(field)) {
          // A tick is a "P" on these sheets. Anything else in the cell counts
          // as a tick too — an operator writing "Y" or "x" means yes, and
          // reading only "P" would silently drop the mode.
          (row as unknown as Record<string, boolean>)[field] = value.length > 0;
          return;
        }
        (row as unknown as Record<string, string>)[field] = value;
      });

      if (!row.customer) { skipped++; continue; }

      row.primaryEmail = emailIn(row.primaryContact);
      row.backupEmail = emailIn(row.backupContact);
      row.backup2Email = emailIn(row.backup2Contact);
      rows.push(row);
    }
  }

  return { rows, sheets, skipped };
}
