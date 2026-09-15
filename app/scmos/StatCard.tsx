"use client";

import type { ReactNode } from "react";
import { css } from "./theme";
import { iconFor, STAT_PATHS, type StatIcon } from "./statIcons";

/**
 * The figure card every screen counts with, drawn the way the department's
 * templates draw one: a lit icon tile at the left in the figure's own colour,
 * the label in small capitals, the figure large and tabular, and the line
 * under it that says what was counted.
 *
 * One component, because until 15 September every screen drew its own —
 * twenty near-copies with four different prop names — and the templates
 * asked for all of them to look like one thing. Each screen keeps its own
 * `Tile` as a one-line adapter over this, so nothing that calls one moved.
 *
 * The icon is chosen from the label when the caller does not say: the
 * labels are a small vocabulary — jobs, delays, cancellations, suppliers,
 * documents, training — and a card that names one is a card that can be
 * given its glyph without every screen listing them.
 */
export function StatGlyph({ icon, size = 22 }: { icon: StatIcon; size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ display: "block" }}>
      <path d={STAT_PATHS[icon]} />
    </svg>
  );
}

export function StatCard(p: {
  label: string;
  value: ReactNode;
  /** The line under the figure — what was counted, or how it moved. */
  note?: ReactNode;
  /** The figure's colour; the tile, the figure and the top edge take it. */
  tone?: string;
  icon?: StatIcon;
  onClick?: () => void;
  /** Tighter, for a row of six. */
  compact?: boolean;
}) {
  const tone = p.tone ?? "#0A2240";
  const icon = p.icon ?? iconFor(p.label);
  // The tile's tint and glow are the tone with an alpha, which only a six-digit
  // hex can take; a named or short colour keeps the tile plain.
  const alpha = (hex: string) => (/^#[0-9a-f]{6}$/i.test(tone) ? tone + hex : "transparent");
  const pad = p.compact ? "10px 12px 11px" : "13px 15px 14px";
  const size = p.compact ? 38 : 46;
  const skin = `background:#fff;border:1px solid #D8E0E8;border-top:2px solid ${tone};border-radius:8px;padding:${pad};`
    + "display:flex;align-items:center;gap:13px;text-align:left;width:100%;min-width:0;box-shadow:0 1px 2px rgba(10,34,64,.04)";
  const body = (
    <>
      <div aria-hidden="true" style={css(`flex:none;width:${size}px;height:${size}px;border-radius:10px;display:flex;align-items:center;justify-content:center;`
        + `color:${tone};background:${alpha("14")};border:1px solid ${alpha("55")};box-shadow:0 0 0 1px ${alpha("10")},0 0 14px ${alpha("33")}`)}>
        <StatGlyph icon={icon} size={p.compact ? 19 : 22} />
      </div>
      <div style={css("flex:1;min-width:0")}>
        <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>{p.label}</div>
        <div className="sc-figure" style={css(`font-family:'IBM Plex Mono',ui-monospace,monospace;font-size:${p.compact ? 22 : 26}px;font-weight:600;line-height:1.2;margin-top:1px;color:${tone}`)}>{p.value}</div>
        {p.note !== undefined && p.note !== "" && (
          <div style={css("font-size:11.5px;color:#7B8CA0;line-height:1.4;margin-top:1px")}>{p.note}</div>
        )}
      </div>
    </>
  );
  return p.onClick
    ? <button onClick={p.onClick} className="card-hover" style={css(skin + ";font-family:inherit;cursor:pointer")}>{body}</button>
    : <div style={css(skin)}>{body}</div>;
}
