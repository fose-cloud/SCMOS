"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import type { Period } from "../period";

/**
 * The issues behind one number of the carrier scorecard (1 Oct 2026).
 *
 * The API reads them by the same rule that counted them, so the list is always the number that was clicked. Each one
 * can be said again — which of the customer's columns it counts under — and tied to the CAR/PAR that answers it, or
 * escalated into a new one: the scorecard, Operational Issues and Incident & CAR/PAR are then one record seen three ways.
 */

type Row = {
  id: number; code: string; foundOn: string; foundAt: string; source: string; jobRef: string; jobKey: string; detail: string;
  category: string; severity: string; status: string; accidentGrade: string;
  chosenColumn: string; column: string; caseId: number | null; caseReference: string; caseStage: string;
};

type CaseOption = { id: number; reference: string; title: string; jobKey: string; stage: string; kind: string };

export type EscalatedIssue = { id: number; code: string; detail: string; jobKey: string };

/** The ungraded accidents of a carrier, asked for as though they were a column. */
export const UNGRADED = "ungraded";

export function ScorecardIssues({ period, carrier, column, label, canEdit, onClose, onChanged, onEscalate, onOpenCase, onOpenLog, onToast }: {
  period: Period;
  carrier: string;
  /** One of the scorecard's columns as the API names them, or {@link UNGRADED}. */
  column: string;
  label: string;
  canEdit: boolean;
  onClose: () => void;
  /** Something moved a count: the scorecard behind this is read again. */
  onChanged: () => void;
  onEscalate?: (issue: EscalatedIssue) => void;
  onOpenCase?: (caseId: number) => void;
  /** The same carrier in Operational Issues, for everything this panel does not edit. */
  onOpenLog?: () => void;
  onToast: (message: string) => void;
}) {
  const [rows, setRows] = useState<Row[] | null>(null);
  const [failure, setFailure] = useState("");
  const [columns, setColumns] = useState<string[]>([]);
  const [cases, setCases] = useState<CaseOption[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const query = new URLSearchParams({ carrier, column });
    if (period.year && period.year !== "ALL") query.set("year", period.year);
    if (period.month && period.month !== "ALL") query.set("month", period.month);
    if (period.day && period.day !== "ALL") query.set("day", period.day.slice(0, 2));
    try {
      const response = await apiFetch(`/api/kpi/scorecard/issues?${query}`, { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as Row[] | { error?: string } | null;
      if (!response.ok || !Array.isArray(body)) throw new Error((body as { error?: string } | null)?.error ?? `อ่านรายการไม่สำเร็จ (${response.status})`);
      setRows(body); setFailure("");
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, [carrier, column, period]);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  /** Escape closes it, as the backdrop does. */
  useEffect(() => {
    function pressed(event: KeyboardEvent) { if (event.key === "Escape") onClose(); }
    window.addEventListener("keydown", pressed);
    return () => window.removeEventListener("keydown", pressed);
  }, [onClose]);

  /** Every case still free to link — all stages, none already linked to an issue (2 Oct 2026). */
  const loadCases = useCallback(async () => {
    const response = await apiFetch("/api/incidents/linkable", { headers: { accept: "application/json" } });
    setCases(response.ok ? await response.json() as CaseOption[] : []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const form = await apiFetch("/api/issues/form", { headers: { accept: "application/json" } });
      const formBody = form.ok ? await form.json() as { scorecardColumns?: string[] } : null;
      if (!cancelled) setColumns(formBody?.scorecardColumns ?? []);
    })();
    return () => { cancelled = true; };
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void loadCases(); }, [loadCases]);

  async function send(path: string, method: "PATCH" | "PUT", body: unknown, done: string) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(path, { method, headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(response.ok ? reply.message ?? done : reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
      if (response.ok) { await Promise.all([load(), loadCases()]); onChanged(); }
    } finally { setBusy(false); }
  }

  /** Every linkable case, those on the same job first, then the rest newest first — open or closed. */
  function options(row: Row): CaseOption[] {
    const sameJob = row.jobKey ? cases.filter((one) => one.jobKey === row.jobKey) : [];
    return [...sameJob, ...cases.filter((one) => !sameJob.includes(one))];
  }

  return (
    <div role="dialog" aria-modal="true" aria-label={`${label} · ${carrier}`}
      style={css("position:fixed;inset:0;z-index:60;display:flex;justify-content:flex-end")}>
      {/* The backdrop is a button of its own, so closing by clicking beside the panel is also a keyboard target. */}
      <button type="button" aria-label="ปิด" tabIndex={-1} onClick={onClose}
        style={css("position:absolute;inset:0;border:none;padding:0;background:rgba(10,34,64,.35);cursor:default")} />
      <div
        style={css("position:relative;width:min(760px,100%);height:100%;background:#fff;box-shadow:-8px 0 24px rgba(10,34,64,.18);display:flex;flex-direction:column")}>
        <div style={css("padding:14px 18px;border-bottom:1px solid #E9EFF5;display:flex;align-items:flex-start;gap:10px")}>
          <div style={css("flex:1;min-width:0")}>
            <div style={css("font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
            <div style={css("font-size:15px;font-weight:700;color:#0A2240;margin-top:2px")}>{carrier}</div>
            {rows && <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>{rows.length} รายการ</div>}
          </div>
          {onOpenLog && (
            <button type="button" onClick={onOpenLog}
              style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;border-radius:5px;background:#fff;color:#0A2240;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
              Operational Issues →
            </button>
          )}
          <button type="button" onClick={onClose} aria-label="ปิด"
            style={css("height:30px;width:30px;border:1px solid #C9D6E2;border-radius:5px;background:#fff;color:#465A6E;font-size:15px;cursor:pointer")}>×</button>
        </div>

        <div style={css("flex:1;overflow:auto;padding:12px 18px;display:flex;flex-direction:column;gap:10px")}>
          {failure && <div style={css("font-size:12.5px;color:#B42318")}>{failure}</div>}
          {!rows && !failure && <div style={css("font-size:12.5px;color:#94A3B8")}>กำลังอ่าน…</div>}
          {rows && rows.length === 0 && <div style={css("font-size:12.5px;color:#94A3B8")}>ไม่มีรายการในคอลัมน์นี้แล้ว</div>}
          {rows?.map((row) => (
            <div key={row.id} style={css("border:1px solid #E3E8EE;border-radius:6px;padding:10px 12px;display:flex;flex-direction:column;gap:7px")}>
              <div style={css("display:flex;gap:8px;align-items:baseline;flex-wrap:wrap")}>
                <span style={css("font-family:ui-monospace,monospace;font-size:12px;font-weight:700;color:#0A2240")}>{row.code}</span>
                <span style={css("font-family:ui-monospace,monospace;font-size:11px;color:#7B8CA0")}>{row.foundOn} {row.foundAt}</span>
                {row.jobRef && <span style={css("font-size:11px;color:#5A6B7D")}>งาน {row.jobRef}</span>}
                <span style={css("font-size:11px;color:#5A6B7D")}>{[row.category, row.severity, row.source].filter(Boolean).join(" · ")}</span>
                {row.status && <span style={css("margin-left:auto;font-size:11px;color:#7B8CA0")}>{row.status}</span>}
              </div>
              {row.detail && <div style={css("font-size:12px;color:#16232F;white-space:pre-wrap;line-height:1.5")}>{row.detail}</div>}

              <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
                <span style={css("font-size:11px;color:#7B8CA0")}>นับเป็น</span>
                {canEdit ? (
                  <select aria-label={`นับ ${row.code} เป็น`} value={row.chosenColumn || row.column} disabled={busy}
                    onChange={(event) => void send(`/api/issues/${row.id}`, "PATCH", { scorecardColumn: event.target.value }, "บันทึกแล้ว")}
                    style={css("height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 6px;font-size:11.5px;font-family:inherit;max-width:280px")}>
                    {!row.column && <option value="">ยังไม่ระบุชนิดอุบัติเหตุ</option>}
                    {columns.map((one) => <option key={one} value={one}>{one}</option>)}
                  </select>
                ) : (
                  <span style={css("font-size:11.5px;font-weight:600;color:#16232F")}>{row.column || "ยังไม่ระบุชนิดอุบัติเหตุ"}</span>
                )}
                {row.chosenColumn === "" && row.column && <span style={css("font-size:10.5px;color:#94A3B8")}>(ระบบอ่านจากหมวด)</span>}
              </div>

              <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
                <span style={css("font-size:11px;color:#7B8CA0")}>CAR/PAR</span>
                {row.caseId !== null && row.caseReference ? (
                  <>
                    <button type="button" onClick={() => onOpenCase?.(row.caseId!)}
                      style={css("height:26px;padding:0 10px;border:1px solid #D9C8F6;border-radius:13px;background:#F6F1FE;color:#6D28D9;font-size:11.5px;font-weight:700;font-family:ui-monospace,monospace;cursor:pointer")}>
                      {row.caseReference}{row.caseStage ? ` · ${row.caseStage}` : ""} →
                    </button>
                    {canEdit && (
                      <button type="button" disabled={busy} onClick={() => void send(`/api/issues/${row.id}/case`, "PUT", { caseId: null }, "ยกเลิกการผูกแล้ว")}
                        style={css("height:26px;padding:0 9px;border:1px solid #E3E8EE;border-radius:4px;background:#fff;color:#7B8CA0;font-size:11px;cursor:pointer;font-family:inherit")}>
                        ยกเลิกผูก
                      </button>
                    )}
                  </>
                ) : canEdit ? (
                  <>
                    <select aria-label={`ผูก ${row.code} กับ CAR/PAR`} value="" disabled={busy}
                      onChange={(event) => event.target.value && void send(`/api/issues/${row.id}/case`, "PUT", { caseId: Number(event.target.value) }, "ผูกแล้ว")}
                      style={css("height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 6px;font-size:11.5px;font-family:inherit;max-width:300px")}>
                      <option value="">ผูกกับเคสที่มีอยู่…</option>
                      {options(row).map((one) => (
                        <option key={one.id} value={one.id}>
                          {one.reference} · {one.title.slice(0, 50)}{one.stage === "closed" ? " · ปิดแล้ว" : ""}{row.jobKey && one.jobKey === row.jobKey ? " (งานเดียวกัน)" : ""}
                        </option>
                      ))}
                    </select>
                    {onEscalate && (
                      <button type="button" disabled={busy} onClick={() => onEscalate({ id: row.id, code: row.code, detail: row.detail, jobKey: row.jobKey })}
                        style={css("height:28px;padding:0 10px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
                        เปิด CAR/PAR ใหม่
                      </button>
                    )}
                  </>
                ) : (
                  <span style={css("font-size:11.5px;color:#94A3B8")}>ยังไม่ผูก</span>
                )}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
