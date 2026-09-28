"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "../aiControl";
import { ZoomBox } from "../TableFrame";
import {
  AUTONOMY_LABEL, autonomyChoices, costText, GOVERNANCE_STATUS_LABEL, parseGovernance, PLATFORM_CHOICES, PLATFORM_ID,
  saveError, SETTABLE_STATUSES, settingsBody, type AgentGovernance, type GovernanceReport,
} from "../aiGovernance";
import s from "./AiControlTower.module.css";

type Draft = { autonomy: number; shadowMode: boolean; status: string; reason: string };

const TONE: Record<string, string> = { ACTIVE: "green", DEGRADED: "amber", PAUSED: "red", MAINTENANCE: "amber", DISABLED: "muted" };
const tokens = (n: number) => n.toLocaleString("en-US");

/**
 * The Agent Platform's governance, as a section of the AI Control Tower: the
 * platform's execution switch and, per agent, autonomy, shadow mode, status,
 * the last day's health and the cost. An administrator changes a setting with a
 * reason; everybody else allowed here reads it. Every change is audited by the
 * API, which refuses a stale revision rather than overwrite.
 */
export function AiGovernancePanel() {
  const [report, setReport] = useState<GovernanceReport | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [saving, setSaving] = useState("");
  const [message, setMessage] = useState("");
  const alive = useRef(true);

  const load = useCallback(async () => {
    setLoading(true); setError("");
    try {
      const response = await apiFetch("/api/ai/agents", { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(response.status === 403 ? "บัญชีนี้ไม่มีสิทธิ์ดูการกำกับ AI" : "อ่านข้อมูลไม่สำเร็จ");
      const parsed = parseGovernance(body);
      if (alive.current) { setReport(parsed); setDrafts({}); }
    } catch (problem) {
      if (alive.current) setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
    } finally { if (alive.current) setLoading(false); }
  }, []);

  useEffect(() => {
    alive.current = true;
    void Promise.resolve().then(load);
    return () => { alive.current = false; };
  }, [load]);

  function draftOf(agent: AgentGovernance): Draft {
    return drafts[agent.id] ?? { autonomy: agent.autonomy, shadowMode: agent.shadowMode, status: agent.status, reason: "" };
  }
  function edit(id: string, base: Draft, change: Partial<Draft>) {
    setDrafts(all => ({ ...all, [id]: { ...base, ...change } }));
  }

  async function save(id: string, draft: Draft, revision: number) {
    if (saving) return;
    setSaving(id); setMessage("");
    try {
      const response = await apiFetch(`/api/ai/agents/${encodeURIComponent(id)}/settings`, {
        method: "PUT", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
        body: JSON.stringify(settingsBody(draft.autonomy, draft.shadowMode, draft.status, draft.reason, revision)),
      });
      const body: unknown = await response.json().catch(() => null);
      const code = typeof body === "object" && body !== null && "code" in body ? (body as { code: unknown }).code : null;
      if (!response.ok) { if (alive.current) setMessage(saveError(code)); return; }
      if (alive.current) setMessage("บันทึกแล้ว");
      await load();
    } catch {
      if (alive.current) setMessage(saveError("unavailable"));
    } finally { if (alive.current) setSaving(""); }
  }

  const platform = report?.platform;
  const platformDraft = drafts[PLATFORM_ID] ?? (platform ? { autonomy: platform.autonomy, shadowMode: false, status: "ACTIVE", reason: "" } : null);

  return <section className={s.panel} aria-labelledby="ai-governance" data-testid="ai-governance">
    <div className={s.sectionTitle}><div><h2 id="ai-governance">การกำกับ AI</h2>
      {report && <p>Prompt {report.promptVersion} · Circuit breaker {report.breakerDegradedAfter}/{report.breakerPauseAfter} ครั้ง · พัก {report.breakerCoolDownMinutes} นาที</p>}</div>
      <button className={s.button} disabled={loading} onClick={() => void load()}>รีเฟรช</button></div>
    {loading && !report ? <div className={s.empty}>กำลังอ่าน…</div>
      : error ? <p role="alert" className={s.error}>{error}</p>
        : report && platform && platformDraft && <>
          {!report.available && <p role="alert" className={s.error}>อ่านการตั้งค่าไม่ได้ — AI จะไม่ทำงานจนกว่าจะอ่านได้</p>}
          <div className={s.status} role="group" aria-label="การทำงานของ AI ทั้งระบบ">
            <div><strong>AI ทั้งระบบ · {platform.stopped ? "หยุด" : platform.executionEnabled ? "เปิดการทำงาน" : "อ่านและแนะนำเท่านั้น"}</strong>
              {platform.stored && <p>{platform.updatedBy} · {platform.updatedAt ? stamp(platform.updatedAt) : ""}{platform.reason ? " · " + platform.reason : ""}</p>}</div>
            {report.canManage && <div className={s.actions + " " + s.governance}>
              <select aria-label="ระดับการทำงานของ AI ทั้งระบบ" value={platformDraft.autonomy} disabled={!!saving}
                onChange={e => edit(PLATFORM_ID, platformDraft, { autonomy: Number(e.target.value) })}>
                {PLATFORM_CHOICES.map(choice => <option key={choice.autonomy} value={choice.autonomy}>{choice.label}</option>)}
              </select>
              {platformDraft.autonomy !== platform.autonomy && <>
                <input aria-label="เหตุผล" placeholder="เหตุผล" maxLength={200} value={platformDraft.reason}
                  onChange={e => edit(PLATFORM_ID, platformDraft, { reason: e.target.value })} />
                <button className={s.button + " " + s.primary} disabled={!!saving || !platformDraft.reason.trim()}
                  onClick={() => void save(PLATFORM_ID, platformDraft, platform.revision)}>{saving === PLATFORM_ID ? "กำลังบันทึก…" : "บันทึก"}</button>
              </>}
            </div>}
          </div>
          {!!message && <p role="status">{message}</p>}
          {/* Uncapped: this panel stacks with the others, and a capped box would scroll inside a page that scrolls. */}
          <ZoomBox capped={false}>
            <table className={s.governanceTable} aria-label="Agent">
              <thead><tr>
                <th>Agent</th><th>สถานะ</th><th>ระดับ</th><th>Shadow</th><th>ตั้งสถานะ</th>
                <th>รอบ 24 ชม.</th><th>ล้มเหลว</th><th>ติดกัน</th><th>เฉลี่ย ms</th><th>Token 24 ชม.</th><th>ค่าใช้จ่าย 24 ชม. / 30 วัน</th>
                {report.canManage && <th />}
              </tr></thead>
              <tbody>{report.agents.map(agent => {
                const draft = draftOf(agent);
                const dirty = draft.autonomy !== agent.autonomy || draft.shadowMode !== agent.shadowMode || draft.status !== agent.status;
                return <tr key={agent.id}>
                  <td><strong>{agent.name}</strong>{!agent.flagEnabled && <div className={s.meta}>ปิดใน configuration</div>}
                    {agent.stored && <div className={s.meta}>{agent.updatedBy}{agent.reason ? " · " + agent.reason : ""}</div>}</td>
                  <td><span className={s.badge + " " + (s[TONE[agent.effectiveStatus] ?? "muted"] ?? "")}>{GOVERNANCE_STATUS_LABEL[agent.effectiveStatus]}</span></td>
                  <td>{report.canManage
                    ? <select aria-label={`ระดับของ ${agent.name}`} value={draft.autonomy} disabled={!!saving}
                      onChange={e => edit(agent.id, draft, { autonomy: Number(e.target.value) })}>
                      {autonomyChoices(agent).map(n => <option key={n} value={n}>{AUTONOMY_LABEL[n]}</option>)}
                    </select>
                    : AUTONOMY_LABEL[agent.effectiveAutonomy]}</td>
                  <td>{report.canManage
                    ? <input type="checkbox" aria-label={`Shadow ของ ${agent.name}`} checked={draft.shadowMode} disabled={!!saving}
                      onChange={e => edit(agent.id, draft, { shadowMode: e.target.checked })} />
                    : agent.shadowMode ? "เปิด" : "ปิด"}</td>
                  <td>{report.canManage
                    ? <select aria-label={`สถานะของ ${agent.name}`} value={draft.status} disabled={!!saving}
                      onChange={e => edit(agent.id, draft, { status: e.target.value })}>
                      {SETTABLE_STATUSES.map(status => <option key={status} value={status}>{GOVERNANCE_STATUS_LABEL[status]}</option>)}
                    </select>
                    : GOVERNANCE_STATUS_LABEL[agent.status]}</td>
                  <td>{agent.runs24h}</td>
                  <td>{agent.failures24h}{agent.failureRate24h !== null && agent.runs24h > 0 ? ` (${Math.round(agent.failureRate24h * 100)}%)` : ""}</td>
                  <td>{agent.consecutiveFailures}</td>
                  <td>{agent.averageMs ?? "—"}</td>
                  <td>{tokens(agent.usage.inputTokens24h)} / {tokens(agent.usage.outputTokens24h)}</td>
                  <td>{costText(agent.usage.cost24h, report.priceCurrency, report.pricesConfigured)} · {costText(agent.usage.cost30d, report.priceCurrency, report.pricesConfigured)}</td>
                  {report.canManage && <td>{dirty && <div className={s.actions}>
                    <input aria-label={`เหตุผลสำหรับ ${agent.name}`} placeholder="เหตุผล" maxLength={200} value={draft.reason}
                      onChange={e => edit(agent.id, draft, { reason: e.target.value })} />
                    <button className={s.button + " " + s.primary} disabled={!!saving || !draft.reason.trim()}
                      onClick={() => void save(agent.id, draft, agent.revision)}>{saving === agent.id ? "กำลังบันทึก…" : "บันทึก"}</button>
                  </div>}</td>}
                </tr>;
              })}</tbody>
            </table>
          </ZoomBox>
        </>}
  </section>;
}
