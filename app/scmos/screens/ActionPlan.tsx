"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import {
  DEVELOPMENT_TH, NO_FILTERS, PRIORITY, STATUS, TARGET_TH, filterQuery, shownStatus,
  type Count, type Dashboard, type Filters, type Meta, type PlanRow,
} from "../actionPlan";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { ActionPlanDetail } from "./ActionPlanDetail";
import {
  Badge, CELL, EMPTY, HEAD, INPUT, LABEL, MONO, Notice, PANEL, PRIMARY, Progress, ProgressBar, SERIES_1, SERIES_2, SMALL, TITLE,
} from "./ActionPlanParts";
import { ActionPlanWizard } from "./ActionPlanWizard";

type View = { kind: "dashboard" } | { kind: "list" } | { kind: "new" } | { kind: "plan"; id: number };

/**
 * Subcontract Management Action Plan (1 Oct 2026): the department's development plans for its people and its
 * subcontractors — a dashboard, the list, a guided form for a new plan, and each plan's own page. What a
 * person may read is the API's to decide; this screen draws what it is sent.
 */
export function ActionPlan({ onToast }: { onToast: (message: string) => void }) {
  const [view, setView] = useState<View>({ kind: "dashboard" });
  const [meta, setMeta] = useState<Meta | null>(null);
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [failure, setFailure] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/action-plans/meta", { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as Meta & { error?: string } | null;
      if (cancelled) return;
      if (!response.ok || !body) setFailure(body?.error ?? `เปิด Action Plan ไม่ได้ (${response.status})`);
      else setMeta(body);
    })();
    return () => { cancelled = true; };
  }, []);

  if (failure) return <Notice tone="#B45309">{failure}</Notice>;
  if (!meta) return <Notice tone="#7B8CA0">กำลังโหลด…</Notice>;

  const open = (id: number) => setView({ kind: "plan", id });
  const tabs: [View["kind"], string][] = [["dashboard", "Dashboard"], ["list", "Action Plans"]];

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;gap:7px;flex-wrap:wrap;align-items:center")}>
        {tabs.map(([kind, label]) => (
          <button key={kind} onClick={() => setView({ kind } as View)} style={css(TAB + (view.kind === kind
            ? "border-color:#0A2240;background:#0A2240;color:#fff" : "border-color:#D3DBE3;background:#fff;color:#3F5265"))}>{label}</button>
        ))}
        {view.kind === "plan" && <span style={css("font-size:12px;color:#7B8CA0")}>› แผน</span>}
        {view.kind !== "plan" && (
          <label style={css("display:flex;align-items:center;gap:6px;font-size:12px;color:#5A6B7D;margin-left:8px")}>ปี
            <select aria-label="ปีของแผน" value={year} onChange={(e) => setYear(e.target.value)} style={css(INPUT)}>
              <option value="">ทุกปี</option>
              {[-1, 0, 1].map((step) => String(new Date().getFullYear() + step)).map((one) => <option key={one} value={one}>{one}</option>)}
            </select>
          </label>
        )}
        {meta.canEdit && <button onClick={() => setView({ kind: "new" })} style={css("margin-left:auto;" + PRIMARY)}>+ New Action Plan</button>}
      </div>

      {view.kind === "dashboard" && <DashboardView year={year} onOpenList={() => setView({ kind: "list" })} />}
      {view.kind === "list" && <ListView year={year} meta={meta} onOpen={open} />}
      {view.kind === "new" && <ActionPlanWizard meta={meta} onToast={onToast} onCreated={open} onCancel={() => setView({ kind: "dashboard" })} />}
      {view.kind === "plan" && <ActionPlanDetail id={view.id} meta={meta} onToast={onToast} onBack={() => setView({ kind: "list" })} />}
    </div>
  );
}

/* ------------------------------------------------------------------ dashboard */

function DashboardView({ year, onOpenList }: { year: string; onOpenList: () => void }) {
  const [data, setData] = useState<Dashboard | null>(null);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch(`/api/action-plans/dashboard${year ? `?year=${year}` : ""}`, { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Dashboard : null;
      if (!cancelled) setData(body);
    })();
    return () => { cancelled = true; };
  }, [year]);
  if (!data) return <Notice tone="#7B8CA0">กำลังโหลด Dashboard…</Notice>;

  const cards: [string, number, string][] = [
    ["แผนทั้งหมด", data.total, "#0A2240"], ["กำลังดำเนินการ", data.active, "#0A5C97"], ["เสร็จแล้ว", data.completed, "#16794C"],
    ["เลยกำหนด", data.overdue, data.overdue ? "#B42318" : "#0A2240"], ["ครบกำหนดเดือนนี้", data.dueThisMonth, "#B45309"],
    ["People Development", data.people, "#0A2240"], ["Carrier Development", data.carrier, "#0A2240"],
  ];
  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:9px")}>
        {cards.map(([label, value, tone]) => (
          <button key={label} onClick={onOpenList} style={css("text-align:left;background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:11px 13px;cursor:pointer")}>
            <div style={css("font-size:10.5px;letter-spacing:.04em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
            <div style={css("font-size:24px;font-weight:700;margin-top:3px;color:" + tone)}>{value}</div>
          </button>
        ))}
      </div>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(300px,1fr));gap:12px")}>
        <Bars title="Action Plan by Status" rows={data.byStatus} label={(key) => STATUS[key]?.label ?? key} tone={(key) => STATUS[key]?.tone ?? SERIES_1} />
        <Bars title="Action Plan by Category" rows={data.byCategory} />
        <Bars title="Action Plan by Priority" rows={data.byPriority} label={(key) => PRIORITY[key]?.label ?? key} />
        <Bars title="Action Plan by Owner" rows={data.byOwner.slice(0, 10)} />
        <Monthly rows={data.monthly} />
        <div style={css(PANEL + "display:flex;flex-direction:column;gap:12px")}>
          <div style={css(TITLE)}>ความคืบหน้าเฉลี่ย</div>
          <Progress label="Employee Development" value={data.peopleProgress} />
          <Progress label="Carrier Development" value={data.carrierProgress} />
        </div>
      </div>
    </div>
  );
}

/** A single-series bar list, one hue for magnitude; a state breakdown keeps each state's own colour and label. */
function Bars({ title, rows, label = (key) => key, tone }: {
  title: string; rows: Count[]; label?: (key: string) => string; tone?: (key: string) => string;
}) {
  const most = Math.max(1, ...rows.map((row) => row.count));
  return (
    <div style={css(PANEL)}>
      <div style={css(TITLE + "margin-bottom:9px")}>{title}</div>
      {rows.length === 0 && <div style={css("font-size:12px;color:#94A3B8")}>ยังไม่มีแผน</div>}
      <div style={css("display:flex;flex-direction:column;gap:6px")}>
        {rows.map((row) => (
          <div key={row.key} title={`${label(row.key)}: ${row.count} แผน`} style={css("display:grid;grid-template-columns:minmax(90px,38%) 1fr 32px;gap:8px;align-items:center;font-size:12px")}>
            <span style={css("color:#334155;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>{label(row.key)}</span>
            <span style={css("height:12px;background:#F1F5F9;border-radius:0 4px 4px 0;overflow:hidden")}>
              <span style={css(`display:block;height:100%;width:${(row.count / most) * 100}%;background:${tone?.(row.key) ?? SERIES_1};border-radius:0 4px 4px 0`)} />
            </span>
            <span style={css("text-align:right;color:#0F2B46;font-weight:650;font-family:ui-monospace,monospace")}>{row.count}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/** Completed against due, month by month — two series, a legend, one axis. */
function Monthly({ rows }: { rows: Dashboard["monthly"] }) {
  const most = Math.max(1, ...rows.flatMap((row) => [row.completed, row.due]));
  return (
    <div style={css(PANEL)}>
      <div style={css("display:flex;gap:12px;align-items:center;margin-bottom:9px;flex-wrap:wrap")}>
        <span style={css(TITLE)}>Monthly Completion Trend</span>
        <Legend colour={SERIES_1}>เสร็จ</Legend>
        <Legend colour={SERIES_2}>ครบกำหนด</Legend>
      </div>
      <div style={css("display:grid;grid-template-columns:repeat(12,1fr);gap:4px;align-items:end;height:120px;border-bottom:1px solid #D8E0E8")}>
        {rows.map((row) => (
          <div key={row.month} title={`${row.month}: เสร็จ ${row.completed} · ครบกำหนด ${row.due}`} style={css("display:flex;gap:2px;align-items:flex-end;height:100%;justify-content:center")}>
            <span style={css(`width:40%;height:${(row.completed / most) * 100}%;min-height:${row.completed ? 2 : 0}px;background:${SERIES_1};border-radius:4px 4px 0 0`)} />
            <span style={css(`width:40%;height:${(row.due / most) * 100}%;min-height:${row.due ? 2 : 0}px;background:${SERIES_2};border-radius:4px 4px 0 0`)} />
          </div>
        ))}
      </div>
      <div style={css("display:grid;grid-template-columns:repeat(12,1fr);gap:4px;margin-top:4px")}>
        {rows.map((row) => <span key={row.month} style={css("font-size:9.5px;color:#7B8CA0;text-align:center")}>{row.month.slice(0, 2)}</span>)}
      </div>
    </div>
  );
}

function Legend({ colour, children }: { colour: string; children: React.ReactNode }) {
  return <span style={css("display:flex;align-items:center;gap:5px;font-size:11px;color:#5A6B7D")}>
    <span style={css(`width:10px;height:10px;border-radius:2px;background:${colour}`)} />{children}
  </span>;
}

/* ----------------------------------------------------------------------- list */

function ListView({ year, meta, onOpen }: { year: string; meta: Meta; onOpen: (id: number) => void }) {
  const [filters, setFilters] = useState<Filters>({ ...NO_FILTERS, year });
  const [rows, setRows] = useState<PlanRow[] | null>(null);
  const [carriers, setCarriers] = useState<{ id: number; name: string }[]>([]);

  const load = useCallback(async () => {
    const response = await apiFetch(`/api/action-plans${filterQuery(filters)}`, { headers: { accept: "application/json" } });
    setRows(response.ok ? await response.json() as PlanRow[] : []);
  }, [filters]);
  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers?status=approved", { headers: { accept: "application/json" } });
      const list = response.ok ? await response.json() as { id: number; name: string }[] : [];
      if (!cancelled) setCarriers(list.map((one) => ({ id: one.id, name: one.name })));
    })();
    return () => { cancelled = true; };
  }, []);

  const set = (key: keyof Filters) => (event: { target: { value: string } }) => setFilters({ ...filters, [key]: event.target.value });
  const categories = meta.types.filter((type) => !filters.developmentType || type.developmentType === filters.developmentType);

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css(PANEL + "display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
        <Pick label="ปี" value={filters.year} onChange={set("year")} options={[["", "ทุกปี"], ...[-1, 0, 1].map((step) => String(new Date().getFullYear() + step)).map((one) => [one, one] as [string, string])]} />
        <Pick label="ไตรมาส" value={filters.quarter} onChange={set("quarter")} options={[["", "ทั้งหมด"], ["1", "Q1"], ["2", "Q2"], ["3", "Q3"], ["4", "Q4"]]} />
        <Pick label="เดือน" value={filters.month} onChange={set("month")} options={[["", "ทั้งหมด"], ...Array.from({ length: 12 }, (_, index) => [String(index + 1), String(index + 1).padStart(2, "0")] as [string, string])]} />
        <Pick label="Type" value={filters.developmentType} onChange={set("developmentType")} options={[["", "ทั้งหมด"], ["people", "People"], ["subcontractor", "Subcontractor"]]} />
        <Pick label="Category" value={filters.category} onChange={set("category")} options={[["", "ทั้งหมด"], ...categories.map((type) => [type.name, type.name] as [string, string])]} />
        <Pick label="Owner" value={filters.ownerId} onChange={set("ownerId")} options={[["", "ทั้งหมด"], ...meta.people.map((person) => [person.id, person.name] as [string, string])]} />
        <Pick label="Employee" value={filters.employeeId} onChange={set("employeeId")} options={[["", "ทั้งหมด"], ...meta.people.map((person) => [person.id, person.name] as [string, string])]} />
        <Pick label="Carrier" value={filters.supplierId} onChange={set("supplierId")} options={[["", "ทั้งหมด"], ...carriers.map((one) => [String(one.id), one.name] as [string, string])]} />
        <Pick label="Status" value={filters.status} onChange={set("status")} options={[["", "ทั้งหมด"], ...Object.entries(STATUS).map(([key, value]) => [key, value.label] as [string, string])]} />
        <Pick label="Priority" value={filters.priority} onChange={set("priority")} options={[["", "ทั้งหมด"], ...Object.entries(PRIORITY).map(([key, value]) => [key, value.label] as [string, string])]} />
        <label style={css("display:flex;flex-direction:column;gap:3px;flex:1;min-width:180px")}>
          <span style={css(LABEL)}>ค้นหา</span>
          <input aria-label="ค้นหาแผน" value={filters.query} onChange={set("query")} placeholder="เลขแผน ชื่อแผน เป้าหมาย เจ้าของ" style={css(INPUT)} />
        </label>
      </div>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:1100px")}>
            <thead><tr>{["Plan No.", "Title", "Type", "Target", "Owner", "Start", "Due", "Progress", "Status", "Priority", "Updated", ""].map((head) => (
              <th key={head} style={css(HEAD)}>{head}</th>))}</tr></thead>
            <tbody>
              {rows === null && <tr><td colSpan={12} style={css(EMPTY)}>กำลังโหลด…</td></tr>}
              {rows?.length === 0 && <tr><td colSpan={12} style={css(EMPTY)}>ยังไม่มีแผนตามเงื่อนไขนี้</td></tr>}
              {rows?.map((row) => {
                const status = STATUS[shownStatus(row)] ?? STATUS.draft;
                const priority = PRIORITY[row.priority] ?? PRIORITY.medium;
                return (
                  <tr key={row.id} onClick={() => onOpen(row.id)} style={css("cursor:pointer")}>
                    <td style={css(CELL + "font-family:ui-monospace,monospace;white-space:nowrap")}>{row.number}</td>
                    <td style={css(CELL + "font-weight:600;color:#0A2240;min-width:200px")}>{row.title}<div style={css("font-size:10.5px;color:#7B8CA0;font-weight:400")}>{row.category}</div></td>
                    <td style={css(CELL)}>{DEVELOPMENT_TH[row.developmentType]?.replace(" Development", "")}</td>
                    <td style={css(CELL)}>{row.targetName}<div style={css("font-size:10.5px;color:#7B8CA0")}>{TARGET_TH[row.targetType]}</div></td>
                    <td style={css(CELL)}>{row.ownerName}</td>
                    <td style={css(CELL + MONO)}>{row.startDate || "—"}</td>
                    <td style={css(CELL + MONO + (row.overdue ? "color:#B42318;font-weight:700" : ""))}>{row.targetDate}</td>
                    <td style={css(CELL + "min-width:110px")}><ProgressBar value={row.progress} /></td>
                    <td style={css(CELL)}><Badge {...status} /></td>
                    <td style={css(CELL)}><Badge {...priority} /></td>
                    <td style={css(CELL + MONO + "color:#7B8CA0")}>{new Date(row.updatedAt).toLocaleDateString("en-GB")}</td>
                    <td style={css(CELL)}><button onClick={(e) => { e.stopPropagation(); onOpen(row.id); }} style={css(SMALL)}>View</button></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}

function Pick({ label, value, onChange, options }: {
  label: string; value: string; onChange: (event: { target: { value: string } }) => void; options: [string, string][];
}) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}>
    <span style={css(LABEL)}>{label}</span>
    <select aria-label={label} value={value} onChange={onChange} style={css(INPUT + "max-width:170px")}>
      {options.map(([key, text]) => <option key={key} value={key}>{text}</option>)}
    </select>
  </label>;
}

const TAB = "height:33px;padding:0 15px;border:1px solid;border-radius:5px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit;";
