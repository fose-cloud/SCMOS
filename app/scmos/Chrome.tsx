"use client";

import { useEffect, useState, type ReactNode } from "react";
import { badge, css } from "./theme";
import { APP_ENVIRONMENT, APP_VERSION } from "./version";
import { onFetching } from "./api";
import { ALL_NAV, HEADINGS, NAV, NAV_GROUPS, NAV_TAGS, SUB_NAV, type Screen } from "./nav";
import { NavGlyph } from "./navIcons";
import { StatGlyph } from "./StatCard";
import { tabIconFor } from "./statIcons";
import type { SearchGroup, SearchHit } from "./search";

/**
 * The element a screen's own controls are portalled into.
 *
 * A shared id rather than a passed ref: the screen that fills it is several
 * levels below the header and does not otherwise know it exists, and threading
 * a ref through would be plumbing for its own sake.
 */
export const TOOLBAR_SLOT = "scmos-screen-toolbar";

export type HeaderAction = { label: string; title?: string; style: string; go: () => void };
export type TabItem = { label: string; active: boolean; go: () => void };
export type FilterDef = {
  label: string;
  value: string;
  options: string[];
  onChange: (value: string) => void;
};

/**
 * Says out loud that what is on the screen may be a moment behind.
 *
 * Every screen now draws what it last held instead of a placeholder, so the
 * wait for the database to wake up happens behind figures rather than in front
 * of them. That is a better wait, but only if it is visible: a figure nobody
 * told you was stale is a figure you act on.
 */
function Refreshing() {
  const [busy, setBusy] = useState(false);
  useEffect(() => onFetching(setBusy), []);
  if (!busy) return null;
  return (
    <span title="กำลังดึงข้อมูลล่าสุด — ตัวเลขที่เห็นอยู่คือข้อมูลครั้งก่อน"
      style={css("display:inline-flex;align-items:center;gap:6px;font-size:11px;color:#8FB3D9;white-space:nowrap")}>
      <span style={css("width:6px;height:6px;border-radius:50%;background:#5B9BD5;animation:scmos-pulse 1.1s ease-in-out infinite")} />
      กำลังอัปเดต…
      <style>{"@keyframes scmos-pulse{0%,100%{opacity:.25}50%{opacity:1}}"}</style>
    </span>
  );
}

type Props = {
  screen: Screen;
  onNavigate: (screen: Screen) => void;
  navCounts: Record<string, number>;
  /**
   * When set, the only screens this account may open. Everything else is left
   * out of the rail entirely rather than shown and refused.
   */
  allowed?: Screen[];
  collapsed: boolean;
  onToggleSidebar: () => void;
  gq: string;
  onGq: (value: string) => void;
  /** Live results for the header search, grouped by where they live. */
  searchGroups: SearchGroup[];
  searchOpen: boolean;
  onSearchOpen: (open: boolean) => void;
  onSearchHit: (hit: SearchHit) => void;
  userName: string;
  userRole: string;
  userInit: string;
  /** Uploaded picture as a data URL; falls back to the initials when empty. */
  userAvatar: string;
  onLogout: () => void;
  onProfile: () => void;
  onSettings: () => void;
  alertCount: string;
  alertTone: "red" | "amber" | "blue";
  onToggleNotif: () => void;
  crumb: string;
  title: string;
  actions: HeaderAction[];
  tabs: TabItem[];
  /**
   * Whether the screen draws its own tabs.
   *
   * The workspace puts them on its control bar, beside the category buttons
   * and the filters — three tabs and a metre of empty white beside them was a
   * row the job rows were not getting.
   */
  hideTabs?: boolean;
  /**
   * Whether the screen keeps still and scrolls inside itself.
   *
   * The workspace does. Scrolling the page took the tabs, the filters and the
   * period bar off the top along with the rows they steer, which is the one
   * thing that must not move while you read down a grid. With this the shell
   * is fixed and the only thing that scrolls is the job rows.
   *
   * It also goes full bleed: a card inset from a grey page spends a band on
   * every edge, and here the grid <em>is</em> the page.
   */
  lockScroll?: boolean;
  /**
   * What the page is drawn on.
   *
   * Navy for the control tower — the dashboard — and paper for every other
   * screen, which is where the day's keying happens and where the department
   * asked, the same afternoon the navy went on, for white to come back. The
   * rail and the header band stay the tower's whatever the page is.
   */
  canvas?: "light" | "dark";
  filters: { defs: FilterDef[]; q: string; onQ: (value: string) => void; onReset: () => void } | null;
  children: ReactNode;
};

/**
 * The globe behind the mark, turning.
 *
 * A wireframe sphere: three latitude rings that stay put and six meridians
 * that sweep across the face. A meridian is a great circle through the
 * poles, and seen from the side it is an ellipse whose width runs from the
 * full disc, when it faces you, down to a line through the centre, when it
 * is edge-on — so each one animates its rx from R to 0 and back, a sixth of
 * a turn behind the one before it, and the six together read as one sphere
 * turning. Front arcs are brighter than back ones by the same phase, which
 * is what makes it look solid rather than like six rings breathing.
 *
 * SVG's own animation rather than a script: nothing to schedule, nothing to
 * tear down, and it keeps turning while React is busy elsewhere. Somebody
 * who has asked their system for less motion gets the sphere, still — the
 * animation elements are simply not rendered for them, decided after mount
 * so the server's markup and the browser's first paint agree.
 */
function RailGlobe() {
  const R = 40;
  const turn = "14s";
  const meridians = 6;
  const [still, setStill] = useState(false);
  useEffect(() => {
    const query = window.matchMedia?.("(prefers-reduced-motion: reduce)");
    if (!query) return;
    const read = () => setStill(query.matches);
    read();
    query.addEventListener("change", read);
    return () => query.removeEventListener("change", read);
  }, []);
  return (
    <svg aria-hidden="true" className="rail-globe" width="92" height="92" viewBox="0 0 92 92" fill="none"
      style={{ position: "absolute", right: -22, top: -26, opacity: .55 }}>
      <defs>
        <radialGradient id="rail-globe-shade" cx="38%" cy="34%" r="70%">
          <stop offset="0" stopColor="#5CC0F7" stopOpacity=".28" />
          <stop offset=".55" stopColor="#1E6FB8" stopOpacity=".10" />
          <stop offset="1" stopColor="#040E1D" stopOpacity=".55" />
        </radialGradient>
      </defs>
      <g transform="rotate(-18 46 46)" stroke="#3B9EE0" strokeWidth=".8">
        <circle cx="46" cy="46" r={R} fill="url(#rail-globe-shade)" />
        <ellipse cx="46" cy="46" rx={R} ry="14" opacity=".8" />
        <ellipse cx="46" cy="27" rx="30" ry="9" opacity=".55" />
        <ellipse cx="46" cy="65" rx="30" ry="9" opacity=".55" />
        {Array.from({ length: meridians }, (_, i) => {
          // Each meridian starts a sixth of a turn behind the last; a negative
          // begin puts it mid-sweep at first paint instead of all six at once.
          const begin = `${(-(i * 14) / meridians).toFixed(2)}s`;
          // Stilled, the six are spread evenly, which is the sphere at rest.
          const rest = Math.abs(Math.cos((i * Math.PI) / meridians)) * R;
          return (
            <ellipse key={i} cx="46" cy="46" rx={still ? rest : R} ry={R} opacity={still ? .6 : undefined}>
              {!still && <>
                <animate attributeName="rx" values={`${R};0;${R}`} keyTimes="0;.5;1" dur={turn} begin={begin}
                  repeatCount="indefinite" calcMode="spline" keySplines=".45 0 .55 1;.45 0 .55 1" />
                <animate attributeName="opacity" values=".9;.25;.9" keyTimes="0;.5;1" dur={turn} begin={begin}
                  repeatCount="indefinite" />
              </>}
            </ellipse>
          );
        })}
      </g>
    </svg>
  );
}

export function Chrome(p: Props) {
  /**
   * Whether the drawer is open. It means something only on a narrow screen,
   * where the stylesheet turns the rail into an overlay; on a wide one the
   * class does nothing and the rail is the column it always was.
   *
   * This deliberately does not ask JavaScript how wide the screen is. The first
   * version did — `matchMedia` and a change listener — and it was wrong in a way
   * worth remembering: when the event does not arrive, and it can not, the
   * layout stays as it was. A phone then gets the desktop rail, 248 of its 375
   * pixels go to a menu, 117 are left for the work, and nothing short of a
   * reload puts it right. A media query needs no event and cannot miss one.
   */
  const [drawer, setDrawer] = useState(false);
  // Navigating covers the thing the user just asked to see, so the drawer
  // closes behind them.
  //
  // Adjusted while rendering rather than in an effect. React finishes this pass
  // and re-runs with the new value before it paints, so the drawer is never on
  // screen over the page it was covering; an effect would close it one frame
  // late, which on a phone is a visible flick of the old menu over the new
  // screen.
  const [drawerOver, setDrawerOver] = useState(p.screen);
  if (drawerOver !== p.screen) {
    setDrawerOver(p.screen);
    setDrawer(false);
  }

  /**
   * Which branches the user has folded open or shut.
   *
   * Only the ones they have actually clicked. Everything else falls back to
   * "open if that is where you are", so arriving on a sub-screen shows you
   * where you are without a click, and folding one shut keeps it shut.
   */
  const [folded, setFolded] = useState<Partial<Record<Screen, boolean>>>({});

  const dark = p.canvas !== "light";
  /*
   * The band every screen's title sits on.
   *
   * The tower's navy on paper and navy alike, so the screens share one head
   * whatever they are drawn on — asked for on 14 September, once the working
   * screens had gone back to paper: "the same theme, smart hi-tech, dark
   * navy". It is drawn, not photographed: a fine grid at 26px, a glow off the
   * right-hand edge and a wash that deepens towards the title, over the navy
   * the header band already wears. Every colour here is dark or already
   * light, so the skin leaves the band alone inside paper too.
   */
  /*
   * The band is the department's own photograph — the night port that the
   * control tower's hero carries, `public/dashboard-hero.jpg` — held to the
   * right of the band and washed into navy towards the title, so the words
   * sit on solid ground and the picture shows where nothing has to be read.
   * The templates they sent on 14 September have exactly this band on every
   * screen: crumb and title at the left, the photograph across the middle,
   * their slogan at the right, and the screen's own buttons after it.
   */
  const strip = {
    // The photograph is a banner already — the blurred left third is where
    // its own title went — so the wash only has to carry the crumb and the
    // title, and the lower part of the picture is the part with the ship,
    // the containers and the department's line painted on them.
    bg: "linear-gradient(90deg,#071A31 0%,#071A31 22%,rgba(7,26,49,.78) 36%,rgba(7,26,49,.18) 56%,rgba(7,26,49,.08) 100%),"
      + "url(/dashboard-hero.jpg) right 72% / cover no-repeat,#071A31",
    line: "rgba(74,148,214,.32)", crumb: "#7fa8ca", title: "#fff",
    tab: "#8fb4d4", tabOn: "#fff", tabOnBg: "#1668ab", tabOnLine: "#1668ab",
  };
  const glyph = ALL_NAV.find(([key]) => key === p.screen);

  return (
    <div style={css("display:flex;flex-direction:column;height:100vh;min-height:100vh;overflow:hidden;color:#16232F")}>
      <header style={css("flex:none;height:60px;background:#0A2240;display:flex;align-items:center;gap:0;padding:0 20px 0 0;border-bottom:1px solid #071A31;z-index:40")}>
        {/*
          The wordmark is `public/cargo-logo.png`, drawn directly on the band.

          It had been set in type because the artwork available then —
          `brand-leschaco.png` — is a fully transparent PNG, max alpha 0, which
          is why the band looked empty. This one is real and opaque, and already
          carries a navy background close to the header colour. It needs no
          white wrapper: that wrapper was the visible frame around the mark.
        */}
        <div className="brand-band" style={css("width:248px;flex:none;height:60px;display:flex;align-items:center;gap:12px;padding-left:20px;border-right:1px solid #1B3A5C")}>
          {/* A phone header has room for one name, not a company band and a
              product band and a rule between them. SCMOS is the one that tells
              somebody which application they are looking at. */}
          <div className="only-wide" style={css("display:flex;align-items:center;gap:12px")}>
            {/* Sized by height so the mark keeps its proportions whatever the
                file's pixel dimensions turn out to be. */}
            <img src="/cargo-logo.png" alt="Leschaco (Thailand)"
              style={css("height:22px;width:auto;display:block")} />
            <div style={css("width:1px;height:24px;background:#2C4E75")} />
          </div>
          <div style={css("display:flex;flex-direction:column;line-height:1.05")}>
            <span style={css("font-size:15px;font-weight:700;color:#fff;letter-spacing:.06em")}>SCMOS</span>
            <span className="only-wide" style={css("font-size:9px;color:#7FA5CC;letter-spacing:.04em")}>SUBCONTRACT MGMT</span>
          </div>
        </div>

        <button
          className="hdr-btn"
          // One button, right on both: it opens the drawer and it narrows the
          // column, and each of those is inert on the screen where the other
          // applies. Nothing here has to know which screen it is on.
          onClick={() => { setDrawer((open) => !open); p.onToggleSidebar(); }}
          aria-label={drawer ? "ปิดเมนู" : "เปิดเมนู"}
          aria-expanded={drawer}
          style={css("margin-left:14px;width:32px;height:32px;flex:none;border:1px solid #24476E;background:#0E2B4F;border-radius:4px;color:#B9CFE5;cursor:pointer;font-size:13px")}
        >
          ☰
        </button>

        <div className="search-band" style={css("margin-left:16px;flex:1;min-width:0;max-width:520px;position:relative;display:flex;align-items:center")}>
          <span style={css("position:absolute;left:12px;font-size:13px;color:#7FA5CC")}>⌕</span>
          <input
            value={p.gq}
            onChange={(e) => { p.onGq(e.target.value); p.onSearchOpen(true); }}
            onFocus={() => p.onSearchOpen(true)}
            onKeyDown={(e) => {
              if (e.key === "Escape") p.onSearchOpen(false);
              // Enter opens the first hit, which is what you want after typing a
              // container number that only one job carries.
              if (e.key === "Enter" && p.searchGroups[0]?.hits[0]) p.onSearchHit(p.searchGroups[0].hits[0]);
            }}
            placeholder="ค้นหา — เลขตู้, Job/ABS, ทะเบียน, คนขับ, ลูกค้า, ผู้ขนส่ง, ชื่อหน้าจอ…"
            // `min-width:0` is what lets a flex child actually shrink; an input
            // has an intrinsic width of about twenty characters and will hold
            // the whole bar open past the edge of the screen without it.
            style={css("width:100%;min-width:0;height:36px;border-radius:4px;border:1px solid #24476E;background:#0E2B4F;color:#fff;" +
              "font-size:12.5px;padding:0 12px 0 32px;outline:none")}
          />
          {!!p.gq && (
            <button
              onClick={() => { p.onGq(""); p.onSearchOpen(false); }}
              aria-label="Clear search"
              style={css("position:absolute;right:8px;width:22px;height:22px;border:none;background:transparent;color:#7FA5CC;cursor:pointer;font-size:13px")}
            >
              ✕
            </button>
          )}

          {p.searchOpen && p.gq.trim().length >= 2 && (
            <>
              <button
                aria-label="Close search results"
                onClick={() => p.onSearchOpen(false)}
                style={css("position:fixed;inset:0;z-index:44;border:none;background:transparent;cursor:default")}
              />
              <div style={css("position:absolute;top:44px;left:0;right:0;max-height:min(70vh,560px);overflow-y:auto;background:#fff;border:1px solid #D8E0E8;border-radius:6px;box-shadow:0 18px 44px rgba(7,26,49,.28);z-index:45;padding:6px")}>
                {p.searchGroups.length ? p.searchGroups.map((g) => (
                  <div key={g.group}>
                    <div style={css("display:flex;justify-content:space-between;align-items:baseline;padding:8px 10px 5px")}>
                      <span style={css("font-size:10px;font-weight:700;color:#0A2240;letter-spacing:.06em")}>{g.group}</span>
                      {!!g.more && <span style={css("font-size:10px;color:#94A3B8")}>+{g.more} เพิ่มเติม</span>}
                    </div>
                    {g.hits.map((h) => (
                      <button
                        key={h.id}
                        type="button"
                        onClick={() => p.onSearchHit(h)}
                        className="nav-item"
                        style={css("font-family:inherit;text-align:left;width:100%;display:flex;align-items:center;gap:10px;padding:8px 10px;border:none;background:transparent;border-radius:4px;cursor:pointer")}
                      >
                        <span style={css("flex:1;min-width:0;display:flex;flex-direction:column;gap:2px")}>
                          <span style={css("font-size:12.5px;font-weight:600;color:#0A2240;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>{h.title}</span>
                          <span style={css("font-size:11px;color:#64748B;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>{h.sub}</span>
                        </span>
                        {h.demo && (
                          <span style={css("flex:none;font-family:'IBM Plex Mono',monospace;font-size:9px;color:#B45309;background:#FDF2DF;border-radius:3px;padding:2px 5px")}>DEMO</span>
                        )}
                        <span style={css("flex:none;" + badge(h.tag, h.tone))}>{h.tag}</span>
                      </button>
                    ))}
                  </div>
                )) : (
                  <div style={css("padding:16px 12px;font-size:12px;color:#94A3B8;text-align:center")}>
                    ไม่พบ “{p.gq.trim()}” ในงาน ลูกค้า ผู้ขนส่ง อัตราค่าขนส่ง CAR/PAR หรือชื่อเมนู
                  </div>
                )}
              </div>
            </>
          )}
        </div>

        <div className="only-wide" style={css("flex:1")} />

        <div className="header-actions" style={css("flex:none;display:flex;align-items:center;gap:8px")}>
          <Refreshing />
          <button
            className="hdr-btn"
            onClick={p.onToggleNotif}
            style={css("position:relative;height:34px;padding:0 12px;border:1px solid #24476E;background:#0E2B4F;border-radius:4px;color:#D6E5F2;cursor:pointer;font-size:12.5px;display:flex;align-items:center;gap:7px")}
          >
            ◔<span className="only-wide"> Alerts</span>
            <span style={css(
              "background:" + (p.alertTone === "red" ? "#D64545" : p.alertTone === "amber" ? "#B45309" : "#2E7DD1") +
              ";color:#fff;border-radius:9px;padding:1px 6px;font-size:10.5px;font-weight:600;font-family:'IBM Plex Mono',monospace",
            )}>
              {p.alertCount}
            </span>
          </button>

          <div style={css("width:1px;height:26px;background:#1B3A5C;margin:0 4px")} />

          <button
            className="hdr-btn"
            onClick={p.onProfile}
            aria-label={"Profile — " + p.userName}
            style={css("font-family:inherit;text-align:left;display:flex;align-items:center;gap:10px;padding:4px 8px 4px 4px;border:1px solid #24476E;background:#0E2B4F;border-radius:4px;cursor:pointer")}
          >
            {p.userAvatar ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={p.userAvatar} alt="" style={css("width:32px;height:32px;border-radius:4px;object-fit:cover;display:block;flex:none")} />
            ) : (
              <span style={css("width:32px;height:32px;border-radius:4px;background:#2E7DD1;color:#fff;display:flex;align-items:center;justify-content:center;font-size:12px;font-weight:600;letter-spacing:.03em;flex:none")}>
                {p.userInit}
              </span>
            )}
            {/* On a phone the avatar carries the identity on its own; the name
                and the role are the two blocks that push the whole bar off the
                right-hand edge, and they are already in the profile panel a tap
                away. */}
            <span className="only-wide" style={css("display:flex;flex-direction:column;line-height:1.25")}>
              <span style={css("font-size:12.5px;color:#fff;font-weight:600")}>{p.userName}</span>
              <span style={css("font-size:10.5px;color:#7FA5CC")}>{p.userRole}</span>
            </span>
            <span style={css("font-size:9px;color:#7FA5CC;margin-left:2px")}>▾</span>
          </button>

          <button
            className="hdr-btn"
            onClick={p.onSettings}
            aria-label="Settings"
            style={css("width:32px;height:32px;border:1px solid #24476E;background:#0E2B4F;border-radius:4px;color:#B9CFE5;cursor:pointer;font-size:13px")}
          >
            ⚙
          </button>
          <button
            className="hdr-btn"
            onClick={p.onLogout}
            style={css("height:32px;padding:0 12px;border:1px solid #24476E;background:transparent;border-radius:4px;color:#B9CFE5;cursor:pointer;font-size:12px")}
          >
            Sign out
          </button>
        </div>
      </header>

      <div style={css("flex:1;display:flex;min-height:0")}>
        {/* Tapping the page behind the drawer shuts it — the gesture everybody
            already knows, and the only one available when the menu covers the
            button that opened it. */}
        {drawer && (
          // A button rather than a div: it is a control, and made of one it
          // answers Escape and the keyboard as well as the tap, which a div
          // with an onClick never did.
          <button type="button" className="rail-scrim" aria-label="ปิดเมนู"
            onClick={() => setDrawer(false)}
            style={css("display:none;position:fixed;inset:0;border:0;padding:0;background:rgba(4,16,30,.45);z-index:44;cursor:pointer")} />
        )}

        <nav className={"app-rail" + (drawer ? " is-open" : "")}
          style={css("flex:none;width:" + (p.collapsed ? "64px" : "248px") +
            ";background:#040E1D;display:flex;flex-direction:column;border-right:1px solid #0E2A47;transition:width .16s ease;overflow:hidden;padding:8px")}>
          {/*
            The department's own menu: one glass panel with a lit edge, the
            entries in six named sections, the current screen a lit pill.
            The panel is the rail's inner frame rather than the rail itself so
            the glow has somewhere to sit, and so the drawer on a phone keeps
            the same shape.
          */}
          <div className="rail-panel" style={css("flex:1;min-height:0;display:flex;flex-direction:column;border-radius:12px;"
            + "background:linear-gradient(180deg,#0A2140 0%,#071A31 55%,#061428 100%);"
            + "border:1px solid rgba(74,148,214,.36);"
            + "box-shadow:0 0 0 1px rgba(30,140,220,.08),0 0 26px rgba(30,140,220,.16),inset 0 0 40px rgba(20,100,170,.10)")}>

            {!p.collapsed && (
              <div style={css("flex:none;position:relative;padding:14px 16px 10px;border-bottom:1px solid rgba(74,148,214,.2);overflow:hidden")}>
                <RailGlobe />
                <span style={css("display:block;font-size:22px;font-weight:600;letter-spacing:.2em;color:#DDF0FF;line-height:1;text-shadow:0 0 14px rgba(92,192,247,.45)")}>SCM<span style={css("color:#5CC0F7")}>OS</span></span>
                {/* Two lines: at this size and tracking the name does not fit on one. */}
                <span style={css("display:block;margin-top:5px;font-family:'IBM Plex Mono',monospace;font-size:8px;letter-spacing:.18em;line-height:1.5;color:#6FA0CC")}>
                  SUBCONTRACT MANAGEMENT<br />OPERATION SYSTEM
                </span>
              </div>
            )}

            <div style={css("flex:1;overflow-y:auto;overflow-x:hidden;padding:" + (p.collapsed ? "8px 0" : "10px 8px 12px"))}>
              {(() => {
                const allowed = (key: Screen) => !p.allowed || p.allowed.includes(key);
                const entry = (row: [Screen, string, string, number[][]], depth: 0 | 1) => {
                  const [key, label, th, rects] = row;
                  const heading = HEADINGS.includes(key);
                  const active = p.screen === key && !heading;
                  const count = p.navCounts[key];
                  const tag = NAV_TAGS[key];
                  const children = (SUB_NAV[key] ?? []).filter(([child]) => allowed(child));
                  const onChild = children.some(([child]) => child === p.screen);
                  // The user's own choice wins; otherwise open when this branch is
                  // where they are. A dropdown that collapses out from under the
                  // screen you are looking at is one you must reopen to see where
                  // you stand.
                  const openBranch = folded[key] ?? (active || onChild);
                  const tight = p.collapsed;
                  const lit = active;
                  const ink = lit ? "#FFFFFF" : depth ? "#B9D0E6" : "#D3E3F3";
                  const glyph = lit ? "#FFFFFF" : "#5CC0F7";
                  return (
                    <div key={key + "-branch"} style={css("position:relative")}>
                      <button
                        type="button"
                        aria-label={label}
                        title={th}
                        aria-current={active ? "page" : undefined}
                        className={lit ? "rail-lit" : "rail-item"}
                        onClick={() => (heading
                          ? setFolded((was) => ({ ...was, [key]: !openBranch }))
                          : p.onNavigate(key))}
                        aria-expanded={heading ? openBranch : undefined}
                        style={css(
                          "width:100%;text-align:left;font-family:inherit;border:1px solid " + (lit ? "#3B9EE0" : "transparent") + ";border-radius:9px;" +
                          "display:flex;align-items:center;gap:12px;margin:2px 0;cursor:pointer;transition:background .12s,box-shadow .12s;" +
                          (tight ? "padding:10px 0;justify-content:center;"
                            : "padding:" + (depth ? "7px 34px 7px 14px" : "9px 34px 9px 14px") + ";") +
                          (lit
                            ? "background:linear-gradient(100deg,#1668AB,#0F4D82);box-shadow:0 0 0 1px rgba(59,158,224,.2),0 0 18px rgba(30,140,220,.35);"
                            : "background:transparent;") +
                          "color:" + ink,
                        )}
                      >
                        <span style={css("flex:none;display:flex;color:" + glyph + (lit ? ";filter:drop-shadow(0 0 4px rgba(255,255,255,.5))" : ""))}>
                          <NavGlyph screen={key} rects={rects} size={depth ? 15 : 16} />
                        </span>
                        {!tight && (
                          <span style={css("font-size:" + (depth ? "12.5px" : "13.5px") + ";font-weight:" + (lit ? "600" : "500") + ";white-space:nowrap;overflow:hidden;text-overflow:ellipsis;min-width:0")}>{label}</span>
                        )}
                        {!tight && !!tag && (
                          <span style={css("margin-left:auto;font-family:'IBM Plex Mono',monospace;font-size:9px;font-weight:600;letter-spacing:.1em;color:#5CC0F7;border:1px solid rgba(92,192,247,.7);background:rgba(92,192,247,.12);border-radius:9px;padding:2px 8px;box-shadow:0 0 10px rgba(92,192,247,.35)")}>
                            {tag}
                          </span>
                        )}
                        {!tight && !tag && !!count && (
                          <span style={css("margin-left:auto;background:" + (key === "incident" ? "#D64545" : "rgba(92,192,247,.16)") + ";color:" + (key === "incident" ? "#fff" : "#8ED4FF") + ";border-radius:9px;padding:1px 7px;font-size:10.5px;font-weight:600;font-family:'IBM Plex Mono',monospace")}>
                            {count}
                          </span>
                        )}
                      </button>

                      {/* The fold. A sibling of the nav button rather than inside
                          it, because a button within a button is not something a
                          browser or a screen reader can make sense of — and
                          because the two do different things: one goes
                          somewhere, one opens a list. */}
                      {children.length > 0 && !tight && (
                        <button
                          type="button"
                          onClick={() => setFolded((was) => ({ ...was, [key]: !openBranch }))}
                          aria-expanded={openBranch}
                          aria-label={(openBranch ? "ย่อเมนูย่อยของ " : "กางเมนูย่อยของ ") + label}
                          style={css("position:absolute;right:8px;top:7px;width:26px;height:26px;border:0;" +
                            "background:transparent;cursor:pointer;display:flex;align-items:center;justify-content:center;" +
                            "color:" + (onChild ? "#5CC0F7" : "#7FA5CC") + ";" +
                            "transform:rotate(" + (openBranch ? "0deg" : "-90deg") + ");transition:transform .14s")}
                        >
                          <svg width="12" height="12" viewBox="0 0 16 16" stroke="currentColor" strokeWidth="1.8" fill="none" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d="M4 6.5 8 10.5l4-4" /></svg>
                        </button>
                      )}

                      {/* Collapsed, the fold has nowhere to live and no label to
                          read, so the children simply stand on their own as icons.
                          Hiding them would put three screens behind a rail width. */}
                      {children.length > 0 && (openBranch || tight) && children.map((child) => entry(child, 1))}
                    </div>
                  );
                };

                const top = NAV.filter(([key]) => allowed(key));
                const dashboard = top.find(([key]) => key === "dashboard");
                const grouped = new Set(NAV_GROUPS.flatMap((g) => g.keys));
                // Anything the sections do not name still gets drawn, after
                // them, so a new screen is never invisible for want of a group.
                const loose = top.filter(([key]) => key !== "dashboard" && !grouped.has(key));

                return (
                  <>
                    {dashboard && entry(dashboard, 0)}
                    {NAV_GROUPS.map((group) => {
                      const rows = group.keys
                        .map((key) => top.find(([k]) => k === key))
                        .filter((row): row is [Screen, string, string, number[][]] => !!row);
                      if (!rows.length) return null;
                      return (
                        <div key={group.label}>
                          {p.collapsed
                            ? <div aria-hidden="true" style={css("height:1px;margin:8px 12px;background:rgba(74,148,214,.22)")} />
                            : <div style={css("display:flex;align-items:center;gap:10px;padding:16px 6px 5px 6px")}>
                                <span style={css("font-family:'IBM Plex Mono',monospace;font-size:10px;font-weight:600;letter-spacing:.16em;color:#4FB3F0;white-space:nowrap;text-shadow:0 0 10px rgba(79,179,240,.35)")}>{group.label}</span>
                                <span aria-hidden="true" style={css("flex:1;height:1px;background:linear-gradient(90deg,rgba(79,179,240,.55),rgba(79,179,240,0))")} />
                              </div>}
                          {rows.map((row) => entry(row, 0))}
                        </div>
                      );
                    })}
                    {loose.map((row) => entry(row, 0))}
                  </>
                );
              })()}
            </div>

            {!p.collapsed && (
              <div style={css("flex:none;border-top:1px solid rgba(74,148,214,.2);padding:11px 16px 12px;display:flex;flex-direction:column;gap:5px")}>
                <span style={css("font-family:'IBM Plex Mono',monospace;font-size:9.5px;font-weight:600;letter-spacing:.16em;color:#4FB3F0")}>ENVIRONMENT</span>
                <span style={css("display:flex;align-items:center;gap:9px;font-size:11.5px;color:#B9CFE5")}>
                  <svg width="14" height="14" viewBox="0 0 16 16" fill="none" stroke="#5CC0F7" strokeWidth="1.5" strokeLinecap="round" aria-hidden="true"><rect x="2" y="2" width="12" height="4" rx="1" /><rect x="2" y="10" width="12" height="4" rx="1" /><path d="M4.5 4h.01M4.5 12h.01" /></svg>
                  {APP_ENVIRONMENT} · {APP_VERSION}
                </span>
              </div>
            )}
          </div>
        </nav>

        {/*
          `paper` is the class the skin's variables answer to: inside it every
          colour a screen was drawn with resolves to itself rather than to the
          navy — see theme.ts. So a light canvas is one class on the main
          element, not a second rendering of fifty screens.
        */}
        <main className={dark ? undefined : "paper"}
          style={css("flex:1;min-width:0;background:" + (dark ? "#06152a" : "#EEF2F6") + ";"
          + (p.lockScroll ? "overflow:hidden;display:flex;flex-direction:column" : "overflow-y:auto"))}>
          {/*
            The heading, compact where the screen cannot scroll.

            On a locked page every pixel above the grid is permanent, and this
            block was a hundred and twenty of them: a breadcrumb on its own
            line, a title, and a sentence explaining the screen that anybody
            working in it read months ago. Compact, the breadcrumb sits on the
            title's line and the sentence goes. Nothing anybody clicks is
            removed, and every other screen keeps the full heading.
          */}
          <div style={css(`background:${strip.bg};border-bottom:1px solid ${strip.line};position:sticky;top:0;z-index:30;`
            + (p.lockScroll ? "padding:9px 20px 0" : "padding:18px 24px 0;min-height:104px"))}>
            <div className="page-head" style={css("display:flex;align-items:flex-start;gap:20px")}>
              {/* The screen's own glyph in a lit tile, the way the templates
                  lead every title. Off the compact band: at seventeen pixels
                  of title there is no room for a tile beside it. */}
              {!p.lockScroll && glyph && (
                <div aria-hidden="true" style={css("flex:none;width:44px;height:44px;margin-top:3px;border-radius:10px;display:flex;align-items:center;justify-content:center;"
                  + "color:#5CC0F7;background:rgba(10,34,64,.72);border:1px solid rgba(74,148,214,.5);"
                  + "box-shadow:0 0 0 1px rgba(30,140,220,.12),0 0 18px rgba(30,140,220,.35),inset 0 0 14px rgba(20,100,170,.25)")}>
                  <NavGlyph screen={glyph[0]} rects={glyph[3]} size={22} />
                </div>
              )}
              <div style={css("flex:1;min-width:0")}>
                {!p.lockScroll && (
                  <div style={css(`font-size:11px;color:${strip.crumb};letter-spacing:.06em;margin-bottom:5px;font-family:'IBM Plex Mono',monospace`)}>
                    {p.crumb}
                  </div>
                )}
                <div style={css("display:flex;align-items:baseline;gap:12px;flex-wrap:wrap")}>
                  <h1 style={css(`margin:0;font-weight:600;color:${strip.title};letter-spacing:-.01em;font-size:`
                    + (p.lockScroll ? "17px" : "22px"))}>{p.title}</h1>
                  {p.lockScroll && (
                    <span style={css("font-size:10.5px;color:#A6B4C2;letter-spacing:.06em;font-family:'IBM Plex Mono',monospace")}>
                      {p.crumb}
                    </span>
                  )}
                </div>
              </div>
              {/*
                A screen that draws its own controls draws these too.

                They sat up here, and full screen hides everything above the
                grid — so the one place you would want to import from was the
                one place they were not.
              */}
              {/*
                Pushed down to the title's line, not the top of the column.

                The column above them is two lines — the breadcrumb, then the
                title — so the actions clear the breadcrumb's 14px line and its
                5px margin, then centre themselves on the 29px the title
                occupies. It was 10px, which left every export and import
                button riding a few pixels above the heading it belongs to; on
                a screen shown to the customer that reads as a slip.
              */}
              {!p.lockScroll && (
                <div className="page-actions" style={css("display:flex;gap:8px;align-items:center;padding-top:17px")}>
                  {p.actions.map((a) => (
                    <button key={a.label} title={a.title} onClick={a.go} style={css(a.style)}>{a.label}</button>
                  ))}
                </div>
              )}
              {/*
                The mark in the band's top right corner, as the department's
                template for Rate Quotation has it: SCMOS over three lines of
                tracked capitals in a lit frame. Off the compact band, which
                has no height for it, and off narrow screens with it.
              */}
              <div className="only-wide" aria-hidden="true"
                style={css("flex:none;align-self:flex-start;margin-left:6px;border-radius:6px;"
                  + "border:1px solid rgba(92,192,247,.55);background:rgba(4,14,29,.62);"
                  + "box-shadow:0 0 0 1px rgba(30,140,220,.12),0 0 18px rgba(30,140,220,.35),inset 0 0 22px rgba(20,100,170,.18);"
                  + "backdrop-filter:blur(2px);"
                  // The compact band has one line of height, so the mark takes one line.
                  + (p.lockScroll ? "padding:5px 12px;display:flex;align-items:center;gap:10px" : "padding:9px 14px 10px"))}>
                <div style={css("font-weight:700;letter-spacing:.14em;color:#DDF0FF;line-height:1;text-shadow:0 0 12px rgba(92,192,247,.5);font-size:"
                  + (p.lockScroll ? "13px" : "17px"))}>SCMOS</div>
                {p.lockScroll
                  ? <div style={css("font-family:'IBM Plex Mono',monospace;font-size:7.5px;letter-spacing:.2em;color:#8FB4D4;white-space:nowrap")}>
                      SMART LOGISTICS · BETTER <span style={css("color:#5CC0F7")}>TOMORROW</span>
                    </div>
                  : <div style={css("margin-top:6px;font-family:'IBM Plex Mono',monospace;font-size:8px;letter-spacing:.2em;line-height:1.55;color:#8FB4D4")}>
                      SMART<br />LOGISTICS<br />BETTER<br /><span style={css("color:#5CC0F7")}>TOMORROW</span>
                    </div>}
              </div>
            </div>

            {/* The strip scrolls, not the page. Reaching CALENDAR by dragging the
                whole of `main` sideways takes the title and the breadcrumb with
                it, and you arrive at the tab having lost the heading that says
                what you are looking at. */}
            {!p.hideTabs && (
            <div style={css("display:flex;gap:6px;overflow-x:auto;scrollbar-width:thin;" +
              (p.lockScroll ? "margin-top:8px;" : "margin-top:14px;") +
              "-webkit-overflow-scrolling:touch;padding-bottom:2px")}>
              {p.tabs.map((t) => {
                // Pills with a glyph, the current one lit — the department's
                // template for the rate screens' three tabs, and so for all.
                const glyph = tabIconFor(t.label);
                return (
                  <button
                    key={t.label}
                    onClick={t.go}
                    style={css(
                      "height:36px;padding:0 15px;border:1px solid " + (t.active ? "#3B9EE0" : "rgba(74,148,214,.32)") +
                      ";background:" + (t.active ? "rgba(22,104,171,.85)" : "rgba(10,34,64,.55)") +
                      ";color:" + (t.active ? strip.tabOn : strip.tab) +
                      ";font-size:12.5px;font-weight:" + (t.active ? "600" : "400") +
                      (t.active ? ";box-shadow:0 0 0 1px rgba(92,192,247,.25),0 0 14px rgba(59,158,224,.45)" : "") +
                      // A flex child shrinks by default, so without these the
                      // eight tabs squeeze into unreadable slivers instead of
                      // staying their own width and scrolling.
                      ";border-radius:8px 8px 0 0;cursor:pointer;flex:none;white-space:nowrap;" +
                      "display:inline-flex;align-items:center;gap:7px;font-family:inherit",
                    )}
                  >
                    {glyph && <span aria-hidden="true" style={css("display:flex;color:" + (t.active ? "#fff" : "#5CC0F7"))}><StatGlyph icon={glyph} size={15} /></span>}
                    {t.label}
                  </button>
                );
              })}
            </div>
            )}
          </div>

          {/*
            Where a screen's own controls go.

            Immediately under the tabs and drawn as part of the same white
            header band — the point of moving them was that they belong with
            the title, and a card floating in the page background below a grey
            gap is what they already were.

            Outside the sticky element on purpose. The workspace's filters run
            to four rows, and pinned they left sixty pixels of grid on a laptop:
            a header is only worth sticking while it leaves room for the work
            underneath it. So they sit against the title and scroll away once
            you are reading rows.

            Empty when nothing fills it, and an empty div costs nothing.
          */}
          <div id={TOOLBAR_SLOT} />

          <div style={css(p.lockScroll
            ? "flex:1;min-height:0;display:flex;flex-direction:column"
            : "padding:20px 24px 40px;display:flex;flex-direction:column;gap:16px")}>
            {p.filters && (
              <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 14px;display:flex;gap:10px;align-items:center;flex-wrap:wrap")}>
                <span style={css("font-size:11px;font-weight:600;color:#0A2240;letter-spacing:.06em;padding-right:4px")}>FILTERS</span>
                {p.filters.defs.map((f) => (
                  <label key={f.label} style={css("display:flex;flex-direction:column;gap:3px")}>
                    <span style={css("font-size:10px;color:#8496A8;letter-spacing:.05em;font-weight:500")}>{f.label}</span>
                    <select
                      value={f.value}
                      onChange={(e) => f.onChange(e.target.value)}
                      style={css("height:32px;min-width:132px;border:1px solid #D8E0E8;border-radius:4px;background:#F8FAFC;font-size:12.5px;color:#16232F;padding:0 8px;outline:none;cursor:pointer")}
                    >
                      {f.options.map((o) => <option key={o} value={o}>{o}</option>)}
                    </select>
                  </label>
                ))}
                <label style={css("display:flex;flex-direction:column;gap:3px;flex:1;min-width:200px")}>
                  <span style={css("font-size:10px;color:#8496A8;letter-spacing:.05em;font-weight:500")}>SEARCH</span>
                  <input
                    value={p.filters.q}
                    onChange={(e) => p.filters!.onQ(e.target.value)}
                    placeholder="Type to search this table…"
                    style={css("height:32px;border:1px solid #D8E0E8;border-radius:4px;background:#F8FAFC;font-size:12.5px;padding:0 10px;outline:none")}
                  />
                </label>
                <div style={css("display:flex;gap:8px;align-self:flex-end")}>
                  <button className="ghost-btn" onClick={p.filters.onReset} style={css("height:32px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:12px;color:#475569;cursor:pointer")}>
                    ↻ Reset
                  </button>
                  <button className="ghost-btn" style={css("height:32px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:12px;color:#475569;cursor:pointer")}>
                    ★ Save view
                  </button>
                </div>
              </div>
            )}

            {p.children}
          </div>
        </main>
      </div>
    </div>
  );
}
