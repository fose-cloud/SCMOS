"use client";

import type { ReactNode } from "react";
import { css } from "./theme";

/**
 * A screen split into tabs (14 Oct 2026: Administration and KPI), drawn like the AI Control Tower's tab bar but in
 * the inline styles those screens use. One bar for both, so the two cannot drift apart in look or in keyboard and
 * screen-reader wiring.
 */
export type TabSpec<T extends string> = { id: T; label: string; count?: number };

// Whole border values only: a shorthand beside a longhand that changes on select makes React warn of styling bugs.
const TAB = "display:inline-flex;align-items:center;gap:8px;padding:10px 16px;min-height:42px;border-top:0;border-left:0;border-right:0;"
  + "border-bottom:2px solid transparent;background:none;color:#52657A;font-size:13px;font-weight:600;white-space:nowrap;margin-bottom:-1px;"
  + "cursor:pointer;font-family:inherit";
const TAB_ON = ";color:#1D5FA8;border-bottom:2px solid #1D5FA8";
const COUNT = css("min-width:20px;padding:1px 6px;border-radius:10px;background:#EEF3F8;color:#465A6E;font-size:11px;font-weight:700;text-align:center");

/** The tab wanted if it is still offered, else the first: a tab can disappear when the data or the account changes. */
export function shownTab<T extends string>(tabs: readonly TabSpec<T>[], wanted: T): T {
  return tabs.some((one) => one.id === wanted) ? wanted : tabs[0]?.id ?? wanted;
}

/** One pane's props: hidden rather than unmounted, so what is typed in it survives a look at another tab. */
export function paneOf(prefix: string, id: string, shown: string) {
  return { role: "tabpanel" as const, id: `${prefix}-pane-${id}`, "aria-labelledby": `${prefix}-tab-${id}`, hidden: shown !== id };
}

export function TabBar<T extends string>({ name, prefix, tabs, shown, onSelect, children }: {
  /** The tab list's accessible name. */
  name: string;
  /** Ties each tab to its pane: `${prefix}-tab-${id}` controls `${prefix}-pane-${id}`. */
  prefix: string;
  tabs: readonly TabSpec<T>[];
  shown: T;
  onSelect: (id: T) => void;
  /** Kept at the bar's right end — an export button, a read-only mark. */
  children?: ReactNode;
}) {
  return (
    <div style={css("display:flex;align-items:flex-end;justify-content:space-between;gap:10px;flex-wrap:wrap;border-bottom:1px solid #D8E0E8")}>
      <div role="tablist" aria-label={name} style={css("display:flex;gap:2px;overflow-x:auto;overflow-y:hidden;max-width:100%")}>
        {tabs.map((one) => (
          <button key={one.id} type="button" role="tab" id={`${prefix}-tab-${one.id}`} aria-controls={`${prefix}-pane-${one.id}`}
            aria-selected={shown === one.id} onClick={() => onSelect(one.id)} style={css(TAB + (shown === one.id ? TAB_ON : ""))}>
            {one.label}
            {one.count !== undefined && <span style={COUNT}>{one.count.toLocaleString()}</span>}
          </button>
        ))}
      </div>
      {children && <div style={css("display:flex;align-items:center;gap:8px;margin-bottom:6px")}>{children}</div>}
    </div>
  );
}
