export type GridEditKey = {
  key: string;
  ctrlKey?: boolean;
  metaKey?: boolean;
  altKey?: boolean;
  shiftKey?: boolean;
};

export type GridEditIntent =
  | { mode: "keep" }
  | { mode: "replace"; value: string }
  /** Empty every selected cell, the way Delete does over a block in Excel. */
  | { mode: "clear" };

export type GridPoint = { row: number; column: number };

/**
 * Finds the next selectable grid cell for an arrow key.
 *
 * "fields" mirrors the columns on screen. Undefined entries are controls such
 * as the row tick box, so left and right skip over them. At an outer edge the
 * point stays put instead of wrapping into another row.
 */
export function gridArrowTarget(
  key: GridEditKey,
  current: GridPoint,
  rowCount: number,
  fields: (string | undefined)[],
): GridPoint | null {
  if (key.ctrlKey || key.metaKey || key.altKey || rowCount < 1) return null;

  if (key.key === "ArrowUp" || key.key === "ArrowDown") {
    const step = key.key === "ArrowUp" ? -1 : 1;
    return {
      row: Math.min(Math.max(current.row + step, 0), rowCount - 1),
      column: current.column,
    };
  }

  if (key.key === "ArrowLeft" || key.key === "ArrowRight") {
    const step = key.key === "ArrowLeft" ? -1 : 1;
    let column = current.column + step;
    while (column >= 0 && column < fields.length && !fields[column]) column += step;
    if (column < 0 || column >= fields.length) column = current.column;
    return { row: current.row, column };
  }

  return null;
}

/**
 * Turns a key pressed over a selected grid cell into the way editing starts.
 *
 * F2 and Enter keep the existing value, while a printable character replaces
 * it — the same two gestures Excel uses. Shortcut modifiers always stand aside
 * so copy, paste, undo and browser shortcuts keep working. Shift is allowed for
 * printable keys because it is how uppercase letters and many symbols are
 * typed, but Shift+Enter remains available to the rest of the page.
 *
 * Delete and Backspace empty the selection instead of opening an editor, which
 * is the third gesture a spreadsheet has and the one people reach for without
 * thinking. Both keys, because Excel clears with either and nobody remembers
 * which they pressed.
 */
export function gridEditIntent(key: GridEditKey): GridEditIntent | null {
  if (key.ctrlKey || key.metaKey || key.altKey) return null;

  if ((key.key === "F2" || key.key === "Enter") && !key.shiftKey) {
    return { mode: "keep" };
  }

  if (key.key === "Delete" || key.key === "Backspace") {
    return { mode: "clear" };
  }

  // `Array.from` counts Unicode code points, so a Thai character is treated as
  // one printable key just like an English letter. Named keys such as Escape,
  // ArrowLeft and Process contain more than one code point and are ignored.
  if (Array.from(key.key).length === 1) {
    return { mode: "replace", value: key.key };
  }

  return null;
}
