"use client";

import { useEffect, useState, type ReactNode } from "react";
import { badge, css } from "./theme";
import { onFetching } from "./api";
import { HEADINGS, NAV, SUB_NAV, type Screen } from "./nav";
import type { SearchGroup, SearchHit } from "./search";

/**
 * The element a screen's own controls are portalled into.
 *
 * A shared id rather than a passed ref: the screen that fills it is several
 * levels below the header and does not otherwise know it exists, and threading
 * a ref through would be plumbing for its own sake.
 */
export const TOOLBAR_SLOT = "scmos-screen-toolbar";

export type HeaderAction = { label: string; style: string; go: () => void };
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

function NavIcon({ rects, color }: { rects: number[][]; color: string }) {
  return (
    <svg width={16} height={16} viewBox="0 0 16 16" fill={color} style={{ display: "block" }}>
      {rects.map((r, i) => (
        <rect key={i} x={r[0]} y={r[1]} width={r[2]} height={r[3]} rx={0.8} />
      ))}
    </svg>
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
  titleTh: string;
  blurb: string;
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
  filters: { defs: FilterDef[]; q: string; onQ: (value: string) => void; onReset: () => void } | null;
  children: ReactNode;
};

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
            ";background:#071A31;display:flex;flex-direction:column;border-right:1px solid #143254;transition:width .16s ease;overflow:hidden")}>
          <div style={css("flex:1;overflow-y:auto;padding:10px 0")}>
            {NAV.filter(([key]) => !p.allowed || p.allowed.includes(key))
              .map(([key, label, th, rects]) => {
              const active = p.screen === key && !HEADINGS.includes(key);
              const count = p.navCounts[key];
              const children = (SUB_NAV[key] ?? [])
                .filter(([child]) => !p.allowed || p.allowed.includes(child));
              const onChild = children.some(([child]) => child === p.screen);
              // The user's own choice wins; otherwise open when this branch is
              // where they are. A dropdown that collapses out from under the
              // screen you are looking at is one you must reopen to see where
              // you stand.
              const openBranch = folded[key] ?? (active || onChild);
              // A heading has nothing of its own to open. Clicking it works the
              // fold, and it never draws itself as the current page — the child
              // the user is actually on does that.
              const heading = HEADINGS.includes(key);
              // Inside the drawer there is room for labels again, so the
              // collapsed icon-only rail is not what should be drawn there.
              // Inside the drawer there is room for labels again, and the
              // stylesheet gives the rail that width there, so the icon-only
              // form is a desktop idea only.
              const tight = p.collapsed;
              return (
                <div key={key + "-branch"} style={css("position:relative")}>
                <button
                  key={key}
                  type="button"
                  aria-label={label}
                  aria-current={active ? "page" : undefined}
                  className={active ? undefined : "nav-item"}
                  onClick={() => (heading
                    ? setFolded((was) => ({ ...was, [key]: !openBranch }))
                    : p.onNavigate(key))}
                  aria-expanded={heading ? openBranch : undefined}
                  style={css(
                    "width:100%;text-align:left;font-family:inherit;border:0;" +
                    "display:flex;align-items:center;gap:12px;padding:" +
                    (tight ? "10px 0;justify-content:center;"
                      : "8px " + (children.length && !tight ? "34px" : "18px") + " 8px 18px;") +
                    "cursor:pointer;border-left:3px solid " + (active ? "#4E9BE8" : "transparent") +
                    ";background:" + (active ? "#123A66" : "transparent") +
                    ";color:" + (active ? "#fff" : "#C4D6E6") + ";transition:background .12s",
                  )}
                >
                  <span style={css("flex:none;display:flex;color:" + (active ? "#4E9BE8" : "#7FA5CC"))}>
                    <NavIcon rects={rects} color={active ? "#4E9BE8" : "#7FA5CC"} />
                  </span>
                  {!tight && (
                    <span style={css("display:flex;flex-direction:column;line-height:1.2;min-width:0")}>
                      <span style={css("font-size:13px;font-weight:500;white-space:nowrap")}>{label}</span>
                      <span style={css("font-size:10px;color:#5D82A8;white-space:nowrap")}>{th}</span>
                    </span>
                  )}
                  {!tight && !!count && (
                    <span style={css("margin-left:auto;background:" + (key === "incident" ? "#D64545" : "#1B4A7A") + ";color:#fff;border-radius:9px;padding:1px 7px;font-size:10.5px;font-weight:600;font-family:'IBM Plex Mono',monospace")}>
                      {count}
                    </span>
                  )}
                </button>

                {/* The fold. A sibling of the nav button rather than inside it,
                    because a button within a button is not something a browser
                    or a screen reader can make sense of — and because the two
                    do different things: one goes somewhere, one opens a list. */}
                {children.length > 0 && !tight && (
                  <button
                    type="button"
                    onClick={() => setFolded((was) => ({ ...was, [key]: !openBranch }))}
                    aria-expanded={openBranch}
                    aria-label={(openBranch ? "ย่อเมนูย่อยของ " : "กางเมนูย่อยของ ") + label}
                    style={css("position:absolute;right:6px;top:5px;width:24px;height:24px;border:0;" +
                      "background:transparent;cursor:pointer;display:flex;align-items:center;justify-content:center;" +
                      "color:" + (active || onChild ? "#4E9BE8" : "#7FA5CC") + ";font-size:10px;line-height:1;" +
                      "transform:rotate(" + (openBranch ? "0deg" : "-90deg") + ");transition:transform .14s")}
                  >
                    ▼
                  </button>
                )}

                {/* The sub-menu. Only drawn when this branch is open and the
                    rail is wide — collapsed, there is no room for a label, and
                    an unlabelled indent is just a smaller mystery. */}
                {/* Collapsed, the fold has nowhere to live and no label to
                    read, so the children simply stand on their own as icons.
                    Hiding them would put three screens behind a rail width. */}
                {children.length > 0 && (openBranch || tight) && children.map(([child, childLabel, childTh, childRects]) => {
                  const on = p.screen === child;
                  const childCount = p.navCounts[child];
                  return (
                    <button
                      key={child}
                      type="button"
                      aria-label={childLabel}
                      title={tight ? childLabel : undefined}
                      aria-current={on ? "page" : undefined}
                      className={on ? undefined : "nav-item"}
                      onClick={() => p.onNavigate(child)}
                      style={css(
                        "width:100%;text-align:left;font-family:inherit;border:0;" +
                        "display:flex;align-items:center;gap:10px;padding:" +
                        (tight ? "9px 0;justify-content:center;" : "6px 18px 6px 36px;") +
                        "cursor:pointer;border-left:3px solid " + (on ? "#4E9BE8" : "transparent") +
                        ";background:" + (on ? "#123A66" : "transparent") +
                        ";color:" + (on ? "#fff" : "#A9C3DA") + ";transition:background .12s",
                      )}
                    >
                      <span style={css("flex:none;display:flex;opacity:" + (tight ? "1" : ".85"))}>
                        <NavIcon rects={childRects} color={on ? "#4E9BE8" : "#6E93BC"} />
                      </span>
                      {!tight && (
                        <span style={css("display:flex;flex-direction:column;line-height:1.2;min-width:0")}>
                          <span style={css("font-size:12.5px;font-weight:500;white-space:nowrap")}>{childLabel}</span>
                          <span style={css("font-size:9.5px;color:#5D82A8;white-space:nowrap")}>{childTh}</span>
                        </span>
                      )}
                      {!tight && !!childCount && (
                        <span style={css("margin-left:auto;background:#1B4A7A;color:#fff;border-radius:9px;padding:1px 7px;font-size:10.5px;font-weight:600;font-family:'IBM Plex Mono',monospace")}>
                          {childCount}
                        </span>
                      )}
                    </button>
                  );
                })}
                </div>
              );
            })}
          </div>

          {!p.collapsed && (
            <div style={css("flex:none;border-top:1px solid #143254;padding:12px 18px;display:flex;flex-direction:column;gap:3px")}>
              <span style={css("font-size:10px;color:#5D82A8;letter-spacing:.08em")}>ENVIRONMENT</span>
              <span style={css("font-size:11.5px;color:#B9CFE5")}>Production · TH-BKK · v2.4.1</span>
            </div>
          )}
        </nav>

        <main style={css("flex:1;min-width:0;background:#EEF2F6;"
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
          <div style={css("background:#fff;border-bottom:1px solid #D8E0E8;position:sticky;top:0;z-index:30;"
            + (p.lockScroll ? "padding:9px 20px 0" : "padding:16px 24px 0"))}>
            <div className="page-head" style={css("display:flex;align-items:flex-start;gap:20px")}>
              <div style={css("flex:1;min-width:0")}>
                {!p.lockScroll && (
                  <div style={css("font-size:11px;color:#8496A8;letter-spacing:.06em;margin-bottom:5px;font-family:'IBM Plex Mono',monospace")}>
                    {p.crumb}
                  </div>
                )}
                <div style={css("display:flex;align-items:baseline;gap:12px;flex-wrap:wrap")}>
                  <h1 style={css("margin:0;font-weight:600;color:#0A2240;letter-spacing:-.01em;font-size:"
                    + (p.lockScroll ? "17px" : "22px"))}>{p.title}</h1>
                  <span style={css("color:#64748B;font-weight:400;font-size:" + (p.lockScroll ? "12.5px" : "14px"))}>{p.titleTh}</span>
                  {p.lockScroll && (
                    <span style={css("font-size:10.5px;color:#A6B4C2;letter-spacing:.06em;font-family:'IBM Plex Mono',monospace")}>
                      {p.crumb}
                    </span>
                  )}
                </div>
                {!p.lockScroll && (
                  <p style={css("margin:6px 0 0;font-size:12.5px;color:#64748B;max-width:900px;text-wrap:pretty")}>{p.blurb}</p>
                )}
              </div>
              {/*
                A screen that draws its own controls draws these too.

                They sat up here, and full screen hides everything above the
                grid — so the one place you would want to import from was the
                one place they were not.
              */}
              {!p.lockScroll && (
                <div className="page-actions" style={css("display:flex;gap:8px;align-items:center;padding-top:10px")}>
                  {p.actions.map((a) => (
                    <button key={a.label} onClick={a.go} style={css(a.style)}>{a.label}</button>
                  ))}
                </div>
              )}
            </div>

            {/* The strip scrolls, not the page. Reaching CALENDAR by dragging the
                whole of `main` sideways takes the title and the breadcrumb with
                it, and you arrive at the tab having lost the heading that says
                what you are looking at. */}
            {!p.hideTabs && (
            <div style={css("display:flex;gap:2px;overflow-x:auto;scrollbar-width:thin;" +
              (p.lockScroll ? "margin-top:8px;" : "margin-top:14px;") +
              "-webkit-overflow-scrolling:touch;padding-bottom:2px")}>
              {p.tabs.map((t) => (
                <button
                  key={t.label}
                  onClick={t.go}
                  style={css(
                    "height:35px;padding:0 16px;border:1px solid " + (t.active ? "#D8E0E8" : "transparent") +
                    ";border-bottom:" + (t.active ? "1px solid #fff" : "1px solid #D8E0E8") +
                    ";background:" + (t.active ? "#fff" : "transparent") +
                    ";color:" + (t.active ? "#0A2240" : "#64748B") +
                    ";font-size:12.5px;font-weight:" + (t.active ? "600" : "400") +
                    // A flex child shrinks by default, so without these the
                    // eight tabs squeeze into unreadable slivers instead of
                    // staying their own width and scrolling.
                    ";border-radius:4px 4px 0 0;cursor:pointer;margin-bottom:-1px;flex:none;white-space:nowrap",
                  )}
                >
                  {t.label}
                </button>
              ))}
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
