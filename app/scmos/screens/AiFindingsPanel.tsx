"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "../aiControl";
import {
  AGENT_LABEL, answerBody, answerError, draftText, inReadingOrder, parseDecisions, RISK_LABEL, RISK_TONE, SENT_CHANNELS,
  type Decision, type Finding,
} from "../aiFindings";
import s from "./AiControlTower.module.css";

type Filter = "all" | "otd-agent" | "validation-agent" | "vendor-agent" | "communication-agent";

function List({ title, items }: { title: string; items: Finding[] }) {
  if (items.length === 0) return null;
  return <div className={s.findingGroup}><strong>{title}</strong>
    <ul className={s.findingList}>{items.map((item, i) => <li key={i}>{item.text}{item.source && <span className={s.findingSource}>{item.source}</span>}</li>)}</ul>
  </div>;
}

/**
 * What the rule-first agents found and nobody has answered yet — the decision
 * log's open rows, most serious first. The four kinds of statement are shown
 * apart. The job's owner, or a supervisor, answers: agreed, did something else
 * (and why), or not relevant. A Communication draft is a message for that
 * person to send: copied, then answered as sent (and on which channel), sent
 * otherwise, or not sent — the record of what went out, since SCMOS sends
 * nothing itself. The server decides who may; this only asks.
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
  const [channel, setChannel] = useState<string>(SENT_CHANNELS[0]);
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

  // A browser may refuse the clipboard (a policy, an embedded view): then the draft is selected, ready for Ctrl+C.
  async function copy(id: number, text: string) {
    try { await navigator.clipboard.writeText(text); setMessage("คัดลอกข้อความแล้ว"); }
    catch {
      const quote = document.getElementById(`draft-${id}`);
      const selection = window.getSelection();
      if (quote && selection) {
        const range = document.createRange();
        range.selectNodeContents(quote);
        selection.removeAllRanges();
        selection.addRange(range);
        setMessage("เลือกข้อความแล้ว กด Ctrl+C เพื่อคัดลอก");
      } else setMessage("คัดลอกไม่สำเร็จ");
    }
  }

  async function answer(id: number, outcome: "ACCEPTED" | "OVERRIDDEN" | "DISMISSED", sentBy = "") {
    if (busy) return;
    setBusy(id); setMessage("");
    try {
      const response = await apiFetch(`/api/ai/decisions/${id}/outcome`, {
        method: "POST", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
        body: JSON.stringify(outcome === "OVERRIDDEN" ? answerBody(outcome, choice, reason) : answerBody(outcome, sentBy)),
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
        {(["all", "otd-agent", "validation-agent", "vendor-agent", "communication-agent"] as const).map(id =>
          <button key={id} className={s.button + (filter === id ? " " + s.primary : "")} aria-pressed={filter === id}
            onClick={() => setFilter(id)}>{id === "all" ? "ทั้งหมด" : AGENT_LABEL[id]} {count(id)}</button>)}
        <button className={s.button} onClick={() => void load()}>รีเฟรช</button>
      </div></div>
    {!!message && <p role="status">{message}</p>}
    {error ? <p role="alert" className={s.error}>{error}</p>
      : items === null ? <div className={s.empty}>กำลังอ่าน…</div>
        : shown.length === 0 ? <div className={s.empty}>ไม่มีรายการรอคำตอบ</div>
          : <ul className={s.list}>{shown.map(item => { const draft = draftText(item); return <li key={item.id} className={s.finding}>
            <div className={s.findingTop}>
              <button className={s.link} onClick={() => onOpenJob(item.entityId)}>{item.summary}</button>
              <span className={s.actions}>
                {item.riskLevel && <span className={s.badge + " " + (s[RISK_TONE[item.riskLevel] ?? "muted"] ?? "")}>{RISK_LABEL[item.riskLevel] ?? item.riskLevel}</span>}
                <span className={s.badge}>{AGENT_LABEL[item.agentId] ?? item.agentId}</span>
                {item.shadow && <span className={s.badge}>Shadow</span>}
              </span>
            </div>
            <p className={s.findingSource}>{stamp(item.createdAt)}</p>
            <List title="ข้อเท็จจริง" items={item.findings.facts} />
            <List title="ผลตามกฎ" items={item.findings.ruleResults} />
            <List title="ข้อสันนิษฐาน" items={item.findings.inferences} />
            {draft === null ? <List title="ข้อแนะนำ" items={item.findings.recommendations} />
              : <div className={s.findingGroup}><strong>ร่างข้อความ</strong>
                <blockquote id={`draft-${item.id}`} className={s.draft}>{draft}</blockquote>
                <button className={s.button} onClick={() => void copy(item.id, draft)}>คัดลอกข้อความ</button>
              </div>}
            {overriding === item.id
              ? <div className={s.actions + " " + s.governance}>
                <input aria-label={draft === null ? "สิ่งที่ทำแทน" : "ข้อความที่ส่งแทน"} placeholder={draft === null ? "สิ่งที่ทำแทน" : "ข้อความที่ส่งแทน"}
                  maxLength={400} value={choice} onChange={e => setChoice(e.target.value)} />
                <input aria-label="เหตุผล" placeholder="เหตุผล" maxLength={400} value={reason} onChange={e => setReason(e.target.value)} />
                <button className={s.button + " " + s.primary} disabled={busy !== null || !choice.trim() || !reason.trim()}
                  onClick={() => void answer(item.id, "OVERRIDDEN")}>บันทึก</button>
                <button className={s.button} disabled={busy !== null} onClick={() => setOverriding(null)}>ยกเลิก</button>
              </div>
              : draft === null ? <div className={s.actions}>
                <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "ACCEPTED")}>ถูกต้อง</button>
                <button className={s.button} disabled={busy !== null} onClick={() => { setOverriding(item.id); setChoice(""); setReason(""); }}>ทำอย่างอื่น</button>
                <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "DISMISSED")}>ไม่เกี่ยว</button>
              </div>
              : <div className={s.actions}>
                <select aria-label="ส่งทาง" value={channel} onChange={e => setChannel(e.target.value)}>
                  {SENT_CHANNELS.map(one => <option key={one} value={one}>{one}</option>)}
                </select>
                <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "ACCEPTED", channel)}>ส่งแล้ว</button>
                <button className={s.button} disabled={busy !== null} onClick={() => { setOverriding(item.id); setChoice(""); setReason(""); }}>ส่งข้อความอื่น</button>
                <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "DISMISSED")}>ไม่ส่ง</button>
              </div>}
          </li>; })}</ul>}
  </section>;
}
