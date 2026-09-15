/**
 * The DG price a NON-DG price implies on the rate sheet.
 *
 * The department prices dangerous goods as the plain rate plus a fixed
 * surcharge per truck size, and asked on 15 September 2026 for the sheet to
 * write the DG figure the moment the NON-DG one is keyed — and to leave it
 * editable. So the DG cell is filled when it is empty, kept in step while it
 * still reads as "the plain rate plus the surcharge", and left alone the
 * moment somebody has typed their own figure into it. A hand-keyed DG price
 * is a negotiated price; the surcharge is only the starting point.
 *
 * No imports on purpose: the arithmetic should be checkable without a
 * browser or a rate register loaded.
 */

/** Which NON-DG column feeds which DG column, and by how much, in baht. */
export const DG_SURCHARGE: readonly { from: string; to: string; add: number }[] = [
  { from: "4W", to: "4W DG", add: 300 },
  { from: "6W", to: "6W DG", add: 500 },
  { from: "10W", to: "10W DG", add: 800 },
  { from: "20F", to: "20F DG", add: 500 },
  { from: "40F", to: "40F DG", add: 500 },
];

/** One cell write, as the sheet's cell endpoint takes it: `price:<vehicle>`. */
export type CellEdit = { laneId: number; field: string; value: string };

/** A price as typed — "1,300", "1300 " — as a number, or null for anything else. */
export function priceValue(value: string | number | null | undefined): number | null {
  if (typeof value === "number") return Number.isFinite(value) && value > 0 ? value : null;
  const text = String(value ?? "").replace(/[,\s฿]/g, "");
  if (!text) return null;
  const number = Number(text);
  return Number.isFinite(number) && number > 0 ? number : null;
}

/** The vehicle a `price:` field names, or "" for any other field. */
const vehicleOf = (field: string) => (field.startsWith("price:") ? field.slice(6) : "");

/**
 * The DG cells to write beside a set of NON-DG edits.
 *
 * `priceOf` reads the row as it stands before the edits — the DG cell and
 * the old NON-DG figure are what decide whether the DG cell is still
 * derived. A DG cell the same batch already writes is left to that write:
 * a pasted block that carries both columns says what the DG price is.
 */
export function dgFills(
  edits: readonly CellEdit[],
  priceOf: (laneId: number, vehicle: string) => number | null,
): CellEdit[] {
  const written = new Set(edits.map((edit) => `${edit.laneId}|${edit.field}`));
  const fills: CellEdit[] = [];
  for (const edit of edits) {
    const rule = DG_SURCHARGE.find((one) => one.from === vehicleOf(edit.field));
    if (!rule) continue;
    const dgField = `price:${rule.to}`;
    if (written.has(`${edit.laneId}|${dgField}`)) continue;

    const held = priceOf(edit.laneId, rule.to);
    const oldPlain = priceOf(edit.laneId, rule.from);
    // Derived: nothing there yet, or exactly what the old plain rate implied.
    const derived = held === null || (oldPlain !== null && held === oldPlain + rule.add);
    if (!derived) continue;

    const plain = priceValue(edit.value);
    if (plain === null) {
      // The plain rate was cleared: a derived DG figure goes with it, a
      // hand-keyed one stays.
      if (held !== null) fills.push({ laneId: edit.laneId, field: dgField, value: "" });
      continue;
    }
    fills.push({ laneId: edit.laneId, field: dgField, value: String(plain + rule.add) });
  }
  return fills;
}

/** What a fill did, for the toast: "4W DG = ฿1,300 (+300)". */
export function describeFill(fill: CellEdit): string {
  const vehicle = vehicleOf(fill.field);
  const rule = DG_SURCHARGE.find((one) => one.to === vehicle);
  const price = priceValue(fill.value);
  if (price === null) return `${vehicle} ล้างแล้ว`;
  return `${vehicle} = ฿${price.toLocaleString("en-US")}${rule ? ` (+${rule.add})` : ""}`;
}
