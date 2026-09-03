"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { DataTable, type TableModel } from "../DataTable";
import { SHEET_COLUMNS, type SheetColumn } from "../rateSheetColumns";
import { css } from "../theme";
import { cell, type Cell } from "../util";

/**
 * The rate register, laid out as the workbook lays it out, and typed into.
 *
 * The team keeps `Rate Inquiry.xlsx` because it is the shape they think in: one
 * row per journey, the request's date and customer repeated down its rows,
 * twenty-eight price columns across. The register normalises that into requests
 * with lanes under them, which is right for storing it and wrong for reading
 * it. This puts it back.
 *
 * Built on the same grid My Job uses, so it inherits the frozen header, the
 * zoom, the full screen and the paging rather than growing its own copy of any
 * of them. What it does not inherit is that screen's dragged rectangle and its
 * copy, paste and Delete — those live in the workspace and would have to be
 * lifted out to be shared, which is a change worth making on purpose rather
 * than on the way past.
 *
 * Every cell saves on its own. A row here spans a lane and the request above
 * it, and two people editing two lanes of one request in the same minute is
 * ordinary — a whole-row save would have one of them overwrite the other's
 * customer with a copy of the value they started from.
 */

type SheetRow = {
  laneId: number;
  inquiryId: number;
  date: string;
  no: number;
  requestor: string;
  customer: string;
  fuelBand: string;
  fromPlace: string;
  toPlace: string;
  county: string;
  carriers: string;
  fcl: boolean;
  lcl: boolean;
  domestic: boolean;
  remark: string;
  prices: Record<string, number>;
};

type Page = { rows: SheetRow[]; total: number; page: number; per: number };

/** Which cell is open, and what is in the box while it is. */
type Editing = { laneId: number; column: number; value: string };

const PER = 50;

const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;"
  + "font-size:12.5px;font-family:inherit;background:#fff";

export function RateSheet({ canEdit, onToast }: {
  /** Whether this account may change a rate. Shown, not guessed at: the API decides. */
  canEdit: boolean;
  onToast: (message: string) => void;
}) {
  const [page, setPage] = useState<Page | null>(null);
  const [at, setAt] = useState(1);
  const [search, setSearch] = useState("");
  const [applied, setApplied] = useState("");
  const [editing, setEditing] = useState<Editing | null>(null);
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    const query = new URLSearchParams({ page: String(at), per: String(PER), q: applied });
    const response = await apiFetch(`/api/rate-inquiries/sheet?${query}`,
      { headers: { accept: "application/json" } });
    if (!response.ok) { onToast("อ่านตารางอัตราไม่สำเร็จ · HTTP " + response.status); return; }
    setPage(await response.json() as Page);
  }, [at, applied, onToast]);

  // Fetching on mount and whenever the page or the search moves. Every setState
  // runs after an await, so it lands in a microtask rather than while this body
  // does — the same idiom, and the same reason, as the screens next door.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

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

  if (!page) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
        กำลังโหลดตารางอัตรา…
      </div>
    );
  }

  const model: TableModel = {
    title: "ตารางอัตราค่าขนส่ง",
    meta: `${page.total.toLocaleString()} เส้นทาง`
      + (applied ? ` · ค้นหา “${applied}”` : "")
      + (canEdit ? "" : " · อ่านอย่างเดียว"),
    cols: SHEET_COLUMNS.map((column) => ({
      label: column.head,
      style: "padding:7px 9px;background:#F4F7FA;font-size:10.5px;letter-spacing:.04em;"
        + "text-transform:uppercase;color:#465A6E;border-bottom:1px solid #D8E0E8;"
        + "white-space:nowrap;user-select:none;position:sticky;top:0;z-index:1;"
        + `min-width:${column.width ?? 110}px;`
        + (column.kind === "price" ? "text-align:right;" : ""),
      sort: () => undefined,
    })),
    rows: page.rows.map((row) => ({
      key: String(row.laneId),
      style: "",
      cells: SHEET_COLUMNS.map((column, index) => toCell(row, column, index)),
    })),
    total: page.total,
    pageCount: Math.max(1, Math.ceil(page.total / page.per)),
    page: page.page,
    per: page.per,
    tools: [],
  };

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
      c.go = () => setEditing({ laneId: row.laneId, column: index, value: String(value ?? "") });
      c.title = "คลิกเพื่อแก้ไข";
    } else {
      c.title = "บัญชีนี้ไม่มีสิทธิ์แก้ไขอัตราค่าขนส่ง";
    }
    return c;
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:11px 14px;display:flex;gap:9px;align-items:center;flex-wrap:wrap")}>
        <input
          value={search}
          onChange={(event) => setSearch(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === "Enter") { setAt(1); setApplied(search.trim()); }
            if (event.key === "Escape") { setSearch(""); setAt(1); setApplied(""); }
          }}
          placeholder="ค้นหา — ลูกค้า, ผู้ขอ, ต้นทาง, ปลายทาง, จังหวัด, ผู้ขนส่ง, หมายเหตุ"
          style={css(CONTROL + ";flex:1;min-width:240px")} />
        <button onClick={() => { setAt(1); setApplied(search.trim()); }}
          style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
          ค้นหา
        </button>
        {applied && (
          <button onClick={() => { setSearch(""); setAt(1); setApplied(""); }}
            style={css("height:30px;padding:0 12px;border:1px solid #BBD5EE;background:#F4F8FC;color:#0A2240;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
            ล้างการค้นหา
          </button>
        )}
        <span style={css("font-size:11.5px;color:#94A3B8;margin-left:auto")}>
          รูปแบบตารางตามไฟล์ Rate Inquiry — คลิกช่องเพื่อแก้ไข · Enter บันทึก · Esc ยกเลิก
        </span>
      </div>

      <DataTable
        model={model}
        onPage={(next) => { setEditing(null); setAt(next); }}
        onTool={() => undefined} />
    </div>
  );
}

/** What a column reads out of a row. */
function readCell(row: SheetRow, column: SheetColumn): string | number | boolean {
  if (column.kind === "price") return row.prices[column.vehicle!] ?? "";
  const value = row[column.field as keyof SheetRow];
  return value as string | number | boolean;
}
