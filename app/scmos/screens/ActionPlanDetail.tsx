"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import {
  DEVELOPMENT_TH, MOVES, PERIOD_TH, PRIORITY, REFERENCE_KINDS, SCORE_TH, STATUS, TARGET_TH, shownStatus,
  type Meta, type PlanDetail, type PlanItem,
} from "../actionPlan";
import { isoDay, registerDate } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { Badge, CELL, EMPTY, HEAD, INPUT, LABEL, MONO, Notice, OUTLINE, PANEL, PRIMARY, ProgressBar, SAVE, SMALL, TITLE } from "./ActionPlanParts";

type Tab = "overview" | "items" | "development" | "progress" | "evidence" | "review" | "history";
const TABS: [Tab, string][] = [
  ["overview", "Overview"], ["items", "Action Items"], ["development", "Development"], ["progress", "Progress"],
  ["evidence", "Evidence"], ["review", "Review"], ["history", "Audit History"],
];
type HistoryRow = { id: number; at: string; who: string; action: string; field: string; oldValue: string; newValue: string; reason: string };
type ItemForm = Record<keyof Omit<PlanItem, "id" | "planId" | "sequence" | "ownerName" | "progress">, string> & { progress: string };

const BLANK_ITEM: ItemForm = {
  action: "", description: "", ownerId: "", supportingPerson: "", supportingDepartment: "", startDate: "", targetDate: "",
  actualCompletionDate: "", priority: "medium", status: "planned", progress: "0", expectedResult: "", actualResult: "", remark: "",
  trainingTitle: "", trainingType: "", trainer: "", trainingProvider: "", trainingDate: "", participants: "", certificateExpiry: "",
};
const DATE_FIELDS = ["startDate", "targetDate", "actualCompletionDate", "trainingDate", "certificateExpiry"] as const;

/**
 * One Action Plan's page (1 Oct 2026): its state and progress, the workflow's next moves, and seven tabs —
 * the plan, its steps, the development itself (with a subcontractor's scores and the records it refers to),
 * the progress history, evidence, review, and its audit trail.
 */
export function ActionPlanDetail({ id, meta, onToast, onBack }: {
  id: number; meta: Meta; onToast: (message: string) => void; onBack: () => void;
}) {
  const [detail, setDetail] = useState<PlanDetail | null>(null);
  const [failure, setFailure] = useState("");
  const [tab, setTab] = useState<Tab>("overview");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch(`/api/action-plans/${id}`, { headers: { accept: "application/json" } });
    const body = await response.json().catch(() => null) as PlanDetail & { error?: string } | null;
    if (!response.ok || !body) { setFailure(body?.error ?? `เปิดแผนไม่ได้ (${response.status})`); return; }
    setDetail(body); setFailure("");
  }, [id]);
  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  /** Sends one change; the toast says how it went and the page is read again. */
  const send = useCallback(async (path: string, method: string, body?: unknown | FormData): Promise<boolean> => {
    if (busy) return false;
    setBusy(true);
    try {
      const form = body instanceof FormData;
      const response = await apiFetch(path, {
        method, headers: form || body === undefined ? undefined : { "content-type": "application/json" },
        body: body === undefined ? undefined : form ? body : JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (response.ok) await load();
      return response.ok;
    } finally { setBusy(false); }
  }, [busy, load, onToast]);

  if (failure) return <Notice tone="#B45309">{failure} <button onClick={onBack} style={css(SMALL + "margin-left:8px")}>กลับไปรายการ</button></Notice>;
  if (!detail) return <Notice tone="#7B8CA0">กำลังโหลดแผน…</Notice>;

  const plan = detail.plan;
  const status = STATUS[shownStatus({ status: plan.status, overdue: detail.overdue })] ?? STATUS.draft;
  const base = `/api/action-plans/${id}`;
  const open = plan.status !== "completed" && plan.status !== "cancelled";

  async function move(to: string) {
    const reason = to === "pending-review" ? window.prompt("ข้อความถึงผู้ Review (ถ้ามี)") ?? "" : "";
    await send(`${base}/status`, "POST", { status: to, reason });
  }
  async function cancel() {
    const reason = window.prompt("เหตุผลที่ยกเลิกแผนนี้");
    if (reason && reason.trim()) await send(`${base}/status`, "POST", { status: "cancelled", reason });
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css(PANEL + "display:flex;flex-direction:column;gap:9px")}>
        <div style={css("display:flex;gap:10px;align-items:center;flex-wrap:wrap")}>
          <button onClick={onBack} style={css(SMALL)}>‹ รายการ</button>
          <span style={css("font-family:ui-monospace,monospace;font-size:12px;color:#5A6B7D")}>{plan.number}</span>
          <Badge {...status} />
          <Badge {...(PRIORITY[plan.priority] ?? PRIORITY.medium)} />
          <span style={css("font-size:11.5px;color:#7B8CA0")}>{DEVELOPMENT_TH[plan.developmentType]} · {plan.category || "—"}</span>
        </div>
        <div style={css("font-size:17px;font-weight:700;color:#0A2240")}>{plan.title}</div>
        <div style={css("display:flex;gap:18px;flex-wrap:wrap;align-items:center;font-size:12px;color:#334155")}>
          <span>{TARGET_TH[plan.targetType]}: <b>{plan.targetType === "employee" ? plan.employeeName : detail.supplierName || plan.targetName}</b></span>
          <span>เจ้าของ: <b>{plan.ownerName}</b></span>
          <span>{plan.startDate || "—"} → <b style={css(detail.overdue ? "color:#B42318" : "")}>{plan.targetDate}</b></span>
          <span style={css("min-width:180px;flex:1;max-width:320px")}><ProgressBar value={detail.progress} /></span>
        </div>
        {detail.canEdit && open && (
          <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
            {(MOVES[plan.status] ?? []).map((one) => <button key={one.to} disabled={busy} onClick={() => void move(one.to)} style={css(PRIMARY)}>{one.label}</button>)}
            <button disabled={busy} onClick={() => void cancel()} style={css(OUTLINE + "color:#B42318;border-color:#F3C9C4")}>ยกเลิกแผน</button>
          </div>
        )}
        {plan.status === "cancelled" && plan.cancelReason && <div style={css("font-size:12px;color:#B42318")}>ยกเลิก: {plan.cancelReason}</div>}
      </div>

      <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
        {TABS.map(([key, label]) => (
          <button key={key} onClick={() => setTab(key)} style={css("height:30px;padding:0 13px;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;border:1px solid "
            + (tab === key ? "#0A2240;background:#0A2240;color:#fff" : "#D3DBE3;background:#fff;color:#3F5265"))}>
            {label}{key === "items" ? ` ${detail.items.filter((item) => item.status !== "cancelled").length}` : key === "evidence" ? ` ${detail.evidence.length}` : ""}
          </button>
        ))}
      </div>

      {tab === "overview" && <Overview detail={detail} meta={meta} busy={busy} onSave={(body) => send(base, "PUT", body)} />}
      {tab === "items" && <Items detail={detail} meta={meta} busy={busy} send={send} base={base} />}
      {tab === "development" && <Development detail={detail} meta={meta} busy={busy} send={send} base={base} />}
      {tab === "progress" && <ProgressTab detail={detail} busy={busy} send={send} base={base} />}
      {tab === "evidence" && <EvidenceTab detail={detail} busy={busy} send={send} />}
      {tab === "review" && <ReviewTab detail={detail} busy={busy} send={send} base={base} />}
      {tab === "history" && <History id={id} />}
    </div>
  );
}

type Send = (path: string, method: string, body?: unknown | FormData) => Promise<boolean>;

/* ------------------------------------------------------------------ overview */

/** The plan as it stands, and — for whoever may work it — the same fields as a form. */
function Overview({ detail, meta, busy, onSave }: { detail: PlanDetail; meta: Meta; busy: boolean; onSave: (body: unknown) => Promise<boolean> }) {
  const plan = detail.plan;
  const [form, setForm] = useState<Record<string, string> | null>(null);
  const editable = detail.canEdit && plan.status !== "completed" && plan.status !== "cancelled";
  const begin = () => setForm({
    title: plan.title, category: plan.category, priority: plan.priority, period: plan.period, objective: plan.objective,
    expectedOutcome: plan.expectedOutcome, description: plan.description, ownerId: plan.ownerId, startDate: isoDay(plan.startDate),
    targetDate: isoDay(plan.targetDate), metric: plan.metric, baseline: plan.baseline?.toString() ?? "",
    targetValue: plan.targetValue?.toString() ?? "", actualValue: plan.actualValue?.toString() ?? "",
  });
  async function save() {
    if (!form) return;
    const number = (value: string) => (value.trim() === "" ? null : Number(value));
    const body = {
      ...plan, ...form, startDate: registerDate(form.startDate), targetDate: registerDate(form.targetDate),
      baseline: number(form.baseline), targetValue: number(form.targetValue), actualValue: number(form.actualValue), items: null,
    };
    if (await onSave(body)) setForm(null);
  }
  if (form) {
    const set = (key: string) => (event: { target: { value: string } }) => setForm({ ...form, [key]: event.target.value });
    return (
      <div style={css(PANEL + "display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
        <Field label="ชื่อแผน"><input aria-label="ชื่อแผน" value={form.title} onChange={set("title")} style={css(INPUT + "width:360px")} /></Field>
        <Field label="Category">
          <select aria-label="Category" value={form.category} onChange={set("category")} style={css(INPUT)}>
            {meta.types.filter((type) => type.developmentType === plan.developmentType && (type.active || type.name === form.category))
              .map((type) => <option key={type.id} value={type.name}>{type.name}</option>)}
          </select>
        </Field>
        <Field label="ความสำคัญ">
          <select aria-label="ความสำคัญ" value={form.priority} onChange={set("priority")} style={css(INPUT)}>
            {Object.entries(PRIORITY).map(([key, value]) => <option key={key} value={key}>{value.label}</option>)}
          </select>
        </Field>
        <Field label="เจ้าของแผน">
          <select aria-label="เจ้าของแผน" value={form.ownerId} onChange={set("ownerId")} style={css(INPUT)}>
            {meta.people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
          </select>
        </Field>
        <Field label="วันเริ่ม"><input type="date" aria-label="วันเริ่ม" value={form.startDate} onChange={set("startDate")} style={css(INPUT)} /></Field>
        <Field label="วันครบกำหนด"><input type="date" aria-label="วันครบกำหนด" value={form.targetDate} onChange={set("targetDate")} style={css(INPUT)} /></Field>
        <Field label="วัตถุประสงค์"><input aria-label="วัตถุประสงค์" value={form.objective} onChange={set("objective")} style={css(INPUT + "width:320px")} /></Field>
        <Field label="ผลที่คาดหวัง"><input aria-label="ผลที่คาดหวัง" value={form.expectedOutcome} onChange={set("expectedOutcome")} style={css(INPUT + "width:320px")} /></Field>
        <Field label="รายละเอียด"><input aria-label="รายละเอียด" value={form.description} onChange={set("description")} style={css(INPUT + "width:320px")} /></Field>
        <Field label="Metric"><input aria-label="Metric" value={form.metric} onChange={set("metric")} style={css(INPUT)} /></Field>
        <Field label="Baseline"><input aria-label="Baseline" value={form.baseline} onChange={set("baseline")} style={css(INPUT + "width:90px")} /></Field>
        <Field label="Target"><input aria-label="Target" value={form.targetValue} onChange={set("targetValue")} style={css(INPUT + "width:90px")} /></Field>
        <Field label="Actual"><input aria-label="Actual" value={form.actualValue} onChange={set("actualValue")} style={css(INPUT + "width:90px")} /></Field>
        <button disabled={busy} onClick={() => void save()} style={css(SAVE)}>บันทึก</button>
        <button onClick={() => setForm(null)} style={css(OUTLINE)}>ยกเลิก</button>
      </div>
    );
  }
  const improvement = plan.baseline !== null && plan.actualValue !== null ? plan.actualValue - plan.baseline : null;
  return (
    <div style={css(PANEL + "display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px 18px")}>
      <Fact label="รอบแผน">{PERIOD_TH[plan.period]} · {plan.year}{plan.quarter ? ` Q${plan.quarter}` : ""}{plan.month ? ` / ${String(plan.month).padStart(2, "0")}` : ""}</Fact>
      <Fact label="แผนก">{plan.department}</Fact>
      <Fact label="วัตถุประสงค์">{plan.objective || "—"}</Fact>
      <Fact label="ผลที่คาดหวัง">{plan.expectedOutcome || "—"}</Fact>
      <Fact label="รายละเอียด">{plan.description || "—"}</Fact>
      <Fact label="ตัวชี้วัด">{plan.metric ? `${plan.metric}: ${plan.baseline ?? "—"} → ${plan.targetValue ?? "—"} · จริง ${plan.actualValue ?? "—"}${improvement !== null ? ` (${improvement >= 0 ? "+" : ""}${improvement})` : ""}` : "—"}</Fact>
      <Fact label="เสร็จจริง">{plan.actualCompletionDate || "—"}</Fact>
      <Fact label="สร้างโดย">{plan.createdBy} · {new Date(plan.createdAt).toLocaleDateString("en-GB")}</Fact>
      <Fact label="แก้ไขล่าสุด">{plan.updatedBy} · {new Date(plan.updatedAt).toLocaleString("en-GB")}</Fact>
      {editable && <div><button onClick={begin} style={css(OUTLINE)}>แก้ไขแผน</button></div>}
    </div>
  );
}

/* --------------------------------------------------------------------- items */

function Items({ detail, meta, busy, send, base }: { detail: PlanDetail; meta: Meta; busy: boolean; send: Send; base: string }) {
  const [form, setForm] = useState<{ id: number | null; values: ItemForm } | null>(null);
  const editable = detail.canEdit && detail.plan.status !== "completed" && detail.plan.status !== "cancelled";
  const edit = (item: PlanItem) => setForm({ id: item.id, values: {
    ...Object.fromEntries(Object.keys(BLANK_ITEM).map((key) => [key, String((item as unknown as Record<string, unknown>)[key] ?? "")])) as ItemForm,
    ...Object.fromEntries(DATE_FIELDS.map((key) => [key, isoDay(item[key])])),
  } });
  async function save() {
    if (!form) return;
    const body = { ...form.values, progress: Number(form.values.progress) || 0,
      ...Object.fromEntries(DATE_FIELDS.map((key) => [key, registerDate(form.values[key])])) };
    if (await send(form.id === null ? `${base}/items` : `${base}/items/${form.id}`, form.id === null ? "POST" : "PUT", body)) setForm(null);
  }
  const quick = (item: PlanItem, progress: number) => send(`${base}/progress`, "POST", { itemId: item.id, progress, comment: `ขั้นที่ ${item.sequence} → ${progress}%` });
  return (
    <div style={css("display:flex;flex-direction:column;gap:10px")}>
      {form && <ItemEditor form={form.values} meta={meta} busy={busy} onChange={(values) => setForm({ ...form, values })} onSave={() => void save()} onCancel={() => setForm(null)} />}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:980px")}>
            <thead><tr>{["#", "Action", "Owner", "Target", "Status", "Progress", "Expected / Actual", ""].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {detail.items.length === 0 && <tr><td colSpan={8} style={css(EMPTY)}>ยังไม่มีขั้นตอน</td></tr>}
              {detail.items.map((item) => (
                <tr key={item.id} style={css(item.status === "cancelled" ? "opacity:.5" : "")}>
                  <td style={css(CELL + "text-align:right;color:#7B8CA0")}>{item.sequence}</td>
                  <td style={css(CELL + "min-width:220px")}><b style={css("color:#0A2240")}>{item.action}</b>
                    {item.trainingTitle && <div style={css("font-size:10.5px;color:#6D28D9")}>Training: {item.trainingTitle}{item.trainingType ? ` · ${item.trainingType}` : ""}{item.trainingDate ? ` · ${item.trainingDate}` : ""}</div>}
                    {item.description && <div style={css("font-size:11px;color:#5A6B7D")}>{item.description}</div>}</td>
                  <td style={css(CELL)}>{item.ownerName || "—"}{item.supportingPerson && <div style={css("font-size:10.5px;color:#7B8CA0")}>+ {item.supportingPerson}</div>}</td>
                  <td style={css(CELL + MONO)}>{item.targetDate || "—"}</td>
                  <td style={css(CELL)}><Badge {...(STATUS[item.status] ?? STATUS.planned)} /></td>
                  <td style={css(CELL + "min-width:150px")}>
                    <ProgressBar value={item.status === "completed" ? 100 : item.progress} />
                    {editable && item.status !== "completed" && item.status !== "cancelled" && (
                      <div style={css("display:flex;gap:3px;margin-top:4px")}>
                        {[0, 25, 50, 75, 100].map((value) => <button key={value} disabled={busy || value === item.progress} onClick={() => void quick(item, value)}
                          style={css(SMALL + "height:20px;padding:0 5px;font-size:10px")}>{value}</button>)}
                      </div>
                    )}
                  </td>
                  <td style={css(CELL + "font-size:11.5px;min-width:180px")}>{item.expectedResult || "—"}{item.actualResult && <div style={css("color:#16794C")}>✓ {item.actualResult}</div>}</td>
                  <td style={css(CELL + "white-space:nowrap")}>
                    {editable && item.status !== "cancelled" && <>
                      <button onClick={() => edit(item)} style={css(SMALL)}>แก้ไข</button>{" "}
                      <button disabled={busy} onClick={() => { if (window.confirm(`นำขั้นที่ ${item.sequence} ออก?`)) void send(`${base}/items/${item.id}`, "DELETE"); }} style={css(SMALL + "color:#B42318;border-color:#F3C9C4")}>นำออก</button>
                    </>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </ZoomBox>
      </div>
      {editable && !form && <button onClick={() => setForm({ id: null, values: { ...BLANK_ITEM, ownerId: meta.me } })} style={css(OUTLINE + "align-self:flex-start")}>+ เพิ่ม Action Item</button>}
    </div>
  );
}

function ItemEditor({ form, meta, busy, onChange, onSave, onCancel }: {
  form: ItemForm; meta: Meta; busy: boolean; onChange: (values: ItemForm) => void; onSave: () => void; onCancel: () => void;
}) {
  const set = (key: keyof ItemForm) => (event: { target: { value: string } }) => onChange({ ...form, [key]: event.target.value });
  const text = (key: keyof ItemForm, label: string, width = 180) =>
    <Field label={label}><input aria-label={label} value={form[key]} onChange={set(key)} style={css(INPUT + `width:${width}px`)} /></Field>;
  const date = (key: keyof ItemForm, label: string) =>
    <Field label={label}><input type="date" aria-label={label} value={form[key]} onChange={set(key)} style={css(INPUT)} /></Field>;
  return (
    <div style={css(PANEL + "display:flex;flex-direction:column;gap:10px")}>
      <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
        {text("action", "สิ่งที่จะทำ *", 280)}
        {text("description", "รายละเอียด", 240)}
        <Field label="ผู้รับผิดชอบ">
          <select aria-label="ผู้รับผิดชอบ" value={form.ownerId} onChange={set("ownerId")} style={css(INPUT)}>
            <option value="">—</option>{meta.people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
          </select>
        </Field>
        {text("supportingPerson", "ผู้สนับสนุน")}
        {text("supportingDepartment", "หน่วยงานสนับสนุน")}
        {date("startDate", "เริ่ม")}
        {date("targetDate", "ครบกำหนด")}
        {date("actualCompletionDate", "เสร็จจริง")}
        <Field label="สถานะ">
          <select aria-label="สถานะขั้นตอน" value={form.status} onChange={set("status")} style={css(INPUT)}>
            {["planned", "in-progress", "waiting", "completed", "cancelled"].map((key) => <option key={key} value={key}>{STATUS[key].label}</option>)}
          </select>
        </Field>
        <Field label="Progress %"><input aria-label="Progress %" inputMode="numeric" value={form.progress} onChange={(e) => onChange({ ...form, progress: e.target.value.replace(/\D/g, "").slice(0, 3) })} style={css(INPUT + "width:70px")} /></Field>
        <Field label="ความสำคัญ">
          <select aria-label="ความสำคัญขั้นตอน" value={form.priority} onChange={set("priority")} style={css(INPUT)}>
            {Object.entries(PRIORITY).map(([key, value]) => <option key={key} value={key}>{value.label}</option>)}
          </select>
        </Field>
        {text("expectedResult", "ผลที่คาดหวัง", 220)}
        {text("actualResult", "ผลจริง", 220)}
        {text("remark", "หมายเหตุ", 220)}
      </div>
      <details>
        <summary style={css("font-size:12px;color:#6D28D9;cursor:pointer")}>บันทึกการอบรม (ถ้าขั้นนี้เป็นการอบรม)</summary>
        <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end;margin-top:8px")}>
          {text("trainingTitle", "หัวข้ออบรม", 220)}
          <Field label="ประเภทการอบรม">
            <select aria-label="ประเภทการอบรม" value={form.trainingType} onChange={set("trainingType")} style={css(INPUT)}>
              <option value="">—</option>{meta.trainingTypes.map((type) => <option key={type} value={type}>{type}</option>)}
            </select>
          </Field>
          {text("trainer", "วิทยากร")}
          {text("trainingProvider", "ผู้จัด")}
          {date("trainingDate", "วันที่อบรม")}
          {text("participants", "ผู้เข้าอบรม", 240)}
          {date("certificateExpiry", "ใบรับรองหมดอายุ")}
        </div>
      </details>
      <div style={css("display:flex;gap:8px")}>
        <button disabled={busy || !form.action.trim()} onClick={onSave} style={css(SAVE + "opacity:" + (busy || !form.action.trim() ? ".55" : "1"))}>บันทึกขั้นตอน</button>
        <button onClick={onCancel} style={css(OUTLINE)}>ยกเลิก</button>
      </div>
    </div>
  );
}

/* --------------------------------------------------------------- development */

function Development({ detail, meta, busy, send, base }: { detail: PlanDetail; meta: Meta; busy: boolean; send: Send; base: string }) {
  const plan = detail.plan;
  const people = plan.developmentType === "people";
  const editable = detail.canEdit && plan.status !== "completed" && plan.status !== "cancelled";
  const [score, setScore] = useState({ dimension: meta.dimensions[0] ?? "", current: "", target: "" });
  const [reference, setReference] = useState({ kind: "evaluation", refId: "", label: "" });
  const latest = meta.dimensions.map((dimension) => ({ dimension, row: [...detail.scores].reverse().find((one) => one.dimension === dimension) }));
  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css(PANEL + "display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px 18px")}>
        {people ? <>
          <Fact label="พนักงาน">{plan.employeeName || plan.targetName}{plan.position ? ` · ${plan.position}` : ""}</Fact>
          <Fact label="ทีม / หัวหน้า">{plan.team || "—"} · {plan.supervisor || "—"}</Fact>
        </> : <>
          <Fact label="ผู้ขนส่ง">{detail.supplierName || plan.targetName}</Fact>
          <Fact label="ผู้ติดต่อฝั่งผู้ขนส่ง">{plan.carrierContact || "—"}</Fact>
        </>}
        <Fact label="ด้านที่พัฒนา">{plan.developmentArea || "—"}</Fact>
        <Fact label={people ? "ระดับปัจจุบัน → เป้าหมาย" : "ผลงานปัจจุบัน → เป้าหมาย"}>{plan.currentLevel || "—"} → {plan.targetLevel || "—"}</Fact>
        <Fact label="Gap">{plan.gap || "—"}</Fact>
        {people ? <>
          <Fact label="วิธีพัฒนา">{plan.method || "—"}</Fact>
          <Fact label="Coach / Mentor">{plan.coach || "—"}</Fact>
        </> : <Fact label="Root Cause">{plan.rootCause || "—"}</Fact>}
        <Fact label="วิธีประเมินผล">{plan.evaluationMethod || "—"}</Fact>
        <Fact label="วันทบทวน">{plan.reviewDate || "—"}</Fact>
        <Fact label="ผลลัพธ์">{plan.result || "—"}</Fact>
      </div>

      {!people && (
        <div style={css(PANEL)}>
          <div style={css(TITLE + "margin-bottom:8px")}>Carrier Development Scorecard (1–5)</div>
          <ZoomBox capped={false}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:560px")}>
              <thead><tr>{["มิติ", "ก่อนหน้า", "ปัจจุบัน", "เป้าหมาย", "ประเมินโดย"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
              <tbody>{latest.map(({ dimension, row }) => (
                <tr key={dimension}>
                  <td style={css(CELL + "font-weight:600")}>{dimension}</td>
                  {[row?.previousScore, row?.currentScore, row?.targetScore].map((value, index) => (
                    <td key={index} title={value ? SCORE_TH[value] : ""} style={css(CELL + MONO + "text-align:center")}>{value ?? "—"}</td>
                  ))}
                  <td style={css(CELL + "font-size:11px;color:#7B8CA0")}>{row ? `${row.assessedBy} · ${new Date(row.assessedAt).toLocaleDateString("en-GB")}` : "—"}</td>
                </tr>
              ))}</tbody>
            </table>
          </ZoomBox>
          {editable && (
            <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-top:10px")}>
              <Field label="มิติ">
                <select aria-label="มิติ" value={score.dimension} onChange={(e) => setScore({ ...score, dimension: e.target.value })} style={css(INPUT)}>
                  {meta.dimensions.map((one) => <option key={one} value={one}>{one}</option>)}
                </select>
              </Field>
              {(["current", "target"] as const).map((key) => (
                <Field key={key} label={key === "current" ? "ปัจจุบัน" : "เป้าหมาย"}>
                  <select aria-label={key === "current" ? "คะแนนปัจจุบัน" : "คะแนนเป้าหมาย"} value={score[key]} onChange={(e) => setScore({ ...score, [key]: e.target.value })} style={css(INPUT)}>
                    <option value="">—</option>{[1, 2, 3, 4, 5].map((value) => <option key={value} value={value}>{SCORE_TH[value]}</option>)}
                  </select>
                </Field>
              ))}
              <button disabled={busy || (!score.current && !score.target)} onClick={() => void send(`${base}/scores`, "POST", {
                dimension: score.dimension, current: score.current ? Number(score.current) : null, target: score.target ? Number(score.target) : null,
              }).then((ok) => ok && setScore({ ...score, current: "", target: "" }))} style={css(SAVE)}>บันทึกคะแนน</button>
            </div>
          )}
        </div>
      )}

      <div style={css(PANEL)}>
        <div style={css(TITLE + "margin-bottom:8px")}>ข้อมูลอ้างอิงใน SCMOS</div>
        {detail.references.length === 0 && <div style={css("font-size:12px;color:#94A3B8")}>ยังไม่มี</div>}
        {detail.references.map((one) => (
          <div key={one.id} style={css("display:flex;gap:8px;align-items:center;font-size:12px;padding:3px 0")}>
            <Badge label={REFERENCE_KINDS[one.kind] ?? one.kind} tone="#1D5FA8" background="#E7F0FA" />
            <span style={css("font-family:ui-monospace,monospace")}>{one.refId}</span><span>{one.label}</span>
            {editable && <button disabled={busy} onClick={() => void send(`${base}/references/${one.id}`, "DELETE")} style={css(SMALL)}>นำออก</button>}
          </div>
        ))}
        {editable && (
          <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end;margin-top:8px")}>
            <Field label="ประเภท">
              <select aria-label="ประเภทข้อมูลอ้างอิง" value={reference.kind} onChange={(e) => setReference({ ...reference, kind: e.target.value })} style={css(INPUT)}>
                {Object.entries(REFERENCE_KINDS).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
              </select>
            </Field>
            <Field label="เลขอ้างอิง"><input aria-label="เลขอ้างอิง" value={reference.refId} onChange={(e) => setReference({ ...reference, refId: e.target.value })} style={css(INPUT)} /></Field>
            <Field label="คำอธิบาย"><input aria-label="คำอธิบายอ้างอิง" value={reference.label} onChange={(e) => setReference({ ...reference, label: e.target.value })} style={css(INPUT + "width:260px")} /></Field>
            <button disabled={busy || (!reference.refId.trim() && !reference.label.trim())}
              onClick={() => void send(`${base}/references`, "POST", reference).then((ok) => ok && setReference({ ...reference, refId: "", label: "" }))} style={css(SAVE)}>เพิ่ม</button>
          </div>
        )}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ progress */

function ProgressTab({ detail, busy, send, base }: { detail: PlanDetail; busy: boolean; send: Send; base: string }) {
  const [form, setForm] = useState({ comment: "", itemId: "", progress: "" });
  const editable = detail.canEdit && detail.plan.status !== "completed" && detail.plan.status !== "cancelled";
  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {editable && (
        <div style={css(PANEL + "display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ความคืบหน้า / ติดตามผล">
            <input aria-label="ความคืบหน้า" value={form.comment} onChange={(e) => setForm({ ...form, comment: e.target.value })} placeholder="เช่น ผู้ขนส่งยืนยันรถเพิ่ม 2 คัน" style={css(INPUT + "width:360px")} />
          </Field>
          <Field label="ขั้นตอน">
            <select aria-label="ขั้นตอนที่อัปเดต" value={form.itemId} onChange={(e) => setForm({ ...form, itemId: e.target.value })} style={css(INPUT)}>
              <option value="">— ทั้งแผน —</option>
              {detail.items.filter((item) => item.status !== "cancelled").map((item) => <option key={item.id} value={item.id}>{item.sequence}. {item.action}</option>)}
            </select>
          </Field>
          {form.itemId && <Field label="Progress %">
            <select aria-label="Progress ใหม่" value={form.progress} onChange={(e) => setForm({ ...form, progress: e.target.value })} style={css(INPUT)}>
              <option value="">ไม่เปลี่ยน</option>{[0, 25, 50, 75, 100].map((value) => <option key={value} value={value}>{value}%</option>)}
            </select>
          </Field>}
          <button disabled={busy || (!form.comment.trim() && !form.progress)} onClick={() => void send(`${base}/progress`, "POST", {
            comment: form.comment, itemId: form.itemId ? Number(form.itemId) : null, progress: form.progress ? Number(form.progress) : null,
          }).then((ok) => ok && setForm({ comment: "", itemId: "", progress: "" }))} style={css(SAVE)}>บันทึก</button>
        </div>
      )}
      <div style={css(PANEL)}>
        {detail.updates.length === 0 && <div style={css("font-size:12px;color:#94A3B8")}>ยังไม่มีประวัติความคืบหน้า</div>}
        {detail.updates.map((update) => (
          <div key={update.id} style={css("padding:7px 0;border-bottom:1px solid #F1F5F9;font-size:12px;color:#334155")}>
            <span style={css("font-family:ui-monospace,monospace;color:#7B8CA0")}>{new Date(update.createdAt).toLocaleString("en-GB")}</span>
            {" · "}<b>{update.createdBy}</b>
            {update.comment && <div style={css("margin-top:2px")}>{update.comment}</div>}
            {(update.progressBefore !== update.progressAfter || update.statusBefore !== update.statusAfter) && (
              <div style={css("font-size:11px;color:#5A6B7D")}>
                {update.progressBefore !== update.progressAfter && `Progress ${update.progressBefore ?? 0}% → ${update.progressAfter ?? 0}%`}
                {update.statusBefore !== update.statusAfter && ` · ${STATUS[update.statusBefore]?.label ?? update.statusBefore} → ${STATUS[update.statusAfter]?.label ?? update.statusAfter}`}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ evidence */

const EVIDENCE_KINDS = ["certificate", "training-attendance", "meeting-minutes", "evaluation-form", "photo", "other"];

function EvidenceTab({ detail, busy, send }: { detail: PlanDetail; busy: boolean; send: Send }) {
  const [form, setForm] = useState({ kind: "certificate", note: "", itemId: "" });
  const editable = detail.canEdit && detail.plan.status !== "cancelled";
  function upload(file: File) {
    const body = new FormData();
    body.append("actionPlanId", String(detail.plan.id));
    if (form.itemId) body.append("actionPlanItemId", form.itemId);
    body.append("kind", form.kind);
    body.append("note", form.note);
    body.append("file", file);
    void send("/api/documents", "POST", body).then((ok) => ok && setForm({ ...form, note: "" }));
  }
  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {editable && (
        <div style={css(PANEL + "display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ประเภทหลักฐาน">
            <select aria-label="ประเภทหลักฐาน" value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })} style={css(INPUT)}>
              {EVIDENCE_KINDS.map((kind) => <option key={kind} value={kind}>{kind}</option>)}
            </select>
          </Field>
          <Field label="ขั้นตอน">
            <select aria-label="ขั้นตอนของหลักฐาน" value={form.itemId} onChange={(e) => setForm({ ...form, itemId: e.target.value })} style={css(INPUT)}>
              <option value="">— ทั้งแผน —</option>
              {detail.items.map((item) => <option key={item.id} value={item.id}>{item.sequence}. {item.action}</option>)}
            </select>
          </Field>
          <Field label="หมายเหตุ"><input aria-label="หมายเหตุหลักฐาน" value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} style={css(INPUT + "width:240px")} /></Field>
          <input type="file" aria-label="ไฟล์หลักฐาน" disabled={busy} accept="application/pdf,image/*,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.msg"
            onChange={(e) => { const file = e.target.files?.[0]; e.target.value = ""; if (file) upload(file); }} style={css("font-size:11.5px")} />
        </div>
      )}
      <div style={css(PANEL)}>
        {detail.evidence.length === 0 && <div style={css("font-size:12px;color:#94A3B8")}>ยังไม่มีหลักฐาน</div>}
        {detail.evidence.map((file) => (
          <div key={file.id} style={css("display:flex;gap:10px;align-items:center;flex-wrap:wrap;padding:6px 0;border-bottom:1px solid #F1F5F9;font-size:12px")}>
            <a href={`/api/documents/${file.id}/content${file.canShow ? "?inline=1" : ""}`} target="_blank" rel="noreferrer" style={css("color:#0A5C97;font-weight:600")}>{file.fileName}</a>
            <Badge label={file.kind} tone="#475569" background="#F1F5F9" />
            {file.note && <span style={css("color:#5A6B7D")}>{file.note}</span>}
            <span style={css("margin-left:auto;color:#7B8CA0;font-size:11px")}>{file.uploadedBy} · {new Date(file.uploadedAt).toLocaleString("en-GB")}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

/* -------------------------------------------------------------------- review */

function ReviewTab({ detail, busy, send, base }: { detail: PlanDetail; busy: boolean; send: Send; base: string }) {
  const [form, setForm] = useState({ result: "approved", comment: "" });
  const waiting = detail.plan.status === "pending-review";
  const reopenable = detail.plan.status === "completed";
  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {detail.canReview && (waiting || reopenable) && (
        <div style={css(PANEL + "display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ผล Review">
            <select aria-label="ผล Review" value={reopenable ? "reopen" : form.result} disabled={reopenable} onChange={(e) => setForm({ ...form, result: e.target.value })} style={css(INPUT)}>
              {!reopenable && <option value="approved">Approved</option>}
              {!reopenable && <option value="need-improvement">Need Improvement</option>}
              <option value="reopen">Reopen</option>
            </select>
          </Field>
          <Field label="ความเห็น"><input aria-label="ความเห็น Review" value={form.comment} onChange={(e) => setForm({ ...form, comment: e.target.value })} style={css(INPUT + "width:360px")} /></Field>
          <button disabled={busy} onClick={() => void send(`${base}/review`, "POST", { result: reopenable ? "reopen" : form.result, comment: form.comment })
            .then((ok) => ok && setForm({ result: "approved", comment: "" }))} style={css(SAVE)}>{reopenable ? "Reopen แผน" : "บันทึกผล Review"}</button>
        </div>
      )}
      {!detail.canReview && waiting && <Notice tone="#6D28D9">รอหัวหน้า Review</Notice>}
      <div style={css(PANEL)}>
        {detail.reviews.length === 0 && <div style={css("font-size:12px;color:#94A3B8")}>ยังไม่เคยส่ง Review</div>}
        {detail.reviews.map((review) => (
          <div key={review.id} style={css("padding:7px 0;border-bottom:1px solid #F1F5F9;font-size:12px;color:#334155")}>
            ส่งโดย <b>{review.submittedBy}</b> · {new Date(review.submittedAt).toLocaleString("en-GB")}
            {review.result
              ? <div>ผล <b style={css("color:" + (review.result === "approved" ? "#16794C" : "#B45309"))}>{review.result}</b> โดย {review.reviewer} · {review.reviewedAt ? new Date(review.reviewedAt).toLocaleString("en-GB") : ""}{review.comment ? ` — ${review.comment}` : ""}</div>
              : <div style={css("color:#6D28D9")}>รอ Review{review.comment ? ` — ${review.comment}` : ""}</div>}
          </div>
        ))}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------- history */

function History({ id }: { id: number }) {
  const [rows, setRows] = useState<HistoryRow[] | null>(null);
  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch(`/api/action-plans/${id}/history`, { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as HistoryRow[] : [];
      if (!cancelled) setRows(body);
    })();
    return () => { cancelled = true; };
  }, [id]);
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <ZoomBox capped={false}>
        <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:760px")}>
          <thead><tr>{["เวลา", "ผู้ทำ", "การกระทำ", "รายการ", "เดิม", "ใหม่", "เหตุผล"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
          <tbody>
            {rows === null && <tr><td colSpan={7} style={css(EMPTY)}>กำลังโหลด…</td></tr>}
            {rows?.length === 0 && <tr><td colSpan={7} style={css(EMPTY)}>ยังไม่มีประวัติ</td></tr>}
            {rows?.map((row) => (
              <tr key={row.id}>
                <td style={css(CELL + MONO + "white-space:nowrap")}>{new Date(row.at).toLocaleString("en-GB")}</td>
                <td style={css(CELL)}>{row.who}</td>
                <td style={css(CELL)}>{row.action}</td>
                <td style={css(CELL)}>{row.field}</td>
                <td style={css(CELL + "color:#7B8CA0")}>{row.oldValue || "—"}</td>
                <td style={css(CELL)}>{row.newValue || "—"}</td>
                <td style={css(CELL + "color:#5A6B7D")}>{row.reason || ""}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </ZoomBox>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}><span style={css(LABEL)}>{label}</span>{children}</label>;
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><div style={css(LABEL)}>{label}</div><div style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{children}</div></div>;
}
