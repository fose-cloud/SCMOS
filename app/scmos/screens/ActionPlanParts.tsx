"use client";

import { css } from "../theme";

/**
 * The pieces the Action Plan screens share (1 Oct 2026): the progress bar and figure, the badge, the notice,
 * and the styles — one copy, imported by the dashboard, the form and the plan page alike.
 */

export function Progress({ label, value }: { label: string; value: number | null }) {
  return (
    <div>
      <div style={css("display:flex;justify-content:space-between;font-size:12px;color:#334155;margin-bottom:4px")}>
        <span>{label}</span><b style={css("font-family:ui-monospace,monospace")}>{value === null ? "—" : `${value}%`}</b>
      </div>
      <div style={css("height:8px;background:#F1F5F9;border-radius:4px;overflow:hidden")}>
        <div style={css(`height:100%;width:${value ?? 0}%;background:${SERIES_1};border-radius:4px`)} />
      </div>
    </div>
  );
}

export function ProgressBar({ value }: { value: number | null }) {
  return <div style={css("display:flex;align-items:center;gap:6px")} title={value === null ? "ยังไม่มีขั้นตอน" : `${value}%`}>
    <div style={css("flex:1;height:7px;background:#F1F5F9;border-radius:4px;overflow:hidden;min-width:50px")}>
      <div style={css(`height:100%;width:${value ?? 0}%;background:${SERIES_1};border-radius:4px`)} />
    </div>
    <span style={css("font-family:ui-monospace,monospace;font-size:11px;color:#334155;min-width:32px;text-align:right")}>{value === null ? "—" : `${value}%`}</span>
  </div>;
}

export function Badge({ label, tone, background }: { label: string; tone: string; background: string }) {
  return <span style={css(`font-size:10.5px;font-weight:700;color:${tone};background:${background};border-radius:999px;padding:2px 8px;white-space:nowrap`)}>{label}</span>;
}

export function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid " + tone + ";border-radius:6px;padding:16px 18px;font-size:12.5px;color:#5A6B7D")}>{children}</div>;
}

/** Categorical slots 1 and 2 of the reference palette, checked by the dataviz validator against white (1 Oct 2026). */
export const SERIES_1 = "#2a78d6";
export const SERIES_2 = "#eb6834";

export const PANEL = "background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 14px;";
export const TITLE = "font-size:12.5px;font-weight:650;color:#0A2240;";
export const LABEL = "font-size:11px;color:#7B8CA0;";
export const INPUT = "height:30px;padding:0 8px;border:1px solid #C9D6E2;border-radius:4px;background:#fff;font-size:12.5px;font-family:inherit;";
export const HEAD = "text-align:left;padding:8px 10px;background:#F8FAFC;font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap";
export const CELL = "padding:8px 10px;border-bottom:1px solid #F1F5F9;vertical-align:middle;color:#0F2B46;";
export const MONO = "font-family:ui-monospace,monospace;font-size:11.5px;";
export const EMPTY = "padding:24px;text-align:center;color:#94A3B8;font-size:12.5px";
export const PRIMARY = "height:30px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer";
export const OUTLINE = "height:30px;padding:0 13px;border:1px solid #C9D6E2;background:#fff;color:#334155;border-radius:4px;font-size:12.5px;cursor:pointer";
export const SAVE = "height:30px;padding:0 15px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;";
export const SMALL = "height:25px;padding:0 9px;border:1px solid #9CC2E8;background:#F4F8FC;color:#0A5C97;border-radius:4px;font-size:11px;font-weight:600;cursor:pointer;";
