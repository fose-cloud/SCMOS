"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { STATUS as PLAN_STATUS, shownStatus } from "../actionPlan";
import { METRIC_LABEL, METRIC_STATUS, shown, type CampaignView, type EvaluationPlan, type ResultDetail, type Snapshot } from "../annualEvaluation";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { Badge, CELL, HEAD, INPUT, LABEL, MONO, OUTLINE, SAVE, TITLE } from "./ActionPlanParts";

/**
 * One carrier of a campaign (Annual Evaluation, Phase 5): its score and how it was reached — each KPI's figure, band
 * score and weight, each department's score — and the evidence behind it, by snapshot version, down to the records.
 * Pricing is assessed here; an improvement plan is started from here, drafted by the API from what the evaluation
 * found (Phase 10), and the plans already opened for the carrier are listed.
 */
export function EvaluationCarrierPanel({ campaignId, carrierId, view, canManage, plans, onClose, onChanged, onToast, onPlan, onOpenPlan }: {
  campaignId: number;
  carrierId: number;
  view: CampaignView;
  canManage: boolean;
  onClose: () => void;
  onChanged: () => void;
  onToast: (message: string) => void;
  /** The Action Plans opened from this carrier's evaluation. */
  plans: EvaluationPlan[];
  /** Starts a plan from the API's draft; absent without EditActionPlans. */
  onPlan?: () => void;
  onOpenPlan?: (planId: number) => void;
}) {
  const [result, setResult] = useState<ResultDetail | null>(null);
  const [resultVersion, setResultVersion] = useState<number | null>(null);
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null);
  const [snapshotVersion, setSnapshotVersion] = useState<number | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [manual, setManual] = useState({ score: "", note: "" });
  const [busy, setBusy] = useState(false);
  const base = `/api/annual-evaluations/${campaignId}/carriers/${carrierId}`;

  const load = useCallback(async () => {
    const [scored, evidence] = await Promise.all([
      apiFetch(`${base}/result${resultVersion ? `?version=${resultVersion}` : ""}`, { headers: { accept: "application/json" } }),
      apiFetch(`${base}/snapshot${snapshotVersion ? `?version=${snapshotVersion}` : ""}`, { headers: { accept: "application/json" } }),
    ]);
    setResult(scored.ok ? await scored.json() as ResultDetail : null);
    setSnapshot(evidence.ok ? await evidence.json() as Snapshot : null);
  }, [base, resultVersion, snapshotVersion]);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    function pressed(event: KeyboardEvent) { if (event.key === "Escape") onClose(); }
    window.addEventListener("keydown", pressed);
    return () => window.removeEventListener("keydown", pressed);
  }, [onClose]);

  const manualKpis = view.kpis.filter((kpi) => kpi.enabled && kpi.method === "manual");
  const departmentName = (id: number) => view.departments.find((row) => row.departmentId === id)?.name ?? String(id);
  const bandLabel = (code: string) => view.scoreBands.find((band) => band.code === code)?.label ?? code;
  const carrier = snapshot?.carrier ?? result?.row.carrier ?? "";

  async function saveManual(kpiCode: string) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`${base}/manual-score`, {
        method: "PUT", headers: { "content-type": "application/json" },
        body: JSON.stringify({ kpiCode, score: Number(manual.score), note: manual.note }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `ไม่สำเร็จ (${response.status})`);
      if (response.ok) { setManual({ score: "", note: "" }); onChanged(); }
    } finally { setBusy(false); }
  }

  return (
    <div role="dialog" aria-modal="true" aria-label={carrier} style={css("position:fixed;inset:0;z-index:60;display:flex;justify-content:flex-end")}>
      <button type="button" aria-label="ปิด" tabIndex={-1} onClick={onClose}
        style={css("position:absolute;inset:0;border:none;padding:0;background:rgba(10,34,64,.35);cursor:default")} />
      <div style={css("position:relative;width:min(900px,100%);height:100%;background:#fff;box-shadow:-8px 0 24px rgba(10,34,64,.18);display:flex;flex-direction:column")}>
        <div style={css("padding:14px 18px;border-bottom:1px solid #E9EFF5;display:flex;gap:10px;align-items:flex-start")}>
          <div style={css("flex:1;min-width:0")}>
            <div style={css(LABEL)}>{view.campaign.code}</div>
            <div style={css("font-size:15px;font-weight:700;color:#0A2240")}>{carrier}</div>
          </div>
          {onPlan && <button type="button" style={css(OUTLINE)} onClick={onPlan}>+ Action Plan</button>}
          <button type="button" onClick={onClose} aria-label="ปิด" style={css("height:30px;width:30px;border:1px solid #C9D6E2;border-radius:5px;background:#fff;color:#465A6E;font-size:15px;cursor:pointer")}>×</button>
        </div>

        <div style={css("flex:1;overflow:auto;padding:14px 18px;display:flex;flex-direction:column;gap:14px")}>
          {plans.length > 0 && (
            <div style={css("display:flex;flex-direction:column;gap:5px")}>
              <div style={css(TITLE)}>แผนพัฒนา · {plans.length}</div>
              {plans.map((plan) => {
                const status = PLAN_STATUS[shownStatus(plan)] ?? { label: plan.status, tone: "#475569", background: "#F1F5F9" };
                return (
                  <button key={plan.planId} type="button" disabled={!onOpenPlan} onClick={() => onOpenPlan?.(plan.planId)}
                    style={css("display:flex;gap:8px;align-items:center;text-align:left;padding:6px 9px;border:1px solid #E3E8EE;border-radius:5px;background:#fff;"
                      + `font-family:inherit;cursor:${onOpenPlan ? "pointer" : "default"}`)}>
                    <span style={css(MONO + "font-weight:700;color:#0A2240")}>{plan.number}</span>
                    <span style={css("flex:1;font-size:12.5px;min-width:0;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>{plan.title}</span>
                    <Badge label={status.label} tone={status.tone} background={status.background} />
                    <span style={css(LABEL + MONO)}>{plan.progress == null ? "—" : `${plan.progress}%`}</span>
                  </button>
                );
              })}
            </div>
          )}
          {/* The score */}
          <section style={css("display:flex;flex-direction:column;gap:8px")}>
            <div style={css("display:flex;gap:10px;align-items:baseline;flex-wrap:wrap")}>
              <span style={css(TITLE)}>คะแนน</span>
              {result && result.versions.length > 1 && (
                <select aria-label="รุ่นของคะแนน" value={resultVersion ?? result.row.version} onChange={(e) => setResultVersion(Number(e.target.value))} style={css(INPUT + "height:26px")}>
                  {result.versions.map((version) => <option key={version} value={version}>คำนวณครั้งที่ {version}</option>)}
                </select>
              )}
              {result?.row.reason && <span style={css(LABEL)}>เหตุผล: {result.row.reason}</span>}
            </div>
            {!result ? <div style={css(LABEL)}>ยังไม่ได้คำนวณ</div> : (
              <>
                <div style={css("display:flex;gap:18px;flex-wrap:wrap;align-items:baseline")}>
                  <Figure label="System" value={shown(result.row.systemScore)} note={`วัดได้ ${shown(result.row.systemWeightAvailable)} / ${result.detail.weights.system}`} />
                  <Figure label="Department" value={shown(result.row.humanScore)} note={`น้ำหนัก ${result.detail.weights.human}`} />
                  <Figure label="คะแนนรวม" value={shown(result.row.finalScore)} note={result.row.band ? bandLabel(result.row.band) : ""} strong />
                </div>
                {result.detail.why && <div style={css("font-size:12px;color:#B45309")}>{result.detail.why}</div>}
                <ZoomBox zoomable={false} capped={false}><table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
                  <thead><tr>{["KPI", "น้ำหนัก", "ค่า", "คะแนน", "นับ"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
                  <tbody>
                    {result.detail.kpis.map((kpi) => (
                      <tr key={kpi.code} style={css(kpi.counted ? "" : "color:#94A3B8")}>
                        <td style={css(CELL)}>{kpi.name}</td>
                        <td style={css(CELL + MONO)}>{shown(kpi.weight)}</td>
                        <td style={css(CELL + MONO)}>{kpi.method === "manual" ? "ประเมินเอง" : kpi.value === null ? (METRIC_STATUS[kpi.metricStatus] || "—") : shown(kpi.value)}</td>
                        <td style={css(CELL + MONO + "font-weight:600")}>{shown(kpi.score)}</td>
                        <td style={css(CELL + "font-size:11px")}>{kpi.counted ? "✓" : kpi.why}</td>
                      </tr>
                    ))}
                  </tbody>
                </table></ZoomBox>
                <ZoomBox zoomable={false} capped={false}><table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
                  <thead><tr>{["แผนก", "น้ำหนัก", "คะแนน", "ผู้ตอบ"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
                  <tbody>
                    {result.detail.departments.map((row) => (
                      <tr key={row.departmentId} style={css(row.score === null ? "color:#94A3B8" : "")}>
                        <td style={css(CELL)}>{departmentName(row.departmentId)}</td>
                        <td style={css(CELL + MONO)}>{shown(row.weight)}</td>
                        <td style={css(CELL + MONO)}>{shown(row.score)}</td>
                        <td style={css(CELL + MONO)}>{row.responses}</td>
                      </tr>
                    ))}
                  </tbody>
                </table></ZoomBox>
              </>
            )}
          </section>

          {canManage && manualKpis.map((kpi) => (
            <section key={kpi.code} style={css("display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap;border:1px solid #E3E8EE;border-radius:6px;padding:10px 12px")}>
              <div style={css(TITLE + "align-self:center")}>{kpi.name}</div>
              <label style={css("display:flex;flex-direction:column;gap:3px")}>
                <span style={css(LABEL)}>คะแนน 0–100</span>
                <input aria-label={`คะแนน ${kpi.name}`} type="number" min={0} max={100} step="any" value={manual.score}
                  onChange={(e) => setManual({ ...manual, score: e.target.value })} style={css(INPUT + "width:80px")} />
              </label>
              <label style={css("display:flex;flex-direction:column;gap:3px;flex:1;min-width:200px")}>
                <span style={css(LABEL)}>เหตุผล</span>
                <input aria-label={`เหตุผล ${kpi.name}`} value={manual.note} onChange={(e) => setManual({ ...manual, note: e.target.value })} style={css(INPUT)} />
              </label>
              <button type="button" disabled={busy || manual.score === "" || manual.note.trim().length < 4} style={css(SAVE)} onClick={() => void saveManual(kpi.code)}>บันทึก</button>
            </section>
          ))}

          {/* The evidence */}
          <section style={css("display:flex;flex-direction:column;gap:8px")}>
            <div style={css("display:flex;gap:10px;align-items:baseline;flex-wrap:wrap")}>
              <span style={css(TITLE)}>หลักฐาน (snapshot)</span>
              {snapshot && snapshot.versions.length > 0 && (
                <select aria-label="รุ่นของ snapshot" value={snapshotVersion ?? snapshot.snapshot?.version ?? ""} onChange={(e) => setSnapshotVersion(Number(e.target.value))}
                  style={css(INPUT + "height:26px")}>
                  {snapshot.versions.map((one) => <option key={one.version} value={one.version}>ครั้งที่ {one.version}{one.current ? " (ปัจจุบัน)" : ""}</option>)}
                </select>
              )}
              {snapshot?.snapshot && <span style={css(LABEL)}>{snapshot.snapshot.generatedBy} · {snapshot.snapshot.generatedAt.slice(0, 16).replace("T", " ")}{snapshot.snapshot.reason ? ` · ${snapshot.snapshot.reason}` : ""}</span>}
            </div>
            {!snapshot?.snapshot ? <div style={css(LABEL)}>ยังไม่ได้สร้าง snapshot</div> : (
              <ZoomBox zoomable={false} capped={false}><table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
                <thead><tr>{["ตัวชี้วัด", "ค่า", "ฐาน", "หมายเหตุ", "รายการ"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
                <tbody>
                  {snapshot.metrics.map((metric) => (
                    <tr key={metric.code}>
                      <td style={css(CELL)}>{METRIC_LABEL[metric.code] ?? metric.code}{metric.formula && <div style={css(LABEL)}>{metric.formula}</div>}</td>
                      <td style={css(CELL + MONO + "font-weight:600")}>
                        {metric.value === null ? <span style={css("color:#B45309")}>{METRIC_STATUS[metric.status] || "—"}</span> : shown(metric.value)}
                        {metric.value !== null && metric.status === "insufficient-data" && <div style={css("font-size:10.5px;color:#B45309")}>ข้อมูลไม่พอ</div>}
                      </td>
                      <td style={css(CELL + MONO)}>{metric.denominator === null ? "" : `${shown(metric.numerator)} / ${shown(metric.denominator)}`}</td>
                      <td style={css(CELL + "font-size:11px;color:#5A6B7D;max-width:280px")}>{metric.note}</td>
                      <td style={css(CELL + "font-size:11px")}>
                        {metric.sources.length > 0 && (
                          open === metric.code
                            ? <span style={css(MONO + "word-break:break-all")}>{metric.sources.join(", ")}</span>
                            : <button type="button" onClick={() => setOpen(metric.code)} style={css("border:none;background:none;padding:0;color:#0A5FA8;cursor:pointer;font:inherit;text-decoration:underline")}>
                              {metric.sources.length} รายการ
                            </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table></ZoomBox>
            )}
          </section>
        </div>
      </div>
    </div>
  );
}

function Figure({ label, value, note, strong = false }: { label: string; value: string; note: string; strong?: boolean }) {
  return (
    <div>
      <div style={css(LABEL)}>{label}</div>
      <div style={css(MONO + `font-size:${strong ? 22 : 16}px;font-weight:700;color:${strong ? "#0A2240" : "#16232F"}`)}>{value}</div>
      {note && <div style={css(LABEL)}>{note}</div>}
    </div>
  );
}
