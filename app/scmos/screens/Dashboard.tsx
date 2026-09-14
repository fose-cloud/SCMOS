"use client";

import { useEffect, useState, type ReactNode } from "react";
import { css, STATUS_LADDER, STATUS_TH } from "../theme";
import { ZoomBox } from "../TableFrame";
import type { WsTarget } from "../alerts";
import { opsStats, STATUS_RE as RE, type Job, type OpsStats } from "../ops";
import { periodLabel, type Period } from "../period";
import { dowOf, pad } from "../util";
import { PeriodBar } from "../PeriodBar";
import { FilterPickMany } from "../FilterPickMany";
import { ALL_DASHBOARD_FILTERS, dashboardOptions, filterDashboardJobs, type DashboardFilters } from "../dashboardFilters";
import { ControlTower } from "./ControlTower";

/**
 * The dashboard's two calculated tabs, on one navy canvas:
 *
 *   Executive   — the control tower: how the month is going, measured by the
 *                 API, with the operation read by status, day, customer and
 *                 haulier underneath. See ControlTower.
 *   Operational — what needs a person today: the plan day, the pipeline, the
 *                 jobs missing data, the delays, who is carrying what. Its
 *                 wall display is the same figures, larger.
 *
 * TODAY is drawn by SCMOSApp from the API's own answer and does not pass
 * through here. Everything below is computed from the real operation jobs, so
 * the figures agree with the Operation Workspace.
 */

/** What a click on a dashboard figure asks the workspace to show. */
export type Drill = WsTarget;

type Props = {
  /** Real operation jobs, already narrowed to the chosen period. */
  jobs: Job[];
  filters: DashboardFilters;
  onFilters: (filters: DashboardFilters) => void;
  /** Everything in the register, so the period pickers can offer every option. */
  allJobs: Job[];
  period: Period;
  onPeriod: (period: Period) => void;
  loaded: boolean;
  /** What to say while there is nothing to draw — see loadingNote in SCMOSApp. */
  note?: string;
  tab: string;
  userName: string;
  onDrill: (patch: Drill) => void;
  /** Opens another screen by id — the hero's tiles and the rail's findings. */
  onOpen: (screen: string) => void;
  onNewJob: () => void;
  onImport: () => void;
  onExport: () => void;
  /** Carries a question to the AI Control Tower's box. */
  onAsk: (question: string) => void;
  /** Opens the KPI screen, where the full scorecard is. */
  onOpenKpi?: () => void;
};

const CAT_COLOUR: Record<string, string> = { IMPORT: "#5cc0f7", EXPORT: "#3ddc97", DELIVERY: "#f2b13c" };

/* --------------------------------------------------------------- palette */

const BG = "#0c2338";
const LINE = "rgba(74,148,214,.2)";
const INK = "#eaf4fc";
const MUTED = "#8fb4d4";
const DIM = "#7c9fbd";
const TRACK = "#0f2c48";
const MONO = "'IBM Plex Mono',monospace";

/* --------------------------------------------------------------- helpers */

function Panel(p: { title: string; sub?: string; right?: ReactNode; children: ReactNode }) {
  return (
    <div style={css(`background:${BG};border:1px solid ${LINE};border-radius:8px;padding:14px 16px 16px`)}>
      <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:12px;margin-bottom:14px;flex-wrap:wrap")}>
        <div>
          <h3 style={css(`margin:0 0 2px;font-size:14px;font-weight:600;color:${INK}`)}>{p.title}</h3>
          {!!p.sub && <div style={css(`font-size:11px;color:${DIM}`)}>{p.sub}</div>}
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
    return <span style={css(`font-size:11.5px;color:${DIM}`)}>{empty ?? "ไม่มีข้อมูลในชุดนี้"}</span>;
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
          <span style={css("width:136px;flex:none;font-size:11.5px;color:#cfe3f4;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>{i.label}</span>
          <span style={css(`flex:1;height:12px;background:${TRACK};border-radius:6px;overflow:hidden`)}>
            <span style={css("display:block;height:100%;border-radius:6px;width:" + i.pct.toFixed(1) + "%;background:" + i.colour)} />
          </span>
          <span style={css(`width:58px;flex:none;text-align:right;font-size:11.5px;font-weight:600;font-family:${MONO};color:#fff`)}>{i.value}</span>
        </button>
      ))}
    </div>
  );
}

function countBy(jobs: Job[], pick: (j: Job) => string | undefined): Record<string, number> {
  const out: Record<string, number> = {};
  jobs.forEach((j) => {
    const key = (pick(j) || "").trim();
    if (key) out[key] = (out[key] || 0) + 1;
  });
  return out;
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
            `font-family:inherit;text-align:left;width:100%;background:${BG};border-top:3px solid ` + t.colour +
            `;border-right:1px solid ${LINE};border-bottom:1px solid ${LINE};border-left:1px solid ${LINE}` +
            ";border-radius:8px;padding:14px 15px 15px;cursor:" + (t.go ? "pointer" : "default"),
          )}
        >
          <div style={css("display:flex;justify-content:space-between;align-items:flex-start;gap:8px")}>
            <span style={css("display:flex;flex-direction:column;line-height:1.25")}>
              <span style={css(`font-family:${MONO};font-size:9.5px;font-weight:600;letter-spacing:.1em;color:${MUTED}`)}>{t.label.toUpperCase()}</span>
              <span style={css(`font-size:10.5px;color:${DIM}`)}>{t.th}</span>
            </span>
            <span style={css("width:8px;height:8px;border-radius:50%;flex:none;margin-top:3px;background:" + t.colour)} />
          </div>
          <div style={css("display:flex;align-items:baseline;gap:8px;margin-top:12px")}>
            <span style={css("font-size:27px;font-weight:700;color:#fff;letter-spacing:-.02em;font-variant-numeric:tabular-nums")}>{t.value}</span>
            {!!t.note && <span style={css(`font-size:11px;color:${DIM};font-family:${MONO}`)}>{t.note}</span>}
          </div>
        </button>
      ))}
    </div>
  );
}

const GHOST = `height:28px;padding:0 12px;border:1px solid rgba(74,148,214,.3);background:${TRACK};border-radius:6px;font-size:11.5px;color:#cfe3f4;cursor:pointer;font-family:inherit`;

/* ------------------------------------------------------------- dashboard */

export function Dashboard({ jobs, allJobs, filters, onFilters, period, onPeriod, loaded, note, tab, onDrill, onOpenKpi, ...p }: Props) {
  const s = opsStats(jobs);
  const total = s.jobs.length;

  if (!loaded) {
    return (
      <div style={css(`background:${BG};border:1px solid ${LINE};border-radius:8px;padding:34px;text-align:center;font-size:12.5px;color:${DIM}`)}>
        {note ?? "Loading operation data…"}
      </div>
    );
  }

  const dimensionsActive = filters.customer !== "ALL" || filters.trucker !== "ALL";
  /*
   * Each picker offers what the other one leaves possible — see
   * dashboardOptions. Both lists used to be built from the whole register, so a
   * customer and a haulier who have never worked together could both be chosen
   * and the dashboard would go blank with nothing saying why.
   */
  const options = (field: "customer" | "trucker") => dashboardOptions(allJobs, field, filters);
  /*
   * One row, not two: which jobs are we looking at, and over what period. Navy
   * now, because the canvas is — the white bar the department asked for was
   * white against a grey page, and this page is no longer grey.
   */
  const bar = (
    <PeriodBar
      tone="dark"
      allJobs={filterDashboardJobs(allJobs, filters)}
      shown={total}
      period={period}
      onPeriod={onPeriod}
      dimensions={<>
        <FilterPickMany label="CUSTOMER" value={filters.customer} options={options("customer")}
          onPick={customer => onFilters({ ...filters, customer })} tone="dark" />
        <FilterPickMany label="TRUCKER" value={filters.trucker} options={options("trucker")}
          onPick={trucker => onFilters({ ...filters, trucker })} tone="dark" />
        {dimensionsActive && <button type="button" onClick={() => onFilters(ALL_DASHBOARD_FILTERS)}
          style={css("border:1px solid #4E9BE8;background:#16406E;color:#fff;border-radius:4px;height:27px;padding:0 10px;font-size:11.5px;font-family:inherit;cursor:pointer")}>
          ล้าง CUSTOMER / TRUCKER
        </button>}
      </>}
    />
  );

  if (!total) {
    return (
      <div style={css("display:flex;flex-direction:column;gap:12px")}>
        {bar}
        <div style={css(`background:${BG};border:1px solid ${LINE};border-radius:8px;padding:34px;text-align:center;font-size:12.5px;color:${DIM}`)}>
          ไม่มีงานตรงกับตัวกรอง CUSTOMER / TRUCKER และช่วงเวลา “{periodLabel(period)}” — ล้างตัวกรองหรือเลือกช่วงเวลาอื่น
        </div>
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {bar}
      {tab === "Operational"
        ? <Operational s={s} period={period} onDrill={onDrill} />
        : <ControlTower
            s={s}
            allJobs={allJobs}
            period={period}
            onPeriod={onPeriod}
            // The measured cards are the API's, over the period alone — the
            // tower says "ไม่ได้กรองตาม CUSTOMER / TRUCKER" beside them while
            // either picker is narrowing everything else.
            dimensionsActive={dimensionsActive}
            userName={p.userName}
            onDrill={onDrill}
            onOpen={p.onOpen}
            onNewJob={p.onNewJob}
            onImport={p.onImport}
            onExport={p.onExport}
            onAsk={p.onAsk}
            onOpenKpi={onOpenKpi}
          />}
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
    ["Licence missing", "ไม่มีทะเบียนรถ", (j) => j.cat !== "DELIVERY" && !j.licence, "#f2b13c"],
    ["Driver missing", "ไม่มีคนขับ", (j) => j.cat !== "DELIVERY" && !j.driver, "#f2b13c"],
    ["Contact missing", "ไม่มีเบอร์ติดต่อ", (j) => j.cat !== "DELIVERY" && !j.contact, "#f2b13c"],
    ["Container missing", "ไม่มีเลขตู้", (j) => j.cat !== "DELIVERY" && !j.container && !/6WH|4WH|10W|COMBINE/i.test(j.type || ""), "#f2b13c"],
    ["Arrival time missing", "ยังไม่ลงเวลาถึง", (j) => j.cat !== "DELIVERY" && !j.arrTime, "#5cc0f7"],
    ["Data error", "ข้อมูลผิดหรือไม่ครบ", (j) => j.issues.some((i) => i.severity === "error"), "#ff7d86"],
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
          + (wall ? "#4E9BE8;background:#16406E;color:#fff" : "rgba(74,148,214,.3);background:#0f2c48;color:#cfe3f4"))}>
        {wall ? "← กลับไปมุมมองปกติ" : "จอแสดงผลหน้างาน · Wall display"}
      </button>
      <span style={css(`font-size:11.5px;color:${DIM}`)}>
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
        { label: "Open Jobs", th: "งานที่ยังไม่ปิด", value: String(open.length), note: "จาก " + s.jobs.length, colour: "#5cc0f7", go: () => onDrill({ tab: "PENDING" }) },
        { label: "Waiting Truck", th: "รอรถ", value: String(s.waiting.length), colour: "#f2b13c", go: () => onDrill({ tab: "PENDING", kpi: "Wait" }) },
        { label: "In Operation", th: "กำลังปฏิบัติงาน", value: String(s.running.length), colour: "#0A9AA8", go: () => onDrill({ tab: "PENDING", kpi: "Run" }) },
        { label: "Delayed", th: "ล่าช้า", value: String(s.delayed.length), colour: "#ff7d86", go: () => onDrill({ tab: "DELAY", kpi: "Delay" }) },
        { label: "Action Required", th: "ต้องดำเนินการ", value: String(s.action.length), colour: "#f07c2e", go: () => onDrill({ tab: "PENDING", kpi: "Act" }) },
        { label: "Data Error", th: "ข้อมูลผิดหรือไม่ครบ", value: String(s.formatErrors.length), colour: "#ff5f6b", go: () => onDrill({ tab: "PENDING", kpi: "Fmt" }) },
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
                  "font-family:inherit;text-align:left;border:1px solid " + (d === busiest ? "#3b9ee0" : "rgba(74,148,214,.2)") +
                  ";background:" + (d === busiest ? "#123f66" : "#0f2c48") + ";border-radius:6px;padding:10px 11px;cursor:pointer",
                )}
              >
                <div style={css(`font-size:9.5px;color:${DIM};letter-spacing:.06em`)}>{dowOf(d)}</div>
                <div style={css(`font-size:14px;font-weight:600;font-family:${MONO};color:#fff`)}>{d.slice(0, 5)}</div>
                <div style={css("font-size:10.5px;color:#cfe3f4;margin-top:4px")}>{set.length} jobs</div>
                <div style={css("font-size:10.5px;color:" + (late ? "#ff7d86" : DIM))}>{late} delayed</div>
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
                    <span style={css("width:150px;flex:none;font-size:11.5px;color:#cfe3f4;white-space:nowrap;overflow:hidden;text-overflow:ellipsis")}>
                      {st} <span style={css(`color:${DIM}`)}>{STATUS_TH[st] ?? ""}</span>
                    </span>
                    <span style={css(`flex:1;height:12px;background:${TRACK};border-radius:6px;overflow:hidden`)}>
                      <span style={css("display:block;height:100%;border-radius:6px;background:" + CAT_COLOUR[c] + ";width:" + (set.length ? (counts[st] / set.length) * 100 : 0).toFixed(1) + "%")} />
                    </span>
                    <span style={css(`width:42px;flex:none;text-align:right;font-size:11.5px;font-weight:600;font-family:${MONO};color:#fff`)}>{counts[st]}</span>
                  </button>
                ))}
                {!set.length && <span style={css(`font-size:11.5px;color:${DIM}`)}>ยังไม่มีงานประเภทนี้ในแผน</span>}
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
              style={css(GHOST)}
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
              colour: late ? "#ff7d86" : "#5cc0f7",
              go: () => onDrill({ tab: "PENDING" }),
            };
          })} />
        </Panel>
      </div>

      <Panel title="Delayed Jobs" sub="งานล่าช้าที่ต้องติดตาม" right={
        <button
          onClick={() => onDrill({ tab: "DELAY", kpi: "Delay" })}
          style={css(GHOST)}
        >
          ดูทั้งหมด {s.delayed.length}
        </button>
      }>
        {delayedList.length ? (
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:11.5px;color:#dce9f6")}>
              <thead>
                <tr>
                  {["Date", "Category", "Customer", "Job / ABS", "Trucker", "Reason", "Owner"].map((h) => (
                    <th key={h} style={css(`text-align:left;font-family:${MONO};font-size:9.5px;font-weight:600;color:${MUTED};letter-spacing:.08em;padding:0 12px 7px 0;border-bottom:1px solid ${LINE};white-space:nowrap`)}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {delayedList.map((j) => (
                  <tr key={j.key}>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid rgba(74,148,214,.1);font-family:'IBM Plex Mono',monospace;white-space:nowrap")}>{j.date}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid rgba(74,148,214,.1)")}>{j.cat}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid rgba(74,148,214,.1);font-weight:600")}>{j.customer}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid rgba(74,148,214,.1);font-family:'IBM Plex Mono',monospace")}>{j.jobCode || j.abs || "—"}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid rgba(74,148,214,.1)")}>{j.trucker || "—"}</td>
                    <td style={css("padding:7px 12px 7px 0;border-bottom:1px solid rgba(74,148,214,.1);color:#f2b13c")}>{j.reason || "—"}</td>
                    <td style={css("padding:7px 0;border-bottom:1px solid rgba(74,148,214,.1)")}>{j.op}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </ZoomBox>
        ) : (
          <span style={css(`font-size:11.5px;color:${DIM}`)}>ไม่มีงานล่าช้าในแผนนี้</span>
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
