"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import * as XLSX from "xlsx";
import { apiFetch } from "../api";
import { DataTable, type TableModel } from "../DataTable";
import { GridMenu } from "../GridMenu";
import { gridTabTarget } from "../gridEditKey";
import { writeClipboardTable } from "../pasteBlock";
import { badge, css } from "../theme";
import { useGridRange, type GridEdit } from "../useGridRange";
import { cell, type Cell } from "../util";

/**
 * The customer training register — the department's workbook, on screen,
 * and typed into.
 *
 * Nine columns, as the workbook has them, and every one of them a cell that
 * edits in place: click to select, type to replace, double-click to change
 * what is there, Enter to save and step down, Tab to step right; Ctrl+C and
 * Ctrl+V over a rectangle, and the right-click menu, as on the rate sheet.
 * Asked for on 17 Sep 2026 — until then a row was edited through a form row
 * at the top of the table, and the row being edited sat somewhere below it.
 *
 * Nothing here calculates a status. The API derives it from Expire date each
 * time it is asked, so a save comes back with the row as the register now
 * reads it.
 */

type RegisterRow = {
  id: number;
  sequenceNo: string;
  courseCustomer: string;
  firstName: string;
  lastName: string;
  company: string;
  driverLicenseNo: string;
  licenseType: string;
  effectiveDate: string;
  expiryDate: string;
  daysLeft: number | null;
  status: string;
  statusTh: string;
};

type RegisterSummary = {
  total: number;
  valid: number;
  nearExpiry: number;
  expired: number;
  invalidDate: number;
};

type ImportRow = Omit<RegisterRow, "id" | "daysLeft" | "status" | "statusTh">;
type Field = keyof ImportRow;

type RegisterReply = {
  rows: RegisterRow[];
  summary: RegisterSummary;
  alertBeforeDays: number;
};

const EMPTY_SUMMARY: RegisterSummary = {
  total: 0, valid: 0, nearExpiry: 0, expired: 0, invalidDate: 0,
};

/** The workbook's columns, in its order; the first name in each list is the heading. */
const COLUMNS: Record<Field, string[]> = {
  sequenceNo: ["ลำดับ", "no", "no.", "sequence", "sequence no"],
  courseCustomer: ["ชื่อหลักสูตร/ลูกค้า", "ชื่อหลักสูตร / ลูกค้า", "course/customer", "course customer"],
  firstName: ["ชื่อ", "first name", "firstname"],
  lastName: ["นามสกุล", "last name", "lastname", "surname"],
  company: ["บริษัท", "company"],
  driverLicenseNo: ["เลขที่ใบขับขี่", "เลขใบขับขี่", "driver license no", "license no", "licence no"],
  licenseType: ["ประเภทใบขับขี่", "license type", "licence type"],
  effectiveDate: ["Effective date", "effectivedate", "วันที่เริ่มมีผล", "วันที่อบรม"],
  expiryDate: ["Expire date", "expiry date", "expiredate", "expirydate", "วันหมดอายุ", "วันที่หมดอายุ"],
};

const FIELDS = Object.keys(COLUMNS) as Field[];

/** The column's width and whether it is set in the monospace face. */
const SHAPE: Record<Field, { width: number; mono?: boolean; bold?: boolean }> = {
  sequenceNo: { width: 60 },
  courseCustomer: { width: 220, bold: true },
  firstName: { width: 120 },
  lastName: { width: 140 },
  company: { width: 180 },
  driverLicenseNo: { width: 130, mono: true },
  licenseType: { width: 110 },
  effectiveDate: { width: 105, mono: true },
  expiryDate: { width: 105, mono: true },
};

/** What the API accepts per column — the database's own limits. */
const LIMITS: Record<Field, number> = {
  sequenceNo: 40, courseCustomer: 300, firstName: 160, lastName: 160,
  company: 240, driverLicenseNo: 80, licenseType: 120, effectiveDate: 20, expiryDate: 20,
};

/** A row typed in and not yet saved sits at the top under this id. */
const DRAFT_ID = -1;

const BLANK: ImportRow = {
  sequenceNo: "", courseCustomer: "", firstName: "", lastName: "", company: "",
  driverLicenseNo: "", licenseType: "", effectiveDate: "", expiryDate: "",
};

const HEAD = "padding:7px 9px;background:#F4F7FA;font-size:10.5px;letter-spacing:.04em;"
  + "text-transform:uppercase;color:#465A6E;border-bottom:1px solid #D8E0E8;"
  + "white-space:nowrap;user-select:none;position:sticky;top:0;z-index:1;";

const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;background:#fff;font:12.5px inherit;color:#0F2B46";

const norm = (value: string) => value.toLowerCase().replace(/[-\s._/()]/g, "");

function asDate(value: unknown): string {
  if (value instanceof Date && !Number.isNaN(value.valueOf())) {
    const pad = (part: number) => String(part).padStart(2, "0");
    return `${pad(value.getDate())}/${pad(value.getMonth() + 1)}/${value.getFullYear()}`;
  }
  return String(value ?? "").trim();
}

function statusTone(status: string): { bg: string; text: string; border: string; badge: "red" | "amber" | "gray" | "green" } {
  if (status === "EXPIRED") return { bg: "#FEF0EE", text: "#B42318", border: "#F3C9C4", badge: "red" };
  if (status === "EXPIRING_SOON") return { bg: "#FFF8F0", text: "#B45309", border: "#F0D8B8", badge: "amber" };
  if (status === "INVALID_DATE") return { bg: "#F1F5F9", text: "#64748B", border: "#D7E0E8", badge: "gray" };
  return { bg: "#EDF7F1", text: "#16794C", border: "#BFE0CD", badge: "green" };
}

/** The nine workbook values of a row, and nothing else — what the API takes. */
function bodyOf(row: ImportRow): ImportRow {
  return Object.fromEntries(FIELDS.map((field) => [field, String(row[field] ?? "")])) as ImportRow;
}

const PER = 100;

export function CustomerTrainingRegister({ onToast, canEdit = false }: { onToast: (message: string) => void; canEdit?: boolean }) {
  const [rows, setRows] = useState<RegisterRow[]>([]);
  const [summary, setSummary] = useState<RegisterSummary>(EMPTY_SUMMARY);
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("ALL");
  const [page, setPage] = useState(1);
  const [full, setFull] = useState(false);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState("");
  const [saveError, setSaveError] = useState("");
  /** The row being typed in, not yet in the register. */
  const [draft, setDraft] = useState<ImportRow | null>(null);
  /** The one cell open as a text box, and what has been typed into it. */
  const [editing, setEditing] = useState<{ id: number; field: Field; value: string } | null>(null);
  const [preview, setPreview] = useState<{
    fileName: string;
    ok: ImportRow[];
    bad: { row: number; why: string }[];
  } | null>(null);
  const fileInput = useRef<HTMLInputElement>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/training/register");
      if (!response.ok) {
        setFailure(`API ตอบ ${response.status}`);
        return;
      }
      const reply = await response.json() as RegisterReply;
      setRows(reply.rows ?? []);
      setSummary(reply.summary ?? EMPTY_SUMMARY);
      setFailure("");
    } catch (error) {
      setFailure(error instanceof Error ? error.message : String(error));
    }
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const shown = useMemo(() => {
    const wanted = search.trim().toLowerCase();
    const rank: Record<string, number> = { EXPIRED: 0, EXPIRING_SOON: 1, INVALID_DATE: 2, VALID: 3 };
    return rows.filter((row) => {
      if (status !== "ALL" && row.status !== status) return false;
      if (!wanted) return true;
      return [row.sequenceNo, row.courseCustomer, row.firstName, row.lastName,
        row.company, row.driverLicenseNo, row.licenseType]
        .some((value) => value.toLowerCase().includes(wanted));
    }).sort((left, right) =>
      (rank[left.status] ?? 9) - (rank[right.status] ?? 9)
      || (left.daysLeft ?? Number.MAX_SAFE_INTEGER) - (right.daysLeft ?? Number.MAX_SAFE_INTEGER)
      || left.id - right.id);
  }, [rows, search, status]);

  const pageCount = Math.max(1, Math.ceil(shown.length / PER));
  const at = Math.min(page, pageCount);

  /** The rows as drawn: the draft first, then this page of the register. */
  const drawn = useMemo<RegisterRow[]>(() => {
    const slice = shown.slice((at - 1) * PER, at * PER);
    if (!draft) return slice;
    return [{ id: DRAFT_ID, ...draft, daysLeft: null, status: "", statusTh: "" }, ...slice];
  }, [shown, at, draft]);

  /* ------------------------------------------------------- writing */

  /**
   * One row, with these cells changed, to the API. A draft is only typed
   * into; it goes as one POST from the banner. The reply carries the row as
   * the register now reads it — status included — so the grid takes that and
   * nothing is re-fetched.
   */
  async function saveRow(row: RegisterRow, changes: Partial<ImportRow>): Promise<string | null> {
    if (row.id === DRAFT_ID) {
      setDraft((held) => held && ({ ...held, ...changes }));
      return null;
    }
    const body = { ...bodyOf(row), ...changes };
    if (FIELDS.every((field) => body[field] === row[field])) return null;
    const response = await apiFetch(`/api/training/register/${row.id}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    const reply = await response.json().catch(() => null) as { error?: string; message?: string; row?: RegisterRow } | null;
    if (!response.ok) return reply?.error ?? `บันทึกไม่สำเร็จ (${response.status})`;
    if (reply?.row) {
      const saved = reply.row;
      setRows((was) => was.map((one) => (one.id === saved.id ? saved : one)));
    } else {
      await load();
    }
    return null;
  }

  /** One cell, from its text box: kept on the screen if the API refuses it. */
  async function saveCell(row: RegisterRow, field: Field, value: string) {
    if (!canEdit) return;
    setBusy(true);
    try {
      const problem = await saveRow(row, { [field]: value });
      if (problem) {
        setSaveError(`${COLUMNS[field][0]} — ${problem}`);
        setEditing({ id: row.id, field, value });
      } else {
        setSaveError("");
      }
    } catch (error) {
      setSaveError("ไม่ทราบผลการบันทึก: " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setBusy(false);
    }
  }

  /** A pasted block or a cleared rectangle: each row its own save, in order. */
  async function writeBlock(edits: GridEdit<RegisterRow, Field>[], how: "paste" | "clear") {
    if (!canEdit || edits.length === 0) return;
    const byRow = new Map<number, { row: RegisterRow; changes: Partial<ImportRow> }>();
    for (const edit of edits) {
      const held = byRow.get(edit.row.id) ?? { row: edit.row, changes: {} };
      held.changes[edit.field] = edit.value.slice(0, LIMITS[edit.field]);
      byRow.set(edit.row.id, held);
    }
    setBusy(true);
    let written = 0;
    const problems: string[] = [];
    try {
      for (const { row, changes } of byRow.values()) {
        try {
          const problem = await saveRow(row, changes);
          if (problem) problems.push(`${row.firstName || row.courseCustomer || row.id}: ${problem}`);
          else written++;
        } catch (error) {
          problems.push(`${row.id}: ${error instanceof Error ? error.message : String(error)}`);
        }
      }
    } finally {
      setBusy(false);
    }
    const verb = how === "paste" ? "วางแล้ว" : "ล้างแล้ว";
    if (problems.length === 0) onToast(`${verb} ${written} แถว`);
    else onToast(`${verb} ${written} แถว · ไม่สำเร็จ ${problems.length}: ${problems.slice(0, 3).join(" · ")}`);
    if (problems.length > 0) setSaveError(problems.join(" · "));
    else setSaveError("");
  }

  const grid = useGridRange<RegisterRow, Field>({
    rowsOf: () => drawn,
    // The status column has no field: nothing is typed into it, nothing is
    // pasted over it, and a copy of it takes the words on the screen.
    fieldsOf: () => [...FIELDS, undefined],
    headsOf: () => [...FIELDS.map((field) => COLUMNS[field][0]), "สถานะ"],
    read: (row, field) => String(row[field] ?? ""),
    canEdit: () => canEdit && !busy,
    write: (edits, how) => void writeBlock(edits, how),
    openEditor: (row, field, seed) => setEditing({ id: row.id, field, value: seed ?? String(row[field] ?? "") }),
    editing: editing !== null,
    tabDirection: "right",
    onCopied: (lines, columns) => onToast(`คัดลอกแล้ว ${lines} แถว · ${columns} คอลัมน์`),
    onNothingToClear: () => onToast("ช่องที่เลือกว่างอยู่แล้ว"),
    onClipboardBlocked: () => onToast("เบราว์เซอร์ไม่ให้อ่านคลิปบอร์ด — ใช้ Ctrl+V แทน"),
    onClipped: ({ rows: r, columns, unwritable }) => onToast(`วางได้เฉพาะหน้าปัจจุบัน · เกิน ${r} แถว / ${columns} คอลัมน์ · ข้าม ${unwritable} ช่อง`),
  });

  /* --------------------------------------------------------- the draft */

  function beginDraft() {
    if (busy || !canEdit) return;
    if (draft && !window.confirm("ยกเลิกแถวใหม่ที่ยังไม่บันทึกและเริ่มแถวใหม่หรือไม่?")) return;
    setDraft({ ...BLANK });
    setSaveError("");
    setPage(1);
    setEditing({ id: DRAFT_ID, field: "sequenceNo", value: "" });
  }

  async function saveDraft() {
    if (!draft || busy || !canEdit) return;
    setBusy(true);
    setSaveError("");
    try {
      const response = await apiFetch("/api/training/register", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(bodyOf(draft)),
      });
      const reply = await response.json().catch(() => null) as { error?: string; message?: string } | null;
      // A refused row stays on the screen, as typed, with the reason beside it.
      if (!response.ok) { setSaveError(reply?.error ?? `บันทึกไม่สำเร็จ (${response.status})`); return; }
      onToast(reply?.message ?? "เพิ่มรายการอบรมแล้ว");
      setDraft(null);
      await load();
    } catch (error) {
      setSaveError("ไม่ทราบผลการบันทึก กรุณาตรวจรายการก่อนลองอีกครั้ง: " + (error instanceof Error ? error.message : String(error)));
    } finally { setBusy(false); }
  }

  /* -------------------------------------------------------- the import */

  async function readFile(file: File) {
    try {
      const book = XLSX.read(await file.arrayBuffer(), { type: "array", cellDates: true });
      const sheet = book.Sheets[book.SheetNames[0]];
      const source = XLSX.utils.sheet_to_json<Record<string, unknown>>(sheet, { defval: "" });
      const ok: ImportRow[] = [];
      const bad: { row: number; why: string }[] = [];

      source.forEach((raw, index) => {
        const pick = (field: Field) => {
          const wanted = COLUMNS[field].map(norm);
          const key = Object.keys(raw).find((header) => wanted.includes(norm(header)));
          return key ? raw[key] : "";
        };
        const row: ImportRow = {
          sequenceNo: String(pick("sequenceNo") ?? "").trim(),
          courseCustomer: String(pick("courseCustomer") ?? "").trim(),
          firstName: String(pick("firstName") ?? "").trim(),
          lastName: String(pick("lastName") ?? "").trim(),
          company: String(pick("company") ?? "").trim(),
          driverLicenseNo: String(pick("driverLicenseNo") ?? "").trim(),
          licenseType: String(pick("licenseType") ?? "").trim(),
          effectiveDate: asDate(pick("effectiveDate")),
          expiryDate: asDate(pick("expiryDate")),
        };
        const missing: string[] = [];
        if (!row.courseCustomer) missing.push("ชื่อหลักสูตร/ลูกค้า");
        if (!row.firstName && !row.lastName) missing.push("ชื่อหรือนามสกุล");
        if (!row.effectiveDate) missing.push("Effective date");
        if (!row.expiryDate) missing.push("Expire date");
        if (missing.length) bad.push({ row: index + 1, why: `ไม่มี ${missing.join(", ")}` });
        else ok.push(row);
      });

      setPreview({ fileName: file.name, ok, bad });
      if (ok.length === 0 && bad.length === 0) onToast("ไฟล์นี้ยังไม่มีแถวข้อมูลสำหรับนำเข้า");
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  async function importRows() {
    if (!preview || preview.ok.length === 0 || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/training/register/import", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ rows: preview.ok }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "นำเข้าไม่สำเร็จ");
      if (response.ok) {
        setPreview(null);
        await load();
      }
    } finally {
      setBusy(false);
    }
  }

  /** The rows on this page with the workbook's headings, for a mail or a sheet. */
  async function copyWithHeads() {
    const heads = [...FIELDS.map((field) => COLUMNS[field][0]), "สถานะ"];
    const lines = drawn.filter((row) => row.id !== DRAFT_ID)
      .map((row) => [...FIELDS.map((field) => String(row[field] ?? "")), row.statusTh]);
    if (lines.length === 0) { onToast("ไม่มีแถวให้คัดลอก"); return; }
    try {
      await writeClipboardTable(lines, heads);
      onToast(`คัดลอกแล้ว ${lines.length} แถว พร้อมหัวตาราง`);
    } catch {
      onToast("คัดลอกไม่สำเร็จ — เบราว์เซอร์ไม่ให้เขียนคลิปบอร์ด");
    }
  }

  /* ---------------------------------------------------------- cells */

  function toCell(row: RegisterRow, field: Field, r: number, c: number): Cell {
    const value = String(row[field] ?? "");
    const shape = SHAPE[field];
    const open = editing?.id === row.id && editing.field === field;

    if (open) {
      return {
        kind: "input",
        v: editing.value,
        value: editing.value,
        td: "padding:2px 5px;border-bottom:1px solid #EDF1F5;vertical-align:middle;",
        sp: "",
        inpStyle: `width:100%;min-width:${shape.width - 12}px;height:25px;border:1px solid #2E7DD1;border-radius:3px;`
          + "padding:0 5px;font-size:12px;font-family:inherit;outline:none;"
          + (shape.mono ? "font-family:ui-monospace,monospace;" : ""),
        onChange: (event) => setEditing({ ...editing, value: event.target.value.slice(0, LIMITS[field]) }),
        onBlur: () => { const held = editing; setEditing(null); void saveCell(row, field, held.value); },
        onKey: (event) => {
          const fields: (string | undefined)[] = [...FIELDS, undefined];
          if (event.key === "Tab" && !event.ctrlKey && !event.metaKey && !event.altKey) {
            event.preventDefault();
            const next = gridTabTarget(event, { row: r, column: c }, drawn.length, fields, "right");
            const held = editing;
            setEditing(null);
            void saveCell(row, field, held.value);
            if (next) grid.setRange({ grid: "register", r1: next.row, r2: next.row, c1: next.column, c2: next.column });
            return;
          }
          if (event.key === "Enter") {
            // Enter saves and steps down a row (Shift+Enter up), as the rate
            // sheet does; the next cell is selected, and the first key typed
            // opens it.
            event.preventDefault();
            const held = editing;
            setEditing(null);
            void saveCell(row, field, held.value);
            const target = Math.min(Math.max(r + (event.shiftKey ? -1 : 1), 0), drawn.length - 1);
            grid.setRange({ grid: "register", r1: target, r2: target, c1: c, c2: c });
            return;
          }
          // Escape closes the box before the blur that follows it, so what
          // was typed is not written on the way out.
          if (event.key === "Escape") setEditing(null);
        },
      };
    }

    const built = cell(value, { mono: shape.mono, bold: shape.bold, mute: value.length === 0, w: shape.width });
    if (canEdit) {
      built.td += "cursor:cell;";
      // One click selects — the rectangle does that on mousedown. Two clicks
      // edit; typing replaces.
      built.go = (event) => event.stopPropagation();
      built.onDouble = (event) => {
        event.stopPropagation();
        setEditing({ id: row.id, field, value });
      };
      built.title = "คลิกเลือก · ใช้ลูกศรเพื่อย้าย · พิมพ์เพื่อแทนค่า · ดับเบิลคลิกเพื่อแก้ไขค่าเดิม";
    } else {
      built.title = "บัญชีนี้ไม่มีสิทธิ์แก้ไขทะเบียนอบรม";
    }
    return built;
  }

  function statusCell(row: RegisterRow): Cell {
    if (row.id === DRAFT_ID) {
      const pending = cell("คำนวณหลังบันทึก", { mute: true });
      return pending;
    }
    const tone = statusTone(row.status);
    const words = row.statusTh
      + (row.daysLeft !== null
        ? row.daysLeft > 0 ? ` · เหลือ ${row.daysLeft} วัน`
          : row.daysLeft === 0 ? " · หมดอายุวันนี้"
            : ` · เกิน ${Math.abs(row.daysLeft)} วัน`
        : "");
    const built = cell(words, {});
    built.sp = badge(words, tone.badge) + `border:1px solid ${tone.border};border-radius:999px;padding:3px 8px;font-size:11px;font-weight:700;`;
    return built;
  }

  const model: TableModel = {
    title: "ทะเบียนอบรม · Training record",
    meta: `แสดง ${shown.length} จาก ${rows.length} รายการ · สถานะคำนวณใหม่จาก Expire date ทุกครั้งที่เปิดหน้า`,
    tools: [],
    actions: [
      ...(canEdit ? [{
        label: "+ แทรกแถว", disabled: busy, style: "background:#16794C",
        title: "แถวใหม่ที่ด้านบน — พิมพ์ลงช่องแล้วกด “บันทึกแถว”",
        go: beginDraft,
      }] : []),
      ...(canEdit ? [{
        label: "นำเข้า Excel ตามแบบฟอร์ม", disabled: busy, style: "background:#0A2240",
        go: () => fileInput.current?.click(),
      }] : []),
      { label: "คัดลอกพร้อมหัวตาราง", disabled: drawn.length === 0, style: "", go: () => void copyWithHeads() },
    ],
    controls: (
      <>
        <label style={css("display:flex;align-items:center;gap:6px;font-size:11.5px;color:#7B8CA0")}>
          ค้นหา
          <input value={search} onChange={(event) => { setSearch(event.target.value); setPage(1); }}
            placeholder="หลักสูตร/ลูกค้า, ชื่อ, บริษัท หรือเลขใบขับขี่"
            style={css(CONTROL + ";min-width:260px")} />
        </label>
        <label style={css("display:flex;align-items:center;gap:6px;font-size:11.5px;color:#7B8CA0")}>
          สถานะ
          <select value={status} onChange={(event) => { setStatus(event.target.value); setPage(1); }} style={css(CONTROL)}>
            <option value="ALL">ทั้งหมด</option>
            <option value="EXPIRING_SOON">ใกล้หมดอายุ &lt; 60 วัน</option>
            <option value="EXPIRED">หมดอายุแล้ว</option>
            <option value="VALID">ยังใช้ได้</option>
            <option value="INVALID_DATE">วันที่ไม่ถูกต้อง</option>
          </select>
        </label>
        {canEdit && (
          <span style={css("font-size:11px;color:#7B8CA0")}>
            ดับเบิลคลิกเพื่อแก้ไข หรือเลือกช่องแล้วพิมพ์ทับ · Ctrl+C / Ctrl+V · คลิกขวา คัดลอก/วาง · Tab → · Enter บันทึกแล้วลงแถวถัดไป · Esc ยกเลิก
          </span>
        )}
      </>
    ),
    banner: (draft || preview || saveError) ? (
      <div style={css("display:flex;flex-direction:column;gap:8px;padding:10px 12px;border-bottom:1px solid #E3E8EE;background:#F8FAFC")}>
        {draft && (
          <div style={css("display:flex;align-items:center;gap:10px;flex-wrap:wrap;font-size:12.5px;color:#0F2B46")}>
            <strong>แถวใหม่ — ยังไม่บันทึก</strong>
            <span style={css("color:#5A6B7D")}>พิมพ์ลงช่องในแถวบนสุด · วันที่ DD/MM/YYYY · สถานะคำนวณหลังบันทึก</span>
            <button disabled={busy} onClick={() => void saveDraft()}
              style={css("height:28px;padding:0 13px;border:0;background:#16794C;color:#fff;border-radius:4px;font-weight:600;cursor:pointer")}>
              บันทึกแถว
            </button>
            <button disabled={busy} onClick={() => {
              if (window.confirm("ยกเลิกแถวใหม่ที่ยังไม่บันทึกหรือไม่?")) { setDraft(null); setEditing(null); setSaveError(""); }
            }} style={css("height:28px;padding:0 12px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;cursor:pointer")}>
              ยกเลิก
            </button>
          </div>
        )}
        {saveError && <div role="alert" style={css("color:#B42318;font-size:12px")}>{saveError}</div>}
        {preview && (
          <div style={css("background:#fff;border:1px solid #BBD5EE;border-left:3px solid #1D4E80;border-radius:6px;padding:11px 13px")}>
            <div style={css("font-size:13px;font-weight:650;color:#0F2B46")}>{preview.fileName} — พร้อมนำเข้า {preview.ok.length} รายการ</div>
            {preview.bad.length > 0 && (
              <div style={css("margin-top:8px;background:#FFF8F0;border-radius:4px;padding:8px 10px;font-size:11.5px;color:#8A5A12;max-height:120px;overflow:auto")}>
                {preview.bad.slice(0, 10).map((item) => <div key={item.row}>รายการ {item.row}: {item.why}</div>)}
                {preview.bad.length > 10 ? <div>… อีก {preview.bad.length - 10} รายการ</div> : null}
              </div>
            )}
            <div style={css("display:flex;gap:8px;align-items:center;margin-top:10px;flex-wrap:wrap")}>
              <button disabled={busy || preview.ok.length === 0} onClick={() => void importRows()}
                style={css("height:30px;padding:0 15px;border:0;background:" + (busy || preview.ok.length === 0 ? "#C3CFDB" : "#16794C") + ";color:#fff;border-radius:4px;font-weight:600;cursor:pointer")}>
                {busy ? "กำลังนำเข้า…" : `ยืนยันนำเข้า ${preview.ok.length} รายการ`}
              </button>
              <button disabled={busy} onClick={() => setPreview(null)} style={css("height:30px;padding:0 13px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;cursor:pointer")}>ยกเลิก</button>
              <span style={css("font-size:11.5px;color:#7B8CA0")}>ระบบจะข้ามรายการซ้ำ และแจ้งแถวที่วันที่ไม่ถูกต้อง</span>
            </div>
          </div>
        )}
      </div>
    ) : undefined,
    cols: [
      ...FIELDS.map((field) => ({ label: COLUMNS[field][0], style: HEAD + `min-width:${SHAPE[field].width}px;`, sort: () => undefined })),
      { label: "สถานะ", style: HEAD + "min-width:170px;", sort: () => undefined },
    ],
    noSelect: grid.dragSelecting,
    rows: drawn.map((row, r) => {
      const tone = row.id === DRAFT_ID ? null : statusTone(row.status);
      return {
        key: String(row.id),
        style: row.id === DRAFT_ID
          ? "background:#EDF5FF;border-left:3px solid #2E7DD1"
          : row.status === "VALID"
            ? (r % 2 ? "background:#FBFCFE;" : "") + "border-left:3px solid transparent"
            : `background:${tone!.bg};border-left:3px solid transparent`,
        cells: [
          ...FIELDS.map((field, c) => ({ ...toCell(row, field, r, c), ...grid.cellProps("register", r, c, true) })),
          statusCell(row),
        ],
      };
    }),
    total: shown.length,
    pageCount,
    page: at,
    per: PER,
  };

  if (failure) {
    return (
      <div style={css("background:#fff;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:6px;padding:18px")}>
        <div style={css("font-size:13px;font-weight:650;color:#B45309")}>เปิดทะเบียนอบรมไม่ได้</div>
        <div style={css("font-size:12px;color:#5A6B7D;margin-top:4px")}>{failure}</div>
        <button onClick={() => void load()} style={css("margin-top:10px;height:31px;padding:0 14px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:4px;cursor:pointer")}>ลองใหม่</button>
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {(summary.nearExpiry > 0 || summary.expired > 0) && (
        <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-left:4px solid #B45309;border-radius:6px;padding:11px 14px;color:#7A4210;font-size:12.5px;line-height:1.6")}>
          แจ้งเตือนการอบรม: ใกล้หมดอายุภายในน้อยกว่า 60 วัน {summary.nearExpiry} รายการ
          {summary.expired > 0 ? ` · หมดอายุแล้ว ${summary.expired} รายการ` : ""}
        </div>
      )}

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(135px,1fr));gap:9px")}>
        <RegisterTile label="ทั้งหมด" value={summary.total} tone="#0F2B46" />
        <RegisterTile label="ยังใช้ได้" value={summary.valid} tone="#16794C" />
        <RegisterTile label="ใกล้หมดอายุ < 60 วัน" value={summary.nearExpiry} tone="#B45309" />
        <RegisterTile label="หมดอายุแล้ว" value={summary.expired} tone="#B42318" />
        <RegisterTile label="วันที่ไม่ถูกต้อง" value={summary.invalidDate} tone="#64748B" />
      </div>

      <input ref={fileInput} type="file" accept=".xlsx,.xls,.csv" style={css("display:none")}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = "";
          if (file) void readFile(file);
        }} />

      <DataTable
        model={model}
        full={full}
        onFull={() => setFull((on) => !on)}
        onPage={(next) => { setEditing(null); setPage(next); }}
        onTool={() => undefined} />
      <GridMenu at={grid.menu} onClose={grid.closeMenu} canPaste={canEdit}
        onCopy={() => void grid.copyToClipboard()} onPaste={() => void grid.pasteFromClipboard()} />
    </div>
  );
}

function RegisterTile({ label, value, tone }: { label: string; value: number; tone: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:11px 13px")}>
      <div style={css("font-size:10px;letter-spacing:.04em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css("font-size:22px;font-weight:700;color:" + tone + ";margin-top:3px")}>{value}</div>
    </div>
  );
}
