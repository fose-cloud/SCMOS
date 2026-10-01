"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import type { PlanPrefill } from "../actionPlanRequest";
import { MONTHS, MONTH_COLOURS, STATUS_TH, markFor, parsePlanPaste, type AuditPlan, type AuditPlanItem, type Mark, type PastedRow } from "../auditPlan";
import { isoDay, registerDate } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

type Form = {
  id: number | null; kind: string; company: string; target: string; personInCharge: string; auditDate: string;
  schedule: string; status: string; nextDate: string; remark: string; findingSentDate: string; reportSentDate: string; sequence: string;
};

const BLANK: Form = {
  id: null, kind: "re-audit", company: "", target: "LCB", personInCharge: "", auditDate: "", schedule: "fixed", status: "planned",
  nextDate: "", remark: "", findingSentDate: "", reportSentDate: "", sequence: "",
};

/**
 * Audit Planning (1 Oct 2026): the year's EHS audit plan with the truck subcontractors — re-audits of the
 * carriers already working and the audits new subcontractors are approved by — laid out as the department's
 * workbook is, sign-off boxes and legend included. A new subcontractor's line is the one Add New Vendor sets.
 */
export function AuditPlanning({ canEdit, onToast, onActionPlan }: {
  canEdit: boolean; onToast: (message: string) => void;
  /** Starts a corrective plan for the audited company; absent without EditActionPlans. */
  onActionPlan?: (prefill: PlanPrefill) => void;
}) {
  const [year, setYear] = useState(() => new Date().getFullYear());
  const [view, setView] = useState<AuditPlan | null>(null);
  const [failure, setFailure] = useState("");
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState<Form | null>(null);
  const [header, setHeader] = useState<AuditPlan["plan"] | null>(null);
  const [paste, setPaste] = useState<{ text: string; rows: PastedRow[]; problems: string[] } | null>(null);
  const [suppliers, setSuppliers] = useState<string[]>([]);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch(`/api/audit-plan?year=${year}`, { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as AuditPlan & { error?: string } | null;
      if (!response.ok || !body) throw new Error(body?.error ?? `เปิดแผน Audit ไม่ได้ (${response.status})`);
      setView(body); setFailure("");
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, [year]);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
      const rows = response.ok ? await response.json() as { name: string }[] : [];
      if (!cancelled) setSuppliers(rows.map((row) => row.name).sort((a, b) => a.localeCompare(b)));
    })();
    return () => { cancelled = true; };
  }, []);

  const sections = useMemo(() => {
    const items = view?.items ?? [];
    return Object.entries(view?.sections ?? {}).map(([kind, title]) => ({ kind, title, items: items.filter((item) => item.kind === kind) }))
      .filter((section) => section.items.length > 0 || section.kind === "re-audit");
  }, [view]);

  async function send(path: string, method: string, body?: unknown): Promise<boolean> {
    if (busy) return false;
    setBusy(true);
    try {
      const response = await apiFetch(path, {
        method, headers: { "content-type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (response.ok) await load();
      return response.ok;
    } finally { setBusy(false); }
  }

  async function saveItem() {
    if (!form) return;
    const body = {
      kind: form.kind, company: form.company, target: form.target, personInCharge: form.personInCharge,
      auditDate: registerDate(form.auditDate), schedule: form.schedule, status: form.status, nextDate: registerDate(form.nextDate),
      remark: form.remark, findingSentDate: registerDate(form.findingSentDate), reportSentDate: registerDate(form.reportSentDate),
      sequence: form.sequence ? Number(form.sequence) : null,
    };
    if (await send(form.id === null ? "/api/audit-plan/items" : `/api/audit-plan/items/${form.id}`, form.id === null ? "POST" : "PUT", body))
      setForm(null);
  }

  async function removeItem() {
    if (!form || form.id === null || !window.confirm(`นำ ${form.company} ออกจากแผน Audit?`)) return;
    if (await send(`/api/audit-plan/items/${form.id}`, "DELETE")) setForm(null);
  }

  async function saveHeader() {
    if (!header) return;
    if (await send(`/api/audit-plan/${year}`, "PUT", { ...header, reviewedDate: header.reviewedDate })) setHeader(null);
  }

  async function importPaste() {
    if (!paste || paste.rows.length === 0 || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/audit-plan/import", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ rows: paste.rows.map((row) => ({ ...row, schedule: "fixed", status: "planned" })) }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string; errors?: { row: number; error: string }[] };
      onToast([reply.message ?? reply.error ?? "นำเข้าไม่สำเร็จ", ...(reply.errors ?? []).slice(0, 2).map((one) => `แถว ${one.row}: ${one.error}`)].join(" · "));
      if (response.ok) { setPaste(null); await load(); }
    } finally { setBusy(false); }
  }

  function edit(item: AuditPlanItem) {
    if (!canEdit) return;
    setPaste(null);
    setForm({
      id: item.id, kind: item.kind, company: item.company, target: item.target, personInCharge: item.personInCharge,
      auditDate: isoDay(item.auditDate), schedule: item.schedule, status: item.status, nextDate: isoDay(item.nextDate),
      remark: item.remark, findingSentDate: isoDay(item.findingSentDate), reportSentDate: isoDay(item.reportSentDate), sequence: String(item.sequence),
    });
  }

  if (failure) return <Notice tone="#B45309">{failure}</Notice>;
  if (!view) return <Notice tone="#7B8CA0">กำลังโหลดแผน Audit…</Notice>;

  const plan = view.plan;
  const all = view.items.filter((item) => item.status !== "cancelled");
  const done = all.filter((item) => item.status === "done").length;
  const late = all.filter((item) => item.notYetDone).length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      {/* The workbook's own head: the company, the plan's title, and the four sign-off boxes. */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;display:grid;grid-template-columns:minmax(260px,1.4fr) minmax(320px,2fr);overflow:hidden")}>
        <div style={css("padding:14px 18px;display:flex;flex-direction:column;justify-content:center;gap:4px;border-right:1px solid #E9EFF5")}>
          <div style={css("font-size:12px;color:#5A6B7D;font-weight:600")}>Leschaco (Thailand) Co.,Ltd</div>
          <div style={css("font-size:16px;font-weight:700;color:#0A2240")}>{plan.title}</div>
        </div>
        <div style={css("display:grid;grid-template-columns:repeat(4,1fr) 70px")}>
          {(["Prepared by", "Reviewed by", "Reviewed by", "Approved by", "Rev."] as const).map((label, index) => (
            <div key={label + index} style={css("border-left:1px solid #E9EFF5;display:flex;flex-direction:column")}>
              <div style={css("background:#E2EFDA;padding:5px 8px;font-size:11px;color:#375623;font-weight:650;text-align:center")}>{label}</div>
              <div style={css("padding:8px;font-size:12.5px;font-weight:650;color:#0A2240;text-align:center;min-height:22px")}>
                {[plan.preparedBy, plan.reviewedBy, plan.secondReviewedBy, plan.approvedBy, plan.revision][index] || "—"}
              </div>
              <div style={css("padding:4px 8px;font-size:11px;color:#7B8CA0;text-align:center;font-family:ui-monospace,monospace;min-height:18px")}>
                {index === 1 ? plan.reviewedDate : ""}
              </div>
            </div>
          ))}
        </div>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:9px 14px;display:flex;gap:16px;flex-wrap:wrap;align-items:center;font-size:12px;color:#334155")}>
        <Legend mark={{ symbol: "13", tone: "fixed", title: "" }}>Date = Fixed schedule</Legend>
        <Legend mark={{ symbol: "○", tone: "tentative", title: "" }}>Tentative schedule</Legend>
        <Legend mark={{ symbol: "✓", tone: "done", title: "" }}>Done</Legend>
        <Legend mark={{ symbol: "X", tone: "late", title: "" }}>Not yet done</Legend>
        <Legend mark={{ symbol: "→", tone: "moved", title: "" }}>Continue project</Legend>
        <Legend mark={{ symbol: "⇢", tone: "moved", title: "" }}>Postpone to</Legend>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:10px 14px;display:flex;gap:10px;flex-wrap:wrap;align-items:center")}>
        <label style={css("display:flex;align-items:center;gap:6px;font-size:12px;color:#5A6B7D")}>ปี
          <select aria-label="ปี" value={year} onChange={(event) => { setYear(Number(event.target.value)); setForm(null); setHeader(null); }} style={css(INPUT)}>
            {[...new Set([...view.years, year - 1, year + 1])].sort().map((one) => <option key={one} value={one}>{one}</option>)}
          </select>
        </label>
        <span style={css("font-size:12px;color:#5A6B7D")}>
          ทั้งหมด <b style={css("color:#0A2240")}>{all.length}</b> · ตรวจแล้ว <b style={css("color:#16794C")}>{done}</b>
          {" "}· ยังไม่ได้ตรวจ (เลยกำหนด) <b style={css("color:" + (late ? "#B42318" : "#0A2240"))}>{late}</b>
        </span>
        {canEdit && <span style={css("margin-left:auto;display:flex;gap:8px;flex-wrap:wrap")}>
          <button onClick={() => { setHeader(null); setPaste(null); setForm({ ...BLANK }); }} style={css(BUTTON)}>+ เพิ่มรายการ</button>
          <button onClick={() => { setForm(null); setPaste(paste ? null : { text: "", rows: [], problems: [] }); }} style={css(OUTLINE)}>วางจาก Excel</button>
          <button onClick={() => { setForm(null); setHeader(header ? null : { ...plan }); }} style={css(OUTLINE)}>แก้หัวแผน</button>
        </span>}
      </div>

      {header && (
        <div style={css(PANEL + "display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ชื่อแผน"><input aria-label="ชื่อแผน" value={header.title} onChange={(e) => setHeader({ ...header, title: e.target.value })} style={css(INPUT + "width:380px")} /></Field>
          <Field label="Prepared by"><input aria-label="Prepared by" value={header.preparedBy} onChange={(e) => setHeader({ ...header, preparedBy: e.target.value })} style={css(INPUT)} /></Field>
          <Field label="Reviewed by"><input aria-label="Reviewed by" value={header.reviewedBy} onChange={(e) => setHeader({ ...header, reviewedBy: e.target.value })} style={css(INPUT)} /></Field>
          <Field label="วันที่ Review"><input type="date" aria-label="วันที่ Review" value={isoDay(header.reviewedDate)} onChange={(e) => setHeader({ ...header, reviewedDate: registerDate(e.target.value) })} style={css(INPUT)} /></Field>
          <Field label="Reviewed by (2)"><input aria-label="Reviewed by (2)" value={header.secondReviewedBy} onChange={(e) => setHeader({ ...header, secondReviewedBy: e.target.value })} style={css(INPUT)} /></Field>
          <Field label="Approved by"><input aria-label="Approved by" value={header.approvedBy} onChange={(e) => setHeader({ ...header, approvedBy: e.target.value })} style={css(INPUT)} /></Field>
          <Field label="Rev."><input aria-label="Rev." value={header.revision} onChange={(e) => setHeader({ ...header, revision: e.target.value })} style={css(INPUT + "width:70px")} /></Field>
          <button disabled={busy} onClick={() => void saveHeader()} style={css(SAVE)}>บันทึกหัวแผน</button>
        </div>
      )}

      {paste && (
        <div style={css(PANEL + "display:flex;flex-direction:column;gap:8px")}>
          <textarea aria-label="วางตารางจาก Excel" rows={5} value={paste.text}
            placeholder="คัดลอกแถวจากชีตแผน Audit (เลือกตั้งแต่ No ถึง Remark) แล้ววางที่นี่"
            onChange={(e) => { const parsed = parsePlanPaste(e.target.value); setPaste({ text: e.target.value, ...parsed }); }}
            style={css("width:100%;border:1px solid #C9D6E2;border-radius:4px;padding:8px;font-size:12px;font-family:ui-monospace,monospace;box-sizing:border-box")} />
          {paste.rows.length > 0 && (
            <div style={css("font-size:12px;color:#334155;max-height:160px;overflow:auto")}>
              {paste.rows.map((row) => <div key={row.company + row.auditDate}>{row.sequence}. {row.company} · {row.target || "—"} · {row.auditDate} · {row.kind === "new" ? "New" : "Re-audit"}</div>)}
            </div>
          )}
          {paste.problems.length > 0 && <div style={css("font-size:11.5px;color:#B45309")}>{paste.problems.join(" · ")}</div>}
          <div style={css("display:flex;gap:8px")}>
            <button disabled={busy || paste.rows.length === 0} onClick={() => void importPaste()} style={css(SAVE + "opacity:" + (busy || paste.rows.length === 0 ? ".55" : "1"))}>
              นำเข้า {paste.rows.length} รายการ</button>
            <button onClick={() => setPaste(null)} style={css(OUTLINE)}>ยกเลิก</button>
          </div>
        </div>
      )}

      {form && (
        <div style={css(PANEL + "display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ประเภท">
            <select aria-label="ประเภท" value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })} style={css(INPUT)}>
              <option value="re-audit">Re-audit</option><option value="new">ผู้ขนส่งใหม่ (New)</option>
            </select>
          </Field>
          <Field label="บริษัท *">
            <input aria-label="บริษัท" list="audit-suppliers" value={form.company} onChange={(e) => setForm({ ...form, company: e.target.value })} style={css(INPUT + "width:250px")} />
            <datalist id="audit-suppliers">{suppliers.map((name) => <option key={name} value={name} />)}</datalist>
          </Field>
          <Field label="Targets">
            <input aria-label="Targets" list="audit-targets" value={form.target} onChange={(e) => setForm({ ...form, target: e.target.value })} style={css(INPUT + "width:80px")} />
            <datalist id="audit-targets"><option value="LCB" /><option value="BKK" /></datalist>
          </Field>
          <Field label="Person in charge (บรรทัดละคน)">
            <textarea aria-label="Person in charge" rows={2} value={form.personInCharge} onChange={(e) => setForm({ ...form, personInCharge: e.target.value })} style={css(INPUT + "height:auto;width:170px;padding:4px 8px")} />
          </Field>
          <Field label="วันที่ Audit *"><input type="date" aria-label="วันที่ Audit" value={form.auditDate} onChange={(e) => setForm({ ...form, auditDate: e.target.value })} style={css(INPUT)} /></Field>
          <Field label="กำหนดการ">
            <select aria-label="กำหนดการ" value={form.schedule} onChange={(e) => setForm({ ...form, schedule: e.target.value })} style={css(INPUT)}>
              <option value="fixed">Fixed</option><option value="tentative">Tentative ○</option>
            </select>
          </Field>
          <Field label="สถานะ">
            <select aria-label="สถานะ" value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value })} style={css(INPUT)}>
              {Object.entries(STATUS_TH).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          </Field>
          {(form.status === "postponed" || form.status === "continue") && (
            <Field label={form.status === "postponed" ? "เลื่อนไปวันที่ *" : "ทำต่อวันที่ *"}>
              <input type="date" aria-label="วันที่เลื่อนไป" value={form.nextDate} onChange={(e) => setForm({ ...form, nextDate: e.target.value })} style={css(INPUT)} />
            </Field>
          )}
          <Field label="Remark"><input aria-label="Remark" value={form.remark} onChange={(e) => setForm({ ...form, remark: e.target.value })} style={css(INPUT + "width:200px")} /></Field>
          <Field label="ส่ง Finding"><input type="date" aria-label="ส่ง Finding" value={form.findingSentDate} onChange={(e) => setForm({ ...form, findingSentDate: e.target.value })} style={css(INPUT)} /></Field>
          <Field label="ส่ง Audit report"><input type="date" aria-label="ส่ง Audit report" value={form.reportSentDate} onChange={(e) => setForm({ ...form, reportSentDate: e.target.value })} style={css(INPUT)} /></Field>
          <span style={css("display:flex;gap:8px")}>
            <button disabled={busy || !form.company.trim() || !form.auditDate} onClick={() => void saveItem()}
              style={css(SAVE + "opacity:" + (busy || !form.company.trim() || !form.auditDate ? ".55" : "1"))}>
              {form.id === null ? "เพิ่มในแผน" : "บันทึก"}</button>
            {form.id !== null && <button disabled={busy} onClick={() => void removeItem()} style={css(OUTLINE + "color:#B42318;border-color:#F3C9C4")}>นำออกจากแผน</button>}
            <button onClick={() => setForm(null)} style={css(OUTLINE)}>ยกเลิก</button>
            {form.id !== null && onActionPlan && (() => {
              const item = view.items.find((one) => one.id === form.id);
              if (!item) return null;
              return (
                <button onClick={() => onActionPlan({
                  developmentType: "subcontractor", targetType: "subcontractor", supplierId: item.supplierId ?? undefined,
                  category: "Compliance Improvement", title: `${item.company} — แผนแก้ไขจาก Audit ${isoDay(item.auditDate)}`,
                  references: [{ kind: "audit", refId: String(item.id), label: `Audit ${year} · ${item.company} · ${isoDay(item.auditDate)}` }],
                })} style={css(OUTLINE + "color:#1D5FA8;border-color:#B9D3EE")}>+ Action Plan</button>
              );
            })()}
          </span>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:1280px")}>
            <thead>
              <tr>
                {["No", "Subject", "Targets", "Person in charge"].map((head) => <th key={head} rowSpan={2} style={css(HEAD)}>{head}</th>)}
                <th colSpan={12} style={css(HEAD + "text-align:center")}>Schedule</th>
                <th rowSpan={2} style={css(HEAD)}>Remark</th>
                <th colSpan={2} style={css(HEAD + "text-align:center")}>Follow up</th>
              </tr>
              <tr>
                {MONTHS.map((month, index) => <th key={month} style={css(HEAD + `text-align:center;width:38px;background:${MONTH_COLOURS[index]};color:${index >= 5 && index !== 6 && index !== 7 ? "#fff" : "#0A2240"}`)}>{month}</th>)}
                <th style={css(HEAD)}>sent finding</th>
                <th style={css(HEAD)}>Sent audit report</th>
              </tr>
            </thead>
            <tbody>
              {sections.map((section, sectionIndex) => [
                <tr key={section.kind}>
                  <td style={css(SECTION)}>{sectionIndex + 1}</td>
                  <td colSpan={18} style={css(SECTION)}>{section.title}</td>
                </tr>,
                ...section.items.map((item) => (
                  <tr key={item.id} onClick={() => edit(item)} style={css((canEdit ? "cursor:pointer;" : "") + (item.status === "cancelled" ? "opacity:.5;" : ""))}>
                    <td style={css(CELL + "text-align:right;color:#5A6B7D")}>{item.sequence}</td>
                    <td style={css(CELL + "min-width:230px;font-weight:600;color:" + (item.kind === "new" ? "#B42318" : "#0A2240"))}>
                      {item.company}
                      {item.supplierId === null && <div style={css("font-size:10px;font-weight:600;color:#B45309")}>ไม่อยู่ในทะเบียนผู้รับเหมา</div>}
                      {item.supplierStatus && item.supplierStatus !== "approved" && <div style={css("font-size:10px;font-weight:600;color:#7B8CA0")}>{item.supplierStatus}</div>}
                    </td>
                    <td style={css(CELL + "text-align:center")}>{item.target || "—"}</td>
                    <td style={css(CELL + "white-space:pre-line;text-align:center;font-size:11.5px")}>{item.personInCharge || "—"}</td>
                    {MONTHS.map((month, index) => <MonthCell key={month} mark={markFor(item, year, index + 1)} />)}
                    <td style={css(CELL + "min-width:170px")}>{item.remark || `Audit date: ${item.auditDate}`}
                      {item.status !== "planned" && <div style={css("font-size:10.5px;color:#5A6B7D")}>{STATUS_TH[item.status]}{item.nextDate ? ` ${item.nextDate}` : ""}</div>}
                    </td>
                    <td style={css(CELL + "font-family:ui-monospace,monospace;font-size:11px")}>{item.findingSentDate || "—"}</td>
                    <td style={css(CELL + "font-family:ui-monospace,monospace;font-size:11px")}>{item.reportSentDate || "—"}</td>
                  </tr>
                )),
              ])}
              {view.items.length === 0 && <tr><td colSpan={19} style={css("padding:24px;text-align:center;color:#94A3B8;font-size:12.5px")}>ยังไม่มีรายการในแผนปี {year}</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}

const TONES: Record<Mark["tone"], string> = {
  fixed: "background:#C6E0B4;color:#0A2240;font-weight:700",
  tentative: "background:#E2EFDA;color:#375623;font-weight:700",
  done: "background:#E2EFDA;color:#16794C;font-weight:800",
  late: "background:#FFFF00;color:#B42318;font-weight:800",
  moved: "color:#5A6B7D;font-weight:700",
  cancelled: "color:#94A3B8",
};

function MonthCell({ mark }: { mark: Mark | null }) {
  return <td title={mark?.title} style={css(CELL + "text-align:center;padding:4px;" + (mark ? TONES[mark.tone] : ""))}>{mark?.symbol ?? ""}</td>;
}

function Legend({ mark, children }: { mark: Mark; children: React.ReactNode }) {
  return <span style={css("display:flex;align-items:center;gap:6px")}>
    <span style={css("display:inline-block;min-width:22px;height:18px;line-height:18px;text-align:center;border:1px solid #D8E0E8;border-radius:3px;font-size:11px;" + TONES[mark.tone])}>{mark.symbol}</span>{children}
  </span>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}><span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>{children}</label>;
}

function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid " + tone + ";border-radius:6px;padding:16px 18px;font-size:12.5px;color:#5A6B7D")}>{children}</div>;
}

const PANEL = "background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 14px;";
const INPUT = "height:30px;padding:0 8px;border:1px solid #C9D6E2;border-radius:4px;background:#fff;font-size:12.5px;font-family:inherit;";
const HEAD = "padding:7px 8px;background:#D9D9D9;font-size:11px;color:#0A2240;font-weight:700;border:1px solid #BFBFBF;white-space:nowrap;text-align:left";
const CELL = "padding:6px 8px;border:1px solid #E3E8EE;vertical-align:middle;color:#0F2B46;";
const SECTION = "padding:6px 8px;border:1px solid #BFBFBF;background:#C6EFCE;font-weight:700;color:#0A2240";
const BUTTON = "height:30px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer";
const OUTLINE = "height:30px;padding:0 13px;border:1px solid #C9D6E2;background:#fff;color:#334155;border-radius:4px;font-size:12.5px;cursor:pointer";
const SAVE = "height:30px;padding:0 15px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;";
