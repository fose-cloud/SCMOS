"use client";

import type { ReactNode } from "react";
import { badge, css } from "./theme";
import { NAV, type Screen } from "./nav";
import type { SearchGroup, SearchHit } from "./search";

export type HeaderAction = { label: string; style: string; go: () => void };
export type TabItem = { label: string; active: boolean; go: () => void };
export type FilterDef = {
  label: string;
  value: string;
  options: string[];
  onChange: (value: string) => void;
};

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
  filters: { defs: FilterDef[]; q: string; onQ: (value: string) => void; onReset: () => void } | null;
  children: ReactNode;
};

export function Chrome(p: Props) {
  return (
    <div style={css("display:flex;flex-direction:column;height:100vh;min-height:100vh;overflow:hidden;color:#16232F")}>
      <header style={css("flex:none;height:60px;background:#0A2240;display:flex;align-items:center;gap:0;padding:0 20px 0 0;border-bottom:1px solid #071A31;z-index:40")}>
        {/*
          The brand band is set in type rather than from `public/brand-leschaco.png`:
          that file is a fully transparent PNG (max alpha 0), which is why the band
          looked empty. Drop the real artwork in and swap this block back to an
          <img> when it is available.
        */}
        <div style={css("width:248px;flex:none;height:60px;display:flex;align-items:center;gap:12px;padding-left:20px;border-right:1px solid #1B3A5C")}>
          <div style={css("display:flex;flex-direction:column;line-height:1")}>
            <span style={css("font-size:17px;font-weight:700;color:#fff;letter-spacing:.14em")}>LESCHACO</span>
            <span style={css("font-size:8.5px;color:#7FA5CC;letter-spacing:.13em;margin-top:3px")}>THAILAND</span>
          </div>
          <div style={css("width:1px;height:24px;background:#2C4E75")} />
          <div style={css("display:flex;flex-direction:column;line-height:1.05")}>
            <span style={css("font-size:15px;font-weight:700;color:#fff;letter-spacing:.06em")}>SCMOS</span>
            <span style={css("font-size:9px;color:#7FA5CC;letter-spacing:.04em")}>SUBCONTRACT MGMT</span>
          </div>
        </div>

        <button
          className="hdr-btn"
          onClick={p.onToggleSidebar}
          aria-label={p.collapsed ? "Expand navigation" : "Collapse navigation"}
          style={css("margin-left:14px;width:32px;height:32px;flex:none;border:1px solid #24476E;background:#0E2B4F;border-radius:4px;color:#B9CFE5;cursor:pointer;font-size:13px")}
        >
          ☰
        </button>

        <div style={css("margin-left:16px;flex:1;max-width:520px;position:relative;display:flex;align-items:center")}>
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
            placeholder="ค้นหาทุกเมนู — เลขตู้, Job/ABS, ทะเบียน, คนขับ, ลูกค้า, ผู้ขนส่ง, ชื่อหน้าจอ…"
            style={css("width:100%;height:36px;border-radius:4px;border:1px solid #24476E;background:#0E2B4F;color:#fff;font-size:12.5px;padding:0 12px 0 32px;outline:none")}
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

        <div style={css("flex:1")} />

        <div style={css("display:flex;align-items:center;gap:8px")}>
          <button
            className="hdr-btn"
            onClick={p.onToggleNotif}
            style={css("position:relative;height:34px;padding:0 12px;border:1px solid #24476E;background:#0E2B4F;border-radius:4px;color:#D6E5F2;cursor:pointer;font-size:12.5px;display:flex;align-items:center;gap:7px")}
          >
            ◔ Alerts
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
            <span style={css("display:flex;flex-direction:column;line-height:1.25")}>
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
        <nav style={css("flex:none;width:" + (p.collapsed ? "64px" : "248px") + ";background:#071A31;display:flex;flex-direction:column;border-right:1px solid #143254;transition:width .16s ease;overflow:hidden")}>
          <div style={css("flex:1;overflow-y:auto;padding:10px 0")}>
            {NAV.map(([key, label, th, rects]) => {
              const active = p.screen === key;
              const count = p.navCounts[key];
              return (
                <button
                  key={key}
                  type="button"
                  aria-label={label}
                  aria-current={active ? "page" : undefined}
                  className={active ? undefined : "nav-item"}
                  onClick={() => p.onNavigate(key)}
                  style={css(
                    "width:100%;text-align:left;font-family:inherit;border:0;" +
                    "display:flex;align-items:center;gap:12px;padding:" +
                    (p.collapsed ? "10px 0;justify-content:center;" : "8px 18px;") +
                    "cursor:pointer;border-left:3px solid " + (active ? "#4E9BE8" : "transparent") +
                    ";background:" + (active ? "#123A66" : "transparent") +
                    ";color:" + (active ? "#fff" : "#C4D6E6") + ";transition:background .12s",
                  )}
                >
                  <span style={css("flex:none;display:flex;color:" + (active ? "#4E9BE8" : "#7FA5CC"))}>
                    <NavIcon rects={rects} color={active ? "#4E9BE8" : "#7FA5CC"} />
                  </span>
                  {!p.collapsed && (
                    <span style={css("display:flex;flex-direction:column;line-height:1.2;min-width:0")}>
                      <span style={css("font-size:13px;font-weight:500;white-space:nowrap")}>{label}</span>
                      <span style={css("font-size:10px;color:#5D82A8;white-space:nowrap")}>{th}</span>
                    </span>
                  )}
                  {!p.collapsed && !!count && (
                    <span style={css("margin-left:auto;background:" + (key === "incident" ? "#D64545" : "#1B4A7A") + ";color:#fff;border-radius:9px;padding:1px 7px;font-size:10.5px;font-weight:600;font-family:'IBM Plex Mono',monospace")}>
                      {count}
                    </span>
                  )}
                </button>
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

        <main style={css("flex:1;min-width:0;overflow-y:auto;background:#EEF2F6")}>
          <div style={css("background:#fff;border-bottom:1px solid #D8E0E8;padding:16px 24px 0;position:sticky;top:0;z-index:30")}>
            <div style={css("display:flex;align-items:flex-start;gap:20px")}>
              <div style={css("flex:1;min-width:0")}>
                <div style={css("font-size:11px;color:#8496A8;letter-spacing:.06em;margin-bottom:5px;font-family:'IBM Plex Mono',monospace")}>
                  {p.crumb}
                </div>
                <div style={css("display:flex;align-items:baseline;gap:12px;flex-wrap:wrap")}>
                  <h1 style={css("margin:0;font-size:22px;font-weight:600;color:#0A2240;letter-spacing:-.01em")}>{p.title}</h1>
                  <span style={css("font-size:14px;color:#64748B;font-weight:400")}>{p.titleTh}</span>
                </div>
                <p style={css("margin:6px 0 0;font-size:12.5px;color:#64748B;max-width:900px;text-wrap:pretty")}>{p.blurb}</p>
              </div>
              <div style={css("display:flex;gap:8px;align-items:center;padding-top:10px")}>
                {p.actions.map((a) => (
                  <button key={a.label} onClick={a.go} style={css(a.style)}>{a.label}</button>
                ))}
              </div>
            </div>

            <div style={css("display:flex;gap:2px;margin-top:14px")}>
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
                    ";border-radius:4px 4px 0 0;cursor:pointer;margin-bottom:-1px",
                  )}
                >
                  {t.label}
                </button>
              ))}
            </div>
          </div>

          <div style={css("padding:20px 24px 40px;display:flex;flex-direction:column;gap:16px")}>
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
