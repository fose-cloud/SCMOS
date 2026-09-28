"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "../aiControl";
import {
  AGENT_LABEL, answerBody, answerError, inReadingOrder, parseDecisions, RISK_LABEL, RISK_TONE,
  type Decision, type Finding,
} from "../aiFindings";
import s from "./AiControlTower.module.css";

type Filter = "all" | "otd-agent" | "validation-agent" | "vendor-agent";

function List({ title, items }: { title: string; items: Finding[] }) {
  if (items.length === 0) return null;
  return <div className={s.findingGroup}><strong>{title}</strong>
    <ul className={s.findingList}>{items.map((item, i) => <li key={i}>{item.text}{item.source && <span className={s.findingSource}>{item.source}</span>}</li>)}</ul>
  </div>;
}

/**
 * What the OTD and Validation agents found and nobody has answered yet — the
 * decision log's open rows, most serious first. The four kinds of statement
 * are shown apart. The job's owner, or a supervisor, answers: agreed, did
 * something else (and why), or not relevant. The server decides who may; this
 * only asks.
 */
export function AiFindingsPanel({ onOpenJob }: { onOpenJob: (key: string) => void }) {
  const [items, setItems] = useState<Decision[] | null>(null);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [overriding, setOverriding] = useState<number | null>(null);
  const [choice, setChoice] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [message, setMessage] = useState("");
  const alive = useRef(true);

  const load = useCallback(async () => {
    setError("");
    try {
      const response = await apiFetch("/api/ai/decisions?status=OPEN&pageSize=200", { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(response.status === 403 ? "บัญชีนี้ไม่มีสิทธิ์ดูรายการนี้" : "อ่านข้อมูลไม่สำเร็จ");
      const page = parseDecisions(body);
      if (alive.current) { setItems(inReadingOrder(page.items)); setTotal(page.total); }
    } catch (problem) {
      if (alive.current) setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
    }
  }, []);

  useEffect(() => {
    alive.current = true;
    void Promise.resolve().then(load);
    return () => { alive.current = false; };
  }, [load]);

  async function answer(id: number, outcome: "ACCEPTED" | "OVERRIDDEN" | "DISMISSED") {
    if (busy) return;
    setBusy(id); setMessage("");
    try {
      const response = await apiFetch(`/api/ai/decisions/${id}/outcome`, {
        method: "POST", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
        body: JSON.stringify(outcome === "OVERRIDDEN" ? answerBody(outcome, choice, reason) : answerBody(outcome)),
      });
      const body: unknown = await response.json().catch(() => null);
      const code = typeof body === "object" && body !== null && "code" in body ? (body as { code: unknown }).code : null;
      if (!response.ok) { if (alive.current) setMessage(answerError(code)); if (code === "already_answered") await load(); return; }
      if (alive.current) { setOverriding(null); setChoice(""); setReason(""); setMessage("บันทึกแล้ว"); }
      await load();
    } catch {
      if (alive.current) setMessage(answerError(null));
    } finally { if (alive.current) setBusy(null); }
  }

  const shown = (items ?? []).filter(item => filter === "all" || item.agentId === filter);
  const count = (id: Filter) => (items ?? []).filter(item => id === "all" || item.agentId === id).length;

  return <section className={s.panel} aria-labelledby="ai-findings" data-testid="ai-findings">
    <div className={s.sectionTitle}><div><h2 id="ai-findings">งานที่ AI ตรวจพบ</h2>
      {items && <p>{total} รายการรอคำตอบ</p>}</div>
      <div className={s.actions}>
        {(["all", "otd-agent", "validation-agent", "vendor-agent"] as const).map(id =>
          <button key={id} className={s.button + (filter === id ? " " + s.primary : "")} aria-pressed={filter === id}
            onClick={() => setFilter(id)}>{id === "all" ? "ทั้งหมด" : AGENT_LABEL[id]} {count(id)}</button>)}
        <button className={s.button} onClick={() => void load()}>รีเฟรช</button>
      </div></div>
    {!!message && <p role="status">{message}</p>}
    {error ? <p role="alert" className={s.error}>{error}</p>
      : items === null ? <div className={s.empty}>กำลังอ่าน…</div>
        : shown.length === 0 ? <div className={s.empty}>ไม่มีรายการรอคำตอบ</div>
          : <ul className={s.list}>{shown.map(item => <li key={item.id} className={s.finding}>
            <div className={s.findingTop}>
              <button className={s.link} onClick={() => onOpenJob(item.entityId)}>{item.summary}</button>
              <span className={s.actions}>
                <span className={s.badge + " " + (s[RISK_TONE[item.riskLevel] ?? "muted"] ?? "")}>{RISK_LABEL[item.riskLevel] ?? item.riskLevel}</span>
                <span className={s.badge}>{AGENT_LABEL[item.agentId] ?? item.agentId}</span>
                {item.shadow && <span className={s.badge}>Shadow</span>}
              </span>
            </div>
            <p className={s.findingSource}>{stamp(item.createdAt)}</p>
            <List title="ข้อเท็จจริง" items={item.findings.facts} />
            <List title="ผลตามกฎ" items={item.findings.ruleResults} />
            <List title="ข้อสันนิษฐาน" items={item.findings.inferences} />
            <List title="ข้อแนะนำ" items={item.findings.recommendations} />
            {overriding === item.id
              ? <div className={s.actions + " " + s.governance}>
                <input aria-label="สิ่งที่ทำแทน" placeholder="สิ่งที่ทำแทน" maxLength={400} value={choice} onChange={e => setChoice(e.target.value)} />
                <input aria-label="เหตุผล" placeholder="เหตุผล" maxLength={400} value={reason} onChange={e => setReason(e.target.value)} />
                <button className={s.button + " " + s.primary} disabled={busy !== null || !choice.trim() || !reason.trim()}
                  onClick={() => void answer(item.id, "OVERRIDDEN")}>บันทึก</button>
                <button className={s.button} disabled={busy !== null} onClick={() => setOverriding(null)}>ยกเลิก</button>
              </div>
              : <div className={s.actions}>
                <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "ACCEPTED")}>ถูกต้อง</button>
                <button className={s.button} disabled={busy !== null} onClick={() => { setOverriding(item.id); setChoice(""); setReason(""); }}>ทำอย่างอื่น</button>
                <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "DISMISSED")}>ไม่เกี่ยว</button>
              </div>}
          </li>)}</ul>}
  </section>;
}
