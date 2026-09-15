"use client";

import type { ReactNode } from "react";
import { StatGlyph } from "./StatCard";
import type { StatIcon } from "./statIcons";
import { css } from "./theme";

/**
 * The parts a monitoring board is drawn from, as the department's template
 * draws them: a filter chip with the count in a bubble, a status badge with
 * a glyph, and the pager under a table.
 *
 * Shared between the shipment monitor and the CAR/PAR monitor so the two
 * boards read as one system rather than two people's screens.
 */

export const PAGE_SIZES = [25, 50, 100];

/** One filter button: a kind and, in a bubble, how many rows carry it. */
export function Pick({ on, tone, count, onClick, children }: {
  on: boolean; tone: string; count: number; onClick: () => void; children: ReactNode;
}) {
  return (
    <button type="button" onClick={onClick}
      style={css("height:30px;padding:0 6px 0 11px;border-radius:6px;font-size:12px;font-weight:600;"
        + "font-family:inherit;cursor:pointer;white-space:nowrap;display:inline-flex;align-items:center;gap:7px;border:1px solid " + tone
        + ";background:" + (on ? tone : "#fff") + ";color:" + (on ? "#fff" : tone))}>
      {children}
      <span style={css("font-family:'IBM Plex Mono',monospace;font-size:11px;padding:1px 7px;border-radius:10px;"
        + (on ? "background:rgba(255,255,255,.2);color:#fff" : `background:${tone}14;color:${tone}`))}>
        {count}
      </span>
    </button>
  );
}

/** A status badge: a glyph and the words, tinted in the status's colour. */
export function Badge({ tone, icon, children }: {
  tone: string; icon: StatIcon; children: ReactNode;
}) {
  return (
    <span style={css("display:inline-flex;align-items:center;gap:5px;font-size:10.5px;font-weight:700;padding:3px 8px;"
      + `border-radius:5px;white-space:nowrap;border:1px solid ${tone}66;color:${tone};background:${tone}14`)}>
      <StatGlyph icon={icon} size={12} />
      {children}
    </span>
  );
}

/** Which rows are on screen, and the way to the rest. */
export function Pager({ total, page, pageCount, per, onPage, onPer }: {
  total: number; page: number; pageCount: number; per: number;
  onPage: (page: number) => void; onPer: (per: number) => void;
}) {
  const from = (page - 1) * per + 1;
  const to = Math.min(page * per, total);
  const pages = Array.from({ length: Math.min(pageCount, 6) }, (_, i) => i + 1);
  const btn = (on: boolean) =>
    "min-width:30px;height:30px;padding:0 9px;border-radius:6px;font-size:12px;font-family:inherit;cursor:pointer;border:1px solid "
    + (on ? "#1668AB;background:#1668AB;color:#fff;font-weight:600" : "#C9D6E2;background:#fff;color:#0A2240");
  return (
    <div style={css("display:flex;align-items:center;gap:8px;flex-wrap:wrap;padding:10px 14px;border-top:1px solid #E9EFF5;font-size:12px;color:#5A6B7D")}>
      <span>แสดง {from.toLocaleString()}–{to.toLocaleString()} จาก <b style={css("color:#0A2240")}>{total.toLocaleString()}</b> รายการ</span>
      <span style={css("margin-left:auto;display:flex;align-items:center;gap:5px")}>
        <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} style={css(btn(false) + (page <= 1 ? ";opacity:.45;cursor:not-allowed" : ""))}>‹ Prev</button>
        {pages.map((n) => <button key={n} type="button" onClick={() => onPage(n)} style={css(btn(n === page))}>{n}</button>)}
        {pageCount > 6 && <span style={css("padding:0 4px")}>… {pageCount}</span>}
        <button type="button" disabled={page >= pageCount} onClick={() => onPage(page + 1)} style={css(btn(false) + (page >= pageCount ? ";opacity:.45;cursor:not-allowed" : ""))}>Next ›</button>
      </span>
      <span style={css("display:flex;align-items:center;gap:6px")}>
        Rows per page
        <select value={per} onChange={(event) => onPer(Number(event.target.value))}
          style={css("height:30px;border:1px solid #C9D6E2;border-radius:6px;padding:0 8px;font-size:12px;font-family:inherit;background:#fff;color:#0A2240")}>
          {PAGE_SIZES.map((n) => <option key={n} value={n}>{n}</option>)}
        </select>
      </span>
    </div>
  );
}
