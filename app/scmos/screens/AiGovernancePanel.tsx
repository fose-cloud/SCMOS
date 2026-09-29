"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { stamp, type OperationsControl } from "../aiControl";
import { ZoomBox } from "../TableFrame";
import {
  agreementText, AUTONOMY_LABEL, autonomyChoices, callsModel, costText, GOVERNANCE_STATUS_LABEL, parseGovernance, PASS_LABEL,
  PLATFORM_CHOICES, PLATFORM_ID, saveError, SETTABLE_STATUSES, settingsBody, switchBody, switchOnPlan, type AgentGovernance,
  type GovernanceReport,
} from "../aiGovernance";
import s from "./AiControlTower.module.css";

type Draft = { autonomy: number; shadowMode: boolean; status: string; reason: string };
/** A switch waiting for its confirmation: one agent's, its pass's, Operations' own, or every one still off. */
type Pending = { id: string; target: "agent" | "pass" | "operations"; on: boolean } | { id: "*"; target: "all"; on: true };

const TONE: Record<string, string> = { ACTIVE: "green", DEGRADED: "amber", PAUSED: "red", MAINTENANCE: "amber", DISABLED: "muted" };
const tokens = (n: number) => n.toLocaleString("en-US");

/**
 * The Agent Platform's governance, as a section of the AI Control Tower: the
 * platform's execution switch and, per agent, its on/off switch, autonomy,
 * shadow mode, status, the last day's health and the cost. An administrator
 * changes a setting with a reason, or switches an agent after confirming;
 * everybody else allowed here reads it. Every change is audited by the API,
 * which refuses a stale revision rather than overwrite.
 *
 * Operations keeps its own switch (the one above this section); its row here
 * works the same switch, so the whole team of agents is turned on in one place.
 */
export function AiGovernancePanel({ operations, onOperationsChanged }: {
  operations?: OperationsControl | null; onOperationsChanged?: () => void;
} = {}) {
  const [report, setReport] = useState<GovernanceReport | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [drafts, setDrafts] = useState<Record<string, Draft>>({});
  const [saving, setSaving] = useState("");
  const [message, setMessage] = useState("");
  const [pending, setPending] = useState<Pending | null>(null);
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

  /** One switch sent; the error code when it was refused, null when saved. */
  async function sendSwitch(id: string, target: "agent" | "pass", on: boolean, revision: number): Promise<string | null> {
    const response = await apiFetch(`/api/ai/agents/${encodeURIComponent(id)}/switch`, {
      method: "PUT", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
      body: JSON.stringify(switchBody(target, on, revision)),
    });
    const body: unknown = await response.json().catch(() => null);
    if (response.ok) return null;
    return typeof body === "object" && body !== null && "code" in body && typeof body.code === "string" ? body.code : "unavailable";
  }
  async function sendOperations(on: boolean, revision: number): Promise<string | null> {
    const response = await apiFetch("/api/ai/operations-control", {
      method: "POST", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
      body: JSON.stringify({ enabled: on, revision }),
    });
    const body: unknown = await response.json().catch(() => null);
    if (response.ok) return null;
    return typeof body === "object" && body !== null && "code" in body && typeof body.code === "string" ? body.code : "unavailable";
  }

  async function confirmSwitch() {
    if (!pending || !report || saving) return;
    setSaving("switch"); setMessage("");
    let problem: string | null = null;
    try {
      if (pending.target === "all") {
        if (operations?.canManage && operations.available && !operations.enabled && operations.canEnable)
          problem = await sendOperations(true, operations.revision);
        for (const step of switchOnPlan(report.agents)) {
          if (problem) break;
          problem = await sendSwitch(step.id, step.target, true, step.revision);
        }
      } else if (pending.target === "operations") {
        problem = operations ? await sendOperations(pending.on, operations.revision) : "unavailable";
      } else {
        const agent = report.agents.find(one => one.id === pending.id);
        problem = agent ? await sendSwitch(agent.id, pending.target, pending.on, agent.revision) : "unknown_agent";
      }
      if (alive.current) setMessage(problem ? saveError(problem) : "บันทึกแล้ว");
    } catch {
      if (alive.current) setMessage(saveError("unavailable"));
    } finally {
      if (alive.current) { setSaving(""); setPending(null); }
      if (pending.target === "all" || pending.target === "operations") onOperationsChanged?.();
      await load();
    }
  }

  /** The confirmation, in the row it belongs to: what will happen, and whether it can cost money. */
  function confirmation(label: string, on: boolean, costs: boolean) {
    return <div className={s.actions} role="group" aria-label={`ยืนยัน${on ? "เปิด" : "ปิด"} ${label}`}>
      <span>{on ? "เปิด" : "ปิด"} {label}?{on && costs ? " · มีค่าใช้จ่ายตามการใช้งาน" : ""}</span>
      <button className={s.button + " " + s.primary} disabled={!!saving} onClick={() => void confirmSwitch()}>{saving === "switch" ? "กำลังบันทึก…" : "ยืนยัน"}</button>
      <button className={s.button} disabled={!!saving} onClick={() => setPending(null)}>ยกเลิก</button>
    </div>;
  }

  function toggle(label: string, on: boolean, source: string, disabled: boolean, ask: () => void) {
    return <button className={s.button + " " + s.switch} aria-pressed={on} aria-label={`${label}: ${on ? "เปิด" : "ปิด"}`}
      title={source} disabled={disabled} onClick={() => { setMessage(""); ask(); }}>{on ? "เปิด" : "ปิด"}</button>;
  }

  /** The switch column: a toggle for an administrator, the state for everybody else, a dash where nothing is behind it. */
  function switchCell(agent: AgentGovernance, manage: boolean, aiEnabled: boolean) {
    const busy = !!saving || pending !== null;
    if (agent.id === "operations-agent") {
      const on = operations?.available ? operations.enabled : agent.on;
      if (pending?.target === "operations") return confirmation(agent.name, pending.on, true);
      return operations?.canManage
        ? toggle(agent.name, on, "สวิตช์ Operations AI", busy || !operations.available || (!on && !operations.canEnable),
          () => setPending({ id: agent.id, target: "operations", on: !on }))
        : on ? "เปิด" : "ปิด";
    }
    if (!agent.switchable) return "—";
    const source = (stored: boolean | null) => stored === null ? "ตามค่าใน Azure Portal" : "ตั้งใน AI Control Tower";
    const passLabel = agent.pass ? `${agent.name} · ${PASS_LABEL[agent.pass]}` : "";
    const mine = pending && pending.id === agent.id ? pending : null;
    return <>
      {mine?.target === "agent" ? confirmation(agent.name, mine.on, callsModel(agent.id, "agent"))
        : manage ? toggle(agent.name, agent.on, source(agent.switch), busy || !aiEnabled,
          () => setPending({ id: agent.id, target: "agent", on: !agent.on }))
          : agent.on ? "เปิด" : "ปิด"}
      {agent.pass && <div className={s.meta}>{PASS_LABEL[agent.pass]}{" "}
        {mine?.target === "pass" ? confirmation(passLabel, mine.on, callsModel(agent.id, "pass"))
          : manage ? toggle(passLabel, agent.passOn, source(agent.passSwitch), busy || !aiEnabled,
            () => setPending({ id: agent.id, target: "pass", on: !agent.passOn }))
            : agent.passOn ? "เปิด" : "ปิด"}</div>}
    </>;
  }

  const platform = report?.platform;
  const toTurnOn = report ? switchOnPlan(report.agents).length
    + (operations?.canManage && operations.available && !operations.enabled && operations.canEnable ? 1 : 0) : 0;
  const platformDraft = drafts[PLATFORM_ID] ?? (platform ? { autonomy: platform.autonomy, shadowMode: false, status: "ACTIVE", reason: "" } : null);

  return <section className={s.panel} aria-labelledby="ai-governance" data-testid="ai-governance">
    <div className={s.sectionTitle}><div><h2 id="ai-governance">การกำกับ AI</h2>
      {report && <p>Prompt {report.promptVersion} · Circuit breaker {report.breakerDegradedAfter}/{report.breakerPauseAfter} ครั้ง · พัก {report.breakerCoolDownMinutes} นาที</p>}</div>
      <div className={s.actions}>
        {report?.canManage && report.aiEnabled && <button className={s.button} disabled={loading || !!saving || pending !== null || toTurnOn === 0}
          onClick={() => { setMessage(""); setPending({ id: "*", target: "all", on: true }); }}>เปิดทั้งหมด</button>}
        <button className={s.button} disabled={loading} onClick={() => void load()}>รีเฟรช</button>
      </div></div>
    {pending?.target === "all" && confirmation(`${toTurnOn} รายการ`, true, true)}
    {loading && !report ? <div className={s.empty}>กำลังอ่าน…</div>
      : error ? <p role="alert" className={s.error}>{error}</p>
        : report && platform && platformDraft && <>
          {!report.available && <p role="alert" className={s.error}>อ่านการตั้งค่าไม่ได้ — AI จะไม่ทำงานจนกว่าจะอ่านได้</p>}
          {!report.aiEnabled && <p role="alert" className={s.error}>AI__Enabled ปิดใน Azure Portal — ทุก Agent ปิด</p>}
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
                <th>Agent</th><th>เปิด/ปิด</th><th>สถานะ</th><th>ระดับ</th><th>Shadow</th><th>ตั้งสถานะ</th>
                <th>รอบ 24 ชม.</th><th>ล้มเหลว</th><th>ติดกัน</th><th>เฉลี่ย ms</th><th>Token 24 ชม.</th><th>ค่าใช้จ่าย 24 ชม. / 30 วัน</th><th>ตรงกับคน 30 วัน</th>
                {report.canManage && <th />}
              </tr></thead>
              <tbody>{report.agents.map(agent => {
                const draft = draftOf(agent);
                const dirty = draft.autonomy !== agent.autonomy || draft.shadowMode !== agent.shadowMode || draft.status !== agent.status;
                return <tr key={agent.id}>
                  <td><strong>{agent.name}</strong>
                    {agent.stored && <div className={s.meta}>{agent.updatedBy}{agent.reason ? " · " + agent.reason : ""}</div>}</td>
                  <td>{switchCell(agent, report.canManage, report.aiEnabled)}</td>
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
                  <td>{agreementText(agent)}</td>
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
