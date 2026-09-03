"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import * as XLSX from "xlsx";
import { customerMilestones, saveMilestone } from "../flow";
import type { Job } from "../ops";
import {
  ALL_PERIOD, inPeriod, monthLabel, periodLabel, periodOptions, type Period,
} from "../period";
import { MOVEMENT_STAGE, toInstant, toTyped } from "../truckTimes";
import { cell, kilos, paginate, type Cell } from "../util";
import { css } from "../theme";
import { DataTable, type TableModel } from "../DataTable";
import { editHistoryShortcut } from "../editHistory";

/**
 * The L'OREAL truck report, in the shape the customer already receives.
 *
 * The columns and their order are taken from the workbook the customer is sent
 * every month, not invented here — so what this screen produces can be checked
 * against last month's file line for line.
 *
 * Ten of the twenty columns come straight out of the register. Six are movement
 * times — left base, arrived, loading started, loading finished, departed,
 * container returned — and until 2026-09-01 nothing could write them: the
 * `shipment_milestones` table existed with a column for each and stood empty,
 * so the report went to the customer with six blank columns every month.
 *
 * They are typed here now, in the table, by whoever is handling the job. That
 * is the request: not a second screen to visit, the report itself. Each one is
 * saved as the milestone it already corresponds to, so a time typed here is the
 * same time the Shipment Monitor shows — one record, two ways in, rather than
 * two records that will disagree by Christmas.
 *
 * What is still not editable is said on the row rather than left to be
 * discovered: PACKAGE and CARD have nowhere in the register to go, and
 * Estimated Delivery is My Job's DATE and PLAN LOADING TIME joined for the
 * customer's benefit — two fields, edited on My Job, and a single box writing
 * both would have to guess where one ends when an operator has typed a note
 * where a clock was expected.
 */

/** [header, where it comes from, how to read it out of a job] */
type Column = {
  head: string;
  source: "register" | "movement";
  read: (job: Job) => string;
  /**
   * The register field this column writes, when it writes one straight through.
   *
   * Absent on the three columns that cannot be typed here: two have no field
   * behind them at all, and one is two fields joined.
   */
  field?: keyof Job;
};

const NONE = () => "";

export const COLUMNS: Column[] = [
  { head: "Truck by", source: "register", read: (j) => j.trucker, field: "trucker" },
  { head: "JOB CODE", source: "register", read: (j) => j.jobCode, field: "jobCode" },
  { head: "PRODUCT", source: "register", read: (j) => j.product, field: "product" },
  // The workbook's own example leaves PACKAGE empty on every row, and the
  // register has no such field. Kept so the column count matches.
  { head: "PACKAGE", source: "register", read: NONE },
  { head: "TYPE", source: "register", read: (j) => j.type, field: "type" },
  { head: "CY YARD", source: "register", read: (j) => j.cyYard, field: "cyYard" },
  { head: "TOTAL WEIGHT", source: "register", read: (j) => kilos(j.weight), field: "weight" },
  { head: "NO CONTAINER", source: "register", read: (j) => j.container, field: "container" },
  { head: "CARD", source: "register", read: NONE },
  { head: "LICENCE", source: "register", read: (j) => j.licence, field: "licence" },
  { head: "DRIVER", source: "register", read: (j) => j.driver, field: "driver" },
  { head: "Estimated Delivery Time", source: "register", read: (j) => joinDateTime(j.date, j.planTime) },
  { head: "Leave base", source: "movement", read: NONE },
  /*
   * The register holds this as a sentence — `รับตู้ 31.07.26 08.00 น.` — and
   * `standard.ts` already reads it, on every load, into a date and a time. So
   * the two are joined here exactly as Estimated Delivery joins its own pair,
   * rather than parsed a second time: a rule this codebase has written twice
   * has always ended up disagreeing with itself.
   */
  {
    head: "Pick up container", source: "register", field: "pickupPlan",
    read: (j) => joinDateTime(j.pickupPlan, j.pickupTime),
  },
  { head: "Truck arrival", source: "movement", read: NONE },
  { head: "Truck loading time", source: "movement", read: NONE },
  { head: "Truck loading completed", source: "movement", read: NONE },
  { head: "Truck departure", source: "movement", read: NONE },
  { head: "Return container", source: "movement", read: NONE },
  // Writes `remark`. `reason` is the delay note, filled from the delay screen,
  // and is shown here only when there is no remark of its own.
  { head: "Remark", source: "register", read: (j) => j.remark || j.reason, field: "remark" },
];

/**
 * Kilogrammes, to two decimals.
 *
 * The register holds weights that came out of a spreadsheet division, so
 * `18459.335999999999` is a real stored value. Printed as it stands it goes
 * into the customer's file exactly like that — a number nobody wrote and
 * everybody notices. Anything that is not a number is passed through, because
 * an operator's note in the weight column is still worth carrying.
 */
// weight() lived here and in the Chemours report, character for character.
// It is `kilos` in util now, next to the other number formatting.

/**
 * A date and a time in one cell, the way the workbook writes it.
 *
 * The register's arrival time is free text as often as not — "รอรถเข้ารับ" is a
 * status somebody typed where a clock was expected. It is passed through
 * unchanged rather than parsed into something tidier, because it is what the
 * operator meant and hiding it would lose the only note on that row.
 */
function joinDateTime(date: string, time: string): string {
  const d = (date || "").trim();
  const t = (time || "").trim();
  if (!d) return t;
  return t ? `${d} ${t}` : d;
}

export const CUSTOMER = "L'OREAL";

/**
 * Rows to a page.
 *
 * High enough that this customer's month — ninety containers — is one page, so
 * the report reads the way it did before it moved onto the paged grid, and low
 * enough that a customer with a year of work does not render thousands of rows
 * at once.
 */
const PER = 200;

type ReportChange = {
  jobKey: string;
  head: string;
  source: "register" | "movement";
  field?: keyof Job;
  before: string;
  after: string;
};

const HISTORY_LIMIT = 20;
const HISTORY_BTN =
  "height:28px;padding:0 12px;border:1px solid #4E7BA8;background:transparent;color:#fff;"
  + "border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit";

export function Loreal({ jobs, onToast, canEdit, onSetField }: {
  jobs: Job[];
  onToast: (message: string) => void;
  /** The same ownership rule the workspace draws — your jobs, or your team's. */
  canEdit: (job: Job) => boolean;
  /** The register save path, so a cell typed here goes through what My Job uses. */
  onSetField: (job: Job, field: keyof Job, value: string, recordHistory?: boolean) => string;
}) {
  const [period, setPeriod] = useState<Period>(ALL_PERIOD);
  const [page, setPage] = useState(1);
  /** The grid filling the screen with everything else hidden — same as My Job. */
  const [full, setFull] = useState(false);

  /** Recorded times, keyed job then stage. Read once for the whole customer. */
  const [times, setTimes] = useState<Record<string, Record<string, string>>>({});
  /** Which cell is open for typing: the job key and the column head. */
  const [editing, setEditing] = useState<{ key: string; head: string } | null>(null);
  const [draft, setDraft] = useState("");
  /** Bumped after a save, so the times are re-read from the API rather than guessed. */
  const [revision, setRevision] = useState(0);
  /** Report-local history includes both register cells and movement milestones. */
  const [undos, setUndos] = useState<ReportChange[]>([]);
  const [redos, setRedos] = useState<ReportChange[]>([]);
  const [historyBusy, setHistoryBusy] = useState(false);
  const historyLock = useRef(false);
  /** Enter can blur the same input; one gesture must still create one save. */
  const committing = useRef(new Set<string>());

  useEffect(() => {
    let alive = true;
    (async () => {
      const rows = await customerMilestones(CUSTOMER);
      if (!alive || !rows) return;
      const map: Record<string, Record<string, string>> = {};
      for (const row of rows) {
        (map[row.jobKey] ??= {})[row.stage] = toTyped(row.actualAt);
      }
      setTimes(map);
    })();
    return () => { alive = false; };
  }, [revision]);

  const mine = useMemo(
    () => jobs.filter((job) => job.customer.trim().toUpperCase() === CUSTOMER),
    [jobs],
  );

  /**
   * Each job paired with the date the report is about.
   *
   * The report period follows the same My Job DATE shown in Estimated Delivery.
   * Filtering by arrival while printing the plan date would put a row under a
   * different month from the date the customer sees in that row.
   */
  const dated = useMemo(
    () => mine.map((job) => ({ job, keyed: job })),
    [mine],
  );

  // What each dropdown offers, narrowed by the ones above it: picking 2026 then
  // offers only that year's months, and a month only its own days.
  const options = useMemo(
    () => periodOptions(dated.map((one) => one.keyed), period), [dated, period]);

  const rows = useMemo(
    () => dated.filter((one) => inPeriod(one.keyed, period)).map((one) => one.job),
    [dated, period]);

  /** Changing a wider box clears the narrower ones, which no longer apply. */
  function choose(patch: Partial<Period>) {
    // Or a narrower period leaves the pager on a page that no longer exists.
    setPage(1);
    setPeriod((was) => ({
      ...was,
      ...patch,
      ...(patch.year !== undefined ? { month: "ALL", day: "ALL" } : {}),
      ...(patch.month !== undefined ? { day: "ALL" } : {}),
    }));
  }

  /** What a cell shows: the register for most of it, the recorded time for six. */
  function show(job: Job, column: Column): string {
    return column.source === "movement"
      ? times[job.key]?.[MOVEMENT_STAGE[column.head]] ?? ""
      : column.read(job);
  }

  /** Whether this cell can be typed in at all, and why not when it cannot. */
  function why(job: Job, column: Column): string {
    if (column.source === "register" && !column.field) {
      return column.head === "Estimated Delivery Time"
        ? "ดึงจาก DATE และ PLAN LOADING TIME — แก้ที่หน้า My Job (เป็นสองช่อง)"
        : "ยังไม่มีช่องเก็บค่านี้ในระบบ";
    }
    if (!canEdit(job)) return "งานของ " + (job.op || "คนอื่น") + " — แก้ไขไม่ได้";
    return "";
  }

  function remember(change: ReportChange) {
    if (change.before === change.after) return;
    setUndos((was) => was.slice(-(HISTORY_LIMIT - 1)).concat([change]));
    // A new edit starts a new branch, exactly as it does in My Job and Excel.
    setRedos([]);
  }

  function showMovement(jobKey: string, stage: string, value: string) {
    setTimes((was) => {
      const row = { ...(was[jobKey] ?? {}) };
      if (value) row[stage] = value;
      else delete row[stage];
      return { ...was, [jobKey]: row };
    });
  }

  /** Save a movement value and return the canonical value shown by the report. */
  async function writeMovement(job: Job, column: Column, typed: string, announce: boolean) {
    const stage = MOVEMENT_STAGE[column.head];
    const entered = typed.trim();
    const at = entered.length === 0 ? null : toInstant(entered);
    if (at === null && entered.length > 0) {
      onToast(`${column.head}: อ่านเป็นเวลาไม่ได้ — ใช้รูปแบบ วว/ดด/ปปปป ชช:นน เช่น 01/07/2026 08:30`);
      return { ok: false, saved: "" };
    }

    const answer = await saveMilestone(job.key, {
      stage,
      status: at === null ? "pending" : "done",
      actualAt: at,
    });
    if (answer?.ok === false) {
      onToast(answer.message || "บันทึกไม่สำเร็จ");
      return { ok: false, saved: "" };
    }

    const saved = at === null ? "" : toTyped(at);
    showMovement(job.key, stage, saved);
    setRevision((turn) => turn + 1);
    if (announce) {
      onToast(`${column.head} · ${job.jobCode || job.container || job.key}: ${saved || "ล้างค่าแล้ว"}`);
    }
    return { ok: true, saved };
  }

  async function commit(job: Job, column: Column, typed: string) {
    const token = job.key + "\u0000" + column.head;
    if (committing.current.has(token)) return;
    committing.current.add(token);
    try {
      setEditing(null);
      const was = show(job, column);
      if (typed.trim() === was.trim()) return;

      if (column.source === "register") {
        const field = column.field!;
        const before = String(job[field] ?? "");
        const saved = onSetField(job, field, typed, false);
        remember({
          jobKey: job.key, head: column.head, source: "register", field,
          before, after: saved,
        });
        return;
      }

      const written = await writeMovement(job, column, typed, true);
      if (!written.ok) return;
      remember({
        jobKey: job.key, head: column.head, source: "movement",
        before: was, after: written.saved,
      });
    } finally {
      committing.current.delete(token);
    }
  }

  async function moveHistory(change: ReportChange, direction: "undo" | "redo") {
    if (historyLock.current) return false;
    historyLock.current = true;
    setHistoryBusy(true);
    try {
      const job = jobs.find((candidate) => candidate.key === change.jobKey);
      if (!job) { onToast("ไม่พบงานนี้แล้ว — ไม่สามารถย้อนประวัติได้"); return false; }
      if (!canEdit(job)) { onToast("แก้ไม่ได้ — งานนี้เป็นของ " + (job.op || "ผู้อื่น")); return false; }

      const wanted = direction === "undo" ? change.before : change.after;
      if (change.source === "register") {
        onSetField(job, change.field!, wanted, false);
      } else {
        const column = COLUMNS.find((candidate) => candidate.head === change.head);
        if (!column) { onToast("ไม่พบคอลัมน์เดิมแล้ว — ไม่สามารถย้อนประวัติได้"); return false; }
        const written = await writeMovement(job, column, wanted, false);
        if (!written.ok) return false;
      }

      onToast(
        (direction === "undo" ? "ย้อนกลับแล้ว ↶ " : "กลับไปข้างหน้าแล้ว ↷ ")
        + change.head + " · " + (job.jobCode || job.container || job.key),
      );
      return true;
    } finally {
      historyLock.current = false;
      setHistoryBusy(false);
    }
  }

  async function undo() {
    const change = undos[undos.length - 1];
    if (!change) { onToast("ไม่มีการแก้ไขให้ย้อนกลับ"); return; }
    if (!await moveHistory(change, "undo")) return;
    setUndos((was) => was.slice(0, -1));
    setRedos((was) => was.slice(-(HISTORY_LIMIT - 1)).concat([change]));
  }

  async function redo() {
    const change = redos[redos.length - 1];
    if (!change) { onToast("ไม่มีข้อมูลให้ไปข้างหน้า"); return; }
    if (!await moveHistory(change, "redo")) return;
    setRedos((was) => was.slice(0, -1));
    setUndos((was) => was.slice(-(HISTORY_LIMIT - 1)).concat([change]));
  }

  /** The report keeps the same keyboard contract as My Job. */
  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const command = editHistoryShortcut(event);
      if (!command || historyLock.current) return;

      const active = document.activeElement as HTMLElement | null;
      const tag = active?.tagName;
      if (tag === "TEXTAREA" || tag === "SELECT" || active?.isContentEditable) return;
      if (tag === "INPUT") {
        if (!active?.closest?.("td") || !editing) return;
        const input = active as HTMLInputElement;
        if (event.key.toLowerCase() === "x" && input.selectionStart !== input.selectionEnd) return;
        const job = jobs.find((candidate) => candidate.key === editing.key);
        const column = COLUMNS.find((candidate) => candidate.head === editing.head);
        if (!job || !column || draft !== show(job, column)) return;
        setEditing(null);
      }

      event.preventDefault();
      if (command === "undo") void undo();
      else void redo();
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  const missing = COLUMNS.filter(
    (column) => column.source === "movement"
      && rows.every((job) => !show(job, column))).length;


  const pg = paginate(rows, page, PER);

  /**
   * One cell, either showing a value or open for typing.
   *
   * Built through the grid's own cell kinds rather than as bespoke markup, so
   * this table gets the frozen header, the zoom and the full screen that My Job
   * has by being the same table rather than a second one that looks like it.
   */
  function toCell(job: Job, column: Column): Cell {
    const value = show(job, column);
    const refused = why(job, column);
    const movement = column.source === "movement";

    if (editing?.key === job.key && editing.head === column.head) {
      return {
        kind: "input",
        v: draft,
        value: draft,
        td: "padding:2px 6px;border-bottom:1px solid #EDF1F5;vertical-align:middle;",
        sp: "",
        inpStyle: "width:100%;min-width:120px;height:26px;border:1px solid #2E7DD1;border-radius:3px;"
          + "padding:0 6px;font-size:12px;font-family:inherit;outline:none",
        onChange: (event) => setDraft(event.target.value),
        onBlur: () => { void commit(job, column, draft); },
        onKey: (event) => {
          if (event.key === "Enter") { event.preventDefault(); void commit(job, column, draft); }
          // Escape closes the cell before the blur that follows, so the draft
          // is not written on the way out.
          if (event.key === "Escape") setEditing(null);
        },
      };
    }

    const base = cell(value, { mono: movement, mute: !value });
    return {
      ...base,
      td: base.td + (movement ? "background:#FDFAF5;" : "") + (refused ? "cursor:default;" : "cursor:text;"),
      title: refused || (movement ? "คลิกเพื่อกรอกเวลา — วว/ดด/ปปปป ชช:นน" : "คลิกเพื่อแก้ไข"),
      go: () => {
        if (refused) { if (!canEdit(job)) onToast(refused); return; }
        setDraft(value);
        setEditing({ key: job.key, head: column.head });
      },
    };
  }

  const model: TableModel = {
    title: "Truck Report",
    meta: `${rows.length} ตู้ · ${periodLabel(period)} · ${CUSTOMER}`,
    // Not through `cols()`: that one makes every header a sort button, and
    // nothing here sorts. A header that looks clickable and does nothing is
    // worse than a plain one.
    cols: COLUMNS.map((column) => ({
      label: column.head,
      style: "position:sticky;top:0;z-index:2;padding:6px 11px;text-align:left;font-size:10px;"
        + "letter-spacing:.04em;text-transform:uppercase;font-weight:600;white-space:nowrap;"
        + "border-bottom:1px solid #D8E0E8;user-select:none;background:"
        + (column.source === "movement" ? "#FDF6EC" : "#F4F7FA")
        + ";color:" + (column.source === "movement" ? "#B45309" : "#465A6E"),
      sort: () => undefined,
    })),
    rows: pg.slice.map((job) => ({
      key: job.key,
      style: "",
      cells: COLUMNS.map((column) => toCell(job, column)),
    })),
    total: pg.total,
    pageCount: pg.pageCount,
    page: pg.p,
    per: pg.per,
    tools: [],
    fill: true,
    actions: [
      ...(undos.length > 0 ? [{
        label: "↶ ย้อนกลับ",
        title: "ย้อนกลับ (Ctrl+Z)",
        disabled: historyBusy,
        style: HISTORY_BTN,
        go: () => { void undo(); },
      }] : []),
      ...(redos.length > 0 ? [{
        label: "↷ ถัดไป",
        title: "กลับไปข้างหน้า (Ctrl+X หรือ Ctrl+Y)",
        disabled: historyBusy,
        style: HISTORY_BTN,
        go: () => { void redo(); },
      }] : []),
      {
      label: "ดาวน์โหลด Excel",
      style: "height:28px;padding:0 13px;border:1px solid #3FA372;background:#16794C;color:#fff;"
        + "border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit",
      go: () => downloadWorkbook(rows, periodLabel(period), onToast),
      },
    ],
    controls: (
      <div style={css("display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap;margin-top:8px")}>
        <Picker label="ปี" value={period.year} onPick={(year) => choose({ year })}
          all={`ทุกปี · ${mine.length} ตู้`}
          options={options.years.map((year) => [year, year])} />

        <Picker label="เดือน" value={period.month} onPick={(month) => choose({ month })}
          all="ทุกเดือน" options={options.months.map((mm) => [mm, monthLabel(mm)])} />

        <Picker label="วันที่" value={period.day} onPick={(day) => choose({ day })}
          all="ทุกวัน" options={options.days.map((dd) => [dd, dd])} />

        <span style={css("font-size:11.5px;color:#B9CFE5;padding-bottom:7px;line-height:1.6")}>
          {missing > 0
            ? `${missing} ช่องเวลาเดินรถยังว่างทุกแถว — คลิกที่ช่องเพื่อกรอก · วว/ดด/ปปปป ชช:นน`
            : "เวลาเดินรถกรอกครบทุกช่องแล้ว — คลิกที่ช่องเพื่อแก้ไข"}
          {options.undated > 0 && ` · ${options.undated} ตู้ไม่มีวันที่ที่อ่านได้`}
        </span>
      </div>
    ),
  };

  return (
    <div style={css("display:flex;flex-direction:column;gap:11px;min-height:0;flex:1")}>
      {/* Said once, above the grid: what these times are and where they go.
          Hidden in full screen, which is the one place every pixel is the
          table's. */}
      {!full && (
        <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:5px;padding:10px 15px;font-size:12px;color:#8A5A12;line-height:1.6")}>
          เวลาที่กรอกที่นี่บันทึกลงเป็นขั้นตอนเดินรถของงานนั้น (<code style={css("font-family:ui-monospace,monospace")}>shipment_milestones</code>)
          จึงเป็นค่าเดียวกับที่หน้า Shipment Monitor แสดง ไม่ใช่ข้อมูลคนละชุด ·
          เวลาที่กรอกถือตามเวลาไทย (+07:00) เสมอ ไม่ขึ้นกับนาฬิกาของเครื่องที่เปิด ·
          ช่อง PACKAGE, CARD และ Estimated Delivery ยังแก้ที่นี่ไม่ได้ — ดูคำอธิบายเมื่อชี้ที่ช่อง
        </div>
      )}

      <div className="grid-only" style={css("flex:1;min-height:0;display:flex;flex-direction:column")}>
        <DataTable model={model} full={full} onFull={() => setFull((on) => !on)}
          onPage={setPage} onTool={() => undefined} />
      </div>
    </div>
  );
}

/**
 * A period as something Windows will accept as part of a filename.
 *
 * Keeps letters, digits, spaces, dots and dashes and replaces the rest, so the
 * Thai month names come through intact and the slashes in a date do not become
 * folders.
 */
function safeName(scope: string): string {
  return scope.replace(/[^\p{L}\p{N} .-]/gu, "-").trim() || "all";
}

/**
 * One of the three period boxes.
 *
 * Every option carries a value the register actually holds, so a month with no
 * containers is not offered and cannot be chosen into an empty table.
 */
function Picker({ label, value, all, options, onPick }: {
  label: string;
  value: string;
  /** What the "no narrowing" option reads as. */
  all: string;
  options: [string, string][];
  onPick: (value: string) => void;
}) {
  return (
    <div style={css("display:flex;flex-direction:column;gap:3px")}>
      <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</span>
      <select value={value} onChange={(event) => onPick(event.target.value)}
        style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff;min-width:110px")}>
        <option value="ALL">{all}</option>
        {options.map(([key, shown]) => <option key={key} value={key}>{shown}</option>)}
      </select>
    </div>
  );
}

/**
 * The workbook, in the customer's own column order.
 *
 * Written with the same `COLUMNS` the table renders from, so the file and the
 * screen cannot drift apart — the failure this project has hit more than once
 * is the same rule written twice and quietly disagreeing.
 */
function downloadWorkbook(rows: Job[], scope: string, onToast: (message: string) => void) {
  try {
    const sheet = XLSX.utils.aoa_to_sheet([
      COLUMNS.map((column) => column.head),
      ...rows.map((job) => COLUMNS.map((column) => column.read(job))),
    ]);
    sheet["!cols"] = COLUMNS.map((column) => ({ wch: Math.min(Math.max(column.head.length + 3, 12), 22) }));
    sheet["!freeze"] = { xSplit: "0", ySplit: "1" };

    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, CUSTOMER);
    // The period in the filename, so two months on a desk are told apart.
    XLSX.writeFile(book, `Truck Report Loreal ${safeName(scope)}.xlsx`);
    onToast(`ดาวน์โหลดแล้ว ${rows.length} ตู้`);
  } catch (error) {
    onToast("สร้างไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
  }
}
