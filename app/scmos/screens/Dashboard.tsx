"use client";

import { useEffect, useState, type ReactNode } from "react";
import { css, STATUS_LADDER, STATUS_TH } from "../theme";
import { ZoomBox } from "../TableFrame";
import { useRemembered } from "../pageCache";
import type { Ship } from "../demo";
import type { WsTarget } from "../alerts";
import { opsStats, STATUS_RE as RE, type Job, type OpsStats } from "../ops";
import { periodLabel, type Period } from "../period";
import { dowOf, money, pad } from "../util";
import { PeriodBar } from "../PeriodBar";
import { apiFetch } from "../api";
import { byStage } from "../incidentStages";

/**
 * The three dashboard tabs answer three different questions, so they are three
 * different screens rather than one page shown three times:
 *
 *   Executive   — how the month is going: volume, mix, on-time, who we use most.
 *   Operational — what needs a person today: the plan day, the pipeline, the
 *                 jobs missing data, the delays, who is carrying what.
 *   Wall Board  — the few numbers worth putting on a screen in the office.
 *
 * Everything except the panels marked DEMO is computed from the real operation
 * jobs (`ops.json`), so the figures agree with the Operation Workspace.
 */

/** What a click on a dashboard figure asks the workspace to show. */
export type Drill = WsTarget;

type Props = {
  filtered: Ship[];
  /** Real operation jobs, already narrowed to the chosen period. */
  jobs: Job[];
  /** Everything in the register, so the period pickers can offer every option. */
  allJobs: Job[];
  period: Period;
  onPeriod: (period: Period) => void;
  loaded: boolean;
  /** What to say while there is nothing to draw — see loadingNote in SCMOSApp. */
  note?: string;
  tab: string;
  onDrill: (patch: Drill) => void;
  /** Opens the KPI screen, where the full scorecard is. */
  onOpenKpi?: () => void;
};

const CAT_COLOUR: Record<string, string> = { IMPORT: "#0A2240", EXPORT: "#6FA8DC", DELIVERY: "#0A6E8A" };

/* --------------------------------------------------------------- helpers */

function Panel(p: { title: string; sub?: string; right?: ReactNode; children: ReactNode }) {
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:16px 18px")}>
      <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:12px;margin-bottom:14px;flex-wrap:wrap")}>
        <div>
          <h3 style={css("margin:0 0 2px;font-size:13.5px;font-weight:600;color:#0A2240")}>{p.title}</h3>
          {!!p.sub && <div style={css("font-size:11px;color:#94A3B8")}>{p.sub}</div>}
        </div>
        {p.right}
      </div>
      {p.children}
    </div>
  );
}

type BarItem = {
  label: string;
  value: string;
  pct: number;
  colour: string;
  go?: () => void;
  /**
   * What hovering the row says.
   *
   * Set by whoever builds the item rather than worked out here, because the
   * only honest share is one taken over everything — and these lists are cut
   * to the top few, so a percentage computed from what is on screen would be a
   * percentage of the wrong denominator.
   */
  hint?: string;
};

function BarRows({ items, empty }: { items: BarItem[]; empty?: string }) {
  if (!items.length) {
    return <span style={css("font-size:11.5px;color:#94A3B8")}>{empty ?? "ไม่มีข้อมูลในชุดนี้"}</span>;
  }
  return (
    <div style={css("display:flex;flex-direction:column;gap:9px")}>
      {items.map((i) => (
        <button
          key={i.label}
          type="button"
          title={i.hint ?? i.label + " · " + i.value}
          onClick={i.go}
          disabled={!i.go}
          style={css(
            "font-family:inherit;text-align:left;padding:0;border:none;background:none;display:flex;align-items:center;gap:10px;width:100%;cursor:" +
            (i.go ? "pointer" : "default"),
          )}
        >
          <span style={css("width:136px;flex:none;font-size:11.5px;color:#334155;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>{i.label}</span>
          <span style={css("flex:1;height:16px;background:#F1F5F9;border-radius:2px;overflow:hidden")}>
            <span style={css("display:block;height:100%;border-radius:2px;width:" + i.pct.toFixed(1) + "%;background:" + i.colour)} />
          </span>
          <span style={css("width:58px;flex:none;text-align:right;font-size:11.5px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{i.value}</span>
        </button>
      ))}
    </div>
  );
}

function bars(counts: Record<string, number>, colour: string, limit: number, go?: (key: string) => void): BarItem[] {
  const entries = Object.keys(counts)
    .filter((k) => k && k !== "—")
    .map((k) => [k, counts[k]] as [string, number])
    .sort((a, b) => b[1] - a[1])
    .slice(0, limit);
  const max = Math.max(1, ...entries.map((e) => e[1]));
  // Over every key, not only the ones that survived the cut, so the share a
  // person reads on hover is the share of the real total.
  const whole = Object.keys(counts)
    .filter((k) => k && k !== "—")
    .reduce((sum, k) => sum + counts[k], 0);
  const shown = entries.length;
  const all = Object.keys(counts).filter((k) => k && k !== "—").length;

  return entries.map(([label, n], index) => ({
    label,
    value: String(n),
    pct: (n / max) * 100,
    colour,
    go: go ? () => go(label) : undefined,
    hint: `${label} · ${n} งาน`
      + (whole ? ` · ${Math.round((n / whole) * 100)}% ของ ${whole}` : "")
      + ` · อันดับ ${index + 1} จาก ${all}`
      + (shown < all ? ` (แสดง ${shown} อันดับแรก)` : ""),
  }));
}

function countBy(jobs: Job[], pick: (j: Job) => string | undefined): Record<string, number> {
  const out: Record<string, number> = {};
  jobs.forEach((j) => {
    const key = (pick(j) || "").trim();
    if (key) out[key] = (out[key] || 0) + 1;
  });
  return out;
}

function Donut(p: { title: string; sub: string; unit: string; items: [string, number, string][] }) {
  const total = p.items.reduce((sum, i) => sum + i[1], 0);
  let acc = 0;
  const segments = p.items.map((i) => {
    const from = (acc / (total || 1)) * 360;
    acc += i[1];
    return i[2] + " " + from.toFixed(1) + "deg " + ((acc / (total || 1)) * 360).toFixed(1) + "deg";
  });
  return (
    <Panel title={p.title} sub={p.sub}>
      <div style={css("display:flex;align-items:center;gap:20px;flex-wrap:wrap")}>
        <div
          title={total
            ? p.items.map((i) => `${i[0]} ${i[1]} (${Math.round((i[1] / total) * 100)}%)`).join("\n")
              + `\nรวม ${total} ${p.unit}`
            : "ไม่มีข้อมูล"}
          style={{ ...css("position:relative;flex:none;width:128px;height:128px;border-radius:50%"), background: total ? "conic-gradient(" + segments.join(",") + ")" : "#EEF2F6" }}
        >
          <div style={css("position:absolute;inset:26px;border-radius:50%;background:#fff;display:flex;flex-direction:column;align-items:center;justify-content:center")}>
            <span style={css("font-size:22px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{total}</span>
            <span style={css("font-size:10px;color:#94A3B8;letter-spacing:.04em")}>{p.unit}</span>
          </div>
        </div>
        <div style={css("flex:1;min-width:190px;display:flex;flex-direction:column;gap:9px")}>
          {p.items.map((i) => (
            <div
              key={i[0]}
              title={`${i[0]} · ${i[1]} ${p.unit}`
                + (total ? ` · ${Math.round((i[1] / total) * 100)}% ของ ${total}` : "")}
              style={css("display:flex;align-items:center;gap:9px")}
            >
              <span style={css("width:10px;height:10px;border-radius:2px;flex:none;background:" + i[2])} />
              <span style={css("flex:1;font-size:12px;color:#334155;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>{i[0]}</span>
              <span style={css("font-size:12px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{i[1]}</span>
              <span style={css("font-size:11px;color:#94A3B8;width:38px;text-align:right;font-family:'IBM Plex Mono',monospace")}>
                {total ? Math.round((i[1] / total) * 100) + "%" : "—"}
              </span>
            </div>
          ))}
        </div>
      </div>
    </Panel>
  );
}

type Tile = { label: string; th: string; value: string; note?: string; colour: string; go?: () => void };

function Tiles({ items }: { items: Tile[] }) {
  return (
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(178px,1fr));gap:12px")}>
      {items.map((t) => (
        <button
          key={t.label}
          type="button"
          onClick={t.go}
          disabled={!t.go}
          style={css(
            // Sides named individually: the accent colour changes as the period
            // filter moves, and React warns when a `border` shorthand and a
            // `border-top` are both set on a node that rerenders.
            "font-family:inherit;text-align:left;width:100%;background:#fff;border-top:3px solid " + t.colour +
            ";border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8" +
            ";border-radius:5px;padding:14px 15px 15px;cursor:" + (t.go ? "pointer" : "default"),
          )}
        >
          <div style={css("display:flex;justify-content:space-between;align-items:flex-start;gap:8px")}>
            <span style={css("display:flex;flex-direction:column;line-height:1.25")}>
              <span style={css("font-size:11.5px;color:#475569;font-weight:600")}>{t.label}</span>
              <span style={css("font-size:10.5px;color:#94A3B8")}>{t.th}</span>
            </span>
            <span style={css("width:8px;height:8px;border-radius:50%;flex:none;margin-top:3px;background:" + t.colour)} />
          </div>
          <div style={css("display:flex;align-items:baseline;gap:8px;margin-top:12px")}>
            <span style={css("font-size:30px;font-weight:600;color:#0A2240;font-family:'IBM Plex Mono',monospace;letter-spacing:-.02em")}>{t.value}</span>
            {!!t.note && <span style={css("font-size:11px;color:#64748B;font-family:'IBM Plex Mono',monospace")}>{t.note}</span>}
          </div>
        </button>
      ))}
    </div>
  );
}

const DEMO_BADGE = (
  <span style={css("font-family:'IBM Plex Mono',monospace;font-size:10px;font-weight:600;letter-spacing:.06em;color:#B45309;background:#FDF2DF;border-radius:3px;padding:3px 7px")}>
    DEMO DATA
  </span>
);

/* ------------------------------------------------------------- dashboard */

/** One carrier's line on the contract scorecard, as the KPI engine sends it. */
type ScoreRow = { carrier: string; shipments: number; weighted: number | null; weightAvailable: number };

/**
 * The contract scorecard, in one line per carrier that is not meeting it.
 *
 * Fetched here rather than computed: the engine already works it out and caches
 * it against the register's own timestamp, and this screen has been hung once
 * before by doing arithmetic over the whole register on the way in. One request,
 * after the first paint, and the panel simply does not appear until it answers.
 */
function ContractScores({ period, onOpen }: { period: Period; onOpen: () => void }) {
  const [rows, setRows] = useRemembered<ScoreRow[]>("dashboard");

  // The same three parameters the KPI screen sends, so both read one period.
  const query = new URLSearchParams();
  if (period.year && period.year !== "ALL") query.set("year", period.year);
  if (period.month && period.month !== "ALL") query.set("month", period.month);
  if (period.day && period.day !== "ALL") query.set("day", period.day.slice(0, 2));
  const search = query.toString();

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch(`/api/kpi/measures?${search}`,
          { headers: { accept: "application/json" } });
        if (!response.ok || !alive) return;
        const body = await response.json() as { scorecard?: ScoreRow[] | null };
        if (alive) setRows(body.scorecard ?? []);
      } catch { /* the scorecard stays as it was; a failed read is not a clean month */ }
    })();
    return () => { alive = false; };
  }, [search, setRows]);

  if (!rows?.length) return null;

  // Below the ninety-five the agreement asks for, worst first. A carrier meeting
  // it does not need a line on the landing screen.
  const short = rows
    .filter((row) => row.weighted !== null && row.weighted < 95)
    .sort((a, b) => (a.weighted ?? 0) - (b.weighted ?? 0));

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px")}>
      <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>คะแนนตามสัญญา · ผู้ขนส่งที่ยังไม่ถึงเป้า</span>
        <button onClick={onOpen}
          style={css("border:none;background:none;padding:0;font-size:11.5px;color:#2E7DD1;cursor:pointer;font-family:inherit;text-decoration:underline")}>
          ดูคะแนนเต็ม
        </button>
      </div>

      {short.length === 0 ? (
        <div style={css("margin-top:7px;font-size:12px;color:#16794C")}>
          ผู้ขนส่งทุกรายที่วัดได้อยู่ที่ 95% ขึ้นไป
        </div>
      ) : (
        <div style={css("margin-top:8px;display:flex;flex-direction:column;gap:5px")}>
          {short.slice(0, 5).map((row) => (
            <div key={row.carrier} style={css("display:flex;justify-content:space-between;gap:12px;font-size:12px")}>
              <span style={css("color:#16232F;font-weight:600;overflow-wrap:anywhere")}>{row.carrier}</span>
              <span style={css("white-space:nowrap;font-family:'IBM Plex Mono',monospace;font-weight:700;color:"
                + ((row.weighted ?? 0) >= 85 ? "#B45309" : "#B42318"))}>
                {(row.weighted ?? 0).toFixed(1)}
                <span style={css("color:#94A3B8;font-weight:400")}> · {row.shipments} shipment</span>
              </span>
            </div>
          ))}
          {short.length > 5 && (
            <div style={css("font-size:11px;color:#7B8CA0")}>และอีก {short.length - 5} ราย</div>
          )}
        </div>
      )}
    </div>
  );
}

/**
 * The CAR/PAR cases, from the screen that owns them.
 *
 * Read here rather than counted from anything the dashboard already holds:
 * these cases are not derived from the job register, and the only place that
 * knows about them is the incident table. Failing quietly is deliberate — a
 * panel that cannot reach the API keeps the last count it had rather than
 * dropping to zero, which would read as "no open cases".
 */
function useCarPar(): { stage: string }[] | null {
  const [cases, setCases] = useRemembered<{ stage: string }[]>("dashboardCarPar");

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch("/api/incidents", { headers: { accept: "application/json" } });
        if (!response.ok || !alive) return;
        const body = await response.json() as { stage: string }[];
        if (alive) setCases(body);
      } catch { /* keep what was there; an unreachable API is not an empty register */ }
    })();
    return () => { alive = false; };
  }, [setCases]);

  return cases ?? null;
}

export function Dashboard({ filtered: fl, jobs, allJobs, period, onPeriod, loaded, note, tab, onDrill, onOpenKpi }: Props) {
  const s = opsStats(jobs);
  const total = s.jobs.length;
  const pct = (n: number) => (total ? Math.round((n / total) * 100) + "%" : "—");

  if (!loaded) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
        {note ?? "Loading operation data…"}
      </div>
    );
  }

  const bar = <PeriodBar allJobs={allJobs} shown={total} period={period} onPeriod={onPeriod} />;

  if (!total) {
    return (
      <div style={css("display:flex;flex-direction:column;gap:16px")}>
        {bar}
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
          ไม่มีงานในช่วงเวลา “{periodLabel(period)}” — เลือกช่วงอื่นหรือกดล้างช่วงเวลา
        </div>
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:16px")}>
      {bar}
      {tab === "Operational"
        ? <Operational s={s} period={period} onDrill={onDrill} />
        : <Executive s={s} fl={fl} total={total} pct={pct} onDrill={onDrill} />}

      {/* Only on the executive view, and only once the request answers. That is
          the view already asking "how are we doing"; the wall board is for a
          screen on the wall and the operational tab is for today's work. */}
      {tab !== "Wall Board" && tab !== "Operational" && onOpenKpi && (
        <ContractScores period={period} onOpen={onOpenKpi} />
      )}
    </div>
  );
}

/* ------------------------------------------------------------- executive */

function Executive(p: {
  s: OpsStats;
  fl: Ship[];
  total: number;
  pct: (n: number) => string;
  onDrill: (patch: Drill) => void;
}) {
  const { s, fl, total, onDrill } = p;

  // Volume per operation day, newest 14 days, split by direction.
  const days = s.dates.slice(-14).map((d) => {
    const set = s.jobs.filter((j) => j.date === d);
    const imp = set.filter((j) => j.cat === "IMPORT").length;
    const exp = set.filter((j) => j.cat === "EXPORT").length;
    const del = set.length - imp - exp;
    return { d, total: set.length, imp, exp, del };
  });
  const dayMax = Math.max(1, ...days.map((d) => d.total));

  // On-time by trucker, over the jobs that recorded an arrival.
  const truckerOtp = Object.keys(countBy(s.measurable, (j) => j.trucker))
    .map((name) => {
      const set = s.measurable.filter((j) => j.trucker === name);
      const ok = set.filter((j) => s.onTime.indexOf(j) >= 0).length;
      return { name, n: set.length, pct: Math.round((ok / set.length) * 100) };
    })
    .filter((t) => t.n >= 3)
    .sort((a, b) => b.pct - a.pct)
    .slice(0, 8);

  const costBySub: Record<string, number> = {};
  fl.forEach((ship) => { costBySub[ship.sub] = (costBySub[ship.sub] || 0) + ship.cost; });
  const costMax = Math.max(1, ...Object.values(costBySub));
  const costRows: BarItem[] = Object.keys(costBySub)
    .map((k) => ({ label: k, value: money(costBySub[k]).replace("฿", ""), pct: (costBySub[k] / costMax) * 100, colour: "#0A2240" }))
    .sort((a, b) => b.pct - a.pct)
    .slice(0, 8);

  const bill = (k: string) => fl.filter((x) => x.bill === k).length;
  const carPar = useCarPar();

  return (
    <div style={css("display:flex;flex-direction:column;gap:16px")}>
      <Tiles items={[
        { label: "Jobs in Plan", th: "งานทั้งหมดในแผน", value: String(total), note: s.dates.length + " วัน", colour: "#2E7DD1", go: () => onDrill({ tab: "PENDING" }) },
        { label: "Import", th: "งานนำเข้า", value: String(s.imports.length), note: p.pct(s.imports.length), colour: "#0A2240", go: () => onDrill({ tab: "PENDING", cat: "IMPORT" }) },
        { label: "Export", th: "งานส่งออก", value: String(s.exports.length), note: p.pct(s.exports.length), colour: "#6FA8DC", go: () => onDrill({ tab: "PENDING", cat: "EXPORT" }) },
        { label: "Delivery", th: "งานกระจายสินค้า", value: String(s.deliveries.length), note: p.pct(s.deliveries.length), colour: "#0A6E8A", go: () => onDrill({ tab: "PENDING", cat: "DELIVERY" }) },
        { label: "On-Time Arrival", th: "ถึงตรงเวลา", value: s.otpPct + "%", note: "วัดได้ " + s.measurable.length + "/" + total, colour: s.otpPct >= 90 ? "#16794C" : s.otpPct >= 75 ? "#B45309" : "#B42318" },
        { label: "Delayed", th: "ล่าช้า", value: String(s.delayed.length), note: p.pct(s.delayed.length), colour: "#B42318", go: () => onDrill({ tab: "DELAY", kpi: "Delay" }) },
        { label: "Completed", th: "เสร็จสิ้น", value: String(s.done.length), note: p.pct(s.done.length), colour: "#16794C", go: () => onDrill({ tab: "COMPLETED", kpi: "Done" }) },
        { label: "KPI-Ready Data", th: "ข้อมูลพร้อมคิด KPI", value: total ? Math.round(((total - s.formatErrors.length) / total) * 100) + "%" : "—", note: s.formatErrors.length + " ต้องแก้", colour: "#B45309", go: () => onDrill({ kpi: "Fmt" }) },
      ]} />

      <Panel
        title="Operation Volume by Day"
        sub="ปริมาณงานรายวัน · Import / Export / Delivery"
        right={
          <span style={css("display:flex;gap:14px;flex-wrap:wrap")}>
            {(["IMPORT", "EXPORT", "DELIVERY"] as const).map((c) => (
              <span key={c} style={css("display:flex;align-items:center;gap:6px;font-size:11.5px;color:#475569")}>
                <span style={css("width:10px;height:10px;border-radius:2px;background:" + CAT_COLOUR[c])} />{c}
              </span>
            ))}
          </span>
        }
      >
        <div style={css("display:flex;align-items:flex-end;gap:10px;height:190px;padding-bottom:30px;border-bottom:1px solid #E2E8F0")}>
          {days.map((d) => (
            <button
              key={d.d}
              type="button"
              onClick={() => onDrill({ tab: "PENDING", date: d.d })}
              title={d.d + " · " + d.total + " jobs"}
              style={css("font-family:inherit;flex:1;min-width:0;display:flex;flex-direction:column;align-items:center;gap:5px;height:100%;justify-content:flex-end;position:relative;border:none;background:none;padding:0;cursor:pointer")}
            >
              <span style={css("font-size:11px;font-weight:600;color:#0A2240;font-family:'IBM Plex Mono',monospace")}>{d.total}</span>
              <span style={{ ...css("width:100%;max-width:46px;display:flex;flex-direction:column;justify-content:flex-end"), height: (d.total / dayMax) * 100 + "%" }}>
                <span style={{ ...css("background:" + CAT_COLOUR.DELIVERY + ";border-radius:2px 2px 0 0"), height: (d.total ? (d.del / d.total) * 100 : 0) + "%" }} />
                <span style={{ ...css("background:" + CAT_COLOUR.EXPORT), height: (d.total ? (d.exp / d.total) * 100 : 0) + "%" }} />
                <span style={{ ...css("background:" + CAT_COLOUR.IMPORT), height: (d.total ? (d.imp / d.total) * 100 : 0) + "%" }} />
              </span>
              <span style={css("position:absolute;bottom:-26px;font-size:10px;color:#64748B;font-family:'IBM Plex Mono',monospace")}>{d.d.slice(0, 5)}</span>
            </button>
          ))}
        </div>
      </Panel>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px")}>
        <Donut
          title="Work Mix" sub="สัดส่วนงานตามประเภท" unit="JOBS"
          items={[
            ["Import", s.imports.length, CAT_COLOUR.IMPORT],
            ["Export", s.exports.length, CAT_COLOUR.EXPORT],
            ["Delivery", s.deliveries.length, CAT_COLOUR.DELIVERY],
          ]}
        />
        <Donut
          title="Operational Status" sub="ภาพรวมสถานะงาน" unit="JOBS"
          items={[
            ["Waiting truck", s.waiting.length, "#475569"],
            ["Truck confirmed", s.confirmed.length, "#1D5FA8"],
            ["In operation", s.running.length, "#0A6E8A"],
            ["Delayed", s.delayed.length, "#B42318"],
            ["Completed", s.done.length, "#16794C"],
          ]}
        />
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:14px")}>
        <Panel title="Customer Volume" sub="ปริมาณงานตามลูกค้า · คลิกเพื่อเปิดใน Workspace">
          <BarRows items={bars(countBy(s.jobs, (j) => j.customer), "#6FA8DC", 8)} />
        </Panel>
        <Panel title="Trips by Subcontractor" sub="จำนวนเที่ยวตามผู้ขนส่ง">
          <BarRows items={bars(countBy(s.jobs, (j) => j.trucker), "#2E7DD1", 8)} />
        </Panel>
        <Panel title="Trips by Truck / Container Type" sub="จำนวนเที่ยวตามประเภทรถและตู้">
          <BarRows items={bars(countBy(s.jobs, (j) => j.type), "#0A2240", 8)} />
        </Panel>
        <Panel
          title="On-Time Arrival by Subcontractor"
          sub={"อัตราถึงตรงเวลา · เฉพาะงานที่บันทึกเวลาถึงแล้ว (" + s.measurable.length + " งาน)"}
        >
          <BarRows
            empty="ยังไม่มีงานที่บันทึกทั้งเวลานัดและเวลาถึง"
            items={truckerOtp.map((t) => ({
              label: t.name + " (" + t.n + ")",
              value: t.pct + "%",
              pct: t.pct,
              colour: t.pct >= 90 ? "#16794C" : t.pct >= 80 ? "#D89614" : "#B42318",
            }))}
          />
        </Panel>
        <Panel title="Delay Reasons" sub="สาเหตุความล่าช้าที่บันทึกไว้">
          <BarRows
            empty="ยังไม่มีการบันทึกสาเหตุความล่าช้า"
            items={bars(countBy(s.jobs, (j) => j.reason), "#B42318", 8)}
          />
        </Panel>
        <Panel title="Transportation Cost by Subcontractor" sub="ต้นทุนค่าขนส่ง (THB)" right={DEMO_BADGE}>
          <BarRows items={costRows} />
        </Panel>
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px")}>
        <Panel title="Billing Status" sub="สถานะการวางบิล · KPI 4 วัน" right={DEMO_BADGE}>
          <BarRows items={bars(
            { "Within KPI": bill("Within KPI"), "Due Soon": bill("Due Soon"), Overdue: bill("Overdue"), "Not Due": bill("Not Due") },
            "#B45309", 4,
          )} />
        </Panel>
        {/*
          The real cases, by the stage they are sitting at. Seven stages, seven
          rows: the cap is the whole process rather than a top-six, because a
          pipeline with a stage silently missing from the middle reads as a
          shorter pipeline instead of an incomplete picture.
        */}
        <Panel title="CAR / PAR Status" sub="สถานะการแก้ไข/ป้องกัน · จาก Incident & CAR/PAR"
          right={carPar === null
            ? <span style={css("font-size:10px;color:#94A3B8")}>กำลังอ่าน…</span>
            : <span style={css("font-size:10px;color:#94A3B8")}>{carPar.length} เคส</span>}>
          <BarRows items={bars(byStage(carPar ?? []), "#0A2240", 7)} />
        </Panel>
      </div>

      <span style={css("font-size:11px;color:#94A3B8")}>
        แผงที่ติดป้าย DEMO DATA ยังใช้ข้อมูลจำลอง เพราะ ops.json ยังไม่มีค่าขนส่งและสถานะวางบิล — CAR/PAR อ่านจากเคสจริงในเมนู Incident &amp; CAR/PAR และตัวเลขอื่นทั้งหมดมาจากงานจริง {total} งาน
      </span>
    </div>
  );
}

/* ----------------------------------------------------------- operational */

function Operational({ s, period, onDrill }: {
  s: OpsStats; period: Period; onDrill: (patch: Drill) => void;
}) {
  /*
   * The same figures, on a wall.
   *
   * This was a tab of its own, showing open, running, delayed and
   * action-required, the team load and the delayed list — every one of which is
   * already below. What it actually is is a presentation: dark, large enough to
   * read across a room, and nothing to click because nobody is standing at it.
   *
   * As a mode it stops being a second place the same counts have to be kept
   * right, and the switch is where somebody already looking at those counts
   * would think to put the room's screen.
   */
  const [wall, setWall] = useState(false);
  // The plan runs on its own calendar, so "the day" is the busiest day in the
  // data rather than the wall-clock date, which would usually be outside it.
  const busiest = s.dates.reduce((a, b) => (s.dateCount[b] > (s.dateCount[a] ?? 0) ? b : a), s.dates[0] ?? "");
  const strip = s.dates.slice(Math.max(0, s.dates.indexOf(busiest) - 2), Math.max(0, s.dates.indexOf(busiest) - 2) + 7);

  const missing: [string, string, (j: Job) => boolean, string][] = [
    ["Licence missing", "ไม่มีทะเบียนรถ", (j) => j.cat !== "DELIVERY" && !j.licence, "#B45309"],
    ["Driver missing", "ไม่มีคนขับ", (j) => j.cat !== "DELIVERY" && !j.driver, "#B45309"],
    ["Contact missing", "ไม่มีเบอร์ติดต่อ", (j) => j.cat !== "DELIVERY" && !j.contact, "#B45309"],
    ["Container missing", "ไม่มีเลขตู้", (j) => j.cat !== "DELIVERY" && !j.container && !/6WH|4WH|10W|COMBINE/i.test(j.type || ""), "#B45309"],
    ["Arrival time missing", "ยังไม่ลงเวลาถึง", (j) => j.cat !== "DELIVERY" && !j.arrTime, "#1D5FA8"],
    ["Data error", "ข้อมูลผิดหรือไม่ครบ", (j) => j.issues.some((i) => i.severity === "error"), "#B42318"],
  ];
  const open = s.jobs.filter((j) => !RE.done.test(j.status));
  const missingRows: BarItem[] = missing.map(([label, th, test, colour]) => {
    const n = open.filter(test).length;
    return { label: label + " · " + th, value: String(n), pct: open.length ? (n / open.length) * 100 : 0, colour };
  });

  const operators = Object.keys(countBy(s.jobs, (j) => j.op)).sort((a, b) =>
    countBy(s.jobs, (j) => j.op)[b] - countBy(s.jobs, (j) => j.op)[a],
  );

  const delayedList = s.delayed.slice(0, 10);

  const modeSwitch = (
    <div style={css("display:flex;align-items:center;gap:9px;flex-wrap:wrap")}>
      <button type="button" onClick={() => setWall((was) => !was)}
        style={css("height:30px;padding:0 13px;border-radius:5px;font-size:12px;font-weight:600;"
          + "font-family:inherit;cursor:pointer;border:1px solid "
          + (wall ? "#0A2240;background:#0A2240;color:#fff" : "#C9D6E2;background:#fff;color:#0A2240"))}>
        {wall ? "← กลับไปมุมมองปกติ" : "จอแสดงผลหน้างาน · Wall display"}
      </button>
      <span style={css("font-size:11.5px;color:#94A3B8")}>
        {wall
          ? "ตัวเลขชุดเดียวกัน ขยายให้อ่านจากไกล — กดอะไรไม่ได้ตั้งใจ"
          : "ตัวเลขชุดเดียวกัน แบบขยายสำหรับจอติดผนัง"}
      </span>
    </div>
  );

  if (wall) {
    return (
      <div style={css("display:flex;flex-direction:column;gap:12px")}>
        {modeSwitch}
        <WallBoard s={s} period={period} />
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:16px")}>
      {modeSwitch}
      <Tiles items={[
        { label: "Open Jobs", th: "งานที่ยังไม่ปิด", value: String(open.length), note: "จาก " + s.jobs.length, colour: "#2E7DD1", go: () => onDrill({ tab: "PENDING" }) },
        { label: "Waiting Truck", th: "รอรถ", value: String(s.waiting.length), colour: "#475569", go: () => onDrill({ tab: "PENDING", kpi: "Wait" }) },
        { label: "In Operation", th: "กำลังปฏิบัติงาน", value: String(s.running.length), colour: "#0A6E8A", go: () => onDrill({ tab: "PENDING", kpi: "Run" }) },
        { label: "Delayed", th: "ล่าช้า", value: String(s.delayed.length), colour: "#B42318", go: () => onDrill({ tab: "DELAY", kpi: "Delay" }) },
        { label: "Action Required", th: "ต้องดำเนินการ", value: String(s.action.length), colour: "#B45309", go: () => onDrill({ tab: "PENDING", kpi: "Act" }) },
        { label: "Data Error", th: "ข้อมูลผิดหรือไม่ครบ", value: String(s.formatErrors.length), colour: "#B42318", go: () => onDrill({ tab: "PENDING", kpi: "Fmt" }) },
      ]} />

      <Panel title="Plan Days" sub="วันที่มีงานในแผน · คลิกเพื่อเปิดวันนั้นใน Workspace">
        <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(96px,1fr));gap:8px")}>
          {strip.map((d) => {
            const set = s.jobs.filter((j) => j.date === d);
            const late = set.filter((j) => RE.delayed.test(j.status)).length;
            return (
              <button
                key={d}
                type="button"
                onClick={() => onDrill({ tab: "PENDING", date: d })}
                style={css(
                  "font-family:inherit;text-align:left;border:1px solid " + (d === busiest ? "#2E7DD1" : "#E2E8F0") +
                  ";background:" + (d === busiest ? "#F4F8FC" : "#fff") + ";border-radius:4px;padding:10px 11px;cursor:pointer",
                )}
              >
                <div style={css("font-size:9.5px;color:#94A3B8;letter-spacing:.06em")}>{dowOf(d)}</div>
                <div style={css("font-size:14px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{d.slice(0, 5)}</div>
                <div style={css("font-size:10.5px;color:#475569;margin-top:4px")}>{set.length} jobs</div>
                <div style={css("font-size:10.5px;color:" + (late ? "#B42318" : "#94A3B8"))}>{late} delayed</div>
              </button>
            );
          })}
        </div>
      </Panel>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(330px,1fr));gap:14px")}>
        {(["IMPORT", "EXPORT", "DELIVERY"] as const).map((c) => {
          const set = s.jobs.filter((j) => j.cat === c);
          const ladder = STATUS_LADDER[c] ?? [];
          const counts = countBy(set, (j) => j.status);
          return (
            <Panel key={c} title={c + " Pipeline"} sub={set.length + " งาน · คลิกเพื่อกรองสถานะนั้นใน Workspace"}>
              <div style={css("display:flex;flex-direction:column;gap:6px")}>
                {ladder.filter((st) => (counts[st] || 0) > 0).map((st) => (
                  <button
                    key={st}
                    type="button"
                    title={`${st}${STATUS_TH[st] ? " · " + STATUS_TH[st] : ""} · ${counts[st]} งาน`
                      + (set.length ? ` · ${Math.round((counts[st] / set.length) * 100)}% ของงาน ${c} ${set.length} งาน` : "")}
                    onClick={() => onDrill({ tab: "PENDING", cat: c, status: st })}
                    style={css("font-family:inherit;text-align:left;display:flex;align-items:center;gap:10px;width:100%;border:none;background:none;padding:0;cursor:pointer")}
                  >
                    <span style={css("width:150px;flex:none;font-size:11.5px;color:#334155;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>
                      {st} <span style={css("color:#94A3B8")}>{STATUS_TH[st] ?? ""}</span>
                    </span>
                    <span style={css("flex:1;height:14px;background:#F1F5F9;border-radius:2px;overflow:hidden")}>
                      <span style={css("display:block;height:100%;border-radius:2px;background:" + CAT_COLOUR[c] + ";width:" + (set.length ? (counts[st] / set.length) * 100 : 0).toFixed(1) + "%")} />
                    </span>
                    <span style={css("width:42px;flex:none;text-align:right;font-size:11.5px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#0A2240")}>{counts[st]}</span>
                  </button>
                ))}
                {!set.length && <span style={css("font-size:11.5px;color:#94A3B8")}>ยังไม่มีงานประเภทนี้ในแผน</span>}
              </div>
            </Panel>
          );
        })}
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:14px")}>
        <Panel
          title="Missing Information"
          sub={"ข้อมูลที่ยังขาด · นับเฉพาะงานที่ยังไม่ปิด (" + open.length + " งาน)"}
          right={
            <button
              onClick={() => onDrill({ tab: "PENDING", kpi: "Act" })}
              style={css("height:28px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:11.5px;color:#475569;cursor:pointer")}
            >
              เปิดใน Workspace
            </button>
          }
        >
          <BarRows items={missingRows} />
        </Panel>

        <Panel title="Team Workload" sub="งานต่อผู้รับผิดชอบ · คลิกเพื่อดูงานของคนนั้น">
          <BarRows items={operators.map((name) => {
            const set = s.jobs.filter((j) => j.op === name);
            const late = set.filter((j) => RE.delayed.test(j.status)).length;
            return {
              label: name + (late ? " · " + late + " delayed" : ""),
              value: String(set.length),
              pct: s.jobs.length ? (set.length / Math.max(1, ...operators.map((o) => s.jobs.filter((j) => j.op === o).length))) * 100 : 0,
              colour: late ? "#B42318" : "#2E7DD1",
              go: () => onDrill({ tab: "PENDING" }),
            };
          })} />
        </Panel>
      </div>

      <Panel title="Delayed Jobs" sub="งานล่าช้าที่ต้องติดตาม" right={
        <button
          onClick={() => onDrill({ tab: "DELAY", kpi: "Delay" })}
          style={css("height:28px;padding:0 12px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:11.5px;color:#475569;cursor:pointer")}
        >
          ดูทั้งหมด {s.delayed.length}
        </button>
      }>
        {delayedList.length ? (
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px")}>
              <thead>
                <tr>
                  {["Date", "Category", "Customer", "Job / ABS", "Trucker", "Reason", "Owner"].map((h) => (
                    <th key={h} style={css("text-align:left;font-size:10px;color:#64748B;letter-spacing:.05em;padding:0 12px 7px 0;border-bottom:1px solid #E2E8F0;white-space:nowrap")}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {delayedList.map((j) => (
                  <tr key={j.key}>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid #F1F5F9;font-family:'IBM Plex Mono',monospace;white-space:nowrap")}>{j.date}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid #F1F5F9")}>{j.cat}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid #F1F5F9;font-weight:600")}>{j.customer}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid #F1F5F9;font-family:'IBM Plex Mono',monospace")}>{j.jobCode || j.abs || "—"}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid #F1F5F9")}>{j.trucker || "—"}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid #F1F5F9;color:#B45309")}>{j.reason || "—"}</td>
                    <td style={css("padding:7px 0;border-bottom:1px solid #F1F5F9")}>{j.op}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </ZoomBox>
        ) : (
          <span style={css("font-size:11.5px;color:#94A3B8")}>ไม่มีงานล่าช้าในแผนนี้</span>
        )}
      </Panel>
    </div>
  );
}

/* ------------------------------------------------------------ wall board */

function WallBoard({ s, period }: { s: OpsStats; period: Period }) {
  // Rendered empty on the server and filled on the client: the wall board is the
  // one place that shows the real clock, and a server-rendered time would not
  // match the browser's.
  const [clock, setClock] = useState("");
  useEffect(() => {
    const tick = () => {
      const now = new Date();
      setClock(pad(now.getHours()) + ":" + pad(now.getMinutes()));
    };
    tick();
    const timer = setInterval(tick, 30000);
    return () => clearInterval(timer);
  }, []);

  const open = s.jobs.filter((j) => !RE.done.test(j.status));
  const operators = Object.keys(countBy(s.jobs, (j) => j.op));
  const opMax = Math.max(1, ...operators.map((o) => s.jobs.filter((j) => j.op === o).length));

  const big: [string, string, number, string][] = [
    ["OPEN JOBS", "งานที่ยังไม่ปิด", open.length, "#9FD0FF"],
    ["IN OPERATION", "กำลังปฏิบัติงาน", s.running.length, "#7FE0C8"],
    ["DELAYED", "ล่าช้า", s.delayed.length, "#FF9C8F"],
    ["ACTION REQUIRED", "ต้องดำเนินการ", s.action.length, "#FFC978"],
    ["COMPLETED", "เสร็จสิ้น", s.done.length, "#8BE0A4"],
  ];

  return (
    <div style={css("background:#071A31;border-radius:6px;padding:26px 28px;display:flex;flex-direction:column;gap:22px;min-height:560px")}>
      <div style={css("display:flex;justify-content:space-between;align-items:flex-end;gap:16px;flex-wrap:wrap;border-bottom:1px solid #1D4570;padding-bottom:16px")}>
        <div>
          <div style={css("font-size:12px;letter-spacing:.16em;color:#7FA5CC;font-family:'IBM Plex Mono',monospace")}>SCMOS WALL BOARD</div>
          <div style={css("font-size:26px;font-weight:600;color:#fff;letter-spacing:-.02em")}>Operation Status · สถานะงานขนส่ง</div>
        </div>
        <div style={css("text-align:right")}>
          <div style={css("font-size:44px;font-weight:600;color:#fff;font-family:'IBM Plex Mono',monospace;line-height:1")}>{clock || "--:--"}</div>
          <div style={css("font-size:12px;color:#7FA5CC;font-family:'IBM Plex Mono',monospace")}>
            {s.jobs.length} jobs · {s.dates.length} operation days · {periodLabel(period)}
          </div>
        </div>
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:16px")}>
        {big.map(([label, th, value, colour]) => (
          <div key={label} style={css("background:#0C2743;border:1px solid #1D4570;border-radius:6px;padding:18px 20px")}>
            <div style={css("font-size:11.5px;letter-spacing:.1em;color:#7FA5CC;font-family:'IBM Plex Mono',monospace")}>{label}</div>
            <div style={css("font-size:11px;color:#5F87B0;margin-bottom:10px")}>{th}</div>
            <div style={css("font-size:56px;font-weight:600;line-height:1;font-family:'IBM Plex Mono',monospace;letter-spacing:-.03em;color:" + colour)}>{value}</div>
          </div>
        ))}
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:16px")}>
        <div style={css("background:#0C2743;border:1px solid #1D4570;border-radius:6px;padding:18px 20px")}>
          <div style={css("font-size:12px;letter-spacing:.1em;color:#7FA5CC;font-family:'IBM Plex Mono',monospace;margin-bottom:14px")}>TEAM LOAD · งานต่อคน</div>
          <div style={css("display:flex;flex-direction:column;gap:11px")}>
            {operators.map((name) => {
              const set = s.jobs.filter((j) => j.op === name);
              const late = set.filter((j) => RE.delayed.test(j.status)).length;
              return (
                <div
                  key={name}
                  title={`${name} · ${set.length} งาน`
                    + (late ? ` · ล่าช้า ${late} งาน` : " · ไม่มีงานล่าช้า")}
                  style={css("display:flex;align-items:center;gap:12px")}
                >
                  <span style={css("width:110px;flex:none;font-size:14px;color:#DCE8F4")}>{name}</span>
                  <span style={css("flex:1;height:14px;background:#123055;border-radius:3px;overflow:hidden")}>
                    <span style={css("display:block;height:100%;border-radius:3px;background:" + (late ? "#FF9C8F" : "#4E9BE8") + ";width:" + (set.length / opMax) * 100 + "%")} />
                  </span>
                  <span style={css("width:52px;flex:none;text-align:right;font-size:16px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:#fff")}>{set.length}</span>
                </div>
              );
            })}
          </div>
        </div>

        <div style={css("background:#0C2743;border:1px solid #1D4570;border-radius:6px;padding:18px 20px")}>
          <div style={css("font-size:12px;letter-spacing:.1em;color:#FF9C8F;font-family:'IBM Plex Mono',monospace;margin-bottom:14px")}>
            DELAYED · ล่าช้า ({s.delayed.length})
          </div>
          {s.delayed.length ? (
            <div style={css("display:flex;flex-direction:column;gap:10px")}>
              {s.delayed.slice(0, 6).map((j) => (
                <div key={j.key} style={css("display:flex;gap:12px;align-items:baseline;border-bottom:1px solid #123055;padding-bottom:8px")}>
                  <span style={css("font-family:'IBM Plex Mono',monospace;font-size:13px;color:#7FA5CC;flex:none")}>{j.date.slice(0, 5)}</span>
                  <span style={css("font-size:14px;color:#fff;font-weight:600;flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>{j.customer}</span>
                  <span style={css("font-size:12px;color:#FFC978;flex:1;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>{j.reason || j.status}</span>
                  <span style={css("font-size:12px;color:#7FA5CC;flex:none")}>{j.op}</span>
                </div>
              ))}
            </div>
          ) : (
            <span style={css("font-size:14px;color:#7FE0C8")}>ไม่มีงานล่าช้า ✓</span>
          )}
        </div>
      </div>

      <div style={css("display:flex;gap:26px;flex-wrap:wrap;font-family:'IBM Plex Mono',monospace;font-size:13px;color:#7FA5CC;margin-top:auto;border-top:1px solid #1D4570;padding-top:14px")}>
        <span>IMPORT <b style={css("color:#fff")}>{s.imports.length}</b></span>
        <span>EXPORT <b style={css("color:#fff")}>{s.exports.length}</b></span>
        <span>DELIVERY <b style={css("color:#fff")}>{s.deliveries.length}</b></span>
        <span>ON-TIME <b style={css("color:#fff")}>{s.otpPct}%</b></span>
        <span>FORMAT ERROR <b style={css("color:" + (s.formatErrors.length ? "#FF9C8F" : "#fff"))}>{s.formatErrors.length}</b></span>
      </div>
    </div>
  );
}
