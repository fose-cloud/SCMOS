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

/* -------------------------------------------------------------- copying out */

/** The heading row of a copied table, in the app's own navy. */
/**
 * The type a copied table is set in.
 *
 * <p>Written out rather than imported, because this file is checked by the node
 * test runner and a value import of a sibling would have to carry a `.ts`
 * extension the compiler refuses. It is not a second definition: `tableFont.ts`
 * holds the one the screen uses, and a test asserts the two strings are equal —
 * so the pair cannot drift without a failure that names them.</p>
 */
export const COPY_FONT_CSS = "font-family:'Angsana New','AngsanaUPC','Cordia New',serif;font-size:18pt";

const HEAD_BG = "#0A2240";
const HEAD_FG = "#FFFFFF";

/**
 * A block of values as text, and as a table an email will actually render.
 *
 * Tab-separated text is what a spreadsheet reads, and it is all Ctrl+C puts on
 * the clipboard. Pasted into an email it arrives as a run of words with tabs in
 * it, which is why the headings were asked for in the first place: without the
 * column names nobody reading the mail can tell which number is which. So the
 * copy meant for a mail carries an HTML table as well, and the mail client
 * renders the borders and the heading row. Both formats go on the clipboard
 * together and whatever receives it takes the one it can use.
 *
 * Lives here rather than on a screen because two grids copy tables now, and a
 * second implementation would eventually disagree about the heading colour, the
 * escaping, or which of them Outlook honours.
 */
export function copyBlockPayload(lines: string[][], heads: string[] | null) {
  const rows = heads ? [heads, ...lines] : lines;
  /*
   * A tab or a newline inside a value would end the cell or the row.
   *
   * Not hypothetical: the rate register keeps addresses as they were typed, and
   * plenty of them run to three lines with a maps link underneath. Copying fifty
   * of those rows produced a hundred and twenty lines, so everything after the
   * first long address landed a row out in the spreadsheet it was pasted into.
   *
   * Flattened to a space rather than quoted. A quoted value is only understood
   * by some of the things people paste into, and an address that loses a line
   * break is a smaller loss than every row below it losing its alignment.
   */
  const flat = (value: string) => value.replace(/[\t\r\n]+/g, " ").trim();
  const text = rows.map((line) => line.map(flat).join(TAB)).join(NEWLINE);
  const esc = (value: string) =>
    value.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
  const box = (value: string, head: boolean) => {
    const tag = head ? "th" : "td";
    // The heading row in the app's navy with white text — asked for so a pasted
    // table reads as a heading in a mail rather than as a first row that happens
    // to be bold. `bgcolor` and a `color` on the cell as well as the style:
    // Outlook and Excel each drop one or other of them, and a white word on a
    // white cell is worse than no colour at all.
    const style = "border:1px solid " + (head ? HEAD_BG : "#D8E0E8") + ";padding:4px 9px;text-align:left"
      + (head ? `;background-color:${HEAD_BG};color:${HEAD_FG};font-weight:600` : "");
    const attrs = head ? ` bgcolor="${HEAD_BG}"` : "";
    const inner = head
      ? `<font color="${HEAD_FG}">${esc(value) || "&nbsp;"}</font>`
      : (esc(value) || "&nbsp;");
    return `<${tag}${attrs} style="${style}">${inner}</${tag}>`;
  };
  // The same face and size the grid is drawn in, from the same constant. These
  // were a different face at a different size, so a table pasted into a mail
  // looked nothing like the table it was copied from.
  const html = `<table style="border-collapse:collapse;${COPY_FONT_CSS}">`
    + (heads ? `<thead><tr>${heads.map((h) => box(h, true)).join("")}</tr></thead>` : "")
    + `<tbody>${lines.map((line) => `<tr>${line.map((v) => box(v, false)).join("")}</tr>`).join("")}</tbody>`
    + "</table>";
  return { text, html };
}

/**
 * Puts a table on the clipboard in both flavours.
 *
 * Older browsers have no `ClipboardItem`; they still get the text, which is the
 * whole of what Ctrl+C would have given them anyway.
 */
export async function writeClipboardTable(lines: string[][], heads: string[] | null) {
  const { text, html } = copyBlockPayload(lines, heads);
  if (typeof ClipboardItem === "function") {
    await navigator.clipboard.write([new ClipboardItem({
      "text/plain": new Blob([text], { type: "text/plain" }),
      "text/html": new Blob([html], { type: "text/html" }),
    })]);
    return;
  }
  await navigator.clipboard.writeText(text);
}
