import type { SheetRow } from "./rateSheetColumns";

/**
 * Several new rows at once on the rate sheet.
 *
 * One row at a time was how the sheet began: a blank line pinned above the
 * page, created the moment it had a customer and a route. A quotation is
 * rarely one lane, though — the workbook this sheet copies files three or
 * four routes under one number — and somebody with an Excel block of them
 * had to add a row, paste, save, and go round again. So the new-row area
 * holds as many rows as are wanted, a paste that runs past its end grows it
 * rather than writing over the saved rows underneath, and one save files the
 * lot, grouped the way the register groups them.
 *
 * Pure, so the two decisions here — what a paste lands on and how rows are
 * grouped into inquiries — can be checked without a screen.
 */

/** A row the server has not got yet. Negative ids can never be a real lane. */
export const isDraft = (row: { laneId: number }): boolean => row.laneId < 0;

export type DraftEdit<TRow> = { row: TRow; field: string; value: string };

/**
 * A pasted block that starts in the new-row area and runs on past it.
 *
 * The rows below the drafts are saved lanes, and a paste from the top of the
 * sheet reaching into them would rewrite them one cell at a time — the only
 * way that ever happened was by accident, with an Excel block taller than the
 * one blank line. So when a paste touches the last draft and continues into
 * the first saved rows, without a gap, those rows' share of the block becomes
 * new drafts instead. Anything else — a paste inside the drafts, a paste that
 * starts on a saved row, a Delete across both — is left exactly as it came.
 */
export function growDrafts<TRow extends { laneId: number }>(
  edits: DraftEdit<TRow>[],
  drafts: TRow[],
  rows: TRow[],
  blank: () => TRow,
): { edits: DraftEdit<TRow>[]; drafts: TRow[]; grown: number } {
  const last = drafts[drafts.length - 1];
  const unchanged = { edits, drafts, grown: 0 };
  if (!last || !edits.some((one) => one.row.laneId === last.laneId)) return unchanged;

  const saved = [...new Set(edits.map((one) => one.row).filter((row) => !isDraft(row)))];
  if (saved.length === 0) return unchanged;
  const positions = saved.map((row) => rows.indexOf(row)).sort((a, b) => a - b);
  // Exactly the first rows of the page, in order, none missing — the block
  // ran on from the drafts. A hole means a range was chosen by hand.
  if (!positions.every((position, index) => position === index)) return unchanged;

  const added = positions.map(() => blank());
  const replacement = new Map(saved.map((row) => [row.laneId, added[rows.indexOf(row)]]));
  return {
    edits: edits.map((one) => isDraft(one.row) ? one : { ...one, row: replacement.get(one.row.laneId)! }),
    drafts: [...drafts, ...added],
    grown: added.length,
  };
}

/** One inquiry as the register files it: a date, a customer, its lanes. */
export type DraftGroup = { inquiredOn: string; customer: string; rows: SheetRow[] };

/**
 * The drafts as the inquiries they will become.
 *
 * The register numbers an inquiry, not a lane: the three Clariant routes
 * asked for on the same day share one No. So rows with the same customer
 * and date go up together, in the order they were typed, and become one
 * number — which is what the workbook did, and what the sheet shows.
 * A row with no date is today's.
 */
export function draftGroups(drafts: SheetRow[], today: string): DraftGroup[] {
  const groups: DraftGroup[] = [];
  for (const row of drafts) {
    const inquiredOn = row.date.trim() || today;
    const customer = row.customer.trim();
    const group = groups.find((one) =>
      one.inquiredOn === inquiredOn && one.customer.toLowerCase() === customer.toLowerCase());
    if (group) group.rows.push(row);
    else groups.push({ inquiredOn, customer, rows: [row] });
  }
  return groups;
}

/** Today as the sheet writes a date. */
export function sheetToday(now: Date = new Date()): string {
  const dd = String(now.getDate()).padStart(2, "0");
  const mm = String(now.getMonth() + 1).padStart(2, "0");
  return `${dd}/${mm}/${now.getFullYear()}`;
}
