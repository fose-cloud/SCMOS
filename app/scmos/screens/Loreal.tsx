"use client";

import { useEffect, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { customerMilestones, saveMilestone } from "../flow";
import type { Job } from "../ops";
import { monthKey, monthKeyLabel } from "../period";
import { MOVEMENT_STAGE, toInstant, toTyped } from "../truckTimes";
import { kilos } from "../util";
import { css } from "../theme";

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
 * Estimated Delivery is the arrival date and time joined for the customer's
 * benefit — two fields, edited on My Job, and a single box writing both would
 * have to guess where one ends when an operator has typed a note where a clock
 * was expected.
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
  { head: "Estimated Delivery", source: "register", read: (j) => joinDateTime(j.arrDate || j.date, j.arrTime) },
  { head: "Leave base", source: "movement", read: NONE },
  { head: "Pick up container", source: "register", read: (j) => j.pickupPlan, field: "pickupPlan" },
  { head: "Truck arrival", source: "movement", read: NONE },
  { head: "Truck loading time", source: "movement", read: NONE },
  { head: "Truck loading comp", source: "movement", read: NONE },
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

export function Loreal({ jobs, onToast, canEdit, onSetField }: {
  jobs: Job[];
  onToast: (message: string) => void;
  /** The same ownership rule the workspace draws — your jobs, or your team's. */
  canEdit: (job: Job) => boolean;
  /** The register save path, so a cell typed here goes through what My Job uses. */
  onSetField: (job: Job, field: keyof Job, value: string) => void;
}) {
  const [month, setMonth] = useState("ALL");

  /** Recorded times, keyed job then stage. Read once for the whole customer. */
  const [times, setTimes] = useState<Record<string, Record<string, string>>>({});
  /** Which cell is open for typing: the job key and the column head. */
  const [editing, setEditing] = useState<{ key: string; head: string } | null>(null);
  const [draft, setDraft] = useState("");
  /** Bumped after a save, so the times are re-read from the API rather than guessed. */
  const [revision, setRevision] = useState(0);

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

  const months = useMemo(() => {
    const seen = new Set<string>();
    for (const job of mine) {
      const key = monthOf(job);
      if (key) seen.add(key);
    }
    return [...seen].sort();
  }, [mine]);

  const rows = useMemo(
    () => (month === "ALL" ? mine : mine.filter((job) => monthOf(job) === month)),
    [mine, month],
  );

  /** What a cell shows: the register for most of it, the recorded time for six. */
  function show(job: Job, column: Column): string {
    return column.source === "movement"
      ? times[job.key]?.[MOVEMENT_STAGE[column.head]] ?? ""
      : column.read(job);
  }

  /** Whether this cell can be typed in at all, and why not when it cannot. */
  function why(job: Job, column: Column): string {
    if (column.source === "register" && !column.field) {
      return column.head === "Estimated Delivery"
        ? "วันและเวลาที่รถถึง — แก้ที่หน้า My Job (เป็นสองช่อง)"
        : "ยังไม่มีช่องเก็บค่านี้ในระบบ";
    }
    if (!canEdit(job)) return "งานของ " + (job.op || "คนอื่น") + " — แก้ไขไม่ได้";
    return "";
  }

  async function commit(job: Job, column: Column, typed: string) {
    setEditing(null);
    const was = show(job, column);
    if (typed.trim() === was.trim()) return;

    if (column.source === "register") {
      onSetField(job, column.field!, typed);
      return;
    }

    const stage = MOVEMENT_STAGE[column.head];
    // Blank clears the time; anything else has to be a time. Refused rather
    // than saved as midnight — this file goes to the customer.
    const at = typed.trim().length === 0 ? null : toInstant(typed);
    if (at === null && typed.trim().length > 0) {
      onToast(`${column.head}: อ่านเป็นเวลาไม่ได้ — ใช้รูปแบบ วว/ดด/ปปปป ชช:นน เช่น 01/07/2026 08:30`);
      return;
    }

    const answer = await saveMilestone(job.key, {
      stage,
      status: at === null ? "pending" : "done",
      actualAt: at,
    });
    if (answer?.ok === false) { onToast(answer.message || "บันทึกไม่สำเร็จ"); return; }
    onToast(`${column.head} · ${job.jobCode || job.container || job.key}: ${at === null ? "ล้างค่าแล้ว" : typed}`);
    setRevision((turn) => turn + 1);
  }

  const missing = COLUMNS.filter(
    (column) => column.source === "movement"
      && rows.every((job) => !show(job, column))).length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:center;flex-wrap:wrap")}>
        <div style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>เดือน</span>
          <select value={month} onChange={(event) => setMonth(event.target.value)}
            style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff")}>
            <option value="ALL">ทุกเดือน · {mine.length} ตู้</option>
            {months.map((key) => (
              <option key={key} value={key}>{monthKeyLabel(key)} · {mine.filter((j) => monthOf(j) === key).length} ตู้</option>
            ))}
          </select>
        </div>

        <div style={css("flex:1;min-width:180px;font-size:12.5px;color:#5A6B7D;line-height:1.6")}>
          <b style={css("color:#0F2B46")}>{rows.length}</b> ตู้ในรายงาน · ดึงจากทะเบียนงานจริง ลูกค้า {CUSTOMER}
        </div>

        <button
          onClick={() => downloadWorkbook(rows, month, onToast)}
          disabled={rows.length === 0}
          style={css("height:31px;padding:0 15px;border:1px solid #16794C;background:" +
            (rows.length === 0 ? "#C3CFDB" : "#16794C") +
            ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
          ดาวน์โหลด Excel
        </button>
      </div>

      {/* Said once, at the top, in the same words every time: which columns the
          system cannot fill yet, and what would fill them. */}
      <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:5px;padding:12px 15px;font-size:12.5px;color:#8A5A12;line-height:1.65")}>
        {missing > 0
          ? <><b>{missing} ช่องเวลาเดินรถยังว่างทุกแถวในเดือนนี้</b> — คลิกที่ช่องเพื่อกรอกได้เลย
            รูปแบบ วว/ดด/ปปปป ชช:นน เช่น 01/07/2026 08:30</>
          : <><b>เวลาเดินรถกรอกครบทุกช่องแล้ว</b> — คลิกที่ช่องเพื่อแก้ไขได้</>}
        <br />
        เวลาที่กรอกที่นี่บันทึกลงเป็นขั้นตอนเดินรถของงานนั้น (<code style={css("font-family:ui-monospace,monospace")}>shipment_milestones</code>)
        จึงเป็นค่าเดียวกับที่หน้า Shipment Monitor แสดง ไม่ใช่ข้อมูลคนละชุด ·
        เวลาที่กรอกถือตามเวลาไทย (+07:00) เสมอ ไม่ขึ้นกับนาฬิกาของเครื่องที่เปิด ·
        ช่อง PACKAGE, CARD และ Estimated Delivery ยังแก้ที่นี่ไม่ได้ — ดูคำอธิบายเมื่อชี้ที่ช่อง
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow-x:auto")}>
        <table style={css("border-collapse:collapse;font-size:11.5px;white-space:nowrap")}>
          <thead>
            <tr>
              {COLUMNS.map((column) => (
                <th key={column.head}
                  title={column.source === "movement" ? "ยังไม่มีแหล่งข้อมูล" : undefined}
                  style={css("background:" + (column.source === "movement" ? "#FDF6EC" : "#F8FAFC") +
                    ";padding:8px 10px;text-align:left;font-size:10px;letter-spacing:.04em;text-transform:uppercase;color:" +
                    (column.source === "movement" ? "#B45309" : "#7B8CA0") +
                    ";font-weight:600;border-bottom:1px solid #E9EFF5;position:sticky;top:0")}>
                  {column.head}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((job, index) => (
              <tr key={job.key} style={css(index % 2 ? "background:#FBFCFD" : "")}>
                {COLUMNS.map((column) => {
                  const value = show(job, column);
                  const refused = why(job, column);
                  const open = editing?.key === job.key && editing.head === column.head;

                  if (open) {
                    return (
                      <td key={column.head} style={css("padding:2px 4px;border-bottom:1px solid #F1F5F9")}>
                        <input
                          // Same reason as the workspace grid: the box only
                          // exists because the cell was just clicked into, so
                          // the focus follows the click rather than stealing it.
                          // eslint-disable-next-line jsx-a11y/no-autofocus
                          autoFocus
                          value={draft}
                          onChange={(event) => setDraft(event.target.value)}
                          onBlur={() => void commit(job, column, draft)}
                          onKeyDown={(event) => {
                            if (event.key === "Enter") { event.preventDefault(); void commit(job, column, draft); }
                            // Escape puts the cell back without writing. The blur
                            // that follows must not then save the draft, so the
                            // cell is closed before the field loses focus.
                            if (event.key === "Escape") { setEditing(null); }
                          }}
                          placeholder={column.source === "movement" ? "01/07/2026 08:30" : ""}
                          style={css("width:100%;min-width:120px;height:26px;border:1px solid #2E7DD1;border-radius:3px;"
                            + "padding:0 6px;font-size:11.5px;font-family:inherit;outline:none")}
                        />
                      </td>
                    );
                  }

                  return (
                    <td key={column.head}
                      title={refused || (column.source === "movement"
                        ? "คลิกเพื่อกรอกเวลา — วว/ดด/ปปปป ชช:นน"
                        : "คลิกเพื่อแก้ไข")}
                      onClick={() => {
                        if (refused) { if (canEdit(job) === false) onToast(refused); return; }
                        setDraft(value);
                        setEditing({ key: job.key, head: column.head });
                      }}
                      style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;color:" +
                        (value ? "#243B53" : "#C3CFDB") +
                        (column.source === "movement" ? ";background:#FDFAF5" : "") +
                        (refused ? ";cursor:default" : ";cursor:text"))}>
                      {value || "—"}
                    </td>
                  );
                })}
              </tr>
            ))}
            {rows.length === 0 && (
              <tr>
                <td colSpan={COLUMNS.length} style={css("padding:26px;text-align:center;color:#7B8CA0;font-size:12.5px")}>
                  ไม่มีงานของ {CUSTOMER} ในช่วงที่เลือก
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}

/** `MM/YYYY` off whichever date the row actually carries. */
/** This report groups by the arrival, falling back to the plan date. */
function monthOf(job: Job): string {
  return monthKey(job.arrDate || job.date);
}

/**
 * The workbook, in the customer's own column order.
 *
 * Written with the same `COLUMNS` the table renders from, so the file and the
 * screen cannot drift apart — the failure this project has hit more than once
 * is the same rule written twice and quietly disagreeing.
 */
function downloadWorkbook(rows: Job[], month: string, onToast: (message: string) => void) {
  try {
    const sheet = XLSX.utils.aoa_to_sheet([
      COLUMNS.map((column) => column.head),
      ...rows.map((job) => COLUMNS.map((column) => column.read(job))),
    ]);
    sheet["!cols"] = COLUMNS.map((column) => ({ wch: Math.min(Math.max(column.head.length + 3, 12), 22) }));
    sheet["!freeze"] = { xSplit: "0", ySplit: "1" };

    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, CUSTOMER);
    XLSX.writeFile(book, `Truck Report Loreal ${month === "ALL" ? "all" : month}.xlsx`);
    onToast(`ดาวน์โหลดแล้ว ${rows.length} ตู้`);
  } catch (error) {
    onToast("สร้างไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
  }
}
