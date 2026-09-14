import type { ReactNode } from "react";
import type { Screen } from "./nav";

/**
 * The rail's glyphs, one per screen, drawn in one line weight.
 *
 * The rail used to carry rectangles built from `NAV`'s number tuples — fine
 * as a placeholder, and the department's own menu design has a proper glyph
 * beside every entry. These are that set: 16-unit boxes, a 1.5 stroke in
 * `currentColor`, filled only where the shape reads better filled, so one
 * colour change lights the whole icon. A screen with no glyph here falls back
 * to its rectangles, so adding a screen never breaks the rail.
 */
const P = (d: string, extra?: ReactNode) => (
  <>
    <path d={d} />
    {extra}
  </>
);

export const NAV_ICONS: Partial<Record<Screen, ReactNode>> = {
  dashboard: (
    <>
      <rect x="1.5" y="1.5" width="5.5" height="5.5" rx="1.2" fill="currentColor" stroke="none" />
      <rect x="9" y="1.5" width="5.5" height="5.5" rx="1.2" fill="currentColor" stroke="none" />
      <rect x="1.5" y="9" width="5.5" height="5.5" rx="1.2" fill="currentColor" stroke="none" />
      <rect x="9" y="9" width="5.5" height="5.5" rx="1.2" fill="currentColor" stroke="none" />
    </>
  ),
  workspace: P("M2 3.5h12v9H2z M2 7h12 M6.5 7v5.5"),
  myjob: P("M5.5 4h8.5M5.5 8h8.5M5.5 12h8.5", <><circle cx="2.4" cy="4" r="1" fill="currentColor" stroke="none" /><circle cx="2.4" cy="8" r="1" fill="currentColor" stroke="none" /><circle cx="2.4" cy="12" r="1" fill="currentColor" stroke="none" /></>),
  postpone: P("M2.5 3.5h9v4M2.5 3.5v9h5M5 1.8v3M9 1.8v3M2.5 7h9", <><circle cx="11.5" cy="11.5" r="3" /><path d="M11.5 10v1.6l1.1.8" /></>),
  rotation: P("M2.5 5.5h9l-2-2M13.5 10.5h-9l2 2"),
  monitoring: P("M1.6 10.4V4.6h7.6v5.8M9.2 6.4h2.6l2.6 2.6v1.4H9.2", <><circle cx="4.6" cy="11.6" r="1.4" /><circle cx="11.4" cy="11.6" r="1.4" /></>),
  prerun: P("M4.5 2.5h7a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1h-7a1 1 0 0 1-1-1v-10a1 1 0 0 1 1-1zM6 1.8h4M5.6 8.2l1.7 1.7 3.2-3.4"),
  loreal: P("M2 4.5h12v7H2zM2 7.5h12M5 4.5v7M9 4.5v7"),
  chemours: P("M1.6 10.4V5.2h7.6v5.2H1.6zM9.2 6.8h2.6l2.6 2.4v1.2H9.2", <><circle cx="4.4" cy="11.6" r="1.4" /><circle cx="11.4" cy="11.6" r="1.4" /></>),
  partners: P("M1.8 12.8c.5-2.1 1.9-3.2 3.8-3.2s3.3 1.1 3.8 3.2M10.2 9.8c1.7.1 2.9 1.2 3.3 3", <><circle cx="5.6" cy="5.6" r="2.2" /><circle cx="11" cy="6.4" r="1.8" /></>),
  subcontractors: P("M2.5 14V3.5l5.5-2 5.5 2V14M5.5 6h1.5M9 6h1.5M5.5 9h1.5M9 9h1.5M6.5 14v-3h3v3"),
  vendor: P("M2 13.6c.6-2.6 2.5-3.9 5.2-3.9M12 9.5v5M9.5 12h5", <circle cx="7.2" cy="5.4" r="2.6" />),
  training: P("M8 2.2 14.2 5 8 7.8 1.8 5 8 2.2zM4.2 6.6v3.2c0 1.3 1.7 2.2 3.8 2.2s3.8-.9 3.8-2.2V6.6M14 5v3"),
  capacity: P("M2.5 4.2c0 1.2 2.5 2 5.5 2s5.5-.8 5.5-2M2.5 4.2v7.6c0 1.2 2.5 2 5.5 2s5.5-.8 5.5-2V4.2M2.5 8c0 1.2 2.5 2 5.5 2s5.5-.8 5.5-2", <ellipse cx="8" cy="4.2" rx="5.5" ry="2" />),
  evaluation: P("M3.4 1.9h6l3.2 3.2v9H3.4zM9.4 1.9v3.2h3.2", <path d="M8 6.6l.9 1.9 2 .3-1.5 1.4.4 2L8 11.2l-1.8 1 .4-2-1.5-1.4 2-.3z" fill="currentColor" stroke="none" />),
  carrier: P("M1.6 10.4V4.6h7.6v5.8M9.2 6.4h2.6l2.6 2.6v1.4H9.2M4 4.6V3", <><circle cx="4.6" cy="11.6" r="1.4" /><circle cx="11.4" cy="11.6" r="1.4" /></>),
  commercial: (
    <>
      <rect x="2" y="8.5" width="2.8" height="5" rx=".6" fill="currentColor" stroke="none" />
      <rect x="6.6" y="5.5" width="2.8" height="8" rx=".6" fill="currentColor" stroke="none" />
      <rect x="11.2" y="2.5" width="2.8" height="11" rx=".6" fill="currentColor" stroke="none" />
    </>
  ),
  rates: P("M2 8.5V2.5h6l6 6-6 6z", <circle cx="5.2" cy="5.7" r="1.1" fill="currentColor" stroke="none" />),
  quotation: P("M3.5 1.8h9a1 1 0 0 1 1 1v10.4a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1V2.8a1 1 0 0 1 1-1zM5 4.5h6M5 8h1.5M8 8h1.5M11 8h1.5M5 11h1.5M8 11h1.5M11 11h1.5"),
  billing: P("M3.5 1.8h9v12.4l-1.5-1-1.5 1-1.5-1-1.5 1-1.5-1-1.5 1zM5.5 5h5M5.5 8h5M5.5 11h3"),
  oilrate: P("M3 2.5h6v11H3zM3 6.5h6M9 5h2l1.5 1.5v4.5a1.2 1.2 0 0 1-2.4 0V8H9M2 13.5h8"),
  quality: P("M8 1.8 14 4v4.4c0 3-2.5 5-6 5.8-3.5-.8-6-2.8-6-5.8V4z", <path d="M8 5.2l.8 1.7 1.9.3-1.4 1.3.3 1.9L8 9.5l-1.6.9.3-1.9-1.4-1.3 1.9-.3z" fill="currentColor" stroke="none" />),
  issues: P("M8 2.4 14.4 13.2H1.6zM8 6.6v3M8 11.4h.01"),
  incident: P("M8 1.8 14 4v4.4c0 3-2.5 5-6 5.8-3.5-.8-6-2.8-6-5.8V4zM8 5.6v3.2M8 10.9h.01"),
  kpi: P("M2 13h12M3 10.4 6 6.8l2.6 2.2L13 3.4M10.5 3.4H13v2.5"),
  audit: P("M10.4 10.4 14 14M5.2 7l1.4 1.4L9 6", <circle cx="7" cy="7" r="4.6" />),
  documents: P("M3 2.2h6l4 4v7.6H3zM9 2.2v4h4M5.5 9h5M5.5 11.5h3.5"),
  reports: P("M2.6 1.8h10.8a1 1 0 0 1 1 1v10.4a1 1 0 0 1-1 1H2.6a1 1 0 0 1-1-1V2.8a1 1 0 0 1 1-1zM4.5 10.5l2.3-2.6 1.8 1.5 2.9-3.4M4.5 12.5h7"),
  ai: P("M8 4.6V7M8 9v2.4M5.9 6.8 3.9 5.6M10.1 6.8l2-1.2M5.9 9.2l-2 1.2M10.1 9.2l2 1.2", <><circle cx="8" cy="8" r="1.6" fill="currentColor" stroke="none" /><circle cx="8" cy="3" r="1.3" /><circle cx="8" cy="13" r="1.3" /><circle cx="3" cy="5" r="1.3" /><circle cx="13" cy="5" r="1.3" /><circle cx="3" cy="11" r="1.3" /><circle cx="13" cy="11" r="1.3" /></>),
  assistant: P("M3.5 5.5h9a1 1 0 0 1 1 1v5a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-5a1 1 0 0 1 1-1zM8 5.5V3M6 14.5h4M1 8.5h1.5M13.5 8.5H15", <><circle cx="8" cy="2.4" r=".9" fill="currentColor" stroke="none" /><circle cx="6" cy="8.6" r=".9" fill="currentColor" stroke="none" /><circle cx="10" cy="8.6" r=".9" fill="currentColor" stroke="none" /><path d="M6.3 10.8h3.4" /></>),
  integrations: P("M5 2v3M11 2v3M3.5 5h9v2.5a4.5 4.5 0 0 1-9 0zM8 12v2.5"),
  abs: P("M6.5 9.5a2.5 2.5 0 0 0 3.5 0l2.5-2.5a2.5 2.5 0 0 0-3.5-3.5L8 4.5M9.5 6.5a2.5 2.5 0 0 0-3.5 0L3.5 9a2.5 2.5 0 0 0 3.5 3.5l1-1"),
  ccs: P("M2.5 4.5h11v7h-11zM2.5 7.5h11M5.5 10h2M10 10h1"),
  outlook: P("M2 3.5h12v9H2zM2 4l6 4.5L14 4"),
  line: P("M2.5 3h11v7h-6l-3 3v-3h-2z", <><circle cx="5.8" cy="6.5" r=".8" fill="currentColor" stroke="none" /><circle cx="8" cy="6.5" r=".8" fill="currentColor" stroke="none" /><circle cx="10.2" cy="6.5" r=".8" fill="currentColor" stroke="none" /></>),
  admin: P("M8 1.4v2.2M8 12.4v2.2M1.4 8h2.2M12.4 8h2.2M3.3 3.3l1.6 1.6M11.1 11.1l1.6 1.6M12.7 3.3l-1.6 1.6M4.9 11.1l-1.6 1.6", <circle cx="8" cy="8" r="2.4" />),
};

/** A screen's glyph, in the colour of the text beside it. */
export function NavGlyph({ screen, rects, size = 16 }: { screen: Screen; rects: number[][]; size?: number }) {
  const icon = NAV_ICONS[screen];
  if (!icon) {
    return (
      <svg width={size} height={size} viewBox="0 0 16 16" fill="currentColor" aria-hidden="true" style={{ display: "block" }}>
        {rects.map((r, i) => <rect key={i} x={r[0]} y={r[1]} width={r[2]} height={r[3]} rx={0.8} />)}
      </svg>
    );
  }
  return (
    <svg width={size} height={size} viewBox="0 0 16 16" fill="none" stroke="currentColor" strokeWidth="1.5"
      strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" style={{ display: "block" }}>
      {icon}
    </svg>
  );
}
