/**
 * Where a block from the clipboard lands on the grid.
 *
 * The gesture is the one everybody already has: select a range in Excel, copy,
 * click one cell here, paste. Excel spreads the block down and to the right
 * from the cell you clicked, and stops at the edge of the sheet. This is that,
 * and nothing else — the arithmetic only, so it can be checked without a
 * browser.
 *
 * <h3>Two things it has to get right</h3>
 *
 * <b>The anchor, not the selection.</b> Pasting used to write only inside the
 * rectangle that was dragged, so a five-column block pasted onto one selected
 * cell put one value in and dropped the other nineteen. Nobody selects the
 * exact shape of what is on their clipboard first; they click where it should
 * start.
 *
 * <b>Column alignment across columns that cannot be written.</b> A grid has
 * columns with no field behind them — a tick box, a status pill, the row
 * number. The old path filtered those out before matching values to columns,
 * so a block pasted across one had every value after it shifted a column to the
 * left: a destination written into the product column, silently. A value whose
 * column cannot take it is consumed and dropped here, so column three of the
 * clipboard is always column three of the grid.
 *
 * A leaf module: it imports nothing.
 */

const TAB = "\t";
const NEWLINE = "\n";

export type PastePlan<TField> = {
  /** Every cell to write, as grid coordinates and the field behind them. */
  cells: { row: number; column: number; field: TField; value: string }[];
  /** Rows of the block that ran off the bottom of what is loaded. */
  rowsClipped: number;
  /** Columns that ran off the right-hand edge. */
  columnsClipped: number;
  /**
   * Values that landed on a column with nothing behind it — a tick box, a
   * pill. Counted rather than shifted along, because shifting is what wrote
   * them into the wrong column.
   */
  cellsUnwritable: number;
};

/** The clipboard's text as a grid. Tab-separated, which is what a spreadsheet writes. */
export function readClipboardGrid(text: string): string[][] {
  // A spreadsheet ends its last row with a newline. That is punctuation, not
  // an empty row.
  const lines = text.replace(/\r\n?/g, NEWLINE).replace(/\n$/, "").split(NEWLINE);
  return lines.map((line) => line.split(TAB));
}

/**
 * True when the clipboard holds one value rather than a block.
 *
 * One value fills the whole selection instead of landing in a single cell.
 * Putting one carrier on forty rows is most of what this gets used for, and
 * asking for forty copies of it on the clipboard would be the wrong answer.
 */
export function isSingleValue(block: string[][]): boolean {
  return block.length === 1 && block[0].length === 1;
}

/**
 * Where the block goes.
 *
 * @param block      the clipboard, already split
 * @param anchor     the top-left of the selection — where Excel would start
 * @param extent     the bottom-right of the selection, for the single-value case
 * @param size       how many rows are loaded and how many columns the grid has
 * @param fieldAt    the field behind a column, or undefined when it has none
 */
export function planPaste<TField>(
  block: string[][],
  anchor: { row: number; column: number },
  extent: { row: number; column: number },
  size: { rows: number; columns: number },
  fieldAt: (column: number) => TField | undefined,
): PastePlan<TField> {
  const cells: PastePlan<TField>["cells"] = [];
  let cellsUnwritable = 0;

  const push = (row: number, column: number, value: string) => {
    const field = fieldAt(column);
    if (field === undefined) { cellsUnwritable++; return; }
    cells.push({ row, column, field, value: value.trim() });
  };

  if (isSingleValue(block)) {
    // One value over however much is selected.
    const value = block[0][0];
    const lastRow = Math.min(Math.max(anchor.row, extent.row), size.rows - 1);
    const lastColumn = Math.min(Math.max(anchor.column, extent.column), size.columns - 1);
    for (let row = anchor.row; row <= lastRow; row++) {
      for (let column = anchor.column; column <= lastColumn; column++) push(row, column, value);
    }
    return { cells, rowsClipped: 0, columnsClipped: 0, cellsUnwritable };
  }

  const height = block.length;
  const width = block.reduce((widest, line) => Math.max(widest, line.length), 0);

  // What will not fit. Reported rather than silently lost: a paste of sixty
  // rows onto a page of fifty is a paste that half worked, and it looks exactly
  // like one that worked.
  const rowsClipped = Math.max(0, anchor.row + height - size.rows);
  const columnsClipped = Math.max(0, anchor.column + width - size.columns);

  for (let down = 0; down < height; down++) {
    const row = anchor.row + down;
    if (row >= size.rows) break;
    for (let across = 0; across < width; across++) {
      const column = anchor.column + across;
      if (column >= size.columns) break;
      const value = block[down][across];
      // A short line in the middle of the block ends there. The cells past it
      // keep what they had rather than being emptied by a paste that never
      // mentioned them.
      if (value === undefined) continue;
      push(row, column, value);
    }
  }

  return { cells, rowsClipped, columnsClipped, cellsUnwritable };
}
