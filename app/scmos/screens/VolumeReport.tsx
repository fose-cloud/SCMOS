"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { isCancelled, type Job } from "../ops";
import { css } from "../theme";
import {
  busiest, byField, byOperator, byPeriod, owner, type Grain, type Scope, type Tally,
} from "../volumeReport";

/**
 * How much work ran, and whose it was.
 *
 * One screen where the catalogue carried six cards — Shipment Volume, Import
 * Volume, Export Volume, Trips by Supplier, Trips by Truck Type, Trips by
 * Customer. Every one of them was the same count of the same register over the
 * same period, cut a different way, and six cards meant the period had to be
 * typed six times and could be typed six different ways. Asked for as one menu
 * entry that answers all six at once, which is also the honest shape: nobody
 * looks at volume by customer without then asking which carrier moved it.
 *
 * The counting rules are in volumeReport, without imports, so they are tested
 * on their own. This is the page around them.
 */

/** How many rows a ranking shows before it becomes scrolling rather than reading. */
const TOP = 25;

/**
 * The three directions the team plans around, kept as columns even at zero.
 *
 * DELIVERY is the register's word for domestic distribution — the work under
 * The Chemours. A month with none of it should say so; a column that quietly
 * disappears reads as the report having forgotten the work exists.
 */
const DIRECTIONS = ["IMPORT", "EXPORT", "DELIVERY"];

export function VolumeReport({ jobs, onToast, onBack }: {
  jobs: Job[];
  onToast: (message: string) => void;
  onBack: () => void;
}) {
  const [grain, setGrain] = useState<Grain>("month");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  // The cancellation rule comes from ops rather than being written again here.
  // Wrapped only because volumeReport describes a job loosely enough to be
  // testable without the register's full type; the rule itself is ops'.
  const scope: Scope = useMemo(
    () => ({
      from, to, always: DIRECTIONS,
      cancelledRule: (job) => isCancelled({ status: job.status ?? "" }),
    }),
    [from, to]);

  const period = useMemo(() => byPeriod(jobs, grain, scope), [jobs, grain, scope]);

  // Who is carrying what. Two tables rather than one: a person against the
  // three directions is five rows, and a person against sixty-odd vehicle types
  // only reads with the types down the side and the people across the top.
  const operators = useMemo(() => byOperator(jobs, scope), [jobs, scope]);
  const typeByOperator = useMemo(
    () => byField(jobs, (j) => j.type, scope, owner), [jobs, scope]);
  const customers = useMemo(() => byField(jobs, (j) => j.customer, scope), [jobs, scope]);
  const truckers = useMemo(() => byField(jobs, (j) => j.trucker, scope), [jobs, scope]);
  const types = useMemo(() => byField(jobs, (j) => j.type, scope), [jobs, scope]);

  // Import and export do not describe a journey with the same columns, so they
  // are not forced into one table. An import arrives at a yard and goes to a
  // destination; an export leaves a plant and the empty goes back somewhere.
  const impScope = useMemo(() => ({ ...scope, cat: "IMPORT" }), [scope]);
  const expScope = useMemo(() => ({ ...scope, cat: "EXPORT" }), [scope]);
  const impYard = useMemo(() => byField(jobs, (j) => j.cyYard, impScope), [jobs, impScope]);
  const impTo = useMemo(() => byField(jobs, (j) => j.destination, impScope), [jobs, impScope]);
  const expPlant = useMemo(() => byField(jobs, (j) => j.plant, expScope), [jobs, expScope]);
  const expReturn = useMemo(() => byField(jobs, (j) => j.returnLoc, expScope), [jobs, expScope]);

  const top = busiest(period);

  /** Every table, as a workbook — the whole report, not the rows on screen. */
  function exportSheet() {
    if (!period.counted) { onToast("ไม่มีงานในช่วงที่เลือก"); return; }

    const book = XLSX.utils.book_new();
    const range = `${from || "ตั้งแต่แรก"} – ${to || "ถึงล่าสุด"}`;

    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["Volume Report · รายงานปริมาณงาน"],
      ["ช่วงที่รายงาน", range],
      ["จัดกลุ่มตาม", GRAINS.find((g) => g[0] === grain)?.[1] ?? grain],
      [],
      ["เที่ยวที่นับได้", period.counted],
      ...period.cols.map((col) => [col, period.totals[col] ?? 0]),
      [],
      ["ยกเลิก (ไม่นับเป็นปริมาณงาน)", period.cancelled],
      ["วันที่อ่านไม่ได้ (ไม่ถูกจัดเข้าช่วงใด)", period.undated],
    ]), "สรุป");

    // Every sheet is the whole ranking, not the twenty-five the screen shows.
    for (const [name, tallied] of SHEETS({
      period, operators, typeByOperator, customers, truckers, types,
      impYard, impTo, expPlant, expReturn,
    })) {
      XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet(asRows(tallied)), name);
    }

    const stamp = new Date().toISOString().slice(0, 10).replace(/-/g, "");
    const name = `VolumeReport_${stamp}.xlsx`;
    XLSX.writeFile(book, name);
    onToast(`ส่งออกแล้ว · ${period.counted.toLocaleString()} เที่ยว · 11 ชีท · ${name}`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;align-items:center;gap:10px")}>
        <button onClick={onBack} style={BTN_SECONDARY}>← กลับไปรายการรายงาน</button>
        <span style={css("font-size:13px;font-weight:600;color:#0A2240")}>Volume Report · รายงานปริมาณงาน</span>
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <Field label="จัดกลุ่มตาม" width="150px">
          <select value={grain} onChange={(e) => setGrain(e.target.value as Grain)} style={SELECT}>
            {GRAINS.map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select>
        </Field>
        <Field label="ตั้งแต่ (วว/ดด/ปปปป)" width="150px">
          <input value={from} onChange={(e) => setFrom(e.target.value)} placeholder="01/07/2026" style={INPUT} />
        </Field>
        <Field label="ถึง" width="150px">
          <input value={to} onChange={(e) => setTo(e.target.value)} placeholder="31/07/2026" style={INPUT} />
        </Field>
        <button onClick={exportSheet} style={css(BTN_PRIMARY + ";margin-left:auto")}>Export Excel</button>
      </div>

      <div style={css("display:flex;gap:11px;flex-wrap:wrap")}>
        <Tile label="เที่ยวที่นับได้" value={period.counted.toLocaleString()} />
        {period.cols.map((col) => (
          <Tile key={col} label={col} value={(period.totals[col] ?? 0).toLocaleString()}
            note={period.counted ? `${Math.round(((period.totals[col] ?? 0) / period.counted) * 100)}%` : ""} />
        ))}
        {top && <Tile label="ช่วงที่หนักที่สุด" value={String(top.total)} note={top.label} />}
        {/* Both stated rather than folded into the total: a month that quietly
            counted cancelled work, or quietly lost rows it could not date,
            would read as busier or quieter than it was. */}
        <Tile label="ยกเลิก" value={period.cancelled.toLocaleString()}
          note={period.cancelled ? "ไม่นับเป็นปริมาณงาน" : "ไม่มี"} tone={period.cancelled ? "#B45309" : undefined} />
        <Tile label="วันที่อ่านไม่ได้" value={period.undated.toLocaleString()}
          note={period.undated ? "ไม่ถูกจัดเข้าช่วงใด" : "อ่านได้ครบ"} tone={period.undated ? "#B42318" : undefined} />
      </div>

      <Section title="ปริมาณงานตามช่วงเวลา" note="จำนวนเที่ยวต่อช่วง แยกตามประเภทงาน — ใหม่สุดอยู่บนสุด"
        tallied={period} first="ช่วงเวลา" limit={0} />

      <Section title="ตามผู้รับผิดชอบ (Operation)"
        note="งานที่แต่ละคนดูแล แยกนำเข้า / ส่งออก / ในประเทศ — นับตามรหัสผู้รับผิดชอบ ไม่ใช่ชื่อ"
        tallied={operators} first="ผู้รับผิดชอบ" limit={0} />

      <Section title="ประเภทรถ/ตู้ ที่แต่ละคนดูแล"
        note="ประเภทรถอยู่แถว ผู้รับผิดชอบอยู่คอลัมน์ — นับตามที่บันทึกไว้ในทะเบียน"
        tallied={typeByOperator} first="ประเภทรถ/ตู้" limit={TOP} />

      <Section title="ตามลูกค้า" note="เรียงจากลูกค้าที่มีงานมากที่สุด"
        tallied={customers} first="ลูกค้า" limit={TOP} />

      <Section title="ตามผู้ขนส่ง" note="สัดส่วนงานที่แบ่งให้ผู้รับเหมาแต่ละราย"
        tallied={truckers} first="ผู้ขนส่ง" limit={TOP} />

      <Section title="ตามประเภทรถ/ตู้" note="นับตามที่บันทึกไว้ในทะเบียน ไม่ได้รวมคำที่สะกดต่างกันให้เอง"
        tallied={types} first="ประเภท" limit={TOP} />

      <Section title="งานนำเข้า — ตามลานตู้ (CY Yard)" note="เฉพาะงาน IMPORT"
        tallied={impYard} first="ลานตู้" limit={TOP} />

      <Section title="งานนำเข้า — ตามปลายทาง" note="เฉพาะงาน IMPORT"
        tallied={impTo} first="ปลายทาง" limit={TOP} />

      <Section title="งานส่งออก — ตามโรงงานต้นทาง" note="เฉพาะงาน EXPORT"
        tallied={expPlant} first="โรงงานต้นทาง" limit={TOP} />

      <Section title="งานส่งออก — ตามจุดคืนตู้" note="เฉพาะงาน EXPORT"
        tallied={expReturn} first="จุดคืนตู้" limit={TOP} />

      <div style={css("font-size:11px;color:#7B8CA0;line-height:1.7")}>
        อ่านจากงานจริงในทะเบียนตามช่วงวันที่เลือก · งานที่ยกเลิกไม่ถูกนับเป็นปริมาณงาน เพราะจองแล้วไม่ได้วิ่ง
        แต่แสดงจำนวนไว้ให้เห็น · งานที่วันที่อ่านไม่ได้จะไม่ถูกเดาให้เข้าช่วงใด และแสดงจำนวนไว้เช่นกัน ·
        ช่องที่ไม่ได้กรอกจะขึ้นเป็น “ไม่ระบุ” ไม่ได้ถูกตัดทิ้ง ยอดในตารางจึงรวมได้เท่ากับยอดรวมเสมอ ·
        สัปดาห์นับจากวันจันทร์ที่สัปดาห์นั้นเริ่ม · ตารางบนหน้าจอแสดง {TOP} อันดับแรก ปุ่ม Export Excel ได้ครบทุกแถว
      </div>
    </div>
  );
}

const GRAINS: [Grain, string][] = [["day", "รายวัน"], ["week", "รายสัปดาห์"], ["month", "รายเดือน"]];

/** [sheet name, the tally it holds] — the export mirrors the page, in order. */
function SHEETS(all: Record<string, Tally>): [string, Tally][] {
  return [
    ["ตามช่วงเวลา", all.period],
    ["ตามผู้รับผิดชอบ", all.operators],
    ["ประเภทรถรายคน", all.typeByOperator],
    ["ตามลูกค้า", all.customers],
    ["ตามผู้ขนส่ง", all.truckers],
    ["ตามประเภทรถ", all.types],
    ["นำเข้า-ลานตู้", all.impYard],
    ["นำเข้า-ปลายทาง", all.impTo],
    ["ส่งออก-โรงงาน", all.expPlant],
    ["ส่งออก-จุดคืนตู้", all.expReturn],
  ];
}

/** A tally as a sheet: a header row, every row, then the column totals. */
function asRows(tallied: Tally): (string | number)[][] {
  const head = ["", ...tallied.cols, "รวม"];
  const body = tallied.rows.map((row) => [
    row.label, ...tallied.cols.map((col) => row.byCol[col] ?? 0), row.total,
  ]);
  const foot = ["รวมทั้งหมด", ...tallied.cols.map((col) => tallied.totals[col] ?? 0), tallied.counted];
  return [head, ...body, foot];
}

function Section({ title, note, tallied, first, limit }: {
  title: string; note: string; tallied: Tally; first: string;
  /** Rows to draw before saying how many were left off. Zero draws them all. */
  limit: number;
}) {
  const shown = limit > 0 ? tallied.rows.slice(0, limit) : tallied.rows;

  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
      <div style={css("padding:11px 15px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:baseline;gap:12px;flex-wrap:wrap")}>
        <div>
          <div style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>{title}</div>
          <div style={css("font-size:11px;color:#7B8CA0;margin-top:2px")}>{note}</div>
        </div>
        <span style={css("font-size:11.5px;color:#7B8CA0")}>
          {tallied.rows.length.toLocaleString()} แถว · {tallied.counted.toLocaleString()} เที่ยว
          {tallied.blank > 0 && ` · ไม่ระบุ ${tallied.blank.toLocaleString()}`}
        </span>
      </div>

      {tallied.rows.length === 0 ? (
        <div style={css("padding:22px;text-align:center;font-size:12.5px;color:#94A3B8")}>
          ไม่มีงานในช่วงที่เลือก
        </div>
      ) : (
        <div style={css("overflow-x:auto")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead>
              <tr>
                <th style={TH}>{first}</th>
                {tallied.cols.map((col) => <th key={col} style={css(TH_CSS + ";text-align:right")}>{col}</th>)}
                <th style={css(TH_CSS + ";text-align:right")}>รวม</th>
                <th style={css(TH_CSS + ";width:120px")}>สัดส่วน</th>
              </tr>
            </thead>
            <tbody>
              {shown.map((row) => (
                <tr key={row.label}>
                  <td style={css(TD)}>{row.label}</td>
                  {tallied.cols.map((col) => (
                    <td key={col} style={css(TD + ";text-align:right;font-family:'IBM Plex Mono',monospace;color:#475569")}>
                      {row.byCol[col] ?? 0}
                    </td>
                  ))}
                  <td style={css(TD + ";text-align:right;font-family:'IBM Plex Mono',monospace;font-weight:600;color:#0A2240")}>
                    {row.total}
                  </td>
                  <td style={css(TD)}>
                    {/* The bar is against the biggest row, not the total: with a
                        hundred customers every bar against the total is a sliver
                        and the picture says nothing. */}
                    <span style={css("display:block;height:7px;border-radius:3px;background:#E3EBF3")}>
                      <span style={css(`display:block;height:7px;border-radius:3px;background:#2E7DD1;width:${
                        Math.max(2, Math.round((row.total / Math.max(1, shown[0].total)) * 100))}%`)} />
                    </span>
                  </td>
                </tr>
              ))}
              <tr>
                <td style={css(TD + ";font-weight:600;color:#0A2240;background:#F8FAFC")}>รวมทั้งหมด</td>
                {tallied.cols.map((col) => (
                  <td key={col} style={css(TD + ";text-align:right;font-family:'IBM Plex Mono',monospace;font-weight:600;background:#F8FAFC")}>
                    {(tallied.totals[col] ?? 0).toLocaleString()}
                  </td>
                ))}
                <td style={css(TD + ";text-align:right;font-family:'IBM Plex Mono',monospace;font-weight:700;color:#0A2240;background:#F8FAFC")}>
                  {tallied.counted.toLocaleString()}
                </td>
                <td style={css(TD + ";background:#F8FAFC")} />
              </tr>
            </tbody>
          </table>
        </div>
      )}

      {limit > 0 && tallied.rows.length > limit && (
        <div style={css("padding:9px 15px;font-size:11.5px;color:#94A3B8;border-top:1px solid #F1F5F9")}>
          แสดง {limit} อันดับแรกจาก {tallied.rows.length.toLocaleString()} — แถวรวมด้านบนนับครบทุกแถว
          กด Export Excel เพื่อดูทั้งหมด
        </div>
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ pieces */

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;font-family:inherit;width:100%");
const SELECT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;font-family:inherit;background:#fff;width:100%");
const TH_CSS = "background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap";
const TH = css(TH_CSS);
const TD = "padding:7px 10px;border-bottom:1px solid #F1F5F9;vertical-align:middle";
const BTN_PRIMARY = "height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit";
const BTN_SECONDARY = css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit");

function Tile({ label, value, tone, note }: { label: string; value: string; tone?: string; note?: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 16px;min-width:130px;display:flex;flex-direction:column;gap:3px")}>
      <span style={LABEL}>{label}</span>
      <span style={css(`font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${tone ?? "#0A2240"}`)}>{value}</span>
      {note && <span style={css("font-size:10.5px;color:#94A3B8")}>{note}</span>}
    </div>
  );
}

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <label style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`)}>
      <span style={LABEL}>{label}</span>
      {children}
    </label>
  );
}
