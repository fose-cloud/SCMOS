"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "../aiControl";
import { AGENT_LABEL, parseDecisions, RISK_LABEL, type Decision, type Finding } from "../aiFindings";
import { DECISION_STATUS, DECISION_TYPE, decisionsUrl, EMPTY_DECISION_QUERY, queryProblem, type DecisionQuery } from "../aiHistory";
import s from "./AiControlTower.module.css";

const PAGE_SIZE = 25;

function List({ title, items }: { title: string; items: Finding[] }) {
  if (items.length === 0) return null;
  return <div className={s.findingGroup}><strong>{title}</strong>
    <ul className={s.findingList}>{items.map((item, i) => <li key={i}>{item.text}{item.source && <span className={s.findingSource}>{item.source}</span>}</li>)}</ul>
  </div>;
}

/**
 * The AI history (Agent Platform spec §47): every decision the agents wrote,
 * answered or not, searchable by day, agent, action, result, words or job,
 * customer, carrier and who answered — each opening to why the agent concluded
 * it (facts, rules, inferences), what it rested on, and what people did. A
 * decision a model run made links to that run in AI Activity. Read only.
 */
export function AiHistoryPanel({ onOpenJob, onShowRun }: {
  onOpenJob: (key: string) => void;
  onShowRun?: (runId: string) => void;
}) {
  const [form, setForm] = useState<DecisionQuery>(EMPTY_DECISION_QUERY);
  const [query, setQuery] = useState<DecisionQuery>(EMPTY_DECISION_QUERY);
  const [page, setPage] = useState(1);
  const [items, setItems] = useState<Decision[] | null>(null);
  const [total, setTotal] = useState(0);
  const [error, setError] = useState("");
  const [open, setOpen] = useState<number | null>(null);
  const alive = useRef(true);

  const load = useCallback(async (q: DecisionQuery, p: number) => {
    setError("");
    try {
      const response = await apiFetch(decisionsUrl(q, p, PAGE_SIZE), { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(response.status === 403 ? "บัญชีนี้ไม่มีสิทธิ์ดูรายการนี้"
        : response.status === 400 ? "เงื่อนไขค้นหาไม่ถูกต้อง" : "อ่านข้อมูลไม่สำเร็จ");
      const result = parseDecisions(body);
      if (alive.current) { setItems(result.items); setTotal(result.total); }
    } catch (problem) {
      if (alive.current) setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
    }
  }, []);

  useEffect(() => {
    alive.current = true;
    void Promise.resolve().then(() => load(query, page));
    return () => { alive.current = false; };
  }, [load, query, page]);

  function search() {
    const problem = queryProblem(form);
    if (problem) { setError(problem); return; }
    setOpen(null);
    setPage(1);
    setQuery({ ...form });
  }

  const field = (key: keyof DecisionQuery) => (value: string) => setForm(prev => ({ ...prev, [key]: value }));
  const pages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return <section className={s.panel} aria-labelledby="ai-history" data-testid="ai-history">
    <div className={s.sectionTitle}><div><h2 id="ai-history">ประวัติ AI</h2>
      {items && <p>{total} รายการ</p>}</div></div>
    <form className={s.actions + " " + s.governance} onSubmit={e => { e.preventDefault(); search(); }}>
      <input type="date" aria-label="ตั้งแต่วันที่" value={form.from} onChange={e => field("from")(e.target.value)} />
      <input type="date" aria-label="ถึงวันที่" value={form.to} onChange={e => field("to")(e.target.value)} />
      <select aria-label="Agent" value={form.agent} onChange={e => field("agent")(e.target.value)}>
        <option value="">ทุก Agent</option>
        {Object.entries(AGENT_LABEL).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
      </select>
      <select aria-label="ประเภท" value={form.type} onChange={e => field("type")(e.target.value)}>
        <option value="">ทุกประเภท</option>
        {Object.entries(DECISION_TYPE).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
      </select>
      <select aria-label="ผล" value={form.status} onChange={e => field("status")(e.target.value)}>
        <option value="">ทุกผล</option>
        {Object.entries(DECISION_STATUS).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
      </select>
      <input aria-label="งานหรือข้อความ" placeholder="งาน / ข้อความ" maxLength={80} value={form.q} onChange={e => field("q")(e.target.value)} />
      <input aria-label="ลูกค้า" placeholder="ลูกค้า" maxLength={80} value={form.customer} onChange={e => field("customer")(e.target.value)} />
      <input aria-label="ผู้ขนส่ง" placeholder="ผู้ขนส่ง" maxLength={80} value={form.carrier} onChange={e => field("carrier")(e.target.value)} />
      <input aria-label="ผู้ตอบ" placeholder="ผู้ตอบ" maxLength={80} value={form.decidedBy} onChange={e => field("decidedBy")(e.target.value)} />
      <button type="submit" className={s.button + " " + s.primary}>ค้นหา</button>
      <button type="button" className={s.button} onClick={() => { setForm(EMPTY_DECISION_QUERY); setOpen(null); setPage(1); setQuery(EMPTY_DECISION_QUERY); }}>ล้าง</button>
    </form>
    {error ? <p role="alert" className={s.error}>{error}</p>
      : items === null ? <div className={s.empty}>กำลังอ่าน…</div>
        : items.length === 0 ? <div className={s.empty}>ไม่พบรายการ</div>
          : <ul className={s.list}>{items.map(item => <li key={item.id} className={s.finding}>
            <div className={s.findingTop}>
              <button className={s.link} aria-expanded={open === item.id} onClick={() => setOpen(open === item.id ? null : item.id)}>{item.summary}</button>
              <span className={s.actions}>
                {item.riskLevel && <span className={s.badge}>{RISK_LABEL[item.riskLevel] ?? item.riskLevel}</span>}
                <span className={s.badge}>{AGENT_LABEL[item.agentId] ?? item.agentId}</span>
                <span className={s.badge}>{DECISION_STATUS[item.status] ?? item.status}</span>
              </span>
            </div>
            <p className={s.findingSource}>{stamp(item.createdAt)}{item.decidedAt ? ` · ${item.decidedBy || "—"} ${stamp(item.decidedAt)}` : ""}
              {item.humanChoice ? ` · ${item.humanChoice}` : ""}{item.overrideReason ? ` · ${item.overrideReason}` : ""}</p>
            {open === item.id && <>
              <List title="ข้อเท็จจริง" items={item.findings.facts} />
              <List title="ผลตามกฎ" items={item.findings.ruleResults} />
              <List title="ข้อสันนิษฐาน" items={item.findings.inferences} />
              <List title="ข้อแนะนำ" items={item.findings.recommendations} />
              <List title="หลักฐาน" items={item.evidenceReferences.map(text => ({ text, source: null }))} />
              <div className={s.actions}>
                {item.entityType === "job" && <button className={s.button} onClick={() => onOpenJob(item.entityId)}>เปิดงาน</button>}
                {item.runId && onShowRun && <button className={s.button} onClick={() => onShowRun(item.runId)}>ดูรอบการทำงาน</button>}
              </div>
            </>}
          </li>)}</ul>}
    {items && total > PAGE_SIZE && <div className={s.pagination}>
      <button className={s.button} disabled={page <= 1} onClick={() => { setOpen(null); setPage(page - 1); }}>← ก่อนหน้า</button>
      <span>{page} / {pages}</span>
      <button className={s.button} disabled={page >= pages} onClick={() => { setOpen(null); setPage(page + 1); }}>ถัดไป →</button>
    </div>}
  </section>;
}
