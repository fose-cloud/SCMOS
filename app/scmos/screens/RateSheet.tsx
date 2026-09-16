"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../api";
import { DataTable, type TableModel } from "../DataTable";
import { GridMenu } from "../GridMenu";
import { exportRateSheet } from "../excel";
import { FilterPickMany } from "../FilterPickMany";
import { StatGlyph } from "../StatCard";
import { chosenIn } from "../filterChoices";
import { editHistoryShortcut } from "../editHistory";
import { gridTabTarget } from "../gridEditKey";
import { describeFill, dgFills, priceValue, type CellEdit } from "../dgSurcharge";
import { draftGroups, growDrafts, isDraft, sheetToday } from "../rateSheetDrafts";
import { writeClipboardTable } from "../pasteBlock";
import { NO_DATE, monthLabel, partsOf } from "../period";
import { SHEET_COLUMNS, cellText, missingForLane, readCell, editRateDraft, type SheetColumn, type SheetRow } from "../rateSheetColumns";
import { css } from "../theme";
import { useGridRange } from "../useGridRange";
import { cell, nowHM, type Cell } from "../util";
import { ImportWorkbook } from "./ImportWorkbook";

/**
 * The rate register, laid out as the workbook lays it out, and typed into.
 *
 * The team keeps `Rate Inquiry.xlsx` because it is the shape they think in: one
 * row per journey, the request's date and customer repeated down its rows,
 * twenty-nine price columns across. The register normalises that into requests
 * with lanes under them, which is right for storing it and wrong for reading
 * it. This puts it back.
 *
 * Worked the way My Job is, and by now out of the same parts rather than out of
 * copies of them: the header, the zoom, the full screen and the paging from
 * `DataTable`; the dragged rectangle with its copy, paste and Delete from
 * `useGridRange`; the any-of pickers from `FilterPickMany`. The page is held
 * still and the grid scrolls inside it, which is the only way a table this wide
 * can be read — scroll the page sideways and the headings go with it.
 *
 * Every cell saves on its own. A row here spans a lane and the request above
 * it, and two people editing two lanes of one request in the same minute is
 * ordinary — a whole-row save would have one of them overwrite the other's
 * customer with a copy of the value they started from.
 */

type Page = { rows: SheetRow[]; total: number; page: number; per: number };

/** What the pickers may offer — the whole register's values, from the API. */
type Choices = {
  customers: string[];
  requestors: string[];
  carriers: string[];
  counties: string[];
  /** Quotation numbers, highest first. Strings, because every picker takes strings. */
  numbers: string[];
  years: string[];
  months: string[];
  dates: string[];
  undated: number;
};

/** Which cell is open, and what is in the box while it is. */
type Editing = { laneId: number; column: number; value: string };

/**
 * The bar's state, in the shape the query string wants.
 *
 * Every one of them is an any-of value: several customers, several carriers,
 * several days, all at once. "ALL" is the word the pickers use for nothing
 * chosen, so it is what they are seeded with.
 */
type Filters = {
  customer: string;
  requestor: string;
  carrier: string;
  county: string;
  /** The quotation's No. — one number is one request, and its lanes are its rows. */
  number: string;
  year: string;
  month: string;
  day: string;
};

const NO_FILTERS: Filters = {
  customer: "ALL", requestor: "ALL", carrier: "ALL", county: "ALL",
  number: "ALL", year: "ALL", month: "ALL", day: "ALL",
};

/**
 * One cell an action displaced, with both ends of the move.
 *
 * My Job keeps only the "before" and works the inverse out by reading the row
 * again, which it can do because the whole register is sitting in the browser.
 * This sheet holds fifty rows of three thousand and re-reads them from the API
 * after every write, so by the time somebody presses Undo the row may be on
 * another page, behind a filter, or simply not loaded. Keeping both ends at the
 * moment of the edit means undo and redo need nothing on screen to work from.
 */
type SheetEdit = { laneId: number; field: string; before: string; after: string };

/** Everything one action changed, so a pasted block comes back in one press. */
type SheetStep = { label: string; at: string; edits: SheetEdit[] };

/**
 * How many presses of undo the sheet remembers.
 *
 * The same twenty My Job keeps: deep enough to walk back a bad paste, short
 * enough that nobody undoes their way into last Tuesday.
 */
const HISTORY = 20;

const PER = 50;

/**
 * Columns drawn before the workbook's own — just the tick box.
 *
 * The rectangle addresses cells by drawn position, so everything that hands it
 * a column index adds this. Named rather than written as `+ 1`, because the day
 * a second leading column appears the `1`s are unfindable.
 */
const LEAD = 1;

/**
 * The laneId a row not yet on the server carries.
 *
 * Negative so it can never collide with a real one, and so anything that
 * reaches the API with it is obviously wrong rather than quietly editing lane
 * zero. Each new row takes the next one down, so several can sit on the
 * sheet at once and still be told apart by the grid, the editor and a paste.
 */
let nextDraftId = -1;

/** A blank sheet row, for typing into before it exists anywhere else. */
const blankDraft = (): SheetRow => ({
  laneId: nextDraftId--, inquiryId: 0, date: "", no: 0, requestor: "", customer: "",
  fuelBand: "", fromPlace: "", toPlace: "", county: "", carriers: "",
  fcl: true, lcl: false, domestic: false, remark: "", prices: {},
});

/** What one press may remove. The API refuses more; this is so the screen says so first. */
const MAX_DELETE = 100;

/** The heading cell, shared by the tick box and the workbook's own columns. */
/*
 * The heading row.
 *
 * Centred, including over the price columns, which the figures beneath them
 * are not — a column of baht reads right-aligned so the digits line up, but
 * a heading right-aligned above it sits away from the column it names and
 * next to the one before. Forty-one narrow columns made that plain.
 */
const HEAD = "padding:7px 9px;background:#F4F7FA;font-size:10.5px;letter-spacing:.04em;"
  + "text-transform:uppercase;color:#465A6E;border-bottom:1px solid #D8E0E8;"
  + "white-space:nowrap;user-select:none;position:sticky;top:0;z-index:1;"
  + "text-align:center;";

/** What one request may fetch when the whole filtered set is wanted, for the export. */
const BULK = 500;

/**
 * What a column writes, spelled as the API spells it.
 *
 * A price is "price:20F", named for the vehicle. The rectangle carries these
 * rather than column indexes, so a block copied from one place and pasted into
 * another lands in the fields it names.
 */
const fieldOf = (column: SheetColumn) =>
  column.kind === "price" ? `price:${column.vehicle}` : column.field!;

/** The column a field name belongs to, for reading a cell back out. */
function columnFor(field: string): SheetColumn {
  return SHEET_COLUMNS.find((column) => fieldOf(column) === field) ?? SHEET_COLUMNS[0];
}

export function RateSheet({ canEdit, needsSecondFactor = false, onToast }: {
  /** Whether this account may change a rate. Shown, not guessed at: the API decides. */
  canEdit: boolean;
  /**
   * Read-only because of how this session signed in, not because of the role.
   *
   * Worth telling apart. "You do not have permission" sends somebody to their
   * manager; "sign in again with your second factor" is something they can do
   * in a minute.
   */
  needsSecondFactor?: boolean;
  onToast: (message: string) => void;
}) {
  const [page, setPage] = useState<Page | null>(null);
  const [choices, setChoices] = useState<Choices | null>(null);
  const [at, setAt] = useState(1);
  const [search, setSearch] = useState("");
  const [filters, setFilters] = useState<Filters>(NO_FILTERS);
  /** Which panel is open above the grid, or none. */
  const [panel, setPanel] = useState<"none" | "import" | "columns">("none");

  /*
   * Which columns the copy button takes.
   *
   * Empty means "the ones with a price on them", worked out from the rows in
   * front of you each time — see chosenColumns. That is the default because the
   * sheet prices 29 vehicles and a quotation uses four: measured over 1,169
   * quotations in the register, 957 of them price five vehicles or fewer, and
   * only 13 price more than ten. Copying all 41 columns to paste two of them
   * into a mail is the complaint this answers.
   *
   * A non-empty set is a deliberate choice and is obeyed exactly, including
   * columns that are empty — somebody sending a customer a blank 40' column to
   * be filled in is asking for it on purpose.
   */
  const [copyCols, setCopyCols] = useState<Set<string>>(new Set());
  const [editing, setEditing] = useState<Editing | null>(null);
  /*
   * Rows being typed into that the server has not got yet.
   *
   * My Job inserts straight into its grid: a blank row appears at the top,
   * pinned there, with the first cell already open. This does the same, and the
   * only reason it is not the same code is that My Job holds its whole register
   * in the browser and can persist a blank row, while a rate lane is refused
   * without a customer, an origin and a destination. So the rows live here
   * until each has those three, and are created together when asked.
   *
   * Several at once, because a quotation is rarely one lane: the workbook
   * files three or four routes under one number, and the person keying them
   * has them as one Excel block. See rateSheetDrafts for what a paste does
   * when it runs past the last of them.
   */
  const [drafts, setDrafts] = useState<SheetRow[]>([]);
  const draft = drafts.length > 0;
  const [saving, setSaving] = useState(false);
  const creatingDraft = useRef(false);
  const [busy, setBusy] = useState(false);
  /**
   * Rows ticked for a bulk action, by lane id.
   *
   * Kept across paging on purpose — ticking a row, filtering to find the next
   * one and losing the first is the behaviour that makes a select-and-act bar
   * not worth using. What it costs is that the bar can name more rows than are
   * on screen, so it says how many and offers to clear them.
   */
  const [picked, setPicked] = useState<Set<number>>(new Set());
  /**
   * What the last few actions overwrote.
   *
   * Cell edits only. Removing a row is deliberately absent, for the same reason
   * My Job will not undo a created one: there is no history table behind this
   * register, so putting a deleted lane back would mean re-creating it with a
   * new id and a new place in the numbering — a different row that merely looks
   * like the old one. A deletion is recoverable from the audit trail, by a
   * person who can see what it held and decide.
   */
  const [undos, setUndos] = useState<SheetStep[]>([]);
  const [redos, setRedos] = useState<SheetStep[]>([]);
  // A stable array while the page is unchanged. `page?.rows ?? []` builds a
  // fresh [] on every render when there is no page, which makes anything
  // memoised against it recompute every time — including the copy's column
  // list, which walks every row of every price column.
  // The draft sits above the page, the way My Job's new row sits above the
  // register — it is what somebody is working on, and a row that sorted itself
  // into 3,005 others the moment it was created would be lost.
  const rows = useMemo(() => page?.rows ?? [], [page]);
  const shown = useMemo(() => (drafts.length ? [...drafts, ...rows] : rows), [drafts, rows]);

  /*
   * The rectangle, and everything a spreadsheet does with one.
   *
   * The same hook My Job uses. What differs is only what a row is and how a
   * cell is written — here a block goes to the API in one request, because the
   * register is the only copy and the browser holds none of it.
   */
  const grid = useGridRange<SheetRow, string>({
    // The rows as drawn, draft included. The cells are rendered from `shown`
    // and addressed by their position in it, so a range reading `rows` instead
    // would move every index down by one the moment a draft existed — a paste
    // would land a row above where it was dropped.
    rowsOf: () => shown,
    // The tick box leads with no field of its own, which is what keeps a
    // dragged rectangle, a paste and a Delete off it.
    fieldsOf: () => [undefined, ...SHEET_COLUMNS.map((column) => column.kind === "tick" ? undefined : fieldOf(column))],
    headsOf: () => ["", ...SHEET_COLUMNS.map((column) => column.head)],
    // What Ctrl+C and the right-click copy put on the clipboard: a price
    // grouped as the screen shows it, "2,400" — Excel reads that back as a
    // number, and a paste back into this sheet strips the comma (editRateDraft,
    // priceValue). Asked for on 16 Sep 2026: the pasted figures had no comma.
    read: (row, field) => cellText(row, columnFor(field), "x", "grouped"),
    canEdit: () => canEdit && !saving,
    write: (edits, how) => void writeBlock(edits, how),
    openEditor: (row, field, seed) => {
      const at = SHEET_COLUMNS.findIndex((column) => fieldOf(column) === field);
      if (at < 0) return;
      setEditing({
        laneId: row.laneId,
        column: at,
        value: seed ?? String(readCell(row, SHEET_COLUMNS[at]) ?? ""),
      });
    },
    editing: editing !== null,
    // Tab steps right and Shift+Tab left, the way every spreadsheet does —
    // asked for on 16 Sep 2026; it had been the other way round.
    tabDirection: "right",
    onCopied: (lines, columns) => onToast(`คัดลอกแล้ว ${lines} แถว · ${columns} คอลัมน์`),
    onNothingToClear: () => onToast("ช่องที่เลือกว่างอยู่แล้ว"),
    onClipboardBlocked: () => onToast("เบราว์เซอร์ไม่ให้อ่านคลิปบอร์ด — ใช้ Ctrl+V แทน"),
    onClipped: ({ rows, columns, unwritable }) => onToast(`วางได้เฉพาะหน้าปัจจุบัน · เกิน ${rows} แถว / ${columns} คอลัมน์ · ข้าม ${unwritable} ช่อง`),
  });

  /** Everything the bar is narrowing by, as the API's query string. */
  const query = useCallback((extra: Record<string, string>) => new URLSearchParams({
    q: search.trim(),
    customer: filters.customer, requestor: filters.requestor,
    carrier: filters.carrier, county: filters.county, number: filters.number,
    year: filters.year, month: filters.month, day: filters.day,
    ...extra,
  }), [search, filters]);

  const load = useCallback(async () => {
    const response = await apiFetch(
      `/api/rate-inquiries/sheet?${query({ page: String(at), per: String(PER) })}`,
      { headers: { accept: "application/json" } });
    if (!response.ok) { onToast("อ่านตารางอัตราไม่สำเร็จ · HTTP " + response.status); return; }
    setPage(await response.json() as Page);
  }, [at, query, onToast]);

  // Fetching on mount and whenever the page, the search or the bar moves.
  // Every setState inside is after an await, so it runs in a microtask rather
  // than while this body does — the rule cannot see past the await and reads it
  // as a synchronous set. The same idiom, and the same exemption, as the other
  // eleven screens that fetch what they show.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  /*
   * The pickers' lists, asked for once.
   *
   * Separately from the rows and not narrowed by them. A list built out of what
   * is already showing makes the second picker useless: tick a customer and the
   * carrier list becomes that customer's carriers, with no way back out except
   * by clearing the tick you just made.
   */
  const loadChoices = useCallback(async () => {
    const response = await apiFetch("/api/rate-inquiries/sheet/choices",
      { headers: { accept: "application/json" } });
    if (!response.ok) return;
    setChoices(await response.json() as Choices);
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void loadChoices(); }, [loadChoices]);

  /**
   * What every write ends with: the rows again, and the pickers' lists again.
   *
   * A new quotation is a new No.; a new customer typed into a draft row is a
   * new customer; a deleted lane may take the last row of a carrier with it.
   * The lists were re-read after an import and a delete and not after an
   * insert or an edit, so No. 128 was on the grid and not in the picker
   * until the screen was reopened. One place now, so the next write cannot
   * forget either.
   */
  const reload = useCallback(async () => {
    await load();
    void loadChoices();
  }, [load, loadChoices]);

  /** A block of cells in one request, so a paste is one round trip. */
  /* ---- undo -------------------------------------------------------------- */

  /*
   * Ctrl+Z and Ctrl+Shift+Z, resolved by the same module My Job uses.
   *
   * Not while a cell editor is open: inside a text box Ctrl+Z belongs to the
   * box, and stealing it would undo somebody's last keystroke by throwing away
   * their last saved cell instead.
   */
  useEffect(() => {
    if (!canEdit) return;
    const onKey = (event: KeyboardEvent) => {
      if (editing !== null) return;
      const target = event.target as HTMLElement | null;
      if (target && /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)) return;
      const command = editHistoryShortcut(event);
      if (!command) return;
      event.preventDefault();
      void (command === "undo" ? undo() : redo());
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
    // `undo`/`redo` are redeclared each render and close over the current
    // stacks; listing them would rebind the listener on every keystroke.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [canEdit, editing, undos, redos, saving]);

  /**
   * Files what an action is about to overwrite.
   *
   * Called with everything one action touched, so a pasted block or a cleared
   * rectangle comes back in a single press rather than forty.
   */
  function remember(label: string, edits: SheetEdit[]) {
    const moved = edits.filter((one) => one.before !== one.after);
    if (moved.length === 0) return;
    setUndos((was) => was.slice(-(HISTORY - 1)).concat([{ label, at: nowHM(), edits: moved }]));
    // Like a spreadsheet: a new edit makes the forward path invalid.
    setRedos([]);
  }

  /**
   * Writes one saved step back through the ordinary cell endpoint.
   *
   * Undo is an edit, not a rollback. It goes through the same door as any other
   * change — the same permission check, the same validation, and its own line in
   * the audit trail naming who pressed it. A quiet restore that left no trace
   * would be the one kind of change nobody could account for later.
   */
  async function move(step: SheetStep, direction: "before" | "after", label: string) {
    setSaving(true);
    try {
      const response = await apiFetch("/api/rate-inquiries/sheet/cells", {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({
          edits: step.edits.map((one) => ({
            laneId: one.laneId, field: one.field, value: one[direction],
          })),
        }),
      });
      const reply = await response.json().catch(() => ({})) as
        { saved?: number; refused?: string[]; error?: string };
      if (!response.ok) { onToast(reply.error ?? `ย้อนกลับไม่สำเร็จ (${response.status})`); return false; }

      const refused = reply.refused ?? [];
      onToast(`${label} ${step.label} · ${reply.saved ?? 0} ช่อง`
        // A row deleted since the edit cannot take its old value back, and the
        // API says so per cell rather than failing the whole step.
        + (refused.length ? ` · ข้าม ${refused.length} — ${refused[0]}` : ""));
      setEditing(null);
      await reload();
      return true;
    } finally { setSaving(false); }
  }

  async function undo() {
    if (undos.length === 0) { onToast("ไม่มีการแก้ไขให้ย้อนกลับ"); return; }
    const step = undos[undos.length - 1];
    if (!await move(step, "before", "ย้อนกลับแล้ว ↶")) return;
    setUndos((was) => was.slice(0, -1));
    setRedos((was) => was.slice(-(HISTORY - 1)).concat([step]));
  }

  async function redo() {
    if (redos.length === 0) { onToast("ไม่มีข้อมูลให้ไปข้างหน้า"); return; }
    const step = redos[redos.length - 1];
    if (!await move(step, "after", "ไปข้างหน้าแล้ว ↷")) return;
    setRedos((was) => was.slice(0, -1));
    setUndos((was) => was.slice(-(HISTORY - 1)).concat([step]));
  }

  /**
   * The DG cells a set of NON-DG edits fill in — see dgSurcharge.ts.
   *
   * Read against the rows as they stand (a draft's own copy for a draft), so
   * a DG figure somebody typed is recognised and left alone.
   */
  function fillsFor(edits: CellEdit[], among: SheetRow[]): CellEdit[] {
    const byLane = new Map(among.map((row) => [row.laneId, row]));
    return dgFills(edits, (laneId, vehicle) =>
      priceValue(byLane.get(laneId)?.prices[vehicle] ?? null));
  }

  async function writeBlock(
    edits: { row: SheetRow; field: string; value: string }[],
    how: "paste" | "clear",
  ) {
    // Draft edits stay local; only persisted lane IDs reach the cell endpoint.
    if (!canEdit || saving) return;
    // A paste that starts in the new rows and runs on past them makes more
    // new rows rather than rewriting the saved ones underneath.
    let held = drafts;
    let grown = 0;
    if (how === "paste") ({ edits, drafts: held, grown } = growDrafts(edits, drafts, rows, blankDraft));
    const draftEdits = edits.filter((one) => isDraft(one.row));
    if (draftEdits.length) {
      try {
        const draftFills = fillsFor(draftEdits.map((one) => ({ laneId: one.row.laneId, field: one.field, value: one.value })), held);
        const next = held.map((row) => draftEdits
          .filter((edit) => edit.row.laneId === row.laneId)
          .map((edit) => ({ field: edit.field, value: edit.value }))
          .concat(draftFills.filter((fill) => fill.laneId === row.laneId))
          .reduce((so, edit) => editRateDraft(so, edit.field, edit.value), row));
        setDrafts(next);
        const touched = new Set(draftEdits.map((one) => one.row.laneId)).size;
        onToast(how === "clear"
          ? `ล้างช่องในแถวใหม่แล้ว ${draftEdits.length} ช่อง`
          : `วางข้อมูลในแถวใหม่แล้ว ${touched} แถว`
            + (grown ? ` · เพิ่มแถวใหม่อีก ${grown} แถวให้พอดีกับที่วาง` : "")
            + " — กดบันทึกแถวใหม่");
      } catch (error) { onToast(error instanceof Error ? error.message : "วางข้อมูลไม่สำเร็จ"); return; }
    }
    edits = edits.filter((one) => !isDraft(one.row));
    if (edits.length === 0) return;

    // A pasted NON-DG price fills its DG cell the way a typed one does; a
    // pasted DG price is what it says. The fills ride in the same write.
    const savedRows = edits.map((one) => one.row);
    const fills = fillsFor(edits.map((one) => ({ laneId: one.row.laneId, field: one.field, value: one.value })), savedRows);
    const cells: CellEdit[] = edits.map((one) => ({ laneId: one.row.laneId, field: one.field, value: one.value })).concat(fills);
    const rowOf = new Map(savedRows.map((row) => [row.laneId, row]));

    // Filed before the write, because afterwards the page is re-read and the
    // old values are gone from the browser as well as from the register.
    remember(how === "clear" ? "ล้างช่อง" : "วางข้อมูล", cells.map((one) => ({
      laneId: one.laneId,
      field: one.field,
      before: String(readCell(rowOf.get(one.laneId)!, columnFor(one.field)) ?? ""),
      after: one.value,
    })));

    setSaving(true);
    try {
      const reply = await writeCells(cells);
      if (!reply.ok) { onToast(reply.error ?? "บันทึกไม่สำเร็จ"); return; }

      const doing = how === "clear" ? "ล้าง" : "วาง";
      const refused = reply.refused ?? [];
      onToast(`${doing}แล้ว ${reply.saved ?? 0} ช่อง`
        + (fills.length ? ` · เติม DG ให้ ${fills.length} ช่อง` : "")
        + (refused.length ? ` · ข้าม ${refused.length} — ${refused[0]}` : ""));
      await reload();
    } finally { setSaving(false); }
  }

  /** A block of cells in one request. The API answers per cell, so a refused one is named. */
  async function writeCells(cells: CellEdit[]): Promise<{ ok: boolean; saved?: number; refused?: string[]; error?: string }> {
    const response = await apiFetch("/api/rate-inquiries/sheet/cells", {
      method: "POST",
      headers: { "content-type": "application/json", accept: "application/json" },
      body: JSON.stringify({ edits: cells }),
    });
    const reply = await response.json().catch(() => ({})) as
      { saved?: number; refused?: string[]; error?: string };
    return { ok: response.ok, ...reply, error: reply.error ?? (response.ok ? undefined : `บันทึกไม่สำเร็จ (${response.status})`) };
  }

  /**
   * Writes one cell and puts the answer on the row.
   *
   * The API is the authority on whether a value is allowed — a price that is
   * not a number, a date that is not a date, an account that may not change a
   * rate — so a refusal is shown as it comes back and the old value stays.
   */
  /**
   * A cell of a draft row, which is not saved one cell at a time.
   *
   * Keep fields and prices locally until the explicit save action. The create
   * endpoint validates and commits the complete request and its prices together.
   */
  async function saveDraft(row: SheetRow, column: SheetColumn, value: string) {
    if (!canEdit || saving) return;
    try {
      const field = fieldOf(column);
      const fills = fillsFor([{ laneId: row.laneId, field, value }], [row]);
      const next = fills.reduce((so, fill) => editRateDraft(so, fill.field, fill.value), editRateDraft(row, field, value));
      setDrafts((was) => was.map((one) => one.laneId === row.laneId ? next : one));
      if (fills.length) onToast(`เติม ${fills.map(describeFill).join(" · ")} ให้แล้ว — แก้ตัวเลขได้`);
    } catch (error) { onToast(error instanceof Error ? error.message : "ข้อมูลไม่ถูกต้อง"); }
  }

  /** Drops one new row without touching the others. */
  function dropDraft(laneId: number) {
    setDrafts((was) => was.filter((one) => one.laneId !== laneId));
    if (editing?.laneId === laneId) setEditing(null);
  }

  /**
   * Files every new row.
   *
   * Grouped by customer and date into the inquiries the register numbers —
   * see draftGroups — and sent one group at a time, so a refusal names the
   * group and leaves it, and everything after it, on the sheet with what was
   * typed. The groups that went through are gone from the sheet and back on
   * the page with their numbers.
   */
  async function createDrafts() {
    if (drafts.length === 0 || !canEdit || saving || creatingDraft.current) return;
    let next = drafts;
    try {
      if (editing && isDraft(editing)) {
        const value = editing.value;
        const column = editing.column;
        next = next.map((row) => row.laneId === editing.laneId
          ? editRateDraft(row, fieldOf(SHEET_COLUMNS[column]), value) : row);
      }
    } catch (error) { onToast(error instanceof Error ? error.message : "ข้อมูลไม่ถูกต้อง"); return; }
    setDrafts(next);
    setEditing(null);
    for (let index = 0; index < next.length; index++) {
      const missing = missingForLane(next[index]);
      if (missing.length) {
        onToast((next.length > 1 ? `แถวใหม่ที่ ${index + 1}: ` : "") + "กรุณากรอก " + missing.join(", "));
        return;
      }
    }
    creatingDraft.current = true;

    setSaving(true);
    try {
      const groups = draftGroups(next, sheetToday());
      const filed: number[] = [];
      let lanes = 0;
      for (const group of groups) {
        const response = await apiFetch("/api/rate-inquiries", {
          method: "POST",
          headers: { "content-type": "application/json", accept: "application/json" },
          body: JSON.stringify({
            inquiredOn: group.inquiredOn,
            customer: group.customer,
            lanes: group.rows.map((row) => ({
              fromPlace: String(row.fromPlace).trim(),
              toPlace: String(row.toPlace).trim(),
              county: String(row.county).trim(),
              carriers: String(row.carriers).trim(),
              remark: String(row.remark).trim(),
              fcl: row.fcl, lcl: row.lcl, domestic: row.domestic,
              prices: row.prices,
            })),
          }),
        });
        const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
        if (!response.ok) {
          // The rows stay on screen with what was typed in them. Clearing them
          // here would throw the work away over a refusal the operator can fix.
          const kept = next.filter((row) => !filed.includes(row.laneId));
          setDrafts(kept);
          onToast((groups.length > 1 ? `${group.customer}: ` : "")
            + (reply.error ?? `เพิ่มแถวไม่สำเร็จ (${response.status})`)
            + (filed.length ? ` · บันทึกไปแล้ว ${filed.length} แถวก่อนหน้า` : ""));
          if (filed.length) { setAt(1); await reload(); }
          return;
        }
        filed.push(...group.rows.map((row) => row.laneId));
        lanes += group.rows.length;
      }
      setDrafts([]);
      setEditing(null);
      onToast(groups.length === 1 && lanes === 1
        ? "เพิ่มแถวแล้ว — ใส่ราคาได้เลย"
        : `เพิ่ม ${lanes} แถวแล้ว · ${groups.length} ใบขอราคา — ใส่ราคาได้เลย`);
      setAt(1);
      await reload();
    } catch { onToast("ยังยืนยันการบันทึกไม่ได้ ข้อมูลแถวใหม่ยังอยู่ — ตรวจรายการก่อนลองบันทึกซ้ำ"); }
    finally { creatingDraft.current = false; setSaving(false); }
  }

  async function save(row: SheetRow, column: SheetColumn, value: string) {
    if (isDraft(row)) { await saveDraft(row, column, value); return; }
    const field = column.kind === "price" ? `price:${column.vehicle}` : column.field!;
    const before = readCell(row, column);
    if (value.trim() === String(before).trim()) return;

    // A NON-DG price carries its DG price with it — see dgSurcharge.ts. Both
    // cells go in one write, and one undo brings both back.
    const fills = fillsFor([{ laneId: row.laneId, field, value }], [row]);
    remember(`แก้ ${column.head}`,
      [{ laneId: row.laneId, field, before: String(before ?? ""), after: value }]
        .concat(fills.map((fill) => ({
          laneId: fill.laneId, field: fill.field,
          before: String(readCell(row, columnFor(fill.field)) ?? ""), after: fill.value,
        }))));

    setSaving(true);
    try {
      if (fills.length) {
        const reply = await writeCells([{ laneId: row.laneId, field, value }, ...fills]);
        onToast(reply.ok
          ? `บันทึกแล้ว · เติม ${fills.map(describeFill).join(" · ")} ให้แล้ว — แก้ตัวเลขได้`
          : reply.error ?? "บันทึกไม่สำเร็จ");
        if (reply.ok) await reload();
        return;
      }
      const response = await apiFetch(`/api/rate-inquiries/sheet/${row.laneId}`, {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ field, value }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
      // Re-read rather than patching in place: editing a request's own field
      // moves every lane under it, and only the server knows which those are.
      if (response.ok) await reload();
    } finally { setSaving(false); }
  }

  /* ---- selection ------------------------------------------------------- */

  const togglePick = (laneId: number) => setPicked((was) => {
    const next = new Set(was);
    if (next.has(laneId)) next.delete(laneId); else next.add(laneId);
    return next;
  });

  /** Ticks or clears every row of the page being looked at. */
  const togglePage = (allPicked: boolean) => setPicked((was) => {
    const next = new Set(was);
    rows.forEach((row) => { if (allPicked) next.delete(row.laneId); else next.add(row.laneId); });
    return next;
  });

  const allPagePicked = rows.length > 0 && rows.every((row) => picked.has(row.laneId));
  /**
   * The ticked rows this page is actually holding.
   *
   * Deleting works from these rather than from the ticks, because the row is
   * what the confirmation has to name — a lane id says nothing to the person
   * being asked whether to destroy it.
   */
  const onPage = rows.filter((row) => picked.has(row.laneId));
  const onPagePicked = onPage.length;

  /**
   * Removes the ticked rows, once somebody has read what they are.
   *
   * The confirmation names them rather than counting them. There is no history
   * table behind the rate book, so this is the last moment the figures exist —
   * and "ลบ 12 แถว?" is a question nobody can actually answer, while three
   * journeys and a customer is.
   */
  async function removePicked() {
    const chosen = onPage;
    if (chosen.length === 0) { onToast("แถวที่เลือกไม่ได้อยู่ในหน้านี้ · เปิดหน้าที่มีแถวนั้นก่อน"); return; }
    if (chosen.length > MAX_DELETE) { onToast(`ลบได้ครั้งละไม่เกิน ${MAX_DELETE} แถว`); return; }

    const sample = chosen.slice(0, 3)
      .map((row) => `${row.customer || "—"} · ${row.fromPlace || "—"} → ${row.toPlace || "—"}`)
      .join("\n");
    if (!window.confirm(
      `ลบ ${chosen.length} แถวออกจากตารางอัตรา?\n\n${sample}`
      + (chosen.length > 3 ? `\nและอีก ${chosen.length - 3} แถว` : "")
      + "\n\nราคาที่บันทึกไว้ในแถวนี้จะหายไปด้วย และกู้คืนไม่ได้",
    )) return;

    setBusy(true);
    try {
      const response = await apiFetch("/api/rate-inquiries/sheet/lanes", {
        method: "DELETE",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ laneIds: chosen.map((row) => row.laneId) }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `ลบไม่สำเร็จ (${response.status})`);
      if (!response.ok) return;

      // Only what actually went, so a row that was refused stays ticked and
      // visible rather than quietly disappearing from the selection.
      const gone = new Set(chosen.map((row) => row.laneId));
      setPicked((was) => new Set([...was].filter((id) => !gone.has(id))));
      setEditing(null);
      await reload();
    } finally { setBusy(false); }
  }

  /**
   * The whole filtered set, for the file.
   *
   * The grid holds fifty rows; an export that quietly held only those would be
   * the file somebody then negotiates from. So the pages are walked, five
   * hundred at a time, under the filters that are set — what is exported is
   * what the bar says is there.
   */
  async function exportAll() {
    if (busy) return;
    setBusy(true);
    try {
      const all: SheetRow[] = [];
      for (let at = 1; ; at++) {
        const response = await apiFetch(
          `/api/rate-inquiries/sheet?${query({ page: String(at), per: String(BULK) })}`,
          { headers: { accept: "application/json" } });
        if (!response.ok) { onToast("อ่านข้อมูลไม่สำเร็จ · HTTP " + response.status); return; }
        const body = await response.json() as Page;
        all.push(...body.rows);
        if (all.length >= body.total || body.rows.length === 0) break;
      }
      const scope = narrowed || search.trim() ? "filtered" : "all";
      onToast(`บันทึกไฟล์ ${exportRateSheet(all, scope)} · ${all.length.toLocaleString()} เส้นทาง`);
    } finally { setBusy(false); }
  }

  /**
   * The selected rectangle with its headings, for pasting into a mail.
   *
   * The grid's own Ctrl+C leaves the headings out on purpose — that copy gets
   * pasted back into the grid, where a heading row would be written in as data.
   * This is the other use, and it is a different thing rather than the same
   * thing with a flag.
   */
  /**
   * Copy the table with its column headings, for pasting into a mail.
   *
   * The whole page, never the selected rectangle. It used to copy the selection,
   * which was reasonable while a single click opened an editor and a rectangle
   * only existed if somebody had dragged for it. Editing moved to a double click
   * so that a click could select instead — and that quietly turned this button
   * into "copy this cell", which is how it was reported. My Job had the same
   * fault, for the same reason, and the note above its copyWithHeads says so.
   *
   * The rectangle still has Ctrl+C. This button has a name, and the name says
   * table.
   */
  /**
   * The columns the copy takes, in sheet order.
   *
   * With nothing chosen: every detail column, and only the vehicles priced
   * somewhere in the rows being copied. A vehicle column that is blank all the
   * way down carries nothing and costs a column of width in whatever it is
   * pasted into.
   */
  const chosenColumns = useMemo(() => {
    if (copyCols.size > 0) return SHEET_COLUMNS.filter((one) => copyCols.has(one.head));
    return SHEET_COLUMNS.filter((one) =>
      one.kind !== "price"
      || rows.some((row) => String(readCell(row, one) ?? "").trim() !== ""));
  }, [copyCols, rows]);

  async function copyWithHeads() {
    const heads = chosenColumns.map((column) => column.head);
    // Through cellText, so a tick box pastes as a box with a tick rather than
    // as the word "true" — asked for on 15 September 2026.
    const lines = rows.map((row) =>
      chosenColumns.map((column) => cellText(row, column, "box", "grouped")));

    if (!heads.length) { onToast("ยังไม่ได้เลือกคอลัมน์ — กด “เลือกคอลัมน์” ก่อน"); return; }

    if (!lines.length) { onToast("ไม่มีแถวให้คัดลอก"); return; }

    try {
      await writeClipboardTable(lines, heads);
      // The register is paged by the server, so only this page is in the
      // browser. Saying "50 rows" while the bar says 3,007 would read as the
      // whole thing.
      const missing = (page?.total ?? 0) > lines.length ? (page!.total - lines.length) : 0;
      onToast(`คัดลอกพร้อมหัวตารางแล้ว ${lines.length} แถว · ${heads.length} จาก ${SHEET_COLUMNS.length} คอลัมน์`
        + (copyCols.size === 0 ? " (เฉพาะรถที่มีราคา)" : " (ตามที่เลือกไว้)")
        + (missing ? ` · ยังเหลืออีก ${missing.toLocaleString()} แถวที่ยังไม่ได้โหลด — ใช้ Export Excel เพื่อเอาครบ` : ""));
    } catch {
      onToast("เบราว์เซอร์ไม่อนุญาตให้คัดลอก — ลองกดที่ตารางก่อนแล้วกดปุ่มอีกครั้ง");
    }
  }

  /** Narrowing anything sends you back to page one — page nine of eleven rows is nothing. */
  const narrow = (change: Partial<Filters>) => {
    setEditing(null);
    setAt(1);
    setFilters((was) => ({ ...was, ...change }));
  };

  const noDateChosen = chosenIn(filters.year).includes(NO_DATE);
  const narrowed = (Object.keys(NO_FILTERS) as (keyof Filters)[])
    .some((key) => chosenIn(filters[key]).length > 0);

  /*
   * The day picker's list, narrowed by the year and month above it.
   *
   * Three thousand lanes across fourteen months is several hundred dates, and
   * an unnarrowed list of those is a list nobody scrolls. The years and months
   * are left whole: those are the controls that widen it again.
   */
  const dayOptions = (choices?.dates ?? []).filter((date) => {
    const parts = partsOf(date);
    if (!parts) return false;
    const years = chosenIn(filters.year).filter((one) => one !== NO_DATE);
    const months = chosenIn(filters.month);
    return (!years.length || years.includes(parts.y)) && (!months.length || months.includes(parts.m));
  });

  /** Days that no longer sit inside the year and month, dropped as those change. */
  const keptDays = (year: string, month: string) => {
    const kept = chosenIn(filters.day).filter((date) => {
      const parts = partsOf(date);
      const years = chosenIn(year).filter((one) => one !== NO_DATE);
      const months = chosenIn(month);
      return !!parts && (!years.length || years.includes(parts.y))
        && (!months.length || months.includes(parts.m));
    });
    return kept.length ? kept.join("|") : "ALL";
  };

  if (!page) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
        กำลังโหลดตารางอัตรา…
      </div>
    );
  }

  /*
   * The bar, in My Job's own control and on My Job's own navy.
   *
   * Every one of these narrows at the API rather than in the browser. The grid
   * holds fifty rows of three thousand, so a filter applied here would be a
   * filter over the page you can already see — which is not a filter, it is a
   * way of hiding forty of the fifty and reporting the wrong count.
   */
  const controlBar = (
    <div style={css("display:flex;align-items:center;gap:9px;flex-wrap:wrap;padding:5px 0")}>
      <FilterPickMany label="CUSTOMER" value={filters.customer}
        options={choices?.customers ?? []}
        onPick={(value) => narrow({ customer: value })} />
      <FilterPickMany label="REQUESTOR" value={filters.requestor}
        options={choices?.requestors ?? []}
        onPick={(value) => narrow({ requestor: value })} />
      <FilterPickMany label="SUBCON" value={filters.carrier}
        options={choices?.carriers ?? []}
        onPick={(value) => narrow({ carrier: value })} />
      {/* Beside COUNTY rather than with the dates, because it is a thing you
          pick, not a period you narrow to. One No. is one quotation, which is
          the unit somebody copies rows for. */}
      <FilterPickMany label="No." value={filters.number}
        options={choices?.numbers ?? []}
        onPick={(value) => narrow({ number: value })} />
      <FilterPickMany label="COUNTY" value={filters.county}
        options={choices?.counties ?? []}
        onPick={(value) => narrow({ county: value })} />

      <span style={css("width:1px;height:20px;background:#24476E;flex:none")} />
      <span style={css("font-size:11px;font-weight:700;color:#CFE2F7;letter-spacing:.06em;white-space:nowrap")}>ช่วงเวลา</span>
      {/*
        "No date" among the years, because it is the same question with "none"
        as an answer — and because every other choice on this bar hides those
        rows, which are the ones somebody has to go and fix. Offered only when
        there are some, so the option never promises rows that are not there.
      */}
      <FilterPickMany label="ปี" value={filters.year}
        options={(choices?.years ?? []).concat(choices?.undated ? [NO_DATE] : [])}
        render={(year) => (year === NO_DATE ? `ไม่มีวันที่ (${choices?.undated ?? 0})` : year)}
        onPick={(value) => narrow({
          year: value,
          // Nothing narrower survives "no date": a month beside it matches
          // nothing and would read as though it might.
          month: chosenIn(value).includes(NO_DATE) ? "ALL" : filters.month,
          day: chosenIn(value).includes(NO_DATE) ? "ALL" : keptDays(value, filters.month),
        })} />
      {!noDateChosen && (
        <>
          <FilterPickMany label="เดือน" value={filters.month}
            options={choices?.months ?? []}
            render={(month) => monthLabel(month) + " (" + month + ")"}
            onPick={(value) => narrow({ month: value, day: keptDays(filters.year, value) })} />
          <FilterPickMany label="วัน" value={filters.day} options={dayOptions}
            onPick={(value) => narrow({ day: value })} />
        </>
      )}

      <button
        disabled={!narrowed && !search.trim()}
        onClick={() => {
          setEditing(null);
          setAt(1);
          setFilters(NO_FILTERS);
          setSearch("");
        }}
        style={css("height:29px;padding:0 10px;border-radius:4px;font-size:11px;font-weight:600;font-family:inherit;white-space:nowrap;"
          + ((narrowed || search.trim())
            ? "border:1px solid #4E9BE8;background:#16406E;color:#fff;cursor:pointer"
            : "border:1px solid #24476E;background:transparent;color:#4F7096;cursor:default"))}
      >
        ล้างตัวกรอง
      </button>

      <span style={css("margin-left:auto;display:flex;align-items:center;gap:8px;white-space:nowrap")}>
        <span aria-hidden="true" style={css("display:flex;color:#5CC0F7")}><StatGlyph icon="chart" size={15} /></span>
        <span style={css("font-size:15px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#fff")}>
          {page.total.toLocaleString()}
        </span>
        <span style={css("font-size:11.5px;color:#CFE2F7")}>เส้นทาง</span>
        {/*
          How to start an edit, said first.

          This line listed Ctrl+C, Tab, Enter and Esc — everything you do once
          you are already editing, and nothing about how to begin. The gesture
          was only in a cell's tooltip, which nobody hovers long enough to see,
          so a grid that has always been editable read as a grid that was not.
        */}
        {canEdit
          ? <span style={css("font-size:11px;color:#8FB4DC")}>
              · <b style={css("color:#CFE2F7")}>ดับเบิลคลิกเพื่อแก้ไข</b> หรือเลือกช่องแล้วพิมพ์ทับ
              · Ctrl+C / Ctrl+V · คลิกขวา คัดลอก/วาง · Tab → · Shift+Tab ← · Enter บันทึกแล้วลงแถวถัดไป · Esc ยกเลิก
            </span>
          : <span style={css("font-size:11px;color:#E0A33A")}>
              · อ่านอย่างเดียว — {needsSecondFactor
                ? "การแก้ไขอัตราค่าขนส่งต้องเข้าสู่ระบบด้วยการยืนยันตัวตนสองขั้น (MFA) · ออกจากระบบแล้วเข้าใหม่ด้วย MFA"
                : "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง"}
            </span>}
      </span>
    </div>
  );

  const model: TableModel = {
    title: "ตารางอัตราค่าขนส่ง",
    meta: "รูปแบบตามไฟล์ Rate Inquiry",
    fill: true,
    actions: [
      ...(draft && canEdit ? [{
        label: saving ? "กำลังบันทึก…" : drafts.length > 1 ? `บันทึกแถวใหม่ทั้ง ${drafts.length} แถว` : "บันทึกแถวใหม่",
        title: drafts.length > 1
          ? "บันทึกทุกแถวใหม่ — ลูกค้าเดียวกันวันเดียวกันจะได้เลขที่เดียวกัน"
          : "บันทึกลูกค้า เส้นทาง และราคาที่กรอก",
        disabled: saving,
        style: "background:#16794C", go: () => void createDrafts() }] : []),
      // First, and only once there is something to walk back — a permanently
      // greyed pair of buttons teaches people to stop looking at that corner.
      ...(undos.length > 0
        ? [{ label: "↶ ย้อนกลับ", title: `ย้อนกลับ ${undos[undos.length - 1].label} (Ctrl+Z)`,
            disabled: saving, style: "", go: () => void undo() }]
        : []),
      ...(redos.length > 0
        ? [{ label: "↷ ไปข้างหน้า", title: `ทำซ้ำ ${redos[redos.length - 1].label} (Ctrl+Shift+Z)`,
            disabled: saving, style: "", go: () => void redo() }]
        : []),
      {
        // Inline, the way My Job inserts: the row appears at the top of the
        // grid with its first cell already open, instead of a panel above the
        // grid that has to be filled and submitted before anything appears.
        // Every press adds one more below the last, so a quotation's four
        // routes can be keyed as four rows and saved once.
        label: "+ แทรกแถว",
        title: canEdit
          ? draft
            ? "เพิ่มแถวใหม่อีกแถวต่อจากแถวที่มี"
            : "แทรกแถวเปล่าไว้บนสุด แล้วพิมพ์ในตารางได้เลย · กดซ้ำเพื่อเพิ่มหลายแถว"
          : "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง",
        disabled: !canEdit || saving,
        style: "background:#0A2240",
        go: () => {
          setPanel("none");
          const added = blankDraft();
          const position = drafts.length;
          setDrafts((was) => [...was, added]);
          // Select, rather than open an input: Ctrl+V can immediately spread
          // an Excel block across the new row, and typing opens the editor.
          const at = SHEET_COLUMNS.findIndex((one) => one.field === "customer");
          setEditing(null);
          grid.setRange({ grid: "sheet", r1: position, r2: position, c1: at + LEAD, c2: at + LEAD });
          (document.activeElement as HTMLElement | null)?.blur();
          onToast(position === 0
            ? "แทรกแถวแล้ว — กรอกหรือวางลูกค้า ต้นทาง ปลายทาง แล้วกดบันทึกแถวใหม่ · กด + แทรกแถว ซ้ำเพื่อเพิ่มอีก"
            : `แทรกแถวใหม่แถวที่ ${position + 1} แล้ว`);
        },
      },
      ...(draft && canEdit ? [{
        label: drafts.length > 1 ? `ทิ้งแถวใหม่ทั้ง ${drafts.length} แถว` : "ทิ้งแถวใหม่",
        title: "ทิ้งแถวที่ยังไม่ได้บันทึก",
        disabled: saving,
        style: "",
        go: () => { setDrafts([]); setEditing(null); onToast("ทิ้งแถวใหม่แล้ว"); },
      }] : []),
      {
        label: panel === "import" ? "ปิดการนำเข้า" : "Import from Excel",
        title: "อ่านไฟล์ Rate Inquiry.xlsx เข้าระบบ",
        style: "",
        go: () => setPanel((was) => (was === "import" ? "none" : "import")),
      },
      {
        label: panel === "columns"
          ? "ปิดการเลือกคอลัมน์"
          : `เลือกคอลัมน์ (${chosenColumns.length}/${SHEET_COLUMNS.length})`,
        title: "เลือกว่าปุ่มคัดลอกจะเอาคอลัมน์ไหนไปบ้าง",
        style: "",
        go: () => setPanel((was) => (was === "columns" ? "none" : "columns")),
      },
      {
        label: busy ? "กำลังเตรียมไฟล์…" : "Export Excel",
        title: "บันทึกทุกแถวที่ตัวกรองเหลือไว้ ไม่ใช่เฉพาะหน้านี้",
        disabled: busy,
        style: "",
        go: () => void exportAll(),
      },
    ],
    tools: ["คัดลอกพร้อมหัวตาราง"],
    controls: controlBar,
    /*
     * The panels, drawn as the grid's banner.
     *
     * Full screen hides everything above the grid, and the banner is inside it
     * — so the one place you would want to import from is not the one place it
     * disappears from.
     */
    banner: (picked.size === 0 && panel === "none") ? null : (
      <>
        {picked.size > 0 && (
          <div style={css("padding:10px 16px;background:#FFF7DE;border-bottom:1px solid #EADFC8;"
            + "display:flex;align-items:center;gap:10px;flex-wrap:wrap")}>
            <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>
              เลือกไว้ {picked.size} แถว
            </span>
            <span style={css("font-size:11px;color:#7B6A45")}>
              {onPagePicked === picked.size
                ? "ลบพร้อมกันได้ทั้งชุด · ราคาในแถวจะหายไปด้วย"
                : `อยู่ในหน้านี้ ${onPagePicked} แถว · ลบได้เฉพาะแถวที่เห็นอยู่`}
            </span>
            <button type="button" disabled={busy || onPagePicked === 0}
              onClick={() => void removePicked()}
              style={css("margin-left:auto;height:30px;padding:0 12px;border-radius:4px;font:inherit;"
                + "font-size:12px;font-weight:600;"
                + (busy || onPagePicked === 0
                  ? "border:1px solid #E3E8EE;background:#F4F6F8;color:#9AA7B4;cursor:default"
                  : "border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;cursor:pointer"))}>
              {busy ? "กำลังลบ…" : `ลบ ${onPagePicked} แถวที่เลือก`}
            </button>
            <button type="button" onClick={() => setPicked(new Set())}
              style={css("height:30px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;"
                + "border-radius:4px;font:inherit;font-size:12px;color:#475569;cursor:pointer")}>
              ล้างการเลือก
            </button>
          </div>
        )}
        {panel !== "none" && (
      <div style={css("padding:10px 16px;background:#F4F8FC;border-bottom:1px solid #D8E0E8")}>
        {panel === "import" ? (
          <ImportWorkbook onToast={onToast} onDone={() => {
            setPanel("none");
            setAt(1);
            void reload();
          }} />
        ) : panel === "columns" ? (
          <ColumnPicker
            chosen={copyCols}
            effective={chosenColumns}
            rows={rows}
            onChange={setCopyCols}
          />
        ) : null}
      </div>
        )}
      </>
    ),
    search: {
      value: search,
      onChange: (value: string) => { setSearch(value); setAt(1); },
      placeholder: "ค้นหา — ลูกค้า, ผู้ขอ, ต้นทาง, ปลายทาง, จังหวัด, ผู้ขนส่ง, หมายเหตุ",
    },
    cols: [
      {
        // The same header the workspace uses to tick a page: a box that is
        // drawn full when the page is, and empties it when it is pressed again.
        label: allPagePicked ? "☑" : "☐",
        style: HEAD + "min-width:34px;width:34px;cursor:pointer;",
        sort: () => togglePage(allPagePicked),
      },
      ...SHEET_COLUMNS.map((column) => ({
        label: column.head,
        style: HEAD + `min-width:${column.width ?? 110}px;`,
        sort: () => undefined,
      })),
    ],
    noSelect: grid.dragSelecting,
    rows: shown.map((row, r) => ({
      key: String(row.laneId),
      // A ticked row is coloured and edged, as it is in My Job — the bar acts
      // on rows, so which rows it will act on has to be visible.
      style: isDraft(row)
        ? "background:#EDF5FF;border-left:3px solid #2E7DD1"
        : picked.has(row.laneId)
          ? "background:#FFF7DE;border-left:3px solid #D89614"
          : "border-left:3px solid transparent",
      cells: [
        pickCell(row),
        ...SHEET_COLUMNS.map((column, index) => ({
          ...toCell(row, column, index),
          // A tick box is not part of a rectangle: dragging across one selects
          // nothing there, and a paste cannot land in it. Nor is the selection
          // box, which is why the sheet's columns start at LEAD.
          ...grid.cellProps("sheet", r, index + LEAD, column.kind !== "tick"),
        })),
      ],
    })),
    total: page.total,
    pageCount: Math.max(1, Math.ceil(page.total / page.per)),
    page: page.page,
    per: page.per,
  };

  /** The leading tick box, so a row can be chosen without opening anything. */
  function pickCell(row: SheetRow): Cell {
    const c = cell("", {});
    c.kind = "check";
    c.checked = picked.has(row.laneId);
    // A row the server has not got cannot be selected for deletion — there is
    // nothing to delete, and the tick would sit beside a count that is wrong.
    // Its box is a way to drop that one row instead, since with several new
    // rows on the sheet "throw them all away" is the wrong size of undo.
    if (isDraft(row)) {
      const drop = cell("✕", { mute: true });
      drop.td = "padding:4px 6px 4px 10px;border-bottom:1px solid #EDF1F5;text-align:center;vertical-align:middle;width:34px;"
        + (canEdit && !saving ? "cursor:pointer;" : "");
      drop.title = canEdit ? "ทิ้งแถวใหม่แถวนี้" : "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง";
      drop.go = (event) => { event.stopPropagation(); if (canEdit && !saving) dropDraft(row.laneId); };
      return drop;
    }
    c.disabled = !canEdit || busy;
    c.td = "padding:4px 6px 4px 10px;border-bottom:1px solid #EDF1F5;text-align:center;vertical-align:middle;width:34px;";
    c.title = canEdit ? "เลือกแถวนี้" : "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง";
    c.onCheck = () => togglePick(row.laneId);
    return c;
  }

  function toCell(row: SheetRow, column: SheetColumn, index: number): Cell {
    const value = readCell(row, column);
    const open = editing?.laneId === row.laneId && editing.column === index;

    if (column.kind === "tick") {
      const c = cell("", { });
      c.kind = "check";
      c.checked = Boolean(value);
      c.disabled = !canEdit || saving;
      c.td = "padding:4px 9px;border-bottom:1px solid #EDF1F5;text-align:center;vertical-align:middle;";
      c.onCheck = () => void save(row, column, value ? "" : "x");
      c.title = canEdit ? "" : "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง";
      return c;
    }

    if (open) {
      return {
        kind: "input",
        v: editing.value,
        value: editing.value,
        td: "padding:2px 5px;border-bottom:1px solid #EDF1F5;vertical-align:middle;",
        sp: "",
        inpStyle: "width:100%;min-width:70px;height:25px;border:1px solid #2E7DD1;border-radius:3px;"
          + "padding:0 5px;font-size:12px;font-family:inherit;outline:none;"
          + (column.kind === "price" ? "text-align:right;font-family:ui-monospace,monospace;" : ""),
        onChange: (event) => setEditing({ ...editing, value: event.target.value }),
        onBlur: () => { const held = editing; setEditing(null); void save(row, column, held.value); },
        onKey: (event) => {
          if (event.key === "Tab" && !event.ctrlKey && !event.metaKey && !event.altKey) {
            event.preventDefault();
            // Drafts sit above the page, so a page row's position is offset by them.
            const r = isDraft(row)
              ? drafts.findIndex((one) => one.laneId === row.laneId)
              : drafts.length + rows.findIndex((one) => one.laneId === row.laneId);
            const next = gridTabTarget(event, { row: r, column: index + LEAD }, drafts.length + rows.length,
              [undefined, ...SHEET_COLUMNS.map(col => col.kind === "tick" ? undefined : fieldOf(col))], "right");
            const held = editing;
            setEditing(null);
            void save(row, column, held.value);
            if (next) grid.setRange({ grid: "sheet", r1: next.row, r2: next.row, c1: next.column, c2: next.column });
            return;
          }
          if (event.key === "Enter") {
            event.preventDefault();
            const held = editing;
            setEditing(null);
            void save(row, column, held.value);
            // Enter saves and steps down a row (Shift+Enter up), the way a
            // spreadsheet does — asked for on 16 Sep 2026, so a column of
            // prices is keyed as type, Enter, type, Enter. The next cell is
            // selected, not opened: the first key typed opens it.
            const r = isDraft(row)
              ? drafts.findIndex((one) => one.laneId === row.laneId)
              : drafts.length + rows.findIndex((one) => one.laneId === row.laneId);
            const last = drafts.length + rows.length - 1;
            const target = Math.min(Math.max(r + (event.shiftKey ? -1 : 1), 0), last);
            grid.setRange({ grid: "sheet", r1: target, r2: target, c1: index + LEAD, c2: index + LEAD });
          }
          // Escape closes the box before the blur that follows it, so the draft
          // is not written on the way out.
          if (event.key === "Escape") setEditing(null);
        },
      };
    }

    const shown = column.kind === "price" && typeof value === "number"
      ? value.toLocaleString("en-US")
      : String(value ?? "");

    const c = cell(shown, {
      mono: column.kind === "price",
      align: column.kind === "price" ? "right" : undefined,
      mute: shown.length === 0,
    });
    if (canEdit) {
      c.td += "cursor:cell;";
      // One click selects — the rectangle does that on mousedown. Two clicks
      // edit. The same gesture My Job settled on, and for the same reason: a
      // single click put a text box under the pointer every time somebody
      // touched a cell to read it, and fought the drag, because the anchor
      // turned into an input halfway through the gesture.
      c.go = (event) => event.stopPropagation();
      c.onDouble = (event) => {
        event.stopPropagation();
        setEditing({ laneId: row.laneId, column: index, value: String(value ?? "") });
      };
      c.title = "คลิกเลือก · ใช้ลูกศรเพื่อย้าย · พิมพ์เพื่อแทนค่า · ดับเบิลคลิกเพื่อแก้ไขค่าเดิม";
    } else {
      c.title = "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง";
    }
    return c;
  }

  return (
    // The page does not scroll; this fills it and the grid scrolls inside.
    <div style={css("flex:1;min-height:0;display:flex;flex-direction:column")}>
      <DataTable
        model={model}
        onPage={(next) => { setEditing(null); setAt(next); }}
        onTool={(label) => { if (label === "คัดลอกพร้อมหัวตาราง") void copyWithHeads(); }} />
      <GridMenu at={grid.menu} onClose={grid.closeMenu}
        onCopy={() => void grid.copyToClipboard()} onPaste={() => void grid.pasteFromClipboard()} />
    </div>
  );
}

/**
 * Which columns the copy button takes.
 *
 * The sheet is 41 columns wide because the workbook it mirrors prices 29
 * vehicles. A quotation uses four of them: over the 1,169 quotations in the
 * register, 957 price five vehicles or fewer and only 13 price more than ten.
 * So the button was copying two useful columns and twenty-seven empty ones,
 * which is what somebody pasting into a mail had to delete by hand.
 *
 * Nothing chosen is not "no columns" — it is the sensible default, the vehicles
 * actually priced in the rows in front of you. That has to be the default
 * rather than a preset somebody selects, because it is right without anybody
 * knowing this panel exists.
 *
 * Choosing anything at all switches to obeying the choice exactly, empty
 * columns included. Sending a customer a blank 40' column to be filled in is a
 * real thing to want, and a picker that silently dropped it would be wrong.
 */
function ColumnPicker(props: {
  chosen: Set<string>;
  effective: SheetColumn[];
  rows: SheetRow[];
  onChange: (next: Set<string>) => void;
}) {
  const { chosen, effective, rows, onChange } = props;
  const taken = new Set(effective.map((one) => one.head));
  const details = SHEET_COLUMNS.filter((one) => one.kind !== "price");
  const prices = SHEET_COLUMNS.filter((one) => one.kind === "price");

  /** Whether any row on the page prices this vehicle — shown so the empty ones are obvious. */
  const priced = (column: SheetColumn) =>
    rows.some((row) => String(readCell(row, column) ?? "").trim() !== "");

  function toggle(head: string) {
    // The first tick has to start from what is on screen, not from nothing.
    // Starting empty would make one tick mean "only this column", throwing away
    // the Date and Customer the person could see were included a moment ago.
    const next = new Set(chosen.size > 0 ? chosen : taken);
    if (next.has(head)) next.delete(head); else next.add(head);
    onChange(next);
  }

  const Box = (column: SheetColumn) => {
    const on = chosen.size > 0 ? chosen.has(column.head) : taken.has(column.head);
    const empty = column.kind === "price" && !priced(column);
    return (
      <label key={column.head} title={empty ? "ไม่มีราคาในหน้านี้" : undefined}
        style={css("display:inline-flex;align-items:center;gap:5px;padding:3px 8px;border-radius:4px;"
          + "font-size:11.5px;cursor:pointer;border:1px solid "
          + (on ? "#9CC2E8;background:#EAF3FC;color:#0A2240" : "#E3E8EE;background:#fff;")
          + (on ? "" : empty ? "color:#AFBAC6" : "color:#475569"))}>
        <input type="checkbox" checked={on} onChange={() => toggle(column.head)}
          style={css("margin:0;cursor:pointer")} />
        {column.head}
        {empty && <span style={css("font-size:10px;color:#AFBAC6")}>·ว่าง</span>}
      </label>
    );
  };

  return (
    <div style={css("display:grid;gap:8px")}>
      <div style={css("display:flex;align-items:center;gap:8px;flex-wrap:wrap")}>
        <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>
          คัดลอก {effective.length} จาก {SHEET_COLUMNS.length} คอลัมน์
        </span>
        <span style={css("font-size:11px;color:#64748B")}>
          {chosen.size === 0
            ? "ค่าเริ่มต้น — เอาเฉพาะรถที่มีราคาในหน้านี้"
            : "เลือกเองไว้ — จะเอาตามนี้ทุกครั้ง รวมช่องที่ว่าง"}
        </span>
        <span style={css("margin-left:auto;display:flex;gap:6px")}>
          <button type="button" onClick={() => onChange(new Set())}
            style={css("height:26px;padding:0 10px;border:1px solid #D8E0E8;background:#fff;"
              + "border-radius:4px;font:inherit;font-size:11.5px;color:#475569;cursor:pointer")}>
            กลับค่าเริ่มต้น
          </button>
          <button type="button" onClick={() => onChange(new Set(SHEET_COLUMNS.map((one) => one.head)))}
            style={css("height:26px;padding:0 10px;border:1px solid #D8E0E8;background:#fff;"
              + "border-radius:4px;font:inherit;font-size:11.5px;color:#475569;cursor:pointer")}>
            เลือกทั้งหมด
          </button>
          <button type="button" onClick={() => onChange(new Set(details.map((one) => one.head)))}
            style={css("height:26px;padding:0 10px;border:1px solid #D8E0E8;background:#fff;"
              + "border-radius:4px;font:inherit;font-size:11.5px;color:#475569;cursor:pointer")}>
            เฉพาะรายละเอียด
          </button>
        </span>
      </div>

      <div style={css("display:flex;gap:5px;flex-wrap:wrap")}>{details.map(Box)}</div>
      <div style={css("border-top:1px solid #E3E8EE;padding-top:7px;display:flex;gap:5px;flex-wrap:wrap")}>
        {prices.map(Box)}
      </div>
    </div>
  );
}
