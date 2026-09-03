/** One row's values before an edit, keyed by the register's stable row key. */
export type EditHistoryEdit<Row extends { key: string }> = {
  key: string;
  before: Partial<Row>;
};

/** Everything changed by one user action, so a pasted block moves as one. */
export type EditHistoryStep<Row extends { key: string }> = {
  label: string;
  at: string;
  edits: EditHistoryEdit<Row>[];
};

export type EditHistoryResult<Row extends { key: string }> = {
  /** The values displaced by this move, ready to move in the other direction. */
  inverse: EditHistoryEdit<Row>[];
  touched: Row[];
  refused: number;
  gone: number;
};

/**
 * Apply one undo/redo step and collect its exact inverse.
 *
 * The same operation drives both directions: Undo writes the old values and
 * produces a Redo step; Redo writes those values and produces the next Undo
 * step. Only rows and fields that actually moved are included in the inverse.
 */
export function applyEditHistory<Row extends { key: string }>(
  rows: Row[],
  step: EditHistoryStep<Row>,
  canEdit: (row: Row) => boolean,
  onField: (row: Row, field: string, from: unknown, to: unknown) => void,
): EditHistoryResult<Row> {
  const byKey = new Map(rows.map((row) => [row.key, row]));
  const inverse: EditHistoryEdit<Row>[] = [];
  const touched = new Map<string, Row>();
  let refused = 0;
  let gone = 0;

  for (const edit of step.edits) {
    const row = byKey.get(edit.key);
    if (!row) { gone++; continue; }
    if (!canEdit(row)) { refused++; continue; }

    const record = row as unknown as Record<string, unknown>;
    const before: Record<string, unknown> = {};
    for (const [field, wantedValue] of Object.entries(edit.before)) {
      const from = record[field] ?? "";
      const to = wantedValue ?? "";
      if (Object.is(from, to)) continue;
      before[field] = from;
      record[field] = to;
      onField(row, field, from, to);
    }

    if (Object.keys(before).length > 0) {
      inverse.push({ key: row.key, before: before as Partial<Row> });
      touched.set(row.key, row);
    }
  }

  return { inverse, touched: [...touched.values()], refused, gone };
}

export type EditHistoryCommand = "undo" | "redo";

/** Resolve only the app-level shortcuts; text editors may still keep their own. */
export function editHistoryShortcut(key: {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  shiftKey?: boolean;
  altKey?: boolean;
}): EditHistoryCommand | null {
  if (!(key.ctrlKey || key.metaKey) || key.altKey) return null;
  const pressed = key.key.toLowerCase();
  if (pressed === "z") return key.shiftKey ? "redo" : "undo";
  if (!key.shiftKey && (pressed === "x" || pressed === "y")) return "redo";
  return null;
}
