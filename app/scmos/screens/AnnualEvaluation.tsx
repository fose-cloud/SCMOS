"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import type { PlanPrefill } from "../actionPlanRequest";
import {
  ELIGIBILITY, MOVE_LABEL, STATUS, isBackward, type CampaignRow, type CampaignSummary, type CampaignView, type CarrierRow, type IssuedLink, type ResultRow,
} from "../annualEvaluation";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { Badge, CELL, EMPTY, HEAD, INPUT, LABEL, MONO, Notice, OUTLINE, PANEL, PRIMARY, SMALL, TITLE } from "./ActionPlanParts";
import { AnnualEvaluationSetup } from "./AnnualEvaluationSetup";
import { EvaluationBoard } from "./EvaluationBoard";
import { EvaluationCarrierPanel } from "./EvaluationCarrierPanel";
import { EvaluationDecisions } from "./EvaluationDecisions";
import { EvaluationEvaluators } from "./EvaluationEvaluators";

/**
 * Annual Carrier Evaluation (1 Oct 2026, Phase 5) — in place of the screen that averaged a few scores into a grade.
 * A campaign is one year's evaluation: its rules, its carriers, the evidence each is scored on and the scores. Every
 * figure and every rule is the API's; this screen asks, shows, and sends what a person decided.
 */
export function AnnualEvaluation({ canManage, onToast, onActionPlan }: {
  canManage: boolean;
  onToast: (message: string) => void;
  /** Starts an improvement plan for a carrier from its result; absent without EditActionPlans. */
  onActionPlan?: (prefill: PlanPrefill) => void;
}) {
  const [list, setList] = useState<CampaignRow[] | null>(null);
  const [failure, setFailure] = useState("");
  const [openId, setOpenId] = useState<number | null>(null);
  const [year, setYear] = useState(String(new Date().getFullYear()));
  const [copyFrom, setCopyFrom] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/annual-evaluations", { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as CampaignRow[] | { error?: string } | null;
      if (!response.ok || !Array.isArray(body)) throw new Error((body as { error?: string } | null)?.error ?? `เปิด Annual Evaluation ไม่ได้ (${response.status})`);
      setList(body); setFailure("");
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, []);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function create() {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/annual-evaluations", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ year: Number(year), copyFrom: copyFrom ? Number(copyFrom) : null }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string; id?: number };
      onToast(reply.message ?? reply.error ?? `สร้างไม่สำเร็จ (${response.status})`);
      if (response.ok && reply.id) setOpenId(reply.id);
      await load();
    } finally { setBusy(false); }
  }

  if (openId !== null) {
    return <CampaignScreen id={openId} canManage={canManage} onToast={onToast} onActionPlan={onActionPlan}
      onBack={() => { setOpenId(null); void load(); }} />;
  }
  if (failure) return <Notice tone="#B45309">{failure}</Notice>;
  if (!list) return <Notice tone="#7B8CA0">กำลังโหลด…</Notice>;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {canManage && (
        <div style={css(PANEL + "display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap")}>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>ปี</span>
            <input aria-label="ปี" value={year} onChange={(e) => setYear(e.target.value.replace(/\D/g, "").slice(0, 4))} style={css(INPUT + "width:80px")} />
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>เกณฑ์เริ่มต้น</span>
            <select aria-label="เกณฑ์เริ่มต้น" value={copyFrom} onChange={(e) => setCopyFrom(e.target.value)} style={css(INPUT + "min-width:200px")}>
              <option value="">ค่ามาตรฐานของแผนก</option>
              {list.map((row) => <option key={row.id} value={row.id}>คัดลอกจาก {row.code}</option>)}
            </select>
          </label>
          <button type="button" disabled={busy || year.length !== 4} onClick={() => void create()} style={css(PRIMARY)}>+ สร้างแคมเปญ</button>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["แคมเปญ", "ชื่อ", "ช่วงประเมิน", "รับประเมิน", "ผู้ขนส่ง", "สถานะ"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {list.map((row) => (
                <tr key={row.id} onClick={() => setOpenId(row.id)} style={css("cursor:pointer")}>
                  <td style={css(CELL + MONO + "font-weight:700")}>{row.code}</td>
                  <td style={css(CELL)}>{row.name}</td>
                  <td style={css(CELL + MONO)}>{row.periodStart} → {row.periodEnd}</td>
                  <td style={css(CELL + MONO)}>{row.openOn ?? "—"} → {row.dueOn ?? "—"}</td>
                  <td style={css(CELL + MONO)}>{row.included} / {row.carriers}</td>
                  <td style={css(CELL)}><StatusBadge status={row.status} /></td>
                </tr>
              ))}
              {list.length === 0 && <tr><td colSpan={6} style={css(EMPTY)}>ยังไม่มีแคมเปญ</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}

export function StatusBadge({ status }: { status: string }) {
  const style = STATUS[status] ?? { label: status, tone: "#475569", background: "#F1F5F9" };
  return <Badge label={style.label} tone={style.tone} background={style.background} />;
}

type Tab = "results" | "review" | "carriers" | "evaluators" | "setup";

function CampaignScreen({ id, canManage, onBack, onToast, onActionPlan }: {
  id: number; canManage: boolean; onBack: () => void; onToast: (message: string) => void; onActionPlan?: (prefill: PlanPrefill) => void;
}) {
  const [view, setView] = useState<CampaignView | null>(null);
  const [carriers, setCarriers] = useState<CarrierRow[]>([]);
  const [results, setResults] = useState<ResultRow[]>([]);
  const [summary, setSummary] = useState<CampaignSummary | null>(null);
  const [failure, setFailure] = useState("");
  const [tab, setTab] = useState<Tab>("results");
  const [picked, setPicked] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState("");
  const [register, setRegister] = useState<{ id: number; code: string; name: string }[]>([]);
  const [issued, setIssued] = useState<IssuedLink[]>([]);

  const load = useCallback(async () => {
    try {
      const read = (path: string) => apiFetch(`/api/annual-evaluations/${id}${path}`, { headers: { accept: "application/json" } });
      const [campaign, carrierList, resultList, summaryRead] = await Promise.all([read(""), read("/carriers"), read("/results"), read("/summary")]);
      const body = await campaign.json().catch(() => null) as CampaignView & { error?: string } | null;
      if (!campaign.ok || !body) throw new Error(body?.error ?? `เปิดแคมเปญไม่ได้ (${campaign.status})`);
      setView(body);
      setCarriers(carrierList.ok ? await carrierList.json() as CarrierRow[] : []);
      setResults(resultList.ok ? await resultList.json() as ResultRow[] : []);
      setSummary(summaryRead.ok ? await summaryRead.json() as CampaignSummary : null);
      setFailure("");
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, [id]);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  // The register's carriers, to add one that is not yet approved for every campaign.
  useEffect(() => {
    if (!canManage) return;
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
      const rows = response.ok ? await response.json() as { id: number; code: string; name: string; legalName?: string; isCarrier?: boolean }[] : [];
      if (!cancelled) setRegister(rows.filter((row) => row.isCarrier !== false).map((row) => ({ id: row.id, code: row.code, name: row.legalName || row.name })));
    })();
    return () => { cancelled = true; };
  }, [canManage]);

  /** Sends one change and reads the campaign again. True when the API took it. */
  async function send(path: string, method: "POST" | "PUT", body: unknown): Promise<boolean> {
    if (busy) return false;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/annual-evaluations/${id}${path}`, {
        method, headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      await load();
      return response.ok;
    } finally { setBusy(false); }
  }

  /** A reason, when the API will want one; null when somebody cancelled. */
  function reasonFor(needed: boolean, question: string): string | null {
    if (!needed) return "";
    const answer = window.prompt(question);
    return answer === null ? null : answer.trim();
  }

  /** Leaving the campaign drops any link still on screen; it is never shown again, so ask first. */
  const leave = () => {
    if (issued.length === 0 || window.confirm(`ลิงก์ใหม่ ${issued.length} ลิงก์จะไม่แสดงอีก — ออกจากแคมเปญ?`)) onBack();
  };

  if (failure) return <div style={css("display:flex;flex-direction:column;gap:10px")}><Back onBack={leave} /><Notice tone="#B45309">{failure}</Notice></div>;
  if (!view) return <Notice tone="#7B8CA0">กำลังโหลด…</Notice>;

  const campaign = view.campaign;
  const manage = canManage && view.canManage;
  const closedForEvidence = ["approved", "finalized", "archived"].includes(campaign.status);

  async function move(to: string) {
    if (to === "finalized" && !window.confirm("สรุปผลแล้วจะแก้คะแนนและการตัดสินไม่ได้อีก และผลจะถูกส่งเข้าทะเบียนผู้ขนส่ง — ยืนยัน?")) return;
    const reason = reasonFor(isBackward(campaign.status, to), `เหตุผลที่ย้อนเป็น "${MOVE_LABEL[to] ?? to}"`);
    if (reason === null) return;
    await send("/status", "POST", { status: to, reason });
  }

  async function snapshot() {
    const reason = reasonFor(view!.locked, "แคมเปญเปิดแล้ว — เหตุผลที่สร้าง snapshot ใหม่");
    if (reason === null) return;
    await send("/generate-snapshot", "POST", { reason });
  }

  async function calculate() {
    const reason = reasonFor(["closed", "under-review"].includes(campaign.status) && results.some((row) => row.version > 0), "ปิดรับแล้ว — เหตุผลที่คำนวณใหม่");
    if (reason === null) return;
    await send("/calculate", "POST", { reason });
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css(PANEL + "display:flex;gap:12px;align-items:center;flex-wrap:wrap")}>
        <Back onBack={leave} />
        <div style={css("flex:1;min-width:200px")}>
          <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
            <span style={css(MONO + "font-weight:700;color:#0A2240")}>{campaign.code}</span>
            <StatusBadge status={campaign.status} />
            {view.locked && <Badge label="ล็อกเกณฑ์แล้ว" tone="#6D28D9" background="#F3EEFE" />}
          </div>
          <div style={css(TITLE + "font-size:15px;margin-top:2px")}>{campaign.name}</div>
          <div style={css(LABEL)}>{campaign.periodStart} → {campaign.periodEnd} · System {campaign.systemWeight} / Department {campaign.humanWeight}</div>
        </div>
        {manage && !closedForEvidence && (
          <>
            <button type="button" disabled={busy} onClick={() => void snapshot()} style={css(OUTLINE)}>สร้าง snapshot</button>
            <button type="button" disabled={busy} onClick={() => void calculate()} style={css(OUTLINE)}>คำนวณคะแนน</button>
          </>
        )}
        {view.moves.filter((to) => (["approved", "finalized", "archived"].includes(to) ? view.canDecide : manage)).map((to) => (
          <button key={to} type="button" disabled={busy} onClick={() => void move(to)}
            style={css(isBackward(campaign.status, to) ? OUTLINE : PRIMARY)}>{MOVE_LABEL[to] ?? to}</button>
        ))}
      </div>

      {view.problems.length > 0 && (
        <Notice tone="#B45309">
          <div style={css("font-weight:650;color:#8A4B08;margin-bottom:4px")}>ยังเปิดไม่ได้ · {view.problems.length}</div>
          <ul style={css("margin:0;padding-left:18px;display:flex;flex-direction:column;gap:2px")}>
            {view.problems.map((problem) => <li key={problem}>{problem}</li>)}
          </ul>
        </Notice>
      )}

      <div style={css("display:flex;gap:6px")}>
        {([["results", "ผลการประเมิน"], ["review", "การพิจารณา"], ["carriers", `ผู้ขนส่ง (${view.included})`], ["evaluators", "ผู้ประเมิน"],
          ["setup", "ตั้งค่าเกณฑ์"]] as [Tab, string][]).map(([key, label]) => (
          <button key={key} type="button" onClick={() => setTab(key)}
            style={css(tab === key ? PRIMARY : OUTLINE)}>{label}</button>
        ))}
      </div>

      {tab === "results" && <EvaluationBoard view={view} results={results} summary={summary} onPick={setPicked} />}

      {tab === "review" && (
        <EvaluationDecisions view={view} results={results} summary={summary} busy={busy}
          editable={(manage || view.canDecide) && campaign.status === "under-review"}
          onDecide={(carrier, decision, note) => send(`/carriers/${carrier}/decision`, "PUT", { decision, note })} />
      )}

      {tab === "carriers" && (
        <div style={css("display:flex;flex-direction:column;gap:10px")}>
          {manage && !view.locked && (
            <div style={css(PANEL + "display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
              <button type="button" disabled={busy} onClick={() => void send("/carriers", "POST", { action: "add-eligible" })} style={css(PRIMARY)}>
                + ผู้ขนส่งที่อนุมัติทั้งหมด
              </button>
              <select aria-label="เพิ่มผู้ขนส่ง" value={adding} onChange={(e) => setAdding(e.target.value)} style={css(INPUT + "min-width:240px")}>
                <option value="">เลือกผู้ขนส่งจากทะเบียน…</option>
                {register.filter((row) => carriers.every((one) => one.supplierId !== row.id)).map((row) => (
                  <option key={row.id} value={row.id}>{row.code} · {row.name}</option>
                ))}
              </select>
              <button type="button" disabled={busy || !adding} onClick={() => { void send("/carriers", "POST", { action: "add", supplierIds: [Number(adding)] }); setAdding(""); }} style={css(OUTLINE)}>เพิ่ม</button>
              <button type="button" disabled={busy} onClick={() => void send("/carriers/count", "POST", {})} style={css(OUTLINE)}>นับงานใหม่</button>
            </div>
          )}
          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
            <ZoomBox>
              <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
                <thead><tr>{["ผู้ขนส่ง", "สถานะในทะเบียน", "งาน", "เสร็จสิ้น", "เกณฑ์", "ประเมิน", ""].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
                <tbody>
                  {carriers.map((row) => (
                    <tr key={row.id} style={css(row.included ? "" : "opacity:.55")}>
                      <td style={css(CELL)}><div style={css("font-weight:600")}>{row.name}</div><div style={css(LABEL + MONO)}>{row.code}</div></td>
                      <td style={css(CELL + "font-size:11.5px")}>{row.supplierStatus}</td>
                      <td style={css(CELL + MONO)}>{row.totalJobs ?? "—"}</td>
                      <td style={css(CELL + MONO)}>{row.completedJobs ?? "—"}</td>
                      <td style={css(CELL)}>{ELIGIBILITY[row.eligibility] ?? "—"}</td>
                      <td style={css(CELL + "font-size:11.5px")}>{row.included ? "ประเมิน" : `ไม่ประเมิน — ${row.excludedReason}`}</td>
                      <td style={css(CELL + "white-space:nowrap")}>
                        {manage && !view.locked && (row.included ? (
                          <button type="button" disabled={busy} style={css(SMALL)} onClick={() => {
                            const reason = reasonFor(true, `เหตุผลที่ไม่ประเมิน ${row.name}`);
                            if (reason) void send("/carriers", "POST", { action: "exclude", supplierIds: [row.supplierId], reason });
                          }}>ไม่ประเมิน</button>
                        ) : (
                          <button type="button" disabled={busy} style={css(SMALL)}
                            onClick={() => void send("/carriers", "POST", { action: "include", supplierIds: [row.supplierId] })}>นำกลับมา</button>
                        ))}
                      </td>
                    </tr>
                  ))}
                  {carriers.length === 0 && <tr><td colSpan={7} style={css(EMPTY)}>ยังไม่ได้เลือกผู้ขนส่ง</td></tr>}
                </tbody>
              </table>
            </ZoomBox>
          </div>
        </div>
      )}

      {tab === "evaluators" && <EvaluationEvaluators campaignId={id} view={view} carriers={carriers} manage={manage}
        issued={issued} setIssued={setIssued} onToast={onToast} />}

      {tab === "setup" && (
        <AnnualEvaluationSetup view={view} editable={manage && !view.locked} busy={busy}
          onSave={(path, body) => send(path, "PUT", body)}
          onAddDepartment={async (name) => {
            const response = await apiFetch("/api/annual-evaluations/departments", {
              method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ name, active: true }),
            });
            const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
            onToast(reply.message ?? reply.error ?? `ไม่สำเร็จ (${response.status})`);
            await load();
          }} />
      )}

      {picked !== null && (
        <EvaluationCarrierPanel campaignId={id} carrierId={picked} view={view} canManage={manage && !closedForEvidence}
          onClose={() => setPicked(null)} onChanged={() => void load()} onToast={onToast} onActionPlan={onActionPlan} />
      )}
    </div>
  );
}

function Back({ onBack }: { onBack: () => void }) {
  return <button type="button" onClick={onBack} style={css(OUTLINE)}>‹ รายการแคมเปญ</button>;
}
