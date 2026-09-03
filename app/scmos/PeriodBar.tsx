"use client";

import { css } from "./theme";
import type { Job } from "./ops";
import { ALL_PERIOD, NO_DATE, latestDay, monthLabel, periodLabel, periodOptions, type Period } from "./period";

/**
 * Year → month → day, each list built from the jobs that exist. Choosing a year
 * narrows the months, choosing a month narrows the days, so the picker can never
 * land on an empty period by accident.
 */
export function PeriodBar(p: {
  allJobs: Job[];
  shown: number;
  period: Period;
  onPeriod: (period: Period) => void;
}) {
  const options = periodOptions(p.allJobs, p.period);
  const active = p.period.year !== "ALL" || p.period.month !== "ALL" || p.period.day !== "ALL";
  const latest = latestDay(p.allJobs);

  /** Offered on the year, and only when there is something to look at. */
  const undatedOption = options.undated > 0
    ? <option value={NO_DATE}>ไม่มีวันที่ ({options.undated})</option>
    : null;

  const select = (label: string, value: string, values: string[], render: (v: string) => string,
    onPick: (v: string) => void, extra?: React.ReactNode) => (
    <label style={css("display:flex;align-items:center;gap:6px")}>
      <span style={css("font-size:10.5px;color:#8496A8;letter-spacing:.05em;font-weight:600")}>{label}</span>
      <select
        value={value}
        onChange={(e) => onPick(e.target.value)}
        style={css("height:32px;min-width:92px;border:1px solid #D8E0E8;border-radius:4px;background:#F8FAFC;font-size:12.5px;color:#16232F;padding:0 8px;outline:none;cursor:pointer")}
      >
        <option value="ALL">ทั้งหมด</option>
        {extra}
        {values.map((v) => <option key={v} value={v}>{render(v)}</option>)}
      </select>
    </label>
  );

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:11px 14px;display:flex;align-items:center;gap:12px;flex-wrap:wrap")}>
      <span style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em")}>ช่วงเวลา</span>

      {select("ปี", p.period.year, options.years, (v) => v,
        (v) => p.onPeriod({ year: v, month: "ALL", day: "ALL" }), undatedOption)}
      {/* A month or a day beside "no date" narrows nothing and reads as though
          it might, so both are put away while it is chosen. */}
      {p.period.year !== NO_DATE && (
        <>
          {select("เดือน", p.period.month, options.months, (v) => monthLabel(v) + " (" + v + ")", (v) => p.onPeriod({ ...p.period, month: v, day: "ALL" }))}
          {select("วัน", p.period.day, options.days, (v) => v, (v) => p.onPeriod({ ...p.period, day: v }))}
        </>
      )}

      {latest && (
        <button
          onClick={() => p.onPeriod(latest)}
          style={css("height:30px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:11.5px;color:#475569;cursor:pointer")}
        >
          วันล่าสุดในแผน
        </button>
      )}
      {active && (
        <button
          onClick={() => p.onPeriod(ALL_PERIOD)}
          style={css("height:30px;padding:0 12px;border:1px solid #BBD5EE;background:#F4F8FC;border-radius:4px;font-size:11.5px;color:#0A2240;font-weight:600;cursor:pointer")}
        >
          ล้างช่วงเวลา
        </button>
      )}

      <span style={css("margin-left:auto;display:flex;align-items:baseline;gap:8px")}>
        <span style={css("font-size:11.5px;color:#64748B")}>{periodLabel(p.period)} ·</span>
        <span style={css("font-size:15px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{p.shown}</span>
        <span style={css("font-size:11.5px;color:#64748B")}>จาก {p.allJobs.length} งาน</span>
        {!!options.undated && (
          <span style={css("font-size:11px;color:#B45309")} title="งานที่วันที่ยังไม่ถูกต้อง จะไม่ถูกนับเมื่อเลือกช่วงเวลา">
            · วันที่ใช้ไม่ได้ {options.undated}
          </span>
        )}
      </span>
    </div>
  );
}
