"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";
import type { Job } from "../ops";
import { monthKey, monthKeyLabel } from "../period";
import { dnum, kilos } from "../util";

/**
 * The Chemours delivery details, in the shape the account already receives.
 *
 * Taken column for column from `Del details-CHEM-(DTT)`: the workbook the
 * customer is sent, so what this produces can be checked against last month's
 * file line by line rather than argued about.
 *
 * Unlike the L'OREAL report, every column here has a real source. This is a
 * delivery run — a warehouse, a job number, a pick-up date, pallets and kilos,
 * a count of vehicles by size — and the register already stores all of it,
 * because DELIVERY jobs were modelled on this very sheet. Nothing is blank
 * because nothing is missing.
 *
 * Which jobs belong to the account is the one thing the register cannot answer
 * on its own. The workbook says "Ware house : UNITHAI" in its header and the job
 * numbers run LSTH_U_…, so the warehouse is what identifies them — but that is
 * read off one month's file, not off a rule anybody wrote down. It is a visible
 * control rather than a hidden constant for exactly that reason: if UNITHAI is
 * the wrong answer, it is one dropdown away from the right one instead of a
 * silent filter nobody can see.
 */

type Column = { head: string; sub?: string; read: (job: Job) => string; align?: "right" };

/** Numbers as the workbook writes them: grouped, and blank rather than zero. */
function count(value: string | undefined): string {
  const raw = (value ?? "").trim();
  if (!raw) return "";
  const number = Number(raw.replace(/,/g, ""));
  if (!Number.isFinite(number) || number === 0) return raw === "0" ? "" : raw;
  return number.toLocaleString("en-US");
}

export const COLUMNS: Column[] = [
  { head: "W/H", read: (j) => j.wh ?? "" },
  { head: "JOB NO.", read: (j) => j.jobCode || j.jobNo || "" },
  { head: "Pick-Up Date", read: (j) => j.date },
  { head: "SID NO.", read: (j) => j.sid ?? "" },
  { head: "Customer List", read: (j) => j.customer },
  { head: "Province", read: (j) => j.province ?? "" },
  { head: "ZIP CODE", read: (j) => j.zip ?? "" },
  { head: "QTY", sub: "PALLET", read: (j) => count(j.pallet), align: "right" },
  { head: "QTY", sub: "KGS.", read: (j) => kilos(j.weight || j.kgs), align: "right" },
  { head: "TYPE of Vehicle", sub: "4W", read: (j) => count(j.v4), align: "right" },
  { head: "TYPE of Vehicle", sub: "6W", read: (j) => count(j.v6), align: "right" },
  { head: "TYPE of Vehicle", sub: "10W", read: (j) => count(j.v10), align: "right" },
  { head: "TYPE of Vehicle", sub: "TRAILER", read: (j) => count(j.vtr), align: "right" },
  { head: "Transportation", read: (j) => count(j.cost), align: "right" },
  { head: "Remark", read: (j) => j.remark },
];

/** This report groups by the pick-up date, which is the only date it carries. */
const monthOf = (job: Job) => monthKey(job.date);

export function Chemours({ jobs, onToast }: { jobs: Job[]; onToast: (message: string) => void }) {
  const [warehouse, setWarehouse] = useState("UNITHAI");
  const [month, setMonth] = useState("ALL");

  const deliveries = useMemo(() => jobs.filter((job) => job.cat === "DELIVERY"), [jobs]);

  /**
   * Every warehouse the delivery jobs actually name, with how many each holds.
   *
   * Counted in one pass. The dropdown showed the count beside each name by
   * filtering the whole delivery list again per option, which is a full scan
   * per warehouse on every render of a screen whose whole job is to be looked
   * at while somebody changes the filter.
   */
  const warehouses = useMemo(() => {
    const tally = new Map<string, number>();
    deliveries.forEach((job) => {
      const name = (job.wh ?? "").trim().toUpperCase();
      if (name) tally.set(name, (tally.get(name) ?? 0) + 1);
    });
    return [...tally.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [deliveries]);

  const mine = useMemo(
    () => (warehouse === "ALL"
      ? deliveries
      : deliveries.filter((job) => (job.wh ?? "").trim().toUpperCase() === warehouse)),
    [deliveries, warehouse],
  );

  const months = useMemo(() => {
    const tally = new Map<string, number>();
    mine.forEach((job) => {
      const key = monthOf(job);
      if (key) tally.set(key, (tally.get(key) ?? 0) + 1);
    });
    return [...tally.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [mine]);

  const rows = useMemo(
    () => (month === "ALL" ? mine : mine.filter((job) => monthOf(job) === month))
      .slice()
      .sort((a, b) => dnum(a.date) - dnum(b.date)),
    [mine, month],
  );

  /** The one figure the account checks first. */
  const total = useMemo(
    () => rows.reduce((sum, job) => {
      const value = Number((job.cost ?? "").replace(/,/g, ""));
      return sum + (Number.isFinite(value) ? value : 0);
    }, 0),
    [rows],
  );

  function exportSheet() {
    if (!rows.length) { onToast("ไม่มีงานให้ส่งออกในมุมมองนี้"); return; }
    // Two header rows, as the workbook has them: the grouped heading and the
    // sub-heading underneath. Written as plain rows rather than merged cells —
    // the customer reads the file, and a merge that survives one Excel version
    // and not the next is not worth the risk.
    const head = COLUMNS.map((column) => column.head);
    const sub = COLUMNS.map((column) => column.sub ?? "");
    const body = rows.map((job) => COLUMNS.map((column) => column.read(job)));
    const sheet = XLSX.utils.aoa_to_sheet([head, sub, ...body]);
    sheet["!cols"] = COLUMNS.map((column) => ({ wch: Math.max(10, column.head.length + 4) }));
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, "Del details-CHEM");
    const scope = (warehouse === "ALL" ? "ALL" : warehouse) + "_" + (month === "ALL" ? "ALL" : month);
    const name = `Del_details_CHEM_${scope}.xlsx`;
    XLSX.writeFile(book, name);
    onToast(`ส่งออก ${rows.length} รายการแล้ว · ${name}`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <div style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>คลังสินค้า</span>
          <select value={warehouse} onChange={(e) => setWarehouse(e.target.value)}
            style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff")}>
            <option value="UNITHAI">
              UNITHAI · {warehouses.find(([name]) => name === "UNITHAI")?.[1] ?? 0} รายการ
            </option>
            {warehouses.filter(([name]) => name !== "UNITHAI").map(([name, held]) => (
              <option key={name} value={name}>{name} · {held} รายการ</option>
            ))}
            <option value="ALL">ทุกคลัง · {deliveries.length} รายการ</option>
          </select>
        </div>

        <div style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>เดือน</span>
          <select value={month} onChange={(e) => setMonth(e.target.value)}
            style={css("height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff")}>
            <option value="ALL">ทุกเดือน · {mine.length} รายการ</option>
            {months.map(([key, held]) => (
              <option key={key} value={key}>{monthKeyLabel(key)} · {held} รายการ</option>
            ))}
          </select>
        </div>

        <div style={css("display:flex;flex-direction:column;gap:2px;margin-left:auto")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ค่าขนส่งรวม</span>
          <span style={css("font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>
            {total ? total.toLocaleString("en-US") : "—"}
          </span>
        </div>

        <button onClick={exportSheet}
          style={css("height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
          Export Excel
        </button>
      </div>

      <div style={css("font-size:11px;color:#7B8CA0;line-height:1.6")}>
        ทุกคอลัมน์ในรายงานนี้อ่านจากทะเบียนงานจริง ไม่มีช่องไหนที่เว้นไว้เพราะไม่มีข้อมูล ·
        งานที่เข้ารายงานคืองานประเภท DELIVERY ของคลังที่เลือก — ไฟล์ต้นฉบับระบุหัวกระดาษว่า UNITHAI
        จึงตั้งเป็นค่าเริ่มต้น เปลี่ยนได้ที่ช่องคลังสินค้า
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
        {rows.length === 0 ? (
          <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            {deliveries.length === 0
              ? "ยังไม่มีงานประเภท DELIVERY ในทะเบียน"
              : "ไม่มีงานของคลังและเดือนที่เลือก"}
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {COLUMNS.map((column, index) => (
                    <th key={index} style={css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap")}>
                      {column.head}
                      {column.sub && (
                        <span style={css("display:block;font-weight:400;color:#7B8CA0")}>{column.sub}</span>
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {rows.map((job) => (
                  <tr key={job.key} className="row-hover">
                    {COLUMNS.map((column, index) => (
                      <td key={index} style={css("padding:7px 10px;border-bottom:1px solid #F1F5F9;white-space:nowrap" +
                        (column.align === "right" ? ";text-align:right;font-family:'IBM Plex Mono',monospace" : ""))}>
                        {column.read(job) || "—"}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
