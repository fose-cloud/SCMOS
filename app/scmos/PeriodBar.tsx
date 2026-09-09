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
  /**
   * Filters that belong on this row rather than a row of their own.
   *
   * The dashboard's CUSTOMER and TRUCKER pickers sat in a navy bar above this
   * one, so two bars were spent on one question — which jobs are we looking at.
   * They come in here instead. KPI passes nothing and gets the bar it always
   * had.
   */
  dimensions?: React.ReactNode;
  /**
   * Navy, for the row that carries those pickers.
   *
   * FilterPickMany is drawn for a navy bar — it is My Job's control and is
   * styled for the bar it sits on there — so the row it joins becomes navy
   * rather than the picker being restyled for three screens at once.
   */
  tone?: "light" | "dark";
}) {
  const dark = p.tone === "dark";
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
      <span style={css(`font-size:10.5px;letter-spacing:.05em;font-weight:600;color:${dark ? "#8FB4D8" : "#8496A8"}`)}>{label}</span>
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
    <div style={css(`${dark ? "background:#0A2240;border:1px solid #0A2240" : "background:#fff;border:1px solid #D8E0E8"}`
      + ";border-radius:5px;padding:11px 14px;display:flex;align-items:center;gap:12px;flex-wrap:wrap")}>
      {p.dimensions}
      {/* A rule between the two questions, so one long row still reads as
          "which jobs" and then "over what period". */}
      {p.dimensions && <span style={css("width:1px;align-self:stretch;background:#24476E")} />}
      <span style={css(`font-size:11px;font-weight:700;letter-spacing:.06em;color:${dark ? "#CFE2F7" : "#0A2240"}`)}>ช่วงเวลา</span>

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
          style={css("height:30px;padding:0 12px;border-radius:4px;font-size:11.5px;cursor:pointer;"
            + (dark ? "border:1px solid #24476E;background:#0A2240;color:#CFE2F7" : "border:1px solid #D8E0E8;background:#fff;color:#475569"))}
        >
          วันล่าสุดในแผน
        </button>
      )}
      {active && (
        <button
          onClick={() => p.onPeriod(ALL_PERIOD)}
          style={css("height:30px;padding:0 12px;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;"
            + (dark ? "border:1px solid #4E9BE8;background:#16406E;color:#fff" : "border:1px solid #BBD5EE;background:#F4F8FC;color:#0A2240"))}
        >
          ล้างช่วงเวลา
        </button>
      )}

      <span style={css("margin-left:auto;display:flex;align-items:baseline;gap:8px")}>
        <span style={css(`font-size:11.5px;color:${dark ? "#8FB4D8" : "#64748B"}`)}>{periodLabel(p.period)} ·</span>
        <span style={css(`font-size:15px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${dark ? "#fff" : "#0A2240"}`)}>{p.shown}</span>
        <span style={css(`font-size:11.5px;color:${dark ? "#8FB4D8" : "#64748B"}`)}>จาก {p.allJobs.length} งาน</span>
        {!!options.undated && (
          <span style={css(`font-size:11px;color:${dark ? "#F0B860" : "#B45309"}`)} title="งานที่วันที่ยังไม่ถูกต้อง จะไม่ถูกนับเมื่อเลือกช่วงเวลา">
            · วันที่ใช้ไม่ได้ {options.undated}
          </span>
        )}
      </span>
    </div>
  );
}
