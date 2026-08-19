"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import type { Job } from "../ops";
import { css } from "../theme";

/**
 * The L'OREAL truck report, in the shape the customer already receives.
 *
 * The columns and their order are taken from the workbook the customer is sent
 * every month, not invented here — so what this screen produces can be checked
 * against last month's file line for line.
 *
 * Ten of the twenty columns come straight out of the register. The rest are
 * movement times — left base, arrived, loading, departed, container returned —
 * and the honest answer today is that nothing records them yet: the
 * `shipment_milestones` table exists, with a column for each, and is empty. So
 * this screen leaves those cells blank and says so at the top, rather than
 * filling them with the nearest field that happens to hold a time. A report
 * that quietly prints the wrong timestamp is worse than one that prints none:
 * the blank gets chased, the wrong number gets sent to the customer.
 */

/** [header, where it comes from, how to read it out of a job] */
type Column = {
  head: string;
  source: "register" | "movement";
  read: (job: Job) => string;
};

const NONE = () => "";

export const COLUMNS: Column[] = [
  { head: "Truck by", source: "register", read: (j) => j.trucker },
  { head: "JOB CODE", source: "register", read: (j) => j.jobCode },
  { head: "PRODUCT", source: "register", read: (j) => j.product },
  // The workbook's own example leaves PACKAGE empty on every row, and the
  // register has no such field. Kept so the column count matches.
  { head: "PACKAGE", source: "register", read: NONE },
  { head: "TYPE", source: "register", read: (j) => j.type },
  { head: "CY YARD", source: "register", read: (j) => j.cyYard },
  { head: "TOTAL WEIGHT", source: "register", read: (j) => weight(j.weight) },
  { head: "NO CONTAINER", source: "register", read: (j) => j.container },
  { head: "CARD", source: "register", read: NONE },
  { head: "LICENCE", source: "register", read: (j) => j.licence },
  { head: "DRIVER", source: "register", read: (j) => j.driver },
  { head: "Estimated Delivery", source: "register", read: (j) => joinDateTime(j.arrDate || j.date, j.arrTime) },
  { head: "Leave base", source: "movement", read: NONE },
  { head: "Pick up container", source: "register", read: (j) => j.pickupPlan },
  { head: "Truck arrival", source: "movement", read: NONE },
  { head: "Truck loading time", source: "movement", read: NONE },
  { head: "Truck loading comp", source: "movement", read: NONE },
  { head: "Truck departure", source: "movement", read: NONE },
  { head: "Return container", source: "movement", read: NONE },
  { head: "Remark", source: "register", read: (j) => j.remark || j.reason },
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
function weight(value: string): string {
  const raw = (value || "").trim();
  if (!raw) return "";
  const number = Number(raw.replace(/,/g, ""));
  return Number.isFinite(number)
    ? number.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    : raw;
}

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

export function Loreal({ jobs, onToast }: { jobs: Job[]; onToast: (message: string) => void }) {
  const [month, setMonth] = useState("ALL");

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

  const missing = COLUMNS.filter((column) => column.source === "movement").length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:center;flex-wrap:wrap")}>
        <div style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>เดือน</span>
          <select value={month} onChange={(event) => setMonth(event.target.value)}
            style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff")}>
            <option value="ALL">ทุกเดือน · {mine.length} ตู้</option>
            {months.map((key) => (
              <option key={key} value={key}>{monthLabel(key)} · {mine.filter((j) => monthOf(j) === key).length} ตู้</option>
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
        <b>{missing} ช่องเวลาเดินรถยังว่างทุกแถว</b> — Leave base, Truck arrival, Truck loading time,
        Truck loading comp, Truck departure, Return container
        <br />
        ตารางที่รองรับค่าพวกนี้มีอยู่แล้ว (<code style={css("font-family:ui-monospace,monospace")}>shipment_milestones</code>)
        แต่ยังไม่มีข้อมูลสักแถว จะเต็มเองเมื่อหน้า Shipment Monitor เริ่มบันทึกเวลาจริง —
        ระบบเว้นว่างไว้ ไม่เดาจากช่องอื่นที่บังเอิญเป็นเวลา
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
                  const value = column.read(job);
                  return (
                    <td key={column.head}
                      style={css("padding:6px 10px;border-bottom:1px solid #F1F5F9;color:" +
                        (value ? "#243B53" : "#C3CFDB") +
                        (column.source === "movement" ? ";background:#FDFAF5" : ""))}>
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
function monthOf(job: Job): string {
  const raw = (job.arrDate || job.date || "").trim();
  const parts = raw.split("/");
  return parts.length === 3 ? `${parts[2]}-${parts[1]}` : "";
}

function monthLabel(key: string): string {
  const [year, month] = key.split("-");
  const names = ["ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.",
    "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."];
  const index = Number(month) - 1;
  return `${names[index] ?? month} ${year}`;
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
