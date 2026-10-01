"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { MONTHS, onDay, type AuditPlan, type AuditPlanItem } from "../auditPlan";
import { isoDay, registerDate } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

type OnboardingFile = { id: number; fileName: string; expiryDate: string; canShow: boolean; uploadedBy: string; uploadedAt: string };
type OnboardingItem = {
  no: number; code: string; document: string; note: string; folder: string; kind: string; link: string; expires: boolean;
  status: string; remark: string; updatedBy: string; updatedAt: string | null; files: OnboardingFile[];
};
type Onboarding = {
  supplierId: number; supplier: string; supplierStatus: string; done: number; total: number;
  items: OnboardingItem[]; audit: AuditPlanItem | null;
};

/**
 * A new subcontractor's onboarding checklist (1 Oct 2026): the department's thirteen documents, each Done or
 * Pending with a remark and its files, and the date of the audit that approves the company — which is a line
 * on Audit Planning, so the plan and this screen show one date.
 */
export function OnboardingChecklist({ supplierId, canEdit, canUpload, onToast, onAudit }: {
  supplierId: number; canEdit: boolean; canUpload: boolean; onToast: (message: string) => void; onAudit: () => void;
}) {
  const [view, setView] = useState<Onboarding | null>(null);
  const [failure, setFailure] = useState("");
  const [busy, setBusy] = useState(false);
  const [remarks, setRemarks] = useState<Record<string, string>>({});
  const [expiry, setExpiry] = useState<Record<string, string>>({});
  const [audit, setAudit] = useState({ date: "", person: "", target: "LCB" });

  const load = useCallback(async () => {
    try {
      const response = await apiFetch(`/api/suppliers/${supplierId}/onboarding`, { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as Onboarding & { error?: string } | null;
      if (!response.ok || !body) throw new Error(body?.error ?? `เปิดรายการเอกสารไม่ได้ (${response.status})`);
      setView(body); setFailure("");
      setRemarks(Object.fromEntries(body.items.map((item) => [item.code, item.remark])));
      setAudit({ date: isoDay(body.audit?.auditDate ?? ""), person: body.audit?.personInCharge ?? "", target: body.audit?.target || "LCB" });
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, [supplierId]);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function run(work: () => Promise<Response>, after?: () => void) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await work();
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (response.ok) { await load(); after?.(); }
    } finally { setBusy(false); }
  }

  const setItem = (item: OnboardingItem, status: string) => run(() => apiFetch(`/api/suppliers/${supplierId}/onboarding/${item.code}`, {
    method: "PUT", headers: { "content-type": "application/json" }, body: JSON.stringify({ status, remark: remarks[item.code] ?? "" }),
  }));

  const upload = (item: OnboardingItem, file: File) => {
    const body = new FormData();
    body.append("supplierId", String(supplierId));
    body.append("folder", item.folder);
    body.append("kind", item.kind);
    if (item.expires && expiry[item.code]) body.append("expiryDate", registerDate(expiry[item.code]));
    body.append("file", file);
    return run(() => apiFetch("/api/documents", { method: "POST", body }));
  };

  const saveAudit = () => run(() => apiFetch(`/api/suppliers/${supplierId}/audit-date`, {
    method: "PUT", headers: { "content-type": "application/json" },
    body: JSON.stringify({ date: registerDate(audit.date), personInCharge: audit.person, target: audit.target }),
  }), onAudit);

  if (failure) return <div style={css("padding:12px 16px;font-size:12.5px;color:#B45309")}>{failure}</div>;
  if (!view) return <div style={css("padding:12px 16px;font-size:12.5px;color:#7B8CA0")}>กำลังโหลด…</div>;

  return (
    <div style={css("padding:10px 16px 14px;background:#FAFCFE;border-bottom:1px solid #E9EFF5;display:flex;flex-direction:column;gap:10px")}>
      <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240;align-self:center")}>
          ตรวจสอบเอกสาร · Done {view.done}/{view.total}
        </span>
        <span style={css("margin-left:auto;display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="วันนัด Audit">
            <input type="date" aria-label="วันนัด Audit" value={audit.date} disabled={!canEdit}
              onChange={(e) => setAudit({ ...audit, date: e.target.value })} style={css(INPUT)} />
          </Field>
          <Field label="Targets">
            <input aria-label="Targets ของการ Audit" value={audit.target} disabled={!canEdit}
              onChange={(e) => setAudit({ ...audit, target: e.target.value })} style={css(INPUT + "width:70px")} />
          </Field>
          <Field label="ผู้ตรวจ">
            <input aria-label="ผู้ตรวจ" value={audit.person} disabled={!canEdit} placeholder="คั่นด้วยบรรทัดใหม่ในแผน"
              onChange={(e) => setAudit({ ...audit, person: e.target.value })} style={css(INPUT + "width:170px")} />
          </Field>
          {canEdit && <button disabled={busy || !audit.date} onClick={() => void saveAudit()}
            style={css(SAVE + "opacity:" + (busy || !audit.date ? ".55" : "1"))}>บันทึกวันนัด</button>}
        </span>
      </div>
      <ZoomBox capped={false}>
        <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:860px")}>
          <thead><tr>{["No.", "Documents", "Status", "Remark", "ไฟล์แนบ"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
          <tbody>
            {view.items.map((item) => (
              <tr key={item.code}>
                <td style={css(CELL + "text-align:center;width:40px")}>{item.no}</td>
                <td style={css(CELL + "min-width:300px")}>
                  {item.link ? <a href={item.link} target="_blank" rel="noreferrer" style={css("color:#0A5C97")}>{item.document}</a> : item.document}
                  {item.note && <i style={css("color:#1F6FB2")}> ({item.note})</i>}
                </td>
                <td style={css(CELL + "text-align:center;width:110px")}>
                  {canEdit
                    ? <select aria-label={`สถานะ ${item.no}`} value={item.status} disabled={busy} onChange={(e) => void setItem(item, e.target.value)}
                      style={css(INPUT + "height:26px;font-weight:700;color:" + (item.status === "done" ? "#16794C" : "#B42318"))}>
                      <option value="pending">Pending</option><option value="done">Done</option>
                    </select>
                    : <b style={css("color:" + (item.status === "done" ? "#16794C" : "#B42318"))}>{item.status === "done" ? "Done" : "Pending"}</b>}
                </td>
                <td style={css(CELL + "min-width:180px")}>
                  {canEdit
                    ? <input aria-label={`Remark ${item.no}`} value={remarks[item.code] ?? ""} disabled={busy}
                      onChange={(e) => setRemarks({ ...remarks, [item.code]: e.target.value })}
                      onBlur={() => { if ((remarks[item.code] ?? "") !== item.remark) void setItem(item, item.status); }}
                      style={css(INPUT + "width:100%;box-sizing:border-box")} />
                    : item.remark || "—"}
                </td>
                <td style={css(CELL + "min-width:220px")}>
                  <div style={css("display:flex;flex-direction:column;gap:3px")}>
                    {item.files.map((file) => (
                      <a key={file.id} href={`/api/documents/${file.id}/content${file.canShow ? "?inline=1" : ""}`} target="_blank" rel="noreferrer"
                        title={file.fileName} style={css("color:#0A5C97;font-size:11.5px;max-width:240px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;display:block")}>
                        {file.fileName}{file.expiryDate ? ` · หมดอายุ ${file.expiryDate}` : ""}
                      </a>
                    ))}
                    {canUpload && (
                      <span style={css("display:flex;gap:5px;align-items:center;flex-wrap:wrap")}>
                        {item.expires && <input type="date" aria-label={`วันหมดอายุ ${item.no}`} title="วันหมดอายุ" value={expiry[item.code] ?? ""}
                          onChange={(e) => setExpiry({ ...expiry, [item.code]: e.target.value })} style={css(INPUT + "height:26px;width:130px")} />}
                        <input type="file" aria-label={`แนบไฟล์ ${item.no}`} disabled={busy} accept="application/pdf,image/*,.doc,.docx,.xls,.xlsx,.msg,.eml"
                          onChange={(e) => { const file = e.target.files?.[0]; e.target.value = ""; if (file) void upload(item, file); }}
                          style={css("font-size:11px;max-width:200px")} />
                      </span>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </ZoomBox>
    </div>
  );
}

/**
 * When the new subcontractors are audited (1 Oct 2026): a month of the audit plan as a calendar — each audit on
 * its day, new subcontractors first and re-audits when asked for.
 */
export function AuditCalendar({ refresh }: { refresh: number }) {
  const today = new Date();
  const [month, setMonth] = useState({ year: today.getFullYear(), month: today.getMonth() + 1 });
  const [items, setItems] = useState<AuditPlanItem[]>([]);
  const [withReAudit, setWithReAudit] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch(`/api/audit-plan?year=${month.year}`, { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as AuditPlan : null;
      if (!cancelled) setItems(body?.items ?? []);
    })();
    return () => { cancelled = true; };
  }, [month.year, refresh]);

  const shown = items.filter((item) => withReAudit || item.kind === "new");
  const first = new Date(month.year, month.month - 1, 1).getDay();
  const days = new Date(month.year, month.month, 0).getDate();
  const cells = [...Array(first).fill(null), ...Array.from({ length: days }, (_, index) => index + 1)];
  const step = (by: number) => setMonth((was) => {
    const at = new Date(was.year, was.month - 1 + by, 1);
    return { year: at.getFullYear(), month: at.getMonth() + 1 };
  });
  const isToday = (day: number) => day === today.getDate() && month.month === today.getMonth() + 1 && month.year === today.getFullYear();

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:10px 16px;border-bottom:1px solid #E9EFF5;display:flex;gap:10px;align-items:center;flex-wrap:wrap")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>ปฏิทิน Audit</span>
        <button onClick={() => step(-1)} aria-label="เดือนก่อน" style={css(NAV)}>‹</button>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240;min-width:90px;text-align:center")}>{MONTHS[month.month - 1]} {month.year}</span>
        <button onClick={() => step(1)} aria-label="เดือนถัดไป" style={css(NAV)}>›</button>
        <label style={css("margin-left:auto;display:flex;gap:5px;align-items:center;font-size:12px;color:#5A6B7D")}>
          <input type="checkbox" checked={withReAudit} onChange={(e) => setWithReAudit(e.target.checked)} />แสดง Re-audit ด้วย
        </label>
      </div>
      <div style={css("display:grid;grid-template-columns:repeat(7,minmax(90px,1fr));overflow-x:auto")}>
        {["อา", "จ", "อ", "พ", "พฤ", "ศ", "ส"].map((day) => (
          <div key={day} style={css("padding:5px 8px;background:#F8FAFC;font-size:11px;font-weight:650;color:#7B8CA0;border-bottom:1px solid #E9EFF5")}>{day}</div>
        ))}
        {cells.map((day, index) => {
          const audits = day === null ? [] : onDay(shown, month.year, month.month, day);
          return (
            <div key={index} style={css("min-height:62px;padding:4px 6px;border-right:1px solid #F1F5F9;border-bottom:1px solid #F1F5F9;background:" + (day === null ? "#FBFCFD" : "#fff"))}>
              {day !== null && <div style={css("font-size:11px;font-weight:650;color:" + (isToday(day) ? "#fff;background:#0A2240;border-radius:3px;display:inline-block;padding:0 4px" : "#7B8CA0"))}>{day}</div>}
              {audits.map((item) => (
                <div key={item.id} title={`${item.company} · ${item.target} · ${item.personInCharge.replace(/\n/g, ", ")}`}
                  style={css("margin-top:3px;padding:2px 5px;border-radius:3px;font-size:10.5px;font-weight:650;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;"
                    + (item.status === "done" ? "background:#E2EFDA;color:#16794C" : item.kind === "new" ? "background:#E7F0FA;color:#1D5FA8" : "background:#F1F5F9;color:#334155"))}>
                  {item.status === "done" ? "✓ " : ""}{item.company}
                </div>
              ))}
            </div>
          );
        })}
      </div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}><span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>{children}</label>;
}

const INPUT = "height:30px;padding:0 8px;border:1px solid #C9D6E2;border-radius:4px;background:#fff;font-size:12.5px;font-family:inherit;";
const HEAD = "padding:7px 9px;background:#A9C4E8;font-size:11.5px;color:#0A2240;font-weight:700;border:1px solid #8EA9DB;text-align:center";
const CELL = "padding:6px 9px;border:1px solid #D9E1F2;vertical-align:middle;color:#0F2B46;";
const SAVE = "height:30px;padding:0 15px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;";
const NAV = "height:26px;width:28px;border:1px solid #C9D6E2;background:#fff;border-radius:4px;cursor:pointer;font-size:14px;color:#334155";
