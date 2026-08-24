"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import type { Job } from "../ops";
import { css } from "../theme";
import { dnum, lateLabel, lateMinutes } from "../util";

/**
 * Why a customer's trucks arrived late, and how late.
 *
 * Written for the question management actually asked about the Syensqo account:
 * arrivals keep slipping, the arrangements meant to prevent that are already in
 * place, so what is the real cause. That question cannot be answered from a
 * percentage. It needs the trips themselves — which carrier, which booking,
 * planned for when, arrived when, and what the person who logged it said about
 * why — which is what this puts on one page for one customer over one period.
 *
 * The grace period is the customer's, not the KPI's. The figure reported upward
 * counts a truck late the minute it is late; a service level may allow thirty
 * minutes before it counts. Both are the same subtraction — `lateMinutes` — and
 * this one says on screen which threshold it used, because a delay report that
 * does not say what it counted as late is an argument waiting to happen.
 */

/** What management's own table asks for, in its order. */
const COLUMNS: { head: string; read: (job: Job, late: number) => string; wide?: boolean }[] = [
  { head: "TRUCK", read: (j) => j.trucker || "—" },
  { head: "BOOKING", read: (j) => j.booking || j.jobCode || j.abs || "—" },
  { head: "PLANT LOADING", read: (j) => j.plant || "—" },
  { head: "PLAN LOADING DATE", read: (j) => j.date || "—" },
  { head: "PLAN LOADING TIME", read: (j) => j.planTime || "—" },
  { head: "TYPE", read: (j) => j.type || "—" },
  { head: "ARRIVAL DATE", read: (j) => j.arrDate || "—" },
  { head: "ARRIVAL TIME", read: (j) => j.arrTime || "—" },
  { head: "KPI", read: (_j, late) => (late > 0 ? "Late " + lateLabel(late) : "Early " + lateLabel(late)) },
  { head: "Result", read: (_j, late) => (late > 0 ? "Late" : "On time") },
  { head: "Remark", read: (j) => j.reason || j.remark || "", wide: true },
];

/** Grace periods a customer's service level might allow, in minutes. */
const GRACE = [0, 15, 30, 60];

export function DelayAnalysis({ jobs, onToast, onBack }: {
  jobs: Job[];
  onToast: (message: string) => void;
  onBack: () => void;
}) {
  const [customer, setCustomer] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [grace, setGrace] = useState(30);
  const [lateOnly, setLateOnly] = useState(true);

  /** Customers that have at least one trip anybody could measure. */
  const customers = useMemo(() => {
    const tally = new Map<string, number>();
    jobs.forEach((job) => {
      if (lateMinutes(job) === null) return;
      const name = (job.customer ?? "").trim();
      if (name) tally.set(name, (tally.get(name) ?? 0) + 1);
    });
    return [...tally.entries()].sort((a, b) => a[0].localeCompare(b[0]));
  }, [jobs]);

  /**
   * The trips in scope, with how late each was.
   *
   * A trip with no arrival recorded is not counted either way. It is not on
   * time and it is not late — nobody wrote down when the truck got there — and
   * counting it as either would put a number in a management report that the
   * records do not support. How many were left out is shown instead.
   */
  const measured = useMemo(() => {
    const start = from ? dnum(from) : 0;
    const end = to ? dnum(to) : 0;
    return jobs
      .filter((job) => !customer || (job.customer ?? "").trim() === customer)
      .filter((job) => {
        const day = dnum(job.date);
        if (start && (!day || day < start)) return false;
        if (end && (!day || day > end)) return false;
        return true;
      })
      .map((job) => ({ job, late: lateMinutes(job) }))
      .filter((row): row is { job: Job; late: number } => row.late !== null)
      .sort((a, b) => dnum(a.job.date) - dnum(b.job.date) || b.late - a.late);
  }, [jobs, customer, from, to]);

  /** In scope but unmeasurable — no arrival written down. Reported, not hidden. */
  const unrecorded = useMemo(() => {
    const start = from ? dnum(from) : 0;
    const end = to ? dnum(to) : 0;
    return jobs.filter((job) => {
      if (customer && (job.customer ?? "").trim() !== customer) return false;
      const day = dnum(job.date);
      if (start && (!day || day < start)) return false;
      if (end && (!day || day > end)) return false;
      return lateMinutes(job) === null;
    }).length;
  }, [jobs, customer, from, to]);

  const late = useMemo(() => measured.filter((row) => row.late > grace), [measured, grace]);
  const rows = lateOnly ? late : measured;

  /**
   * What the late trips were blamed on, biggest first.
   *
   * This is the part of the question management asked that the register can
   * actually answer — "truck rotation" being the top line is exactly the kind
   * of finding the email is pushing back on, and seeing it counted rather than
   * asserted is the start of the reply.
   */
  const causes = useMemo(() => {
    const tally = new Map<string, { trips: number; minutes: number }>();
    late.forEach(({ job, late: minutes }) => {
      const cause = (job.reason || job.remark || "").trim() || "ไม่ได้ระบุสาเหตุ";
      const held = tally.get(cause) ?? { trips: 0, minutes: 0 };
      tally.set(cause, { trips: held.trips + 1, minutes: held.minutes + minutes });
    });
    return [...tally.entries()]
      .map(([cause, held]) => ({ cause, ...held }))
      .sort((a, b) => b.trips - a.trips || b.minutes - a.minutes);
  }, [late]);

  const worst = late.length ? Math.max(...late.map((row) => row.late)) : 0;
  const averageLate = late.length
    ? Math.round(late.reduce((sum, row) => sum + row.late, 0) / late.length)
    : 0;

  function exportSheet() {
    if (!rows.length) { onToast("ไม่มีเที่ยวให้ส่งออกในมุมมองนี้"); return; }
    const head = COLUMNS.map((column) => column.head);
    const body = rows.map(({ job, late: minutes }) => COLUMNS.map((column) => column.read(job, minutes)));
    const sheet = XLSX.utils.aoa_to_sheet([
      [`Delay Analysis — ${customer || "ทุกลูกค้า"}`],
      [`ช่วง ${from || "ตั้งแต่ต้น"} ถึง ${to || "ล่าสุด"} · เกินกำหนดเกิน ${grace} นาทีจึงนับว่าสาย`],
      [],
      head,
      ...body,
    ]);
    sheet["!cols"] = COLUMNS.map((column) => ({ wch: column.wide ? 52 : Math.max(12, column.head.length + 3) }));
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, "Delay Analysis");
    const name = `Delay_Analysis_${(customer || "ALL").replace(/[^A-Za-z0-9]+/g, "_")}.xlsx`;
    XLSX.writeFile(book, name);
    onToast(`ส่งออก ${rows.length} เที่ยวแล้ว · ${name}`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;align-items:center;gap:10px")}>
        <button onClick={onBack} style={BTN_SECONDARY}>← กลับไปรายการรายงาน</button>
        <span style={css("font-size:13px;font-weight:600;color:#0A2240")}>Delay Analysis · วิเคราะห์ความล่าช้า</span>
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <Field label="ลูกค้า" width="250px">
          <select value={customer} onChange={(e) => setCustomer(e.target.value)} style={SELECT}>
            <option value="">ทุกลูกค้า · {measuredAll(jobs)} เที่ยวที่วัดได้</option>
            {customers.map(([name, held]) => (
              <option key={name} value={name}>{name} · {held} เที่ยว</option>
            ))}
          </select>
        </Field>
        <Field label="ตั้งแต่ (วว/ดด/ปปปป)" width="150px">
          <input value={from} onChange={(e) => setFrom(e.target.value)} placeholder="01/08/2026" style={INPUT} />
        </Field>
        <Field label="ถึง" width="150px">
          <input value={to} onChange={(e) => setTo(e.target.value)} placeholder="31/08/2026" style={INPUT} />
        </Field>
        <Field label="ผ่อนผัน" width="140px">
          <select value={grace} onChange={(e) => setGrace(Number(e.target.value))} style={SELECT}>
            {GRACE.map((minutes) => (
              <option key={minutes} value={minutes}>
                {minutes === 0 ? "ไม่ผ่อนผัน (KPI)" : `เกิน ${minutes} นาที`}
              </option>
            ))}
          </select>
        </Field>
        <label style={css("display:flex;align-items:center;gap:6px;font-size:12px;color:#31465C;padding-bottom:7px")}>
          <input type="checkbox" checked={lateOnly} onChange={(e) => setLateOnly(e.target.checked)} />
          แสดงเฉพาะเที่ยวที่สาย
        </label>
        <button onClick={exportSheet} style={css(BTN_PRIMARY_CSS + ";margin-left:auto")}>Export Excel</button>
      </div>

      <div style={css("display:flex;gap:11px;flex-wrap:wrap")}>
        <Tile label="เที่ยวที่วัดได้" value={String(measured.length)} />
        <Tile label={`สายเกิน ${grace} นาที`} value={String(late.length)} tone="#B3261E"
          note={measured.length ? `${Math.round((late.length / measured.length) * 100)}% ของที่วัดได้` : ""} />
        <Tile label="สายเฉลี่ย" value={late.length ? lateLabel(averageLate) : "—"} />
        <Tile label="สายที่สุด" value={late.length ? lateLabel(worst) : "—"} tone="#B3261E" />
        <Tile label="ไม่ได้บันทึกเวลาถึง" value={String(unrecorded)}
          note={unrecorded ? "ไม่ถูกนับทั้งฝั่งตรงเวลาและสาย" : "บันทึกครบ"} />
      </div>

      {causes.length > 0 && (
        <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
          <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:600;color:#0A2240")}>
            สาเหตุที่บันทึกไว้ของเที่ยวที่สาย
          </div>
          <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
            <tbody>
              {causes.map((row) => (
                <tr key={row.cause}>
                  <td style={css(TD)}>{row.cause}</td>
                  <td style={css(TD + ";width:110px;text-align:right;font-family:'IBM Plex Mono',monospace")}>
                    {row.trips} เที่ยว
                  </td>
                  <td style={css(TD + ";width:150px;text-align:right;font-family:'IBM Plex Mono',monospace;color:#7B8CA0")}>
                    รวม {lateLabel(row.minutes)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
        {rows.length === 0 ? (
          <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            {measured.length === 0
              ? "ไม่มีเที่ยวที่วัดได้ในขอบเขตนี้ — ต้องมีทั้งวันเวลาตามแผนและวันเวลาที่รถถึงจริง"
              : "ไม่มีเที่ยวที่สายในขอบเขตนี้"}
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>{COLUMNS.map((column) => <th key={column.head} style={TH}>{column.head}</th>)}</tr>
              </thead>
              <tbody>
                {rows.map(({ job, late: minutes }) => (
                  <tr key={job.key} className="row-hover">
                    {COLUMNS.map((column) => (
                      <td key={column.head} style={css(TD
                        + (column.wide ? ";min-width:260px;max-width:420px" : ";white-space:nowrap")
                        + (column.head === "Result" && minutes > grace ? ";color:#B3261E;font-weight:600" : "")
                        + (column.head === "Result" && minutes <= grace ? ";color:#2E7D5B;font-weight:600" : ""))}>
                        {column.head === "Result"
                          ? (minutes > grace ? "Late" : "On time")
                          : column.read(job, minutes) || "—"}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div style={css("font-size:11px;color:#7B8CA0;line-height:1.7")}>
        อ่านจากงานในทะเบียนของลูกค้าที่เลือก — เที่ยวหนึ่งจะวัดได้ก็ต่อเมื่อมีทั้งวัน/เวลาตามแผน และวัน/เวลาที่รถถึงจริง
        เที่ยวที่ไม่ได้บันทึกเวลาถึงจะไม่ถูกนับว่าตรงเวลาและไม่ถูกนับว่าสาย แต่แสดงจำนวนไว้ให้เห็น ·
        ช่อง Remark คือสาเหตุที่ผู้ดูแลงานบันทึกไว้เอง ไม่ใช่สิ่งที่ระบบสรุปให้ ·
        ค่าผ่อนผันเป็นของ service level ลูกค้า ส่วน KPI ที่รายงานขึ้นไปใช้แบบไม่ผ่อนผัน เลือกได้ที่ช่องผ่อนผัน
      </div>
    </div>
  );
}

/** How many trips in the whole register carry both a plan and an arrival. */
function measuredAll(jobs: Job[]): number {
  return jobs.reduce((count, job) => count + (lateMinutes(job) === null ? 0 : 1), 0);
}

/* ------------------------------------------------------------------ pieces */

const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;font-family:inherit;width:100%");
const SELECT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;font-family:inherit;background:#fff;width:100%");
const TH = css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap");
const TD = "padding:7px 10px;border-bottom:1px solid #F1F5F9;vertical-align:top";
const BTN_PRIMARY_CSS = "height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit";
const BTN_SECONDARY = css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit");

function Tile({ label, value, tone, note }: { label: string; value: string; tone?: string; note?: string }) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 16px;min-width:150px;display:flex;flex-direction:column;gap:3px")}>
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
