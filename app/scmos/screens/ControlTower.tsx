"use client";

import { useEffect, useMemo, useState, type ReactNode } from "react";
import { css } from "../theme";
import type { DashboardFilters } from "../dashboardFilters";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import type { Job, OpsStats } from "../ops";
import { ALL_PERIOD, latestDay, monthKey, monthNameEn, periodLabel, type Period } from "../period";
import { returnKind, tripCost } from "../returnLoad";
import { classifyReason } from "../delayCauses";
import type { WsTarget } from "../alerts";

/**
 * The Executive dashboard as a control tower.
 *
 * Laid out from the department's own design: a hero band with the five things
 * people come here to do, six measured figures, then the operation read three
 * ways — by status, by day, by on-time — the top customers, hauliers and delay
 * reasons, and cost. An AI rail on the right carries the morning briefing and a
 * way to ask.
 *
 * What each figure is made from is the point, not the styling. The six cards
 * and the on-time gauge are the API's own measures — the same engine behind the
 * KPI screen and the monthly report, so one number is quoted everywhere. The
 * status, volume, customer and reason panels are counted here over the jobs
 * the period bar has chosen, because those are the ones somebody drills into.
 * Nothing on this screen is invented: a panel with no data says so.
 */

/* ------------------------------------------------------------- palette */

const BG = "#0c2338";
const LINE = "rgba(74,148,214,.2)";
const INK = "#eaf4fc";
const MUTED = "#8fb4d4";
const DIM = "#7c9fbd";
const BLUE = "#5cc0f7";
const GREEN = "#3ddc97";
const RED = "#ff7d86";
const AMBER = "#f2b13c";
const VIOLET = "#a99bff";
const MONO = "'IBM Plex Mono',monospace";

const PANEL = `background:${BG};border:1px solid ${LINE};border-radius:8px;padding:14px 16px 16px;display:flex;flex-direction:column;gap:12px;min-width:0`;
const H3 = `margin:0;font-size:14px;font-weight:600;color:${INK}`;
const CHIP_ON = "padding:6px 13px;border-radius:6px;background:#1668ab;border:1px solid #1668ab;color:#fff;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit";
const CHIP_OFF = `padding:6px 13px;border-radius:6px;background:#102b44;border:1px solid rgba(74,148,214,.24);color:#b9d4ea;font-size:11.5px;cursor:pointer;font-family:inherit`;

const nf = (n: number) => n.toLocaleString("en-US");

/* --------------------------------------------------------- the report */

/** One measure as `/api/kpi/measures` sends it. Types only — the rules stay on the server. */
type Trend = { period: string; value: number | null; base: number };
type Measure = {
  id: string; kind: string; value: number | null; base: number; unit: string; note: string;
  target: number | null; meetsTarget: boolean | null; trend: Trend[] | null;
};
type Supplier = { carrier: string; jobs: number; onTime: number | null; onTimeBase: number; score: number | null };
type Report = { jobs: number; measures: Measure[]; suppliers: Supplier[] };

/**
 * Below this many measured trips a rate says more about the sample than the
 * carrier. The same five the KPI screen and the monthly report use.
 */
const MINIMUM_SAMPLE = 5;

/**
 * The report for the period the bar has chosen, with its months of trend —
 * and, since 22 Sep 2026, for the customers and hauliers the pickers have
 * chosen: the measured cards narrowed the same way as every count beside
 * them, by the engine, not by the browser.
 */
function useReport(period: Period, filters: DashboardFilters): { report: Report | null; failed: boolean } {
  const query = new URLSearchParams({ trend: "true" });
  if (period.year && period.year !== "ALL") query.set("year", period.year);
  if (period.month && period.month !== "ALL") query.set("month", period.month);
  if (period.day && period.day !== "ALL") query.set("day", period.day.slice(0, 2));
  if (filters.customer && filters.customer !== "ALL") query.set("customer", filters.customer);
  if (filters.trucker && filters.trucker !== "ALL") query.set("trucker", filters.trucker);
  const search = query.toString();

  const [report, setReport] = useRemembered<Report>("controlTower:" + search);
  // Which request failed, so a failure for one period does not outlive the
  // move to another, and nothing has to be reset synchronously in the effect.
  const [failedFor, setFailedFor] = useState<string | null>(null);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch(`/api/kpi/measures?${search}`, { headers: { accept: "application/json" } });
        if (!alive) return;
        if (!response.ok) { setFailedFor(search); return; }
        const body = await response.json() as Report;
        if (alive) setReport(body);
      } catch { if (alive) setFailedFor(search); }
    })();
    return () => { alive = false; };
  }, [search, setReport]);

  return { report, failed: failedFor === search && !report };
}

/** The morning briefing, as the API writes it. Shared with the TODAY tab's band. */
type Finding = { urgency: "Now" | "Soon" | "Watch" | "Records"; kind: string; headline: string; detail: string; count: number; screen: string };
type Brief = { today: string; quiet: string; findings: Finding[] };

function useBrief(): Brief | null {
  const [brief, setBrief] = useRemembered<Brief>("briefing");
  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const reply = await apiFetch("/api/dashboard/briefing", { headers: { accept: "application/json" } });
        if (!alive || !reply.ok) return;
        const body = await reply.json() as Brief;
        if (alive) setBrief(body);
      } catch { /* keep the last briefing; an unreachable API is not a quiet morning */ }
    })();
    return () => { alive = false; };
  }, [setBrief]);
  return brief ?? null;
}

/* -------------------------------------------------------------- pieces */

function Spark({ points, colour, width = 58, height = 24 }: { points: (number | null)[]; colour: string; width?: number; height?: number }) {
  const known = points.filter((p): p is number => p !== null);
  // One point is a dot, not a direction.
  if (known.length < 2) return <span style={css(`width:${width}px;height:${height}px;display:inline-block`)} />;
  const min = Math.min(...known);
  const max = Math.max(...known);
  const span = max - min || 1;
  const step = (width - 2) / (points.length - 1);
  const coords = points
    .map((p, i) => (p === null ? null : `${(1 + i * step).toFixed(1)},${(height - 3 - ((p - min) / span) * (height - 6)).toFixed(1)}`))
    .filter((c): c is string => c !== null);
  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} fill="none" aria-hidden="true">
      <polyline points={coords.join(" ")} stroke={colour} strokeWidth="1.6" strokeLinejoin="round" strokeLinecap="round" />
    </svg>
  );
}

function Icon({ path, colour }: { path: ReactNode; colour: string }) {
  return (
    <span style={css(`width:30px;height:30px;flex:none;display:grid;place-items:center;border-radius:7px;background:${colour}29;color:${colour}`)}>
      <svg width="16" height="16" viewBox="0 0 16 16" stroke="currentColor" strokeWidth="1.3" fill="none" aria-hidden="true">{path}</svg>
    </span>
  );
}

const ICON = {
  truck: <><path d="M1.6 10.4V4.6h7.6v5.8" /><path d="M9.2 6.4h2.6l2.6 2.6v1.4" /><circle cx="4.6" cy="11.6" r="1.4" /><circle cx="11.4" cy="11.6" r="1.4" /></>,
  target: <><circle cx="8" cy="8" r="6.2" /><circle cx="8" cy="8" r="2.6" /></>,
  clock: <><circle cx="8" cy="8" r="6.2" /><path d="M8 4.4V8l2.6 1.6" /></>,
  warn: <><path d="M8 2.4 14.4 13.2H1.6L8 2.4Z" /><path d="M8 6.6v3M8 11.4h.01" /></>,
  file: <><path d="M3.4 1.9h6l3.2 3.2v9H3.4v-12.2Z" /><path d="M9.4 1.9v3.2h3.2M5.8 8.6h4.4M5.8 11h3" /></>,
  people: <><circle cx="5.6" cy="5.6" r="2.2" /><circle cx="11" cy="6.4" r="1.8" /><path d="M1.8 12.8c.5-2.1 1.9-3.2 3.8-3.2s3.3 1.1 3.8 3.2M10.2 9.8c1.7.1 2.9 1.2 3.3 3" /></>,
  plus: <><circle cx="8" cy="8" r="6.4" /><path d="M8 5v6M5 8h6" /></>,
  down: <><rect x="2.6" y="1.8" width="10.8" height="12.4" rx="1.6" /><path d="M8 5v5M5.8 7.8 8 10.2l2.2-2.4" /></>,
  up: <><circle cx="8" cy="8" r="6.4" /><path d="M8 10.6V4.8M5.8 7 8 4.6 10.2 7" /></>,
  report: <><rect x="2.6" y="2.2" width="10.8" height="11.6" rx="1.6" /><path d="M5.4 6h5.2M5.4 8.6h5.2M5.4 11.2h3" /></>,
  spark: <><path d="M8 1.8l1.35 3.05L12.4 6.2 9.35 7.55 8 10.6 6.65 7.55 3.6 6.2l3.05-1.35L8 1.8Z" fill="currentColor" stroke="none" /><path d="M12.3 10.2l.55 1.25 1.25.55-1.25.55-.55 1.25-.55-1.25-1.25-.55 1.25-.55.55-1.25Z" fill="currentColor" stroke="none" /></>,
};

function Panel({ title, right, children, tight, tall }: { title: string; right?: ReactNode; children: ReactNode; tight?: boolean; tall?: boolean }) {
  return (
    <section style={css(PANEL + (tight ? ";padding-bottom:10px;gap:10px" : "") + (tall ? ";min-height:250px" : ""))}>
      <div style={css("display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap")}>
        <h3 style={css(H3)}>{title}</h3>
        {right}
      </div>
      {children}
    </section>
  );
}

function Empty({ children }: { children: ReactNode }) {
  return <span style={css(`font-size:11.5px;color:${DIM};padding:6px 0`)}>{children}</span>;
}

function LinkButton({ children, onClick }: { children: ReactNode; onClick?: () => void }) {
  return (
    <button type="button" onClick={onClick} disabled={!onClick}
      style={css(`border:none;background:none;padding:0;font-family:inherit;font-size:11.5px;color:${BLUE};cursor:${onClick ? "pointer" : "default"}`)}>
      {children}
    </button>
  );
}

/* --------------------------------------------------------------- cards */

type Card = {
  label: string; icon: ReactNode; colour: string;
  value: string; valueColour?: string;
  /** "↑ +12%" and its colour, or nothing when there is no last month to compare against. */
  delta?: { text: string; colour: string };
  spark: (number | null)[];
  /** What the figure rests on — "วัดจาก 631" — or why there is none. */
  foot: string;
  go?: () => void;
};

/**
 * How a measure moved against the month before it.
 *
 * Only over the trend the engine sends, which ends at the month being looked
 * at. With the bar on "ทั้งแผน" the headline is the whole register and the
 * trend is its last six months — the two are not comparable, so no arrow is
 * drawn and the card says what the sparkline is instead.
 */
function movement(trend: Trend[] | null | undefined, rate: boolean, monthly: boolean, goodWhen: "up" | "down" | "none"):
  { text: string; colour: string } | undefined {
  if (!monthly || !trend || trend.length < 2) return undefined;
  const last = trend[trend.length - 1].value;
  const prev = trend[trend.length - 2].value;
  if (last === null || prev === null) return undefined;
  const diff = last - prev;
  const arrow = diff > 0 ? "↑" : diff < 0 ? "↓" : "→";
  const text = rate
    ? `${arrow} ${diff >= 0 ? "+" : ""}${diff.toFixed(1)} pt`
    : `${arrow} ${diff >= 0 ? "+" : ""}${prev ? Math.round((diff / prev) * 100) + "%" : nf(diff)}`;
  const colour = goodWhen === "none" || diff === 0 ? BLUE
    : (diff > 0) === (goodWhen === "up") ? GREEN : RED;
  return { text, colour };
}

function KpiCard({ card }: { card: Card }) {
  return (
    <button type="button" onClick={card.go} disabled={!card.go} title={card.foot}
      style={css(`font-family:inherit;text-align:left;background:${BG};border:1px solid ${LINE};border-radius:8px;padding:12px 13px;display:flex;flex-direction:column;gap:8px;min-width:0;cursor:${card.go ? "pointer" : "default"}`)}>
      <div style={css("display:flex;align-items:center;gap:9px;min-width:0")}>
        <Icon path={card.icon} colour={card.colour} />
        <span style={css(`font-family:${MONO};font-size:9.5px;font-weight:600;letter-spacing:.1em;color:${MUTED};white-space:nowrap;overflow:hidden;text-overflow:ellipsis`)}>{card.label}</span>
      </div>
      <span style={css(`font-size:27px;font-weight:700;line-height:1;color:${card.valueColour ?? "#fff"};font-variant-numeric:tabular-nums`)}>{card.value}</span>
      <div style={css("display:flex;align-items:flex-end;justify-content:space-between;gap:6px")}>
        <div style={css("display:flex;flex-direction:column;gap:1px;min-width:0")}>
          {card.delta
            ? <>
                <span style={css(`font-size:11.5px;font-weight:600;color:${card.delta.colour}`)}>{card.delta.text}</span>
                <span style={css(`font-size:10px;color:${DIM}`)}>vs. last month</span>
              </>
            : <span style={css(`font-size:10px;color:${DIM};white-space:nowrap;overflow:hidden;text-overflow:ellipsis`)}>
                {card.foot}
              </span>}
        </div>
        <Spark points={card.spark} colour={card.delta?.colour ?? card.colour} />
      </div>
    </button>
  );
}

/* ------------------------------------------------------------- charts */

function Donut({ items, total, unit }: { items: [string, number, string][]; total: number; unit: string }) {
  const r = 58;
  const C = 2 * Math.PI * r;
  // Each arc starts where the ones before it end.
  const arcs = items.filter((i) => i[1] > 0).map((i, index, all) => {
    const before = all.slice(0, index).reduce((sum, one) => sum + one[1], 0);
    return {
      key: i[0], colour: i[2],
      len: (i[1] / (total || 1)) * C,
      start: (before / (total || 1)) * 360 - 90,
    };
  });
  return (
    <div style={css("position:relative;width:150px;height:150px;flex:none")}
      title={total ? items.map((i) => `${i[0]} ${nf(i[1])} (${Math.round((i[1] / total) * 100)}%)`).join("\n") : "ไม่มีข้อมูล"}>
      <svg width="150" height="150" viewBox="0 0 150 150" aria-hidden="true">
        <circle cx="75" cy="75" r={r} fill="none" stroke="#0f2c48" strokeWidth="17" />
        {arcs.map((a) => (
          <circle key={a.key} cx="75" cy="75" r={r} fill="none" stroke={a.colour} strokeWidth="17"
            strokeDasharray={`${a.len.toFixed(2)} ${(C - a.len).toFixed(2)}`} transform={`rotate(${a.start.toFixed(2)} 75 75)`} />
        ))}
      </svg>
      <div style={css("position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:1px")}>
        <span style={css("font-size:24px;font-weight:700;color:#fff;font-variant-numeric:tabular-nums")}>{nf(total)}</span>
        <span style={css(`font-family:${MONO};font-size:9px;letter-spacing:.14em;color:${MUTED}`)}>{unit}</span>
      </div>
    </div>
  );
}

/** A 270° arc, filled to the rate. */
function Gauge({ pct, label }: { pct: number | null; label: string }) {
  const r = 64;
  const C = 2 * Math.PI * r;
  const track = C * 0.915;
  const fill = pct === null ? 0 : track * Math.min(1, Math.max(0, pct / 100));
  return (
    <div style={css("position:relative;width:158px;height:158px;flex:none")}>
      <svg width="158" height="158" viewBox="0 0 158 158" aria-hidden="true">
        <circle cx="79" cy="79" r={r} fill="none" stroke="#0f2c48" strokeWidth="16" strokeLinecap="round"
          strokeDasharray={`${track.toFixed(1)} ${(C - track).toFixed(1)}`} transform="rotate(105 79 79)" />
        {fill > 0 && (
          <circle cx="79" cy="79" r={r} fill="none" stroke="#2b9fe8" strokeWidth="16" strokeLinecap="round"
            strokeDasharray={`${fill.toFixed(1)} ${(C - fill).toFixed(1)}`} transform="rotate(105 79 79)" />
        )}
      </svg>
      <div style={css("position:absolute;inset:0;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:2px")}>
        <span style={css("font-size:28px;font-weight:700;color:#fff;font-variant-numeric:tabular-nums")}>{pct === null ? "—" : pct + "%"}</span>
        <span style={css(`font-family:${MONO};font-size:10px;letter-spacing:.12em;color:${MUTED}`)}>{label}</span>
      </div>
    </div>
  );
}

/** A y-axis top that lands on a round number. */
function ceilNice(n: number): number {
  if (n <= 0) return 1;
  const mag = Math.pow(10, Math.floor(Math.log10(n)));
  const unit = n / mag;
  const nice = unit <= 1 ? 1 : unit <= 2 ? 2 : unit <= 4 ? 4 : unit <= 5 ? 5 : 10;
  return nice * mag;
}

/* ------------------------------------------------------------- the screen */

type Props = {
  s: OpsStats;
  /** Everything in the register — the cost history runs over months the period bar may not show. */
  allJobs: Job[];
  period: Period;
  onPeriod: (period: Period) => void;
  /** The CUSTOMER / TRUCKER pickers, which narrow the measured cards as well as the counts. */
  filters: DashboardFilters;
  userName: string;
  onDrill: (target: WsTarget) => void;
  onOpen: (screen: string) => void;
  onNewJob: () => void;
  onImport: () => void;
  onExport: () => void;
  onAsk: (question: string) => void;
  onOpenKpi?: () => void;
};

export function ControlTower(p: Props) {
  const { s, period, onDrill } = p;
  const total = s.jobs.length;
  const monthly = period.month !== "ALL" && period.day === "ALL";

  const { report, failed } = useReport(period, p.filters);
  const measure = (id: string) => report?.measures.find((m) => m.id === id);

  /* ---- clock, on the client only: a server-rendered time is the server's ---- */
  const [clock, setClock] = useState<{ date: string; time: string } | null>(null);
  useEffect(() => {
    const tick = () => {
      const now = new Date();
      const weekday = now.toLocaleDateString("en-GB", { weekday: "short" });
      const month = monthNameEn(String(now.getMonth() + 1).padStart(2, "0"), true);
      setClock({
        date: `${weekday}, ${now.getDate()} ${month} ${now.getFullYear()}`,
        time: `${String(now.getHours()).padStart(2, "0")}:${String(now.getMinutes()).padStart(2, "0")}`,
      });
    };
    tick();
    const timer = setInterval(tick, 30000);
    return () => clearInterval(timer);
  }, []);

  /* ---- monthly trip counts, for the one card the engine has no trend for ---- */
  const tripsByMonth = useMemo(() => {
    const counts: Record<string, number> = {};
    p.allJobs.forEach((j) => { const k = monthKey(j.date); if (k) counts[k] = (counts[k] || 0) + 1; });
    return counts;
  }, [p.allJobs]);
  const otd = measure("OnTimeDelivery");
  const delay = measure("Delay");
  const months = (otd?.trend ?? []).map((t) => t.period);
  const tripTrend = months.length >= 2 ? months.map((m) => tripsByMonth[m] ?? 0) : [];

  const rateCard = (id: string, label: string, icon: ReactNode, colour: string): Card => {
    const m = measure(id);
    const value = m?.value ?? null;
    const tone = value === null ? "#fff" : m?.meetsTarget === false ? RED : m?.meetsTarget === true ? GREEN : "#fff";
    return {
      label, icon, colour,
      value: value === null ? "—" : `${value}%`,
      valueColour: tone,
      delta: movement(m?.trend, true, monthly, "up"),
      spark: (m?.trend ?? []).map((t) => t.value),
      foot: m ? (value === null ? (m.note || "ยังวัดไม่ได้") : `วัดจาก ${nf(m.base)}${m.target !== null ? ` · เป้า ${m.target}%` : ""}`) : "กำลังอ่าน…",
    };
  };
  const countCard = (id: string, label: string, icon: ReactNode, colour: string, go?: () => void): Card => {
    const m = measure(id);
    const value = m?.value ?? null;
    return {
      label, icon, colour,
      value: value === null ? "—" : nf(value),
      delta: movement(m?.trend, false, monthly, "down"),
      spark: (m?.trend ?? []).map((t) => t.value),
      foot: m ? (m.note || m.unit) : "กำลังอ่าน…",
      go,
    };
  };

  const cards: Card[] = [
    {
      label: "TOTAL TRIPS", icon: ICON.truck, colour: BLUE,
      value: report ? nf(report.jobs) : "—",
      delta: monthly && tripTrend.length >= 2 && tripTrend[tripTrend.length - 2] > 0
        ? (() => {
            const diff = tripTrend[tripTrend.length - 1] - tripTrend[tripTrend.length - 2];
            return { text: `${diff > 0 ? "↑ +" : diff < 0 ? "↓ " : "→ "}${Math.round((diff / tripTrend[tripTrend.length - 2]) * 100)}%`, colour: BLUE };
          })()
        : undefined,
      spark: tripTrend,
      foot: report ? `${periodLabel(period)} · ${nf(report.jobs)} เที่ยว` : "กำลังอ่าน…",
      go: () => onDrill({ tab: "PENDING" }),
    },
    rateCard("OnTimeDelivery", "ON-TIME DELIVERY", ICON.target, RED),
    countCard("Delay", "DELAY", ICON.clock, BLUE, () => onDrill({ tab: "DELAY", kpi: "Delay" })),
    countCard("Accident", "ACCIDENT", ICON.warn, AMBER, () => p.onOpen("incident")),
    countCard("CarPar", "CAR / PAR", ICON.file, VIOLET, () => p.onOpen("incident")),
    rateCard("SupplierPerformance", "SUPPLIER PERF.", ICON.people, GREEN),
  ];

  /* ---- status mix over the chosen jobs ---- */
  const mix: [string, number, string][] = [
    ["Pending", s.waiting.length, AMBER],
    ["Confirmed", s.confirmed.length, BLUE],
    ["In Operation", s.running.length, "#0A9AA8"],
    ["Delayed", s.delayed.length, RED],
    ["Completed", s.done.length, GREEN],
  ];
  const mixTarget: Record<string, WsTarget> = {
    Pending: { tab: "PENDING", kpi: "Wait" }, Confirmed: { tab: "PENDING", kpi: "Conf" }, "In Operation": { tab: "PENDING", kpi: "Run" },
    Delayed: { tab: "DELAY", kpi: "Delay" }, Completed: { tab: "COMPLETED", kpi: "Done" },
  };

  /* ---- volume by day, the last seven operation days, split by direction ---- */
  const [focus, setFocus] = useState<"ALL" | "IMPORT" | "EXPORT" | "DELIVERY">("ALL");
  const days = s.dates.slice(-7).map((d) => {
    const set = s.jobs.filter((j) => j.date === d);
    const imp = set.filter((j) => j.cat === "IMPORT").length;
    const exp = set.filter((j) => j.cat === "EXPORT").length;
    return { d, imp, exp, del: set.length - imp - exp, total: set.length };
  });
  const dayTop = ceilNice(Math.max(1, ...days.map((d) => Math.max(d.imp, d.exp, d.del))));

  /* ---- who, and how much ---- */
  const countBy = (pick: (j: Job) => string | undefined) => {
    const out: Record<string, number> = {};
    s.jobs.forEach((j) => { const k = (pick(j) || "").trim(); if (k) out[k] = (out[k] || 0) + 1; });
    return Object.entries(out).sort((a, b) => b[1] - a[1]);
  };
  const daily = (pick: (j: Job) => string | undefined, key: string) =>
    s.dates.slice(-7).map((d) => s.jobs.filter((j) => j.date === d && (pick(j) || "").trim() === key).length);
  const customers = countBy((j) => j.customer).slice(0, 5);
  const truckers = countBy((j) => j.trucker).slice(0, 5);
  /*
   * The reason column is typed by whoever was on the phone, and it holds
   * status notes ("Delivery Completed"), pickup appointments and bare clock
   * readings ("13.00") beside the causes. Read the same way Delay Analysis
   * reads it — classifyReason is the one rule — so the panel counts causes,
   * grouped, and says how many entries it left out rather than hiding them.
   */
  const reasonRead = (() => {
    const causes: Record<string, number> = {};
    let skipped = 0;
    s.jobs.forEach((j) => {
      const text = (j.reason || "").trim();
      if (!text) return;
      const read = classifyReason(text);
      if (read.kind === "cause") causes[read.label] = (causes[read.label] || 0) + 1;
      else skipped += 1;
    });
    const rows = Object.entries(causes).sort((a, b) => b[1] - a[1]);
    return { rows: rows.slice(0, 5), total: rows.reduce((sum, r) => sum + r[1], 0), skipped };
  })();
  const reasons = reasonRead.rows;
  const reasonTotal = reasonRead.total;
  const supplierOtd = (name: string): { pct: number | null; base: number } | null => {
    const row = report?.suppliers.find((one) => one.carrier.toLowerCase() === name.toLowerCase());
    if (!row) return null;
    return { pct: row.onTimeBase >= MINIMUM_SAMPLE ? row.onTime : null, base: row.onTimeBase };
  };
  const otdColour = (pct: number | null) => pct === null ? DIM : otd?.target !== null && otd?.target !== undefined && pct < otd.target ? RED : pct >= 90 ? GREEN : AMBER;

  /* ---- cost, from the trips that carry a rate ---- */
  const costOf = (j: Job) => tripCost(j.cost, returnKind(j.returnLoad, j.returnFinished));
  const costed = s.jobs.map(costOf).filter((c): c is number => c !== null);
  const costTotal = costed.reduce((sum, c) => sum + c, 0);
  const costByMonth = useMemo(() => {
    const out: Record<string, { cost: number; trips: number }> = {};
    p.allJobs.forEach((j) => {
      const c = tripCost(j.cost, returnKind(j.returnLoad, j.returnFinished));
      const k = monthKey(j.date);
      if (c === null || !k) return;
      out[k] = { cost: (out[k]?.cost ?? 0) + c, trips: (out[k]?.trips ?? 0) + 1 };
    });
    return Object.keys(out).sort().slice(-9).map((k) => ({ k, ...out[k] }));
  }, [p.allJobs]);
  const costTop = ceilNice(Math.max(1, ...costByMonth.map((m) => m.cost)));
  const perTripTop = ceilNice(Math.max(1, ...costByMonth.map((m) => m.cost / m.trips)));
  const compact = (n: number) => n >= 1e6 ? (n / 1e6).toFixed(1) + "M" : n >= 1e3 ? Math.round(n / 1e3) + "K" : nf(Math.round(n));

  /* ---- where the work is going, from what the jobs name ---- */
  const HUBS: { name: string; x: number; y: number; test: RegExp }[] = [
    { name: "Bangkok", x: 150, y: 84, test: /bangkok|bkk|กรุงเทพ|bangna|บางนา|lat ?krabang|ลาดกระบัง|samut|สมุทร/i },
    { name: "Chonburi", x: 232, y: 108, test: /chonburi|ชลบุรี|amata|อมตะ|sriracha|ศรีราชา|pinthong|ปิ่นทอง/i },
    { name: "Laem Chabang", x: 210, y: 150, test: /laem ?chabang|lcb|lch|แหลมฉบัง/i },
    { name: "Rayong", x: 318, y: 140, test: /rayong|ระยอง|map ?ta ?phut|มาบตาพุด|pluak ?daeng|ปลวกแดง|eastern ?seaboard/i },
  ];
  const placeOf = (j: Job) => [j.destination, j.plant, j.cyYard, j.returnLoc, j.province].filter(Boolean).join(" ");
  const hubCount = HUBS.map((h) => s.jobs.filter((j) => h.test.test(placeOf(j))).length);
  const hubMax = Math.max(1, ...hubCount);
  // Named for what each one counts. The design says "At Risk" where the
  // fourth dot is; the figure behind it is the jobs needing a person, so that
  // is what it says.
  const legend: [string, number, string][] = [
    ["In Transit", s.running.length, "#4aa8e8"], ["Delivered", s.done.length, GREEN],
    ["Delayed", s.delayed.length, "#ef4b57"], ["Action Required", s.action.length, AMBER],
  ];

  /* ---- the rail ---- */
  const brief = useBrief();
  const [rail, setRail] = useState(true);
  const [question, setQuestion] = useState("");
  const hour = clock ? Number(clock.time.slice(0, 2)) : 9;
  const greeting = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
  const attention = (brief?.findings ?? []).filter((f) => f.urgency !== "Records");
  const attentionCount = attention.reduce((sum, f) => sum + f.count, 0);
  const URGENCY: Record<Finding["urgency"], string> = { Now: "#ff5f6b", Soon: AMBER, Watch: MUTED, Records: DIM };
  const prompts = [
    "งานวันนี้ที่เสี่ยงล่าช้ามีอะไรบ้าง",
    "เปรียบเทียบผลงานผู้ขนส่งเดือนนี้",
    "สรุปสาเหตุความล่าช้าสัปดาห์นี้",
    "งานที่ยังไม่มีรถหรือคนขับ",
    "ร่างอีเมลถึงผู้ขนส่งเรื่องงานล่าช้า",
  ];
  const ask = (text: string) => { const q = text.trim(); if (q) p.onAsk(q); };

  const latest = latestDay(p.allJobs);
  const isLatestDay = latest !== null && period.day === latest.day && period.month === latest.month && period.year === latest.year;
  const isLatestMonth = latest !== null && period.day === "ALL" && period.month === latest.month && period.year === latest.year;
  const isAll = period.year === "ALL" && period.month === "ALL" && period.day === "ALL";

  const tile = (label: string, icon: ReactNode, go: () => void, primary = false) => (
    <button key={label} type="button" onClick={go} className="ct-tile"
      style={css("display:flex;flex-direction:column;align-items:center;justify-content:center;gap:7px;border-radius:8px;font-family:inherit;font-size:11.5px;font-weight:600;cursor:pointer;padding:10px 4px;"
        + (primary
          ? "background:linear-gradient(160deg,#123f66,#0d2c49);border:1px solid #3b9ee0;color:#eaf6ff;box-shadow:0 0 0 1px rgba(59,158,224,.18),0 4px 18px rgba(20,110,180,.22)"
          : "background:#0d2438;border:1px solid rgba(74,148,214,.26);color:#d6e8f7"))}>
      <svg width="19" height="19" viewBox="0 0 16 16" stroke="currentColor" strokeWidth="1.2" fill="none" aria-hidden="true">{icon}</svg>
      {label}
    </button>
  );

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>

      {/* ------------------------------------------------ hero + actions */}
      <div className="ct-top">
        {/* public/dashboard-hero.jpg is the department's own band — the night
            port from their design, with the design's darkening already in it
            and its typeset text taken out, so the live text below sits where
            theirs did. Anchored left so a narrow screen keeps the globe and
            the title's ground and loses the far cranes instead. */}
        <section className="ct-hero" style={css("position:relative;min-height:162px;border-radius:9px;overflow:hidden;border:1px solid rgba(74,148,214,.22);"
          + "background:url(/dashboard-hero.jpg) left center/cover no-repeat,#0a1f38")}>
          <div aria-hidden="true" style={css("position:absolute;inset:0;background:linear-gradient(96deg,rgba(5,17,33,.5) 0%,rgba(5,17,33,.22) 40%,rgba(5,17,33,0) 70%)")} />
          <div style={css("position:relative;padding:18px 22px;display:flex;flex-direction:column;gap:6px;height:100%")}>
            <div style={css("display:flex;align-items:flex-start;justify-content:space-between;gap:20px;flex-wrap:wrap")}>
              <div style={css("display:flex;flex-direction:column;gap:2px")}>
                <span style={css("font-size:40px;font-weight:700;letter-spacing:.02em;line-height:1;color:#fff;text-shadow:0 2px 18px rgba(0,0,0,.6)")}>SCMOS</span>
                <span style={css(`font-size:16px;font-weight:600;letter-spacing:.03em;color:${BLUE}`)}>SMART LOGISTICS CONTROL TOWER</span>
              </div>
              <div style={css("display:flex;align-items:center;gap:16px;font-size:12.5px;color:#cfe3f4;flex-wrap:wrap")}>
                <span style={css(`font-family:${MONO};color:#e6f1fa`)}>{clock ? `${clock.date}  ${clock.time}` : "—"}</span>
                <span style={css("color:#a9c8e0")}>Bangkok, TH</span>
                <span style={css(`font-family:${MONO};font-size:11px;color:${MUTED}`)}>{periodLabel(period)} · {nf(total)} งาน</span>
              </div>
            </div>
            <span style={css("margin-top:14px;font-size:11.5px;font-weight:600;letter-spacing:.12em;color:#9dc3e0")}>CONNECTING PEOPLE. MOVING BUSINESS FORWARD.</span>
          </div>
        </section>

        <div style={css("display:flex;flex-direction:column;gap:10px")}>
          <div style={css("display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;flex:1")}>
            {tile("+ New Job", ICON.plus, p.onNewJob)}
            {tile("Import Excel", ICON.down, p.onImport)}
            {tile("Export Excel", ICON.up, p.onExport)}
          </div>
          <div style={css("display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;flex:1")}>
            {tile("AI Assistant", ICON.spark, () => p.onOpen("ai"), true)}
            {tile("Create Report", ICON.report, () => p.onOpen("reports"))}
          </div>
        </div>
      </div>

      <div className="ct-body">
        <div style={css("display:flex;flex-direction:column;gap:12px;min-width:0")}>

          {/* ------------------------------------------------ measured */}
          {failed && <Empty>อ่านตัวชี้วัดจากเซิร์ฟเวอร์ไม่สำเร็จ — การ์ดหกใบด้านล่างจึงยังว่าง ส่วนแผงอื่นนับจากงานที่โหลดไว้ตามปกติ</Empty>}
          <div className="ct-kpis">
            {cards.map((card) => <KpiCard key={card.label} card={card} />)}
          </div>

          {/* ------------------------------------------------ status · volume · on-time */}
          <div className="ct-row3" style={css("grid-template-columns:1.02fr 1.06fr 1fr")}>
            <Panel title="Operation Overview" right={
              <button type="button" onClick={p.onOpenKpi} disabled={!p.onOpenKpi} title="เปิดหน้า KPI"
                style={css(`border:none;background:none;padding:0;color:${MUTED};cursor:pointer;display:grid;place-items:center`)}>
                <svg width="15" height="15" viewBox="0 0 16 16" stroke="currentColor" strokeWidth="1.3" fill="none" aria-hidden="true"><circle cx="8" cy="8" r="2.3" /><path d="M8 1.4v2.2M8 12.4v2.2M1.4 8h2.2M12.4 8h2.2M3.3 3.3l1.6 1.6M11.1 11.1l1.6 1.6M12.7 3.3l-1.6 1.6M4.9 11.1l-1.6 1.6" /></svg>
              </button>
            }>
              <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
                <button type="button" style={css(isLatestDay ? CHIP_ON : CHIP_OFF)} disabled={!latest} onClick={() => latest && p.onPeriod(latest)}>วันล่าสุด</button>
                <button type="button" style={css(isLatestMonth ? CHIP_ON : CHIP_OFF)} disabled={!latest} onClick={() => latest && p.onPeriod({ ...latest, day: "ALL" })}>เดือนล่าสุด</button>
                <button type="button" style={css(isAll ? CHIP_ON : CHIP_OFF)} onClick={() => p.onPeriod(ALL_PERIOD)}>ทั้งแผน</button>
                {!isLatestDay && !isLatestMonth && !isAll && <span style={css(CHIP_ON)}>{periodLabel(period)}</span>}
              </div>
              <div style={css("display:flex;align-items:center;gap:16px;flex-wrap:wrap")}>
                <Donut items={mix} total={total} unit="JOBS" />
                <div style={css("flex:1;min-width:150px;display:flex;flex-direction:column;gap:9px")}>
                  {mix.map(([name, n, colour]) => (
                    <button key={name} type="button" onClick={() => onDrill(mixTarget[name])}
                      title={`${name} · ${nf(n)} งาน` + (total ? ` · ${Math.round((n / total) * 100)}% ของ ${nf(total)}` : "")}
                      style={css("display:grid;grid-template-columns:1fr auto auto;gap:10px;align-items:center;border:none;background:none;padding:0;font-family:inherit;cursor:pointer;text-align:left")}>
                      <span style={css("display:flex;align-items:center;gap:8px;font-size:12.5px;color:#cfe3f4")}>
                        <span style={css(`width:10px;height:10px;border-radius:3px;background:${colour}`)} />{name}
                      </span>
                      <span style={css(`font-family:${MONO};font-size:12.5px;font-weight:600;color:#fff`)}>{nf(n)}</span>
                      <span style={css(`font-family:${MONO};font-size:11.5px;color:${MUTED};width:38px;text-align:right`)}>
                        {total ? (n / total >= 0.1 ? Math.round((n / total) * 100) : ((n / total) * 100).toFixed(1)) + "%" : "—"}
                      </span>
                    </button>
                  ))}
                </div>
              </div>
            </Panel>

            <Panel title="Shipment Volume">
              <div style={css("display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap")}>
                <div style={css("display:flex;gap:6px")}>
                  {(["ALL", "IMPORT", "EXPORT", "DELIVERY"] as const).map((c) => (
                    <button key={c} type="button" style={css(focus === c ? CHIP_ON : CHIP_OFF)} onClick={() => setFocus(c)}>
                      {c === "ALL" ? "All" : c[0] + c.slice(1).toLowerCase()}
                    </button>
                  ))}
                </div>
              </div>
              <div style={css(`display:flex;gap:14px;justify-content:flex-end;font-size:10.5px;color:#a7c6de`)}>
                {([["Import", BLUE], ["Export", GREEN], ["Delivery", AMBER]] as const).map(([name, colour]) => (
                  <span key={name} style={css("display:flex;align-items:center;gap:5px")}>
                    <span style={css(`width:8px;height:8px;border-radius:2px;background:${colour}`)} />{name}
                  </span>
                ))}
              </div>
              {days.length ? (
                <svg viewBox="0 0 330 170" style={css("width:100%;height:auto;overflow:visible")} aria-label="ปริมาณงานรายวัน">
                  <g fontFamily="IBM Plex Mono" fontSize="9" fill={DIM}>
                    {[1, 0.75, 0.5, 0.25, 0].map((f) => (
                      <text key={f} x="0" y={(14 + (1 - f) * 133 + 4).toFixed(0)}>{Math.round(dayTop * f)}</text>
                    ))}
                  </g>
                  <g stroke="#143354" strokeWidth="1">
                    {[0, 0.25, 0.5, 0.75, 1].map((f) => <line key={f} x1="22" y1={(14 + f * 133).toFixed(0)} x2="330" y2={(14 + f * 133).toFixed(0)} />)}
                  </g>
                  {days.map((d, i) => {
                    const x0 = 30 + i * (300 / days.length);
                    const bars: [string, number, string][] = [["IMPORT", d.imp, BLUE], ["EXPORT", d.exp, GREEN], ["DELIVERY", d.del, AMBER]];
                    return (
                      <g key={d.d} style={{ cursor: "pointer" }} onClick={() => onDrill({ tab: "PENDING", date: d.d })}>
                        <title>{`${d.d} · ${d.total} งาน · Import ${d.imp} · Export ${d.exp} · Delivery ${d.del}`}</title>
                        <rect x={x0 - 4} y="14" width={300 / days.length} height="133" fill="transparent" />
                        {bars.map(([cat, n, colour], k) => {
                          const h = (n / dayTop) * 133;
                          const dim = focus !== "ALL" && focus !== cat;
                          return <rect key={cat} x={x0 + k * 11} y={(147 - h).toFixed(1)} width="10" height={h.toFixed(1)} rx="1" fill={colour} opacity={dim ? 0.22 : 1} />;
                        })}
                        <text x={x0 + 16} y="162" fontFamily="IBM Plex Mono" fontSize="9" fill={DIM} textAnchor="middle">{d.d.slice(0, 5)}</text>
                      </g>
                    );
                  })}
                </svg>
              ) : <Empty>ไม่มีวันที่มีงานในช่วงนี้</Empty>}
            </Panel>

            <Panel title="On-Time Delivery" right={
              otd?.target !== null && otd?.target !== undefined
                ? <span style={css(`font-size:11.5px;color:${MUTED}`)}><b style={css(`color:${BLUE};font-weight:600`)}>{otd.target}%</b> Target</span>
                : null
            }>
              <div style={css("display:flex;align-items:center;gap:14px;flex-wrap:wrap")}>
                <Gauge pct={otd?.value ?? null} label="OTD" />
                <div style={css("flex:1;min-width:108px;display:flex;flex-direction:column;gap:9px")}>
                  {([
                    ["วัดได้", otd ? nf(otd.base) : "—", "Measured Trips", "rgba(45,209,138,.08)", "rgba(45,209,138,.3)", "#7fcfa8"],
                    ["ล่าช้า", delay?.value === null || delay?.value === undefined ? "—" : nf(delay.value), "Delayed", "rgba(239,75,87,.08)", "rgba(239,75,87,.3)", "#ff9aa2"],
                    ["ทั้งหมด", report ? nf(report.jobs) : "—", "Total Trips", "rgba(56,158,230,.08)", "rgba(56,158,230,.3)", "#8ecdf7"],
                  ] as const).map(([key, value, label, bg, border, colour]) => (
                    <div key={key} style={css(`background:${bg};border:1px solid ${border};border-radius:7px;padding:8px 10px;display:flex;flex-direction:column;gap:1px`)}>
                      <span style={css(`font-family:${MONO};font-size:16px;font-weight:600;color:#fff`)}>{value}</span>
                      <span style={css(`font-size:10.5px;color:${colour}`)}>{label}</span>
                    </div>
                  ))}
                </div>
              </div>
              {otd && otd.value === null && <Empty>{otd.note || "ยังวัดไม่ได้"}</Empty>}
            </Panel>
          </div>

          {/* ------------------------------------------------ who, and why late */}
          <div className="ct-row3">
            <Panel title="Top Customers" tight right={<LinkButton onClick={() => onDrill({ tab: "PENDING" })}>View All →</LinkButton>}>
              <ListHead cols="16px minmax(0,1fr) 46px 40px 62px" heads={["#", "CUSTOMER", "TRIPS", "SHARE", "TREND"]} />
              {customers.length ? customers.map(([name, n], i) => (
                <Row key={name} cols="16px minmax(0,1fr) 46px 40px 62px" last={i === customers.length - 1}
                  title={`${name} · ${nf(n)} งาน · ${Math.round((n / total) * 100)}% ของ ${nf(total)}`}
                  go={() => onDrill({ tab: "PENDING", customer: name })}>
                  <span style={css(`font-family:${MONO};font-size:11px;color:${DIM}`)}>{i + 1}</span>
                  <span style={css(`font-size:12.5px;font-weight:600;color:${INK};white-space:nowrap;overflow:hidden;text-overflow:ellipsis`)}>{name}</span>
                  <span style={css(`font-family:${MONO};font-size:12px;color:#cfe3f4;text-align:right`)}>{nf(n)}</span>
                  <span style={css(`font-family:${MONO};font-size:12px;font-weight:600;color:${BLUE};text-align:right`)}>{Math.round((n / total) * 100)}%</span>
                  <span style={css("justify-self:end")}><Spark points={daily((j) => j.customer, name)} colour={BLUE} width={62} height={16} /></span>
                </Row>
              )) : <Empty>ไม่มีงานในช่วงนี้</Empty>}
            </Panel>

            <Panel title="Top Subcontractors" tight right={<LinkButton onClick={() => p.onOpen("subcontractors")}>View All →</LinkButton>}>
              <ListHead cols="16px minmax(0,1fr) 46px 40px 62px" heads={["#", "SUBCONTRACTOR", "TRIPS", "OTD", "TREND"]} />
              {truckers.length ? truckers.map(([name, n], i) => {
                const o = supplierOtd(name);
                const pct = o?.pct ?? null;
                return (
                  <Row key={name} cols="16px minmax(0,1fr) 46px 40px 62px" last={i === truckers.length - 1}
                    title={`${name} · ${nf(n)} งาน` + (o ? (pct === null ? ` · ตรงเวลาวัดได้ ${o.base} เที่ยว — น้อยกว่า ${MINIMUM_SAMPLE} จึงไม่คิดเป็นเปอร์เซ็นต์` : ` · ตรงเวลา ${pct}% วัดจาก ${o.base} เที่ยว`) : " · ไม่มีตัวเลขตรงเวลาจากเซิร์ฟเวอร์ในช่วงนี้")}
                    go={() => onDrill({ tab: "PENDING", trucker: name })}>
                    <span style={css(`font-family:${MONO};font-size:11px;color:${DIM}`)}>{i + 1}</span>
                    <span style={css(`font-size:12.5px;font-weight:600;color:${INK};white-space:nowrap;overflow:hidden;text-overflow:ellipsis`)}>{name}</span>
                    <span style={css(`font-family:${MONO};font-size:12px;color:#cfe3f4;text-align:right`)}>{nf(n)}</span>
                    <span style={css(`font-family:${MONO};font-size:12px;font-weight:600;color:${otdColour(pct)};text-align:right`)}>{pct === null ? "—" : pct + "%"}</span>
                    <span style={css("justify-self:end")}><Spark points={daily((j) => j.trucker, name)} colour={otdColour(pct) === DIM ? BLUE : otdColour(pct)} width={62} height={16} /></span>
                  </Row>
                );
              }) : <Empty>ไม่มีงานในช่วงนี้</Empty>}
            </Panel>

            <Panel title="Delay Reasons" tight right={<LinkButton onClick={() => onDrill({ tab: "DELAY", kpi: "Delay" })}>View All →</LinkButton>}>
              <ListHead cols="16px minmax(0,1fr) 48px 96px" heads={["#", "REASON", "CASES", "%"]} />
              {reasons.length ? reasons.map(([name, n], i) => (
                <Row key={name} cols="16px minmax(0,1fr) 48px 96px" last={i === reasons.length - 1}
                  title={`${name} · ${nf(n)} งาน · ${Math.round((n / reasonTotal) * 100)}% ของงานที่บันทึกสาเหตุ ${nf(reasonTotal)} งาน`}
                  go={() => onDrill({ tab: "DELAY", kpi: "Delay" })}>
                  <span style={css(`font-family:${MONO};font-size:11px;color:${DIM}`)}>{i + 1}</span>
                  <span style={css(`font-size:12.5px;color:${INK};white-space:nowrap;overflow:hidden;text-overflow:ellipsis`)}>{name}</span>
                  <span style={css(`font-family:${MONO};font-size:12px;color:#cfe3f4;text-align:right`)}>{nf(n)}</span>
                  <span style={css("height:9px;border-radius:5px;background:#0f2c48;overflow:hidden;display:block")}>
                    <span style={css(`display:block;height:100%;width:${((n / reasons[0][1]) * 100).toFixed(0)}%;background:${i === 0 ? "linear-gradient(90deg,#ef4b57,#ff7d86)" : "#4aa8e8"}`)} />
                  </span>
                </Row>
              )) : <Empty>ยังไม่มีการบันทึกสาเหตุความล่าช้าในช่วงนี้</Empty>}
              {reasonRead.skipped > 0 && (
                <span style={css(`font-size:10.5px;color:${DIM};padding-top:4px`)}
                  title="ค่าในช่อง Reason ที่เป็นสถานะ เวลา หรือนัดรับตู้ ไม่ใช่สาเหตุ — ดูรายการได้ที่ Delay Analysis">
                  ไม่นับ {nf(reasonRead.skipped)} รายการที่ช่อง Reason เป็นสถานะ/เวลา ไม่ใช่สาเหตุ
                </span>
              )}
            </Panel>
          </div>

          {/* ------------------------------------------------ cost · map */}
          <div className="ct-row2">
            <Panel title="Transportation Cost (THB)" tall right={
              <div style={css("display:flex;gap:12px;font-size:10.5px;color:#a7c6de")}>
                <span style={css("display:flex;align-items:center;gap:5px")}><span style={css(`width:8px;height:8px;border-radius:2px;background:${BLUE}`)} />Total Cost</span>
                <span style={css("display:flex;align-items:center;gap:5px")}>
                  <svg width="16" height="8" aria-hidden="true"><line x1="0" y1="4" x2="16" y2="4" stroke="#cfe3f4" strokeWidth="1.6" /><circle cx="8" cy="4" r="2.4" fill="#cfe3f4" /></svg>Cost/Trip
                </span>
              </div>
            }>
              {costByMonth.length ? (
                <div style={css("display:flex;gap:14px;align-items:stretch;flex-wrap:wrap")}>
                  <svg viewBox="0 0 320 150" style={css("flex:1;min-width:220px;height:auto;overflow:visible")} aria-label="ค่าขนส่งรายเดือน">
                    <g fontFamily="IBM Plex Mono" fontSize="9" fill={DIM}>
                      {[1, 0.75, 0.5, 0.25, 0].map((f) => <text key={f} x="0" y={(12 + (1 - f) * 115 + 4).toFixed(0)}>{compact(costTop * f)}</text>)}
                    </g>
                    <g stroke="#143354">{[0, 0.25, 0.5, 0.75, 1].map((f) => <line key={f} x1="28" y1={(12 + f * 115).toFixed(0)} x2="320" y2={(12 + f * 115).toFixed(0)} />)}</g>
                    {costByMonth.map((m, i) => {
                      const slot = 292 / costByMonth.length;
                      const x = 28 + i * slot + slot / 2;
                      const h = (m.cost / costTop) * 115;
                      return (
                        <g key={m.k}>
                          <title>{`${monthNameEn(m.k.slice(5), true)} ${m.k.slice(0, 4)} · รวม ฿${nf(Math.round(m.cost))} · ${nf(m.trips)} เที่ยว · เฉลี่ย ฿${nf(Math.round(m.cost / m.trips))}/เที่ยว`}</title>
                          <rect x={(x - 7).toFixed(1)} y={(127 - h).toFixed(1)} width="14" height={h.toFixed(1)} rx="1.5" fill="#4aa8e8" />
                          <text x={x.toFixed(1)} y="142" fontFamily="IBM Plex Mono" fontSize="9" fill={DIM} textAnchor="middle">{monthNameEn(m.k.slice(5), true)}</text>
                        </g>
                      );
                    })}
                    <polyline fill="none" stroke="#dbeaf7" strokeWidth="1.6"
                      points={costByMonth.map((m, i) => {
                        const slot = 292 / costByMonth.length;
                        return `${(28 + i * slot + slot / 2).toFixed(1)},${(127 - ((m.cost / m.trips) / perTripTop) * 115).toFixed(1)}`;
                      }).join(" ")} />
                    <g fill="#f2f8fd">
                      {costByMonth.map((m, i) => {
                        const slot = 292 / costByMonth.length;
                        return <circle key={m.k} cx={(28 + i * slot + slot / 2).toFixed(1)} cy={(127 - ((m.cost / m.trips) / perTripTop) * 115).toFixed(1)} r="2.6" />;
                      })}
                    </g>
                  </svg>
                  <div style={css("flex:none;width:132px;display:flex;flex-direction:column;gap:10px;justify-content:center")}>
                    <div style={css("background:#0f2c48;border:1px solid rgba(74,148,214,.22);border-radius:7px;padding:10px 11px;display:flex;flex-direction:column;gap:3px")}>
                      <span style={css(`font-size:10.5px;color:${MUTED}`)}>Total Cost</span>
                      <span style={css("font-size:19px;font-weight:700;color:#fff")}>{costed.length ? compact(costTotal) : "—"} <span style={css(`font-family:${MONO};font-size:10px;font-weight:500;color:${MUTED}`)}>THB</span></span>
                      <span style={css(`font-size:10.5px;color:${DIM}`)}>{costed.length ? `${nf(costed.length)} เที่ยวที่มีราคา` : "ไม่มีเที่ยวที่มีราคาในช่วงนี้"}</span>
                    </div>
                    <div style={css("background:#0f2c48;border:1px solid rgba(74,148,214,.22);border-radius:7px;padding:10px 11px;display:flex;flex-direction:column;gap:3px")}>
                      <span style={css(`font-size:10.5px;color:${MUTED}`)}>Cost / Trip</span>
                      <span style={css("font-size:19px;font-weight:700;color:#fff")}>{costed.length ? nf(Math.round(costTotal / costed.length)) : "—"} <span style={css(`font-family:${MONO};font-size:10px;font-weight:500;color:${MUTED}`)}>THB</span></span>
                      <span style={css(`font-size:10.5px;color:${DIM}`)}>{periodLabel(period)}</span>
                    </div>
                  </div>
                </div>
              ) : <Empty>ยังไม่มีเที่ยวที่ระบุค่าขนส่งในทะเบียน — ค่าขนส่งคิดจากเที่ยวที่มี Transportation Rate เท่านั้น</Empty>}
            </Panel>

            <section style={css(`position:relative;border-radius:8px;border:1px solid ${LINE};overflow:hidden;background:radial-gradient(120% 120% at 30% 20%,#0c3350 0%,#081f36 55%,#06152a 100%);min-height:250px;display:flex;flex-direction:column`)}>
              <div aria-hidden="true" style={css("position:absolute;inset:0;opacity:.5;background-image:linear-gradient(rgba(74,148,214,.14) 1px,transparent 1px),linear-gradient(90deg,rgba(74,148,214,.14) 1px,transparent 1px);background-size:34px 34px")} />
              <div style={css("position:relative;padding:14px 16px;display:flex;align-items:flex-start;justify-content:space-between;gap:14px;flex-wrap:wrap")}>
                <h3 style={css(H3)}>Operational Map</h3>
                <div style={css("display:grid;grid-template-columns:auto auto;gap:5px 14px;font-size:10.5px;color:#cfe3f4")}>
                  {legend.map(([name, n, colour]) => (
                    <span key={name} style={css("display:flex;align-items:center;gap:6px")} title={`${name} · ${nf(n)} งาน`}>
                      <span style={css(`width:8px;height:8px;border-radius:50%;background:${colour}`)} />{name} <b style={css(`font-family:${MONO};font-weight:600;color:#fff`)}>{nf(n)}</b>
                    </span>
                  ))}
                </div>
              </div>
              <svg viewBox="0 0 460 220" style={css("position:relative;width:100%;flex:1;min-height:0")} preserveAspectRatio="xMidYMid meet" aria-label="ปลายทางหลัก">
                <path d="M120 22c-14 26-8 52 10 74 22 26 52 30 78 52 20 17 26 42 20 66" fill="none" stroke="rgba(120,180,225,.22)" strokeWidth="26" strokeLinecap="round" />
                <path d="M150 84c30 14 48 40 82 48 28 7 52-2 76-18" fill="none" stroke="rgba(74,168,232,.5)" strokeWidth="1.6" strokeDasharray="6 5" />
                <path d="M150 84c-22 22-24 54 60 66" fill="none" stroke="rgba(47,209,138,.5)" strokeWidth="1.6" strokeDasharray="6 5" />
                {HUBS.map((h, i) => {
                  const n = hubCount[i];
                  const r = n ? 4 + (n / hubMax) * 8 : 3;
                  return (
                    <g key={h.name}>
                      <title>{`${h.name} · ${nf(n)} งานที่ระบุปลายทาง/โรงงาน/ลานตู้แถบนี้`}</title>
                      {n > 0 && <circle cx={h.x} cy={h.y} r={r + 8} fill="rgba(92,192,247,.14)" />}
                      <circle cx={h.x} cy={h.y} r={r} fill={n ? BLUE : "#3b5f80"} />
                      <text x={h.x + r + 6} y={h.y + 4} fontFamily="IBM Plex Sans Thai" fontSize="11" fontWeight="600" fill="#dbeaf7">{h.name}</text>
                      <text x={h.x + r + 6} y={h.y + 16} fontFamily="IBM Plex Mono" fontSize="9" fill={DIM}>{nf(n)}</text>
                    </g>
                  );
                })}
              </svg>
              <div style={css("position:relative;padding:0 16px 14px;display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap")}>
                <span style={css("font-family:'IBM Plex Mono',monospace;font-size:9px;letter-spacing:.1em;color:#5e87ab")}>SCHEMATIC — นับจากชื่อปลายทางในงาน · LIVE GEO FEED PENDING</span>
                <button type="button" onClick={() => p.onOpen("monitoring")}
                  style={css("flex:none;display:flex;align-items:center;gap:7px;padding:8px 14px;background:#1668ab;border:none;border-radius:7px;color:#fff;font-family:inherit;font-size:12px;font-weight:600;cursor:pointer")}>
                  Shipment Monitor →
                </button>
              </div>
            </section>
          </div>
        </div>

        {/* ------------------------------------------------ the rail */}
        <aside className="ct-rail" style={css(`position:sticky;top:12px;background:${BG};border:1px solid rgba(74,148,214,.24);border-radius:8px;padding:14px;display:flex;flex-direction:column;gap:13px`)}>
          <div style={css("display:flex;align-items:center;gap:8px")}>
            <svg width="19" height="19" viewBox="0 0 16 16" fill={BLUE} aria-hidden="true">{ICON.spark}</svg>
            <span style={css(`flex:1;font-size:13px;font-weight:600;color:${INK}`)}>SCMOS AI Co-Pilot</span>
            <span style={css(`font-family:${MONO};font-size:8.5px;font-weight:600;letter-spacing:.08em;color:#0b2438;background:${BLUE};border-radius:9px;padding:3px 7px`)}>Beta</span>
            <button type="button" onClick={() => setRail((v) => !v)} aria-label={rail ? "ย่อแผง AI" : "ขยายแผง AI"}
              style={css(`border:none;background:none;padding:0;color:${MUTED};cursor:pointer;font-size:14px;line-height:1`)}>{rail ? "—" : "+"}</button>
          </div>
          {rail && (
            <>
              <div style={css("display:flex;flex-direction:column;gap:5px")}>
                <span style={css("font-size:17px;font-weight:600;color:#fff")}>{greeting}, {p.userName.split(" ")[0] || "there"}.</span>
                <span style={css("font-size:12.5px;line-height:1.45;color:#a7c6de")}>
                  {brief?.today ? `สรุปสถานการณ์ประจำวันที่ ${brief.today}` : "Here's your operational summary for today."}
                </span>
              </div>
              <div style={css("background:#0f2c48;border:1px solid rgba(74,148,214,.24);border-radius:8px;padding:12px;display:flex;flex-direction:column;gap:10px")}>
                <span style={css("font-size:13.5px;font-weight:600;color:#fff")}>
                  {brief === null ? "กำลังอ่านสรุปเช้านี้…" : attention.length ? `${nf(attentionCount)} รายการต้องดู` : (brief.quiet || "ไม่มีอะไรต้องดูเป็นพิเศษ")}
                </span>
                {attention.slice(0, 6).map((f) => (
                  <button key={f.kind + f.headline} type="button" onClick={() => p.onOpen(f.screen)} title={f.detail}
                    style={css("display:flex;align-items:center;gap:9px;border:none;background:none;padding:0;font-family:inherit;cursor:pointer;text-align:left")}>
                    <span style={css(`width:8px;height:8px;border-radius:50%;flex:none;background:${URGENCY[f.urgency]}`)} />
                    <b style={css(`font-family:${MONO};font-size:12.5px;color:#fff;font-weight:600;flex:none`)}>{nf(f.count)}</b>
                    <span style={css("font-size:12.5px;color:#cfe3f4;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>{f.headline}</span>
                  </button>
                ))}
                <button type="button" onClick={() => ask("สรุปรายการที่ต้องดูเช้านี้ และควรตรวจอะไรต่อ")}
                  style={css("margin-top:2px;padding:10px;background:#1668ab;border:none;border-radius:7px;color:#fff;font-family:inherit;font-size:12.5px;font-weight:600;cursor:pointer")}>
                  Review with AI →
                </button>
              </div>
              <div style={css("display:flex;flex-direction:column;gap:8px")}>
                {prompts.map((text) => (
                  <button key={text} type="button" onClick={() => ask(text)} className="ct-prompt"
                    style={css("padding:9px 12px;background:#0f2c48;border:1px solid rgba(74,148,214,.24);border-radius:7px;font-size:12px;color:#d6e8f7;cursor:pointer;font-family:inherit;text-align:left")}>
                    {text}
                  </button>
                ))}
              </div>
              <form style={css("display:flex;flex-direction:column;gap:8px")} onSubmit={(e) => { e.preventDefault(); ask(question); }}>
                <div style={css("display:flex;align-items:center;gap:8px;height:38px;padding:0 6px 0 12px;background:#0f2c48;border:1px solid rgba(74,148,214,.3);border-radius:8px")}>
                  <input value={question} onChange={(e) => setQuestion(e.target.value)} placeholder="Ask SCMOS AI…" aria-label="ถาม SCMOS AI" maxLength={4000}
                    style={css("flex:1;min-width:0;border:none;background:none;outline:none;font-size:12px;color:#e6f1fa;font-family:inherit")} />
                  <button type="submit" aria-label="ส่งคำถาม"
                    style={css("width:28px;height:28px;flex:none;display:grid;place-items:center;background:#1668ab;border:none;border-radius:6px;color:#fff;cursor:pointer")}>
                    <svg width="14" height="14" viewBox="0 0 16 16" fill="currentColor" aria-hidden="true"><path d="M2 13.6 14 8 2 2.4l2.4 5.6L2 13.6Z" /></svg>
                  </button>
                </div>
                <span style={css("font-size:10px;line-height:1.4;text-align:center;color:#6f97b8")}>AI may produce inaccurate information. Please verify before taking action.</span>
              </form>
            </>
          )}
        </aside>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------- list bits */

function ListHead({ cols, heads }: { cols: string; heads: string[] }) {
  return (
    <div style={css(`display:grid;grid-template-columns:${cols};gap:6px 8px;align-items:center;font-family:${MONO};font-size:9.5px;font-weight:600;letter-spacing:.08em;color:${MUTED};padding-bottom:7px;border-bottom:1px solid ${LINE}`)}>
      {heads.map((h, i) => <span key={h} style={css(i >= 2 ? "text-align:right" : "")}>{h}</span>)}
    </div>
  );
}

function Row({ cols, last, title, go, children }: { cols: string; last: boolean; title: string; go: () => void; children: ReactNode }) {
  return (
    <button type="button" onClick={go} title={title} className="ct-row"
      style={css(`display:grid;grid-template-columns:${cols};gap:8px;align-items:center;padding:7px 0;border:none;background:none;font-family:inherit;text-align:left;cursor:pointer;width:100%;`
        + (last ? "" : "border-bottom:1px solid rgba(74,148,214,.1)"))}>
      {children}
    </button>
  );
}
