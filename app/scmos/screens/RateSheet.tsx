"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { DataTable, type TableModel } from "../DataTable";
import { exportRateSheet } from "../excel";
import { FilterPickMany } from "../FilterPickMany";
import { chosenIn } from "../filterChoices";
import { editHistoryShortcut } from "../editHistory";
import { writeClipboardTable } from "../pasteBlock";
import { NO_DATE, monthLabel, partsOf } from "../period";
import { SHEET_COLUMNS, readCell, type SheetColumn, type SheetRow } from "../rateSheetColumns";
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

/** What one press may remove. The API refuses more; this is so the screen says so first. */
const MAX_DELETE = 100;

/** The heading cell, shared by the tick box and the workbook's own columns. */
const HEAD = "padding:7px 9px;background:#F4F7FA;font-size:10.5px;letter-spacing:.04em;"
  + "text-transform:uppercase;color:#465A6E;border-bottom:1px solid #D8E0E8;"
  + "white-space:nowrap;user-select:none;position:sticky;top:0;z-index:1;";

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

export function RateSheet({ canEdit, onToast }: {
  /** Whether this account may change a rate. Shown, not guessed at: the API decides. */
  canEdit: boolean;
  onToast: (message: string) => void;
}) {
  const [page, setPage] = useState<Page | null>(null);
  const [choices, setChoices] = useState<Choices | null>(null);
  const [at, setAt] = useState(1);
  const [search, setSearch] = useState("");
  const [filters, setFilters] = useState<Filters>(NO_FILTERS);
  /** Which panel is open above the grid, or none. */
  const [panel, setPanel] = useState<"none" | "import" | "add" | "columns">("none");

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
  const [saving, setSaving] = useState(false);
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
  const rows = useMemo(() => page?.rows ?? [], [page]);

  /*
   * The rectangle, and everything a spreadsheet does with one.
   *
   * The same hook My Job uses. What differs is only what a row is and how a
   * cell is written — here a block goes to the API in one request, because the
   * register is the only copy and the browser holds none of it.
   */
  const grid = useGridRange<SheetRow, string>({
    rowsOf: () => rows,
    // The tick box leads with no field of its own, which is what keeps a
    // dragged rectangle, a paste and a Delete off it.
    fieldsOf: () => [undefined, ...SHEET_COLUMNS.map((column) => (canEdit ? fieldOf(column) : undefined))],
    headsOf: () => ["", ...SHEET_COLUMNS.map((column) => column.head)],
    read: (row, field) => String(readCell(row, columnFor(field)) ?? ""),
    canEdit: () => canEdit,
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
    onCopied: (lines, columns) => onToast(`คัดลอกแล้ว ${lines} แถว · ${columns} คอลัมน์`),
    onNothingToClear: () => onToast("ช่องที่เลือกว่างอยู่แล้ว"),
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
      await load();
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

  async function writeBlock(
    edits: { row: SheetRow; field: string; value: string }[],
    how: "paste" | "clear",
  ) {
    // Filed before the write, because afterwards the page is re-read and the
    // old values are gone from the browser as well as from the register.
    remember(how === "clear" ? "ล้างช่อง" : "วางข้อมูล", edits.map((one) => ({
      laneId: one.row.laneId,
      field: one.field,
      before: String(readCell(one.row, columnFor(one.field)) ?? ""),
      after: one.value,
    })));

    setSaving(true);
    try {
      const response = await apiFetch("/api/rate-inquiries/sheet/cells", {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({
          edits: edits.map((one) => ({ laneId: one.row.laneId, field: one.field, value: one.value })),
        }),
      });
      const reply = await response.json().catch(() => ({})) as
        { saved?: number; refused?: string[]; error?: string };
      if (!response.ok) { onToast(reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`); return; }

      const doing = how === "clear" ? "ล้าง" : "วาง";
      const refused = reply.refused ?? [];
      onToast(`${doing}แล้ว ${reply.saved ?? 0} ช่อง`
        + (refused.length ? ` · ข้าม ${refused.length} — ${refused[0]}` : ""));
      await load();
    } finally { setSaving(false); }
  }

  /**
   * Writes one cell and puts the answer on the row.
   *
   * The API is the authority on whether a value is allowed — a price that is
   * not a number, a date that is not a date, an account that may not change a
   * rate — so a refusal is shown as it comes back and the old value stays.
   */
  async function save(row: SheetRow, column: SheetColumn, value: string) {
    const field = column.kind === "price" ? `price:${column.vehicle}` : column.field!;
    const before = readCell(row, column);
    if (value.trim() === String(before).trim()) return;

    remember(`แก้ ${column.head}`,
      [{ laneId: row.laneId, field, before: String(before ?? ""), after: value }]);

    setSaving(true);
    try {
      const response = await apiFetch(`/api/rate-inquiries/sheet/${row.laneId}`, {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ field, value }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
      // Re-read rather than patching in place: editing a request's own field
      // moves every lane under it, and only the server knows which those are.
      if (response.ok) await load();
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
      await load();
      // A deleted lane may have taken the last row of a customer or a carrier
      // with it, and the pickers would go on offering it.
      void loadChoices();
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
    const lines = rows.map((row) =>
      chosenColumns.map((column) => String(readCell(row, column) ?? "")));

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

      <span style={css("margin-left:auto;display:flex;align-items:baseline;gap:8px;white-space:nowrap")}>
        <span style={css("font-size:15px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#fff")}>
          {page.total.toLocaleString()}
        </span>
        <span style={css("font-size:11.5px;color:#CFE2F7")}>เส้นทาง</span>
        {canEdit
          ? <span style={css("font-size:11px;color:#8FB4DC")}>· ดับเบิลคลิกช่องเพื่อแก้ไข · Enter บันทึก · Esc ยกเลิก</span>
          : <span style={css("font-size:11px;color:#E0A33A")}>· อ่านอย่างเดียว</span>}
      </span>
    </div>
  );

  const model: TableModel = {
    title: "ตารางอัตราค่าขนส่ง",
    meta: "รูปแบบตามไฟล์ Rate Inquiry",
    fill: true,
    actions: [
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
        label: panel === "add" ? "ปิดการเพิ่มแถว" : "+ แทรกแถว",
        title: canEdit
          ? "เพิ่มเส้นทางใหม่เข้าตาราง"
          : "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง",
        disabled: !canEdit,
        style: "background:#0A2240",
        go: () => setPanel((was) => (was === "add" ? "none" : "add")),
      },
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
            void load();
            // The file may have brought in customers, carriers and months that
            // were not there before; the pickers would go on offering the old
            // list until the screen was reopened.
            void loadChoices();
          }} />
        ) : panel === "columns" ? (
          <ColumnPicker
            chosen={copyCols}
            effective={chosenColumns}
            rows={rows}
            onChange={setCopyCols}
          />
        ) : (
          <AddLane onToast={onToast} onDone={() => {
            setPanel("none");
            setAt(1);
            void load();
            void loadChoices();
          }} />
        )}
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
        style: HEAD + "text-align:center;min-width:34px;width:34px;cursor:pointer;",
        sort: () => togglePage(allPagePicked),
      },
      ...SHEET_COLUMNS.map((column) => ({
        label: column.head,
        style: HEAD + `min-width:${column.width ?? 110}px;`
          + (column.kind === "price" ? "text-align:right;" : ""),
        sort: () => undefined,
      })),
    ],
    noSelect: grid.dragSelecting,
    rows: page.rows.map((row, r) => ({
      key: String(row.laneId),
      // A ticked row is coloured and edged, as it is in My Job — the bar acts
      // on rows, so which rows it will act on has to be visible.
      style: picked.has(row.laneId)
        ? "background:#FFF7DE;border-left:3px solid #D89614"
        : "border-left:3px solid transparent",
      cells: [
        pickCell(row),
        ...SHEET_COLUMNS.map((column, index) => ({
          ...toCell(row, column, index),
          // A tick box is not part of a rectangle: dragging across one selects
          // nothing there, and a paste cannot land in it. Nor is the selection
          // box, which is why the sheet's columns start at LEAD.
          ...grid.cellProps("sheet", r, index + LEAD, canEdit && column.kind !== "tick"),
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
          if (event.key === "Enter") {
            event.preventDefault();
            const held = editing;
            setEditing(null);
            void save(row, column, held.value);
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
    </div>
  );
}

/**
 * A new row, asking for the four things the register will not do without.
 *
 * A blank row cannot be written: an inquiry needs a customer to be for and a
 * lane needs two ends and a load type, and the API refuses anything less
 * — rightly, because a rate against nowhere is a rate nobody can ever use.
 * Everything else on the row is typed into the grid afterwards, which is the
 * point of the grid.
 *
 * The date and the requestor are not asked for. Today, and whoever is signed
 * in: those are facts about the act of adding the row, not decisions.
 */
function AddLane({ onToast, onDone }: { onToast: (m: string) => void; onDone: () => void }) {
  const [customer, setCustomer] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [load, setLoad] = useState<"fcl" | "lcl">("fcl");
  const [sending, setSending] = useState(false);

  const ready = customer.trim() && from.trim() && to.trim();

  async function send() {
    if (!ready || sending) return;
    setSending(true);
    try {
      const today = new Date();
      const dd = String(today.getDate()).padStart(2, "0");
      const mm = String(today.getMonth() + 1).padStart(2, "0");
      const response = await apiFetch("/api/rate-inquiries", {
        method: "POST",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({
          inquiredOn: `${dd}/${mm}/${today.getFullYear()}`,
          customer: customer.trim(),
          lanes: [{
            fromPlace: from.trim(), toPlace: to.trim(),
            fcl: load === "fcl", lcl: load === "lcl",
          }],
        }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      if (!response.ok) { onToast(reply.error ?? `เพิ่มแถวไม่สำเร็จ (${response.status})`); return; }
      onToast(reply.message ?? "เพิ่มแถวแล้ว");
      setCustomer(""); setFrom(""); setTo("");
      onDone();
    } finally { setSending(false); }
  }

  return (
    <div style={css("display:flex;gap:9px;align-items:flex-end;flex-wrap:wrap")}>
      <Box label="ลูกค้า" value={customer} onChange={setCustomer} width="220px" onEnter={send} />
      <Box label="ต้นทาง" value={from} onChange={setFrom} width="180px" onEnter={send} />
      <Box label="ปลายทาง" value={to} onChange={setTo} width="180px" onEnter={send} />
      <label style={css("display:flex;flex-direction:column;gap:3px")}>
        <span style={css("font-size:11px;color:#7B8CA0")}>ประเภทงาน</span>
        <select value={load} onChange={(event) => setLoad(event.target.value as "fcl" | "lcl")}
          style={css(ADD_CONTROL + ";cursor:pointer")}>
          <option value="fcl">FCL</option>
          <option value="lcl">LCL</option>
        </select>
      </label>
      <button onClick={() => void send()} disabled={!ready || sending}
        style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:"
          + (ready && !sending ? "#0A2240" : "#8FA3B8")
          + ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;font-family:inherit;cursor:"
          + (ready && !sending ? "pointer" : "default"))}>
        {sending ? "กำลังเพิ่ม…" : "เพิ่มแถว"}
      </button>
      <span style={css("font-size:11px;color:#7B8CA0;flex:1;min-width:200px")}>
        วันที่และผู้ขอถูกเติมให้เอง — วันนี้ และบัญชีที่เข้าใช้งานอยู่ · ราคาและคอลัมน์อื่นพิมพ์ในตารางได้เลย
      </span>
    </div>
  );
}

const ADD_CONTROL = "height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;"
  + "font-size:12.5px;font-family:inherit;background:#fff;width:100%";

function Box({ label, value, onChange, width, onEnter }: {
  label: string; value: string; onChange: (v: string) => void;
  width: string; onEnter: () => void;
}) {
  return (
    <label style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`)}>
      <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
      <input value={value} onChange={(event) => onChange(event.target.value)}
        onKeyDown={(event) => { if (event.key === "Enter") onEnter(); }}
        style={css(ADD_CONTROL)} />
    </label>
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
