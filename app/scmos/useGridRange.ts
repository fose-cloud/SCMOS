"use client";

import { useEffect, useRef, useState, type MouseEvent as ReactMouseEvent } from "react";
import { gridArrowTarget, gridEditIntent } from "./gridEditKey";
import { planPaste, readClipboardGrid } from "./pasteBlock";

/**
 * A spreadsheet's rectangle, and everything that hangs off it.
 *
 * Dragging a block of cells, walking it with the arrows, copying it out as
 * tab-separated text, pasting a block back in and emptying it with Delete — the
 * gestures the plan already lives by, because the plan lives in Excel.
 *
 * All of it was written inside the workspace, wired to jobs and to that screen's
 * several grids. The rate sheet needed the same gestures over different rows
 * with different fields, and the choice was to write them a second time or to
 * lift them out. Written twice they would drift, which in this codebase is not
 * a worry but a matter of record.
 *
 * <h3>What the caller owns</h3>
 *
 * The rectangle is coordinates; this file never learns what a row is. The caller
 * says how to read a cell, whether a row may be changed, and what to do with a
 * block of edits — so the workspace can write through its save queue and the
 * rate sheet can post a cell at a time, and neither has to explain itself here.
 */

/** Which grid, and the two corners. A screen may draw more than one. */
export type GridRange = {
  grid: string;
  r1: number;
  c1: number;
  r2: number;
  c2: number;
};

/** One cell of a resolved selection: a row of the caller's, and its field. */
export type GridCell<TRow, TField> = { row: TRow; field: TField };

export type GridEdit<TRow, TField> = { row: TRow; field: TField; value: string };

export type GridRangeOptions<TRow, TField> = {
  /** The rows currently drawn on a grid, in the order they are drawn. */
  rowsOf: (grid: string) => TRow[];
  /**
   * The field behind each column of a grid, undefined where there is none.
   *
   * Undefined is what keeps a tick box or a status pill out of a rectangle, so
   * a drag across one selects nothing there and a paste cannot land in it.
   */
  fieldsOf: (grid: string) => (TField | undefined)[];
  /** The column headings, for the copy that carries them. */
  headsOf?: (grid: string) => string[];
  read: (row: TRow, field: TField) => string;
  canEdit: (row: TRow) => boolean;
  /**
   * A block of cells, written together.
   *
   * `how` is only the word for it — "paste" or "clear" — so the caller can say
   * which happened without this file deciding what either means.
   */
  write: (edits: GridEdit<TRow, TField>[], how: "paste" | "clear") => void;
  /**
   * Open the editor on one cell. `seed` is the character that started it, or
   * null to keep what is there — F2 and Enter keep, a printable key replaces.
   */
  openEditor: (row: TRow, field: TField, seed: string | null) => void;
  /** True while a cell is already open, so the keys belong to that box. */
  editing: boolean;
  /** Told what was copied, so the screen can say so. */
  onCopied?: (rows: number, columns: number) => void;
  /** Told when a clear found nothing to do. */
  onNothingToClear?: () => void;
  /**
   * Told when a pasted block did not fit — rows past the end of the page,
   * columns past the last one, or values landing on a column with no field.
   *
   * Reported because a paste of sixty rows onto a page of fifty looks exactly
   * like a paste that worked.
   */
  onClipped?: (clipped: { rows: number; columns: number; unwritable: number }) => void;
};

const TAB = "\t";
const NEWLINE = "\n";

export function useGridRange<TRow, TField>(options: GridRangeOptions<TRow, TField>) {
  const [range, setRange] = useState<GridRange | null>(null);
  /*
   * Whether a drag is under way, twice.
   *
   * The ref is what the handlers read, because they are attached once and would
   * otherwise close over the value as it was when they were made. The state is
   * what re-renders the table, so the text selection can be turned off for the
   * length of the drag — without that, dragging highlights the rows blue on top
   * of the rectangle and neither can be read.
   */
  const dragging = useRef(false);
  const [dragSelecting, setDragSelecting] = useState(false);

  // A drag ends wherever the mouse is let go, including outside the table.
  useEffect(() => {
    const stop = () => { dragging.current = false; setDragSelecting(false); };
    window.addEventListener("mouseup", stop);
    return () => window.removeEventListener("mouseup", stop);
  }, []);

  const inRange = (grid: string, row: number, column: number) =>
    !!range && range.grid === grid
    && row >= Math.min(range.r1, range.r2) && row <= Math.max(range.r1, range.r2)
    && column >= Math.min(range.c1, range.c2) && column <= Math.max(range.c1, range.c2);

  /**
   * What a cell needs to take part, or nothing when it has no field.
   *
   * Spread onto the cell the caller already built, so a screen keeps its own
   * colours, editors and titles and gains the rectangle.
   */
  function cellProps(grid: string, row: number, column: number, hasField: boolean) {
    if (!hasField) return {};
    return {
      sel: inRange(grid, row, column),
      active: range?.grid === grid && range.r2 === row && range.c2 === column,
      onDown: (event: ReactMouseEvent<HTMLTableCellElement>) => {
        // Shift extends from where the rectangle started rather than beginning
        // a new one, the way a spreadsheet does it.
        if (event.shiftKey && range?.grid === grid) {
          event.preventDefault();
          setRange({ ...range, r2: row, c2: column });
          return;
        }
        dragging.current = true;
        setDragSelecting(true);
        setRange({ grid, r1: row, c1: column, r2: row, c2: column });
      },
      onEnter: () => {
        if (!dragging.current || range?.grid !== grid) return;
        setRange({ ...range, r2: row, c2: column });
      },
    };
  }

  /**
   * The rectangle as rows and fields rather than coordinates.
   *
   * Clamped to what is actually drawn: a page can shrink under a selection —
   * a filter narrows, a save removes a row — and reading past the end would
   * copy undefined into somebody's clipboard.
   */
  function resolve(): { cells: GridCell<TRow, TField>[][]; heads: string[] } {
    if (!range) return { cells: [], heads: [] };
    const rows = options.rowsOf(range.grid);
    const fields = options.fieldsOf(range.grid);
    const labels = options.headsOf?.(range.grid) ?? [];

    const rowFrom = Math.min(range.r1, range.r2);
    const rowTo = Math.min(Math.max(range.r1, range.r2), rows.length - 1);
    const colFrom = Math.min(range.c1, range.c2);
    const colTo = Math.min(Math.max(range.c1, range.c2), fields.length - 1);

    const cells: GridCell<TRow, TField>[][] = [];
    for (let r = rowFrom; r <= rowTo; r++) {
      const line: GridCell<TRow, TField>[] = [];
      for (let c = colFrom; c <= colTo; c++) {
        const field = fields[c];
        if (field === undefined) continue;
        line.push({ row: rows[r], field });
      }
      if (line.length) cells.push(line);
    }

    const heads: string[] = [];
    for (let c = colFrom; c <= colTo; c++) {
      if (fields[c] !== undefined) heads.push(labels[c] ?? "");
    }
    return { cells, heads };
  }

  /*
   * The keys, over a selected rectangle and outside any editor.
   *
   * Arrows move it, F2 and Enter open the anchor, a printable key replaces it,
   * Delete and Backspace empty the block. It stands aside for anything that
   * takes typing, so none of it can fire while a cell is already open — there
   * Enter means "save and move down".
   */
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const el = event.target as HTMLElement | null;
      const tag = el?.tagName;
      if (tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || tag === "BUTTON"
          || tag === "A" || el?.isContentEditable) return;

      if (range && !options.editing) {
        const next = gridArrowTarget(
          event,
          { row: range.r2, column: range.c2 },
          options.rowsOf(range.grid).length,
          options.fieldsOf(range.grid).map((field) => (field === undefined ? undefined : String(field))),
        );
        if (next) {
          event.preventDefault();
          setRange(event.shiftKey
            ? { ...range, r2: next.row, c2: next.column }
            : { grid: range.grid, r1: next.row, c1: next.column, r2: next.row, c2: next.column });
          return;
        }
      }

      const intent = gridEditIntent(event);
      if (!intent || options.editing) return;

      // Resolved at the moment the key lands, not during the render that
      // attached this. The workspace fills its row lists while it draws, after
      // this hook is called, so a value worked out up here would be a render
      // behind — and one page-turn later, a rectangle over rows that had gone.
      const selection = resolve();
      const anchor = selection.cells[0]?.[0];
      if (!anchor) return;

      if (intent.mode === "clear") {
        event.preventDefault();
        // Cells already empty are left out before anything is written, so the
        // count the caller reports is work that actually happened.
        const cleared = selection.cells.flatMap((line) =>
          line
            .filter(({ row, field }) => options.canEdit(row) && options.read(row, field).length > 0)
            .map(({ row, field }) => ({ row, field, value: "" })));
        if (cleared.length) options.write(cleared, "clear");
        else options.onNothingToClear?.();
        return;
      }

      if (!options.canEdit(anchor.row)) return;
      event.preventDefault();
      options.openEditor(anchor.row, anchor.field, intent.mode === "replace" ? intent.value : null);
    };

    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  /*
   * Copy and paste.
   *
   * Copy reads the rectangle that was dragged — that is what was selected, and
   * what a person expects on their clipboard. Paste spreads from the corner it
   * starts at instead, because that is what a spreadsheet does and what the
   * clipboard's own shape asks for.
   *
   * Tab-separated, which is what a spreadsheet reads and writes, so a block
   * copied here opens in Excel as columns and a block copied from Excel lands
   * in the right cells. The browser's own events are used rather than the
   * clipboard permissions API — they carry the data already and never prompt.
   *
   * Both are handed back to the browser while a cell is open: inside an input,
   * Ctrl+C means the text in that box.
   */
  useEffect(() => {
    if (options.editing) return;

    const typing = (target: EventTarget | null) => {
      const el = target as HTMLElement | null;
      const tag = el?.tagName;
      return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el?.isContentEditable;
    };

    const onCopy = (event: ClipboardEvent) => {
      if (typing(event.target)) return;
      const selection = resolve();
      if (!selection.cells.length) return;
      event.preventDefault();
      // No headings: this is the copy that gets pasted back into the grid, and
      // a heading row would be written in as data.
      const text = selection.cells
        .map((line) => line.map(({ row, field }) => options.read(row, field)).join(TAB))
        .join(NEWLINE);
      event.clipboardData?.setData("text/plain", text);
      options.onCopied?.(selection.cells.length, selection.cells[0].length);
    };

    const onPaste = (event: ClipboardEvent) => {
      if (typing(event.target)) return;
      if (!range) return;
      const text = event.clipboardData?.getData("text/plain") ?? "";
      if (!text) return;

      const rows = options.rowsOf(range.grid);
      const fields = options.fieldsOf(range.grid);
      if (rows.length === 0 || fields.length === 0) return;
      event.preventDefault();

      /*
       * Spread from the corner the selection starts at, the way Excel does.
       *
       * Not clamped to the rectangle that was dragged. Nobody selects the exact
       * shape of what is on their clipboard first — they click the cell it
       * should start at and paste, and five columns of it arrive. Clamped, four
       * of those five were dropped without a word.
       */
      const plan = planPaste(
        readClipboardGrid(text),
        { row: Math.min(range.r1, range.r2), column: Math.min(range.c1, range.c2) },
        { row: Math.max(range.r1, range.r2), column: Math.max(range.c1, range.c2) },
        { rows: rows.length, columns: fields.length },
        (column) => fields[column],
      );

      const edits: GridEdit<TRow, TField>[] = plan.cells
        .filter((cell) => options.canEdit(rows[cell.row]))
        .map((cell) => ({ row: rows[cell.row], field: cell.field, value: cell.value }));

      if (plan.rowsClipped || plan.columnsClipped || plan.cellsUnwritable) {
        options.onClipped?.({
          rows: plan.rowsClipped,
          columns: plan.columnsClipped,
          unwritable: plan.cellsUnwritable,
        });
      }
      if (edits.length) options.write(edits, "paste");
    };

    window.addEventListener("copy", onCopy);
    window.addEventListener("paste", onPaste);
    return () => {
      window.removeEventListener("copy", onCopy);
      window.removeEventListener("paste", onPaste);
    };
  });

  return {
    range,
    setRange,
    /** True for the length of a drag, for the grid's `noSelect`. */
    dragSelecting,
    cellProps,
    /**
     * The rectangle as rows and fields, clamped to what is drawn.
     *
     * A function rather than a value, because a screen that fills its row lists
     * while it draws has not filled them when this hook is called.
     */
    resolve,
    clear: () => setRange(null),
  };
}
