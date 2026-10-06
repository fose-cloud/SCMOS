"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "../aiControl";
import {
  AGENT_LABEL, answerBody, answerError, answerMany, canBatch, draftText, inReadingOrder, parseDecisions, RISK_LABEL, RISK_TONE,
  SENT_CHANNELS, type Decision, type Finding,
} from "../aiFindings";
import { myTasks, parseTasks, TASK_CARDS, type AiTaskCounts } from "../aiTasks";
import { draftFromDecision, labelOf } from "../bookingDraft";
import s from "./AiControlTower.module.css";

type Filter = "mine" | "all" | "otd-agent" | "validation-agent" | "vendor-agent" | "communication-agent" | "booking-agent";
const AGENT_FILTERS = ["otd-agent", "validation-agent", "vendor-agent", "communication-agent", "booking-agent"] as const;

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
 *
 * Above the list, the Control Tower's cards (spec §43), counted by the server;
 * "ของฉัน" is My AI Tasks (§44) — only what this person may answer, by section,
 * the most urgent first. A decision this person may not answer has no buttons.
 */
export function AiFindingsPanel({ onOpenJob, onDraftJob }: {
  onOpenJob: (key: string) => void;
  onDraftJob?: (decisionId: number, cat: string, fields: Record<string, string>) => void;
}) {
  const [items, setItems] = useState<Decision[] | null>(null);
  const [total, setTotal] = useState(0);
  const [tasks, setTasks] = useState<AiTaskCounts | null>(null);
  const [error, setError] = useState("");
  const [filter, setFilter] = useState<Filter>("mine");
  const [overriding, setOverriding] = useState<number | null>(null);
  const [choice, setChoice] = useState("");
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState<number | null>(null);
  const [channel, setChannel] = useState<string>(SENT_CHANNELS[0]);
  const [message, setMessage] = useState("");
  // Several answered at once (6 Oct 2026): which are ticked, the answer waiting for its confirmation, and how far it got.
  const [picked, setPicked] = useState<ReadonlySet<number>>(new Set());
  const [confirming, setConfirming] = useState<"ACCEPTED" | "DISMISSED" | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const alive = useRef(true);

  const load = useCallback(async () => {
    setError("");
    // The cards are extra: when they cannot be read the list still is.
    void apiFetch("/api/ai/tasks", { headers: { accept: "application/json" } })
      .then(async response => { const body: unknown = await response.json().catch(() => null); return response.ok ? parseTasks(body) : null; })
      .catch(() => null)
      .then(counts => { if (alive.current) setTasks(counts); });
    try {
      const response = await apiFetch("/api/ai/decisions?status=OPEN&pageSize=200", { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(response.status === 403 ? "บัญชีนี้ไม่มีสิทธิ์ดูรายการนี้" : "อ่านข้อมูลไม่สำเร็จ");
      const page = parseDecisions(body);
      if (alive.current) {
        setItems(inReadingOrder(page.items)); setTotal(page.total);
        // What was ticked and is no longer open (answered here or elsewhere) drops out of the selection.
        const open = new Set(page.items.filter(canBatch).map(item => item.id));
        setPicked(prev => new Set([...prev].filter(id => open.has(id))));
      }
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

  function toggle(id: number) {
    setConfirming(null);
    setPicked(prev => { const next = new Set(prev); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  }
  /** Ticks every batchable item of a list, or clears them when all already are. */
  function toggleAll(list: readonly Decision[]) {
    const ids = list.filter(canBatch).map(item => item.id);
    setConfirming(null);
    setPicked(prev => {
      const next = new Set(prev);
      if (ids.every(id => next.has(id))) ids.forEach(id => next.delete(id)); else ids.forEach(id => next.add(id));
      return next;
    });
  }
  async function answerPicked(outcome: "ACCEPTED" | "DISMISSED") {
    if (busy !== null || picked.size === 0) return;
    const ids = [...picked];
    setBusy(-1); setConfirming(null); setMessage(""); setProgress(0);
    try {
      const result = await answerMany(ids, async id => {
        const response = await apiFetch(`/api/ai/decisions/${id}/outcome`, {
          method: "POST", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
          body: JSON.stringify(answerBody(outcome)),
        });
        if (response.ok) return null;
        const body: unknown = await response.json().catch(() => null);
        const code = typeof body === "object" && body !== null && "code" in body ? (body as { code: unknown }).code : null;
        return typeof code === "string" ? code : "unavailable";
      }, done => { if (alive.current) setProgress(done); });
      if (alive.current) {
        const label = outcome === "ACCEPTED" ? "ถูกต้อง" : "ไม่เกี่ยว";
        const refused = result.refused.length === 0 ? ""
          : ` · ไม่สำเร็จ ${result.refused.length} รายการ (${answerError(result.refused[0])})`;
        setMessage(`บันทึก "${label}" แล้ว ${result.saved} รายการ${refused}`);
        setPicked(new Set());
      }
      await load();
    } finally { if (alive.current) { setBusy(null); setProgress(null); } }
  }

  const all = items ?? [];
  const sections = myTasks(all);
  const mineCount = sections.reduce((sum, section) => sum + section.items.length, 0);
  const shown = filter === "mine" ? [] : all.filter(item => filter === "all" || item.agentId === filter);
  const count = (id: Exclude<Filter, "mine">) => all.filter(item => id === "all" || item.agentId === id).length;

  // A render function, not a component: a component declared here would be a new type every render,
  // remounting each item — and the override boxes would lose focus on every keystroke.
  function renderItem(item: Decision) {
    const draft = draftText(item);
    const booking = draftFromDecision(item);
    return <li key={item.id} className={s.finding + (picked.has(item.id) ? " " + s.findingPicked : "")}>
      <div className={s.findingTop}>
        <span className={s.pickRow}>
          {canBatch(item) && <input type="checkbox" className={s.pick} aria-label={"เลือก " + item.summary}
            checked={picked.has(item.id)} disabled={busy !== null} onChange={() => toggle(item.id)} />}
          {item.entityType === "job"
            ? <button className={s.link} onClick={() => onOpenJob(item.entityId)}>{item.summary}</button>
            : <strong>{item.summary}</strong>}
        </span>
        <span className={s.actions}>
          {item.riskLevel && <span className={s.badge + " " + (s[RISK_TONE[item.riskLevel] ?? "muted"] ?? "")}>{RISK_LABEL[item.riskLevel] ?? item.riskLevel}</span>}
          <span className={s.badge}>{AGENT_LABEL[item.agentId] ?? item.agentId}</span>
          {item.shadow && <span className={s.badge}>Shadow</span>}
        </span>
      </div>
      <p className={s.findingSource}>{stamp(item.createdAt)}</p>
      {booking
        ? <>
          <List title="ข้อเท็จจริง" items={item.findings.facts.filter(fact => !/\.text#/.test(fact.source ?? ""))} />
          <div className={s.findingGroup}><strong>ร่างงาน {booking.category}</strong>
            <ul className={s.findingList}>{Object.entries(booking.fields).map(([field, value]) =>
              <li key={field}>{labelOf(field)}: {value}{booking.quotes[field] && <span className={s.findingSource}>{booking.quotes[field]}</span>}</li>)}</ul>
          </div>
          <List title="ไม่รับ" items={item.findings.observations.filter(note => note.source === null)} />
        </>
        : <List title="ข้อเท็จจริง" items={item.findings.facts} />}
      <List title="ผลตามกฎ" items={item.findings.ruleResults} />
      <List title="ข้อสันนิษฐาน" items={item.findings.inferences} />
      {draft === null ? <List title="ข้อแนะนำ" items={item.findings.recommendations} />
        : <div className={s.findingGroup}><strong>ร่างข้อความ</strong>
          <blockquote id={`draft-${item.id}`} className={s.draft}>{draft}</blockquote>
          <button className={s.button} onClick={() => void copy(item.id, draft)}>คัดลอกข้อความ</button>
        </div>}
      {!item.canAnswer ? null
        : overriding === item.id
          ? <div className={s.actions + " " + s.governance}>
            <input aria-label={draft === null ? "สิ่งที่ทำแทน" : "ข้อความที่ส่งแทน"} placeholder={draft === null ? "สิ่งที่ทำแทน" : "ข้อความที่ส่งแทน"}
              maxLength={400} value={choice} onChange={e => setChoice(e.target.value)} />
            <input aria-label="เหตุผล" placeholder="เหตุผล" maxLength={400} value={reason} onChange={e => setReason(e.target.value)} />
            <button className={s.button + " " + s.primary} disabled={busy !== null || !choice.trim() || !reason.trim()}
              onClick={() => void answer(item.id, "OVERRIDDEN")}>บันทึก</button>
            <button className={s.button} disabled={busy !== null} onClick={() => setOverriding(null)}>ยกเลิก</button>
          </div>
          : booking ? <div className={s.actions}>
            {onDraftJob && <button className={s.button + " " + s.primary} disabled={busy !== null}
              onClick={() => onDraftJob(item.id, booking.category, booking.fields)}>เปิดฟอร์มเพิ่มงาน</button>}
            <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "ACCEPTED")}>สร้างงานแล้ว</button>
            <button className={s.button} disabled={busy !== null} onClick={() => void answer(item.id, "DISMISSED")}>ไม่ใช่ booking</button>
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
    </li>;
  }

  /** A list's heading line: its name and count, and a box that ticks every item of it that may be answered together. */
  function heading(label: string, list: readonly Decision[]) {
    const batchable = list.filter(canBatch);
    const ticked = batchable.length > 0 && batchable.every(item => picked.has(item.id));
    return <div className={s.listHead}>
      {batchable.length > 0 && <label className={s.pickRow}>
        <input type="checkbox" className={s.pick} checked={ticked} disabled={busy !== null}
          aria-label={"เลือกทั้งหมด " + label} onChange={() => toggleAll(list)} />
        เลือกทั้งหมด</label>}
      {label && <h3>{label} {list.length}</h3>}
    </div>;
  }

  return <section className={s.panel} aria-labelledby="ai-findings" data-testid="ai-findings">
    <div className={s.sectionTitle}><div><h2 id="ai-findings">งานที่ AI ตรวจพบ</h2>
      {items && <p>{total} รายการรอคำตอบ</p>}</div>
      <div className={s.actions}>
        <button className={s.button + (filter === "mine" ? " " + s.primary : "")} aria-pressed={filter === "mine"}
          onClick={() => setFilter("mine")}>ของฉัน {mineCount}</button>
        {(["all", ...AGENT_FILTERS] as const).map(id =>
          <button key={id} className={s.button + (filter === id ? " " + s.primary : "")} aria-pressed={filter === id}
            onClick={() => setFilter(id)}>{id === "all" ? "ทั้งหมด" : AGENT_LABEL[id]} {count(id)}</button>)}
        <button className={s.button} onClick={() => void load()}>รีเฟรช</button>
      </div></div>
    {tasks && <div className={s.metrics} data-testid="ai-task-cards">
      {TASK_CARDS.map(card => <article key={card.key} className={s.metric}>
        <h3>{card.label}</h3>
        <strong className={tasks[card.key] > 0 && card.tone ? (card.tone === "red" ? s.metricRed : s.metricAmber) : undefined}>{tasks[card.key]}</strong>
      </article>)}
    </div>}
    {picked.size > 0 && <div className={s.bulkBar} role="group" aria-label="ตอบหลายรายการ">
      <strong>เลือกแล้ว {picked.size} รายการ</strong>
      {progress !== null ? <span className={s.hint}>กำลังบันทึก {progress} / {picked.size}…</span>
        : confirming ? <>
          <span>ยืนยันตอบ “{confirming === "ACCEPTED" ? "ถูกต้อง" : "ไม่เกี่ยว"}” ทั้ง {picked.size} รายการ?</span>
          <button className={s.button + " " + s.primary} disabled={busy !== null} onClick={() => void answerPicked(confirming)}>ยืนยัน</button>
          <button className={s.button} disabled={busy !== null} onClick={() => setConfirming(null)}>ยกเลิก</button>
        </> : <>
          <button className={s.button} disabled={busy !== null} onClick={() => setConfirming("ACCEPTED")}>ถูกต้อง</button>
          <button className={s.button} disabled={busy !== null} onClick={() => setConfirming("DISMISSED")}>ไม่เกี่ยว</button>
          <button className={s.link} disabled={busy !== null} onClick={() => setPicked(new Set())}>ล้างที่เลือก</button>
        </>}
    </div>}
    {!!message && <p role="status">{message}</p>}
    {error ? <p role="alert" className={s.error}>{error}</p>
      : items === null ? <div className={s.empty}>กำลังอ่าน…</div>
        : filter === "mine"
          ? sections.length === 0 ? <div className={s.empty}>ไม่มีรายการรอคำตอบ</div>
            : sections.map(section => <div key={section.id} className={s.findingGroup}>
              {heading(section.label, section.items)}
              <ul className={s.list}>{section.items.map(renderItem)}</ul>
            </div>)
          : shown.length === 0 ? <div className={s.empty}>ไม่มีรายการรอคำตอบ</div>
            : <>{heading("", shown)}<ul className={s.list}>{shown.map(renderItem)}</ul></>}
  </section>;
}
