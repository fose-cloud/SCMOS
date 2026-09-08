"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { apiFetch } from "../api";
import { ZoomBox } from "../TableFrame";
import type { Screen } from "../nav";
import {
  askBody, availability, controlRequest, ControlError, errorText, EVENT_LABEL, issueTarget, number,
  parseAuditPage, parseAuditRun, parseBrief, parseReply, parseStatus, parseToday, stamp, STATUS_LABEL,
  type AiReply, type AuditRun, type Finding,
} from "../aiControl";
import s from "./AiControlTower.module.css";

/** Private, short-lived state only: no prompt/evidence in localStorage or shared page caches. */
function useRemote<T>(path: string | null, parse: (value: unknown) => T) {
  const [state, setState] = useState<{ data: T | null; error: string; loading: boolean; key: string }>({ data: null, error: "", loading: !!path, key: "" });
  const [revision, setRevision] = useState(0);
  const key = (path ?? "") + ":" + revision;
  const refresh = useCallback(() => setRevision(v => v + 1), []);
  useEffect(() => {
    const controller = new AbortController();
    let alive = true;
    const timer = setTimeout(() => controller.abort(), 30000);
    void (async () => {
      await Promise.resolve();
      if (!alive) return;
      setState({ data: null, error: "", loading: !!path, key });
      if (!path) return;
      try {
        const data = await controlRequest(apiFetch, path, parse, controller.signal);
        if (alive) setState({ data, error: "", loading: false, key });
      } catch (error) {
        if (alive) setState({ data: null, error: errorText(controller.signal.aborted ? new ControlError("timeout") : error), loading: false, key });
      } finally { clearTimeout(timer); }
    })();
    return () => { alive = false; clearTimeout(timer); controller.abort(); };
  }, [path, parse, key]);
  // A new cursor/permission must not paint the preceding request's data, even for one frame.
  return { ...(state.key === key ? state : { data: null, error: "", loading: !!path }), refresh };
}

const URGENCY: Record<Finding["urgency"], [string, string]> = {
  Now: ["ต้องติดตาม", "red"], Soon: ["เตรียมดำเนินการ", "amber"],
  Watch: ["เฝ้าดู", "blue"], Records: ["ตรวจคุณภาพข้อมูล", "muted"],
};
const PROMPTS = ["สรุปงานวันนี้", "งานเสี่ยงวันนี้มีอะไรบ้าง", "มีงานล่าช้าอะไรบ้าง", "ค้นหางานลูกค้า "];
const stateName = (value: string) => Object.hasOwn(STATUS_LABEL, value) ? STATUS_LABEL[value] : value;
const eventName = (value: string) => Object.hasOwn(EVENT_LABEL, value) ? EVENT_LABEL[value] : value;
function Badge({ children, tone = "muted" }: { children: React.ReactNode; tone?: string }) {
  return <span className={[s.badge, s[tone]].filter(Boolean).join(" ")}>{children}</span>;
}
function Empty({ children }: { children: React.ReactNode }) { return <div className={s.empty}>{children}</div>; }
function Failure({ message }: { message: string }) { return <p role="alert" className={s.error}>{message}</p>; }

function RunDetail({ run, onOpenJob }: { run: AuditRun; onOpenJob: (key: string) => void }) {
  return <>
    <h3>รายละเอียดรอบการทำงาน</h3>
    <div className={s.runId}>{run.runId}</div>
    <dl>
      <dt>สถานะ</dt><dd>{stateName(run.status)}</dd>
      <dt>ผู้เรียก</dt><dd>{run.userId} · {run.role}</dd>
      <dt>Agent</dt><dd>{run.agentId || "—"}</dd>
      <dt>Model</dt><dd>{run.model || "—"}</dd>
      <dt>ขอบเขตอ่าน</dt><dd>{run.scope.team ? "งานทั้งทีม" : "งานของผู้รับผิดชอบ: " + (run.scope.operatorId || "ไม่ระบุ")}</dd>
      <dt>เครื่องมือ</dt><dd>{run.tool || "ยังไม่มีการเรียกเครื่องมือ"}{run.view ? " · " + run.view : ""}</dd>
      <dt>ผลจากแหล่งข้อมูล</dt><dd>{number(run.returned)} / {number(run.total)} รายการ</dd>
      <dt>สิทธิ์เขียน</dt><dd>ไม่มี · อ่านอย่างเดียว</dd>
      <dt>Tokens</dt><dd>{run.usage ? number(run.usage.inputTokens) + " เข้า / " + number(run.usage.outputTokens) + " ออก" : "N/A"}</dd>
    </dl>
    <ol className={s.timeline}>
      {run.events.map((event, index) => <li key={index}>
        {eventName(event.event)} · {stateName(event.status)}
        <small>{stamp(event.at)}</small>
      </li>)}
    </ol>
    {run.status === "incomplete" && <p className={s.error}>ไม่พบเหตุการณ์จบรอบ ห้ามถือว่ารอบนี้ทำงานสำเร็จ</p>}
    {!!run.sourceKeys.length && <>
      <p className={s.hint}>งานอ้างอิงที่บันทึกไว้ · เปิดดูข้อมูลปัจจุบันตามสิทธิ์ ไม่ใช่ภาพข้อมูลย้อนหลัง</p>
      <div className={s.actions}>{run.sourceKeys.map(key => <button key={key} className={s.button} onClick={() => onOpenJob(key)}>{key}</button>)}</div>
    </>}
  </>;
}

type Props = {
  canViewDashboard: boolean; canViewAudit: boolean; canViewMonitor: boolean;
  onNavigate: (screen: Screen) => void; onOpenJob: (key: string) => void;
};

export function AiControlTower({ canViewDashboard, canViewAudit, canViewMonitor, onNavigate, onOpenJob }: Props) {
  const status = useRemote("/api/ai/status", parseStatus);
  const board = useRemote(canViewDashboard ? "/api/dashboard/today" : null, parseToday);
  const brief = useRemote(canViewDashboard ? "/api/dashboard/briefing" : null, parseBrief);
  const [beforeId, setBeforeId] = useState<number | null>(null);
  const activity = useRemote(canViewAudit ? "/api/ai/audit?take=8" + (beforeId ? "&beforeId=" + beforeId : "") : null, parseAuditPage);
  const [selectedRun, setSelectedRun] = useState<string | null>(null);
  const detail = useRemote(canViewAudit && selectedRun ? "/api/ai/audit/" + selectedRun : null, parseAuditRun);
  const ready = availability(status.data);
  const input = useRef<HTMLTextAreaElement>(null);
  const askPanel = useRef<HTMLElement>(null);
  const activityPanel = useRef<HTMLElement>(null);
  const request = useRef<AbortController | null>(null);
  const mounted = useRef(false);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [reply, setReply] = useState<AiReply | null>(null);
  const [askError, setAskError] = useState("");
  const [confirmSwitch, setConfirmSwitch] = useState<boolean | null>(null);
  const [switchBusy, setSwitchBusy] = useState(false);
  const [switchMessage, setSwitchMessage] = useState("");
  const switchRequest = useRef<AbortController | null>(null);
  const control = status.data?.operationsControl;
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; request.current?.abort(); request.current = null; switchRequest.current?.abort(); };
  }, []);

  function focusAsk() {
    askPanel.current?.scrollIntoView({ block: "start" });
    input.current?.focus({ preventScroll: true });
  }
  function refresh() {
    status.refresh(); board.refresh(); brief.refresh(); activity.refresh(); detail.refresh();
  }
  async function saveSwitch() {
    if (confirmSwitch === null || switchRequest.current || !control?.canManage || !control.available
      || (confirmSwitch && !control.canEnable)) return;
    const controller = new AbortController();
    switchRequest.current = controller;
    setSwitchBusy(true); setSwitchMessage("");
    const timer = setTimeout(() => controller.abort(), 30000);
    try {
      const response = await apiFetch("/api/ai/operations-control", {
        method: "POST", headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" },
        body: JSON.stringify({ enabled: confirmSwitch, revision: control.revision }), signal: controller.signal,
      });
      const result = await response.json();
      if (!response.ok) throw new ControlError(typeof result.code === "string" ? result.code : "unavailable");
      if (result.saved !== true) throw new ControlError("invalid_response");
      if (mounted.current) setSwitchMessage(confirmSwitch ? "บันทึกเปิด Operations AI แล้ว · อ่านอย่างเดียว" : "บันทึกปิด Operations AI แล้ว · ไม่รับคำถามใหม่");
    } catch (error) {
      if (mounted.current) setSwitchMessage(controller.signal.aborted
        ? "ยังยืนยันผลการบันทึกไม่ได้ กรุณารีเฟรชตรวจสถานะก่อนลองใหม่" : errorText(error));
    } finally {
      clearTimeout(timer); switchRequest.current = null;
      if (mounted.current) { setSwitchBusy(false); setConfirmSwitch(null); status.refresh(); }
    }
  }
  async function submit(event?: FormEvent) {
    event?.preventDefault();
    if (request.current || !ready.ready || !message.trim()) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true); setReply(null); setAskError("");
    // Server allows up to 60 seconds plus bounded audit cleanup. Never auto-retry a POST.
    const timer = setTimeout(() => controller.abort(), 80000);
    try {
      const result = await controlRequest(apiFetch, "/api/ai/chat", parseReply, controller.signal, askBody(message));
      if (mounted.current && request.current === controller) setReply(result);
    } catch (error) {
      if (mounted.current && request.current === controller)
        setAskError(errorText(controller.signal.aborted ? new ControlError("timeout") : error));
    } finally {
      clearTimeout(timer);
      if (mounted.current && request.current === controller) {
        request.current = null; setBusy(false);
        activity.refresh();
      }
    }
  }
  function cancel() {
    request.current?.abort(); request.current = null; setBusy(false);
    setAskError("หยุดรอคำตอบแล้ว เซิร์ฟเวอร์อาจกำลังปิดรอบและบันทึก Audit — ตรวจ Activity ได้ภายหลัง");
    activity.refresh();
  }
  const figures = board.data ? [...board.data.volume, ...board.data.attention] : [];
  const metrics = [
    ["total", "งานตามแผน", "แผนวันที่ " + (board.data?.date || "N/A")],
    ["inTransit", "กำลังวิ่ง", "งานในวันแผนที่แสดง"],
    ["delay", "งานสถานะล่าช้า", "นับตามสถานะงานในวันแผน"],
    ["openCarPar", "CAR/PAR ที่เปิดอยู่", "เคสเปิดทั้งหมด ไม่จำกัดวันแผน"],
  ];
  return <div className={s.tower} data-testid="ai-control-tower">
    <section className={s.hero} aria-label="AI Control Tower">
      <div><div className={s.eyebrow}>SCMOS / Operations intelligence</div>
        <h2>มองภาพรวม แล้วลงมือจากข้อมูลจริง</h2>
        <p>สรุปงานจากระบบ · ถาม Operations AI · ตรวจสอบงานต้นทางและร่องรอยการทำงาน</p>
      </div>
      <div className={s.actions}>
        <button className={s.button} onClick={focusAsk}>Ask SCMOS AI ↓</button>
        <button className={s.button + " " + s.ghost} onClick={() => activityPanel.current?.scrollIntoView({ block: "start" })}>AI Activity ↓</button>
        <button className={s.button + " " + s.ghost} onClick={refresh}>รีเฟรชข้อมูล</button>
      </div>
    </section>
    <div className={s.status} role="status">
      <div><strong>{status.loading ? "กำลังตรวจสถานะ AI…" : ready.title}</strong>
        <p>{status.error || ready.detail}</p></div>
      <Badge tone={ready.tone}>{status.data?.mock ? "MOCK · ไม่ใช่ข้อมูลจริง" : "READ ONLY"}</Badge>
    </div>

    {control?.canManage && <section className={s.status} aria-label="ควบคุม Operations AI">
      <div><strong>Operations AI · {control.available ? control.enabled ? "สวิตช์เปิด" : "สวิตช์ปิด" : "ยังไม่ทราบสถานะสวิตช์"}</strong>
        <p>Administrator เท่านั้น · ใช้กับทุกบัญชีตามสิทธิ์เดิม · ไม่เปิดสิทธิ์เขียนข้อมูลงาน</p>
        <p>การปิดหยุดรับคำถามใหม่ รอบที่เริ่มแล้วอาจทำงานต่อจนจบ · ประวัติการเปลี่ยนอยู่ในเมนู Audit</p>
        {!!control.blockReason && <p>{errorText(new ControlError(control.blockReason))}</p>}
        {!!switchMessage && <p role="status">{switchMessage}</p>}
        {confirmSwitch !== null && <div role="group" aria-label="ยืนยันเปลี่ยนสถานะ Operations AI">
          <p>{confirmSwitch ? "ยืนยันเปิดให้ผู้มีสิทธิ์ส่งคำถามไปยังผู้ให้บริการ AI? อาจมีค่าใช้จ่ายตามการใช้งาน" : "ยืนยันปิดรับคำถาม Operations AI ใหม่สำหรับทุกบัญชี?"}</p>
          <div className={s.actions}>
            <button className={s.button} disabled={switchBusy || (confirmSwitch && !control.canEnable)} onClick={() => void saveSwitch()}>{switchBusy ? "กำลังบันทึก…" : "ยืนยัน"}</button>
            <button className={s.button} disabled={switchBusy} onClick={() => setConfirmSwitch(null)}>ยกเลิก</button>
          </div>
        </div>}
      </div>
      {confirmSwitch === null && <button className={s.button} disabled={switchBusy || !control.available || (!control.enabled && !control.canEnable)}
        onClick={() => { setSwitchMessage(""); setConfirmSwitch(!control.enabled); }}>
        {control.enabled ? "ปิด Operations AI" : "เปิด Operations AI"}
      </button>}
    </section>}

    <section aria-labelledby="ai-morning">
      <div className={s.sectionTitle}><div><h2 id="ai-morning">Morning Brief</h2>
        <p>แหล่งข้อมูล: Dashboard · ขอบเขตตามสิทธิ์ ViewDashboard (ภาพรวมระบบ) · ไม่ได้สร้างโดยโมเดล</p></div>
        <span className={s.hint}>คำนวณเมื่อ {stamp(board.data?.computedAt)}</span>
      </div>
      {!canViewDashboard ? <Empty>บัญชีนี้ไม่มีสิทธิ์ ViewDashboard จึงไม่โหลดสรุปงานหรือคิวติดตาม</Empty> : <>
        {board.error && <Failure message={board.error} />}
        <div className={s.metrics} aria-busy={board.loading}>
          {metrics.map(([id, label, source]) => {
            const f = figures.find(f => f.id === id);
            return <article className={s.metric} key={id}>
              <h3>{label}</h3><strong>{board.loading ? "…" : number(f?.value)}</strong>
              <p>{source}{f?.unit ? " · " + f.unit : ""}</p>
              <p>{f?.note || (board.loading ? "กำลังอ่านข้อมูล" : "ยังไม่มีข้อมูลที่ยืนยันได้")}</p>
            </article>;
          })}
        </div>
      </>}
    </section>

    <div className={s.grid}>
      <div className={s.column}>
        <section className={s.panel} aria-labelledby="ai-priority">
          <div className={s.sectionTitle}><div><h2 id="ai-priority">Priority Queue</h2>
            <p>รายการที่ต้องติดตามจากกฎ Dashboard / Shipment Monitor · ณ {brief.data?.today || "N/A"}</p></div></div>
          {!canViewDashboard ? <Empty>ต้องมีสิทธิ์ ViewDashboard</Empty>
            : brief.loading ? <Empty>กำลังตรวจคิวงาน…</Empty> : brief.error ? <Failure message={brief.error} />
              : brief.data && !brief.data.findings.length ? <Empty>{brief.data.quiet || "ไม่พบหัวข้อที่ต้องติดตามจากกฎนี้"}</Empty>
                : <ul className={s.list}>{brief.data?.findings.map((finding, index) => {
                  const requestedTarget = issueTarget(finding.screen);
                  const target = requestedTarget === "monitoring" && !canViewMonitor ? "myjob" : requestedTarget;
                  return <li key={finding.kind + index} className={s.finding}>
                    <div className={s.findingTop}><Badge tone={URGENCY[finding.urgency][1]}>{URGENCY[finding.urgency][0]}</Badge>
                      <span>{number(finding.count)} รายการ</span></div>
                    <h3>{finding.headline}</h3><p>{finding.detail}</p>
                    {target && <button className={s.link} onClick={() => onNavigate(target)}>
                      {target !== requestedTarget ? "เปิด My Job เพื่อตรวจงาน" : "เปิดข้อมูลต้นทาง"} →</button>}
                  </li>;
                })}</ul>}
          <p className={s.hint}>รายการอาจทับซ้อนกัน ห้ามนำยอดแต่ละหัวข้อมาบวกเป็นจำนวนงานทั้งหมด</p>
        </section>
        <section className={s.panel} aria-labelledby="ai-risk">
          <div className={s.sectionTitle}><div><h2 id="ai-risk">Risk Summary</h2><p>จำนวนหัวข้อในคิวติดตาม ไม่ใช่จำนวนงานหรือคะแนนความเสี่ยง AI</p></div></div>
          <div className={s.risk}>{(["Now", "Soon", "Watch"] as const).map(level => <div key={level}>
            <strong>{number(canViewDashboard && brief.data ? brief.data.findings.filter(f => f.urgency === level).length : null)}</strong>
            {URGENCY[level][0]} · หัวข้อ
          </div>)}</div>
          <p className={s.hint}>ใช้กฎเดิมของระบบ ไม่ได้คาดการณ์ความเสี่ยงใหม่ ขอบเขตวันที่ของ Monitor อาจต่างจากคำถาม “งานเสี่ยงวันนี้” ของ Operations AI</p>
        </section>
      </div>

      <section ref={askPanel} className={s.panel} aria-labelledby="ai-ask">
        <div className={s.sectionTitle}><div><h2 id="ai-ask">Ask SCMOS AI</h2>
          <p>Operations Agent · ใช้ขอบเขตงานที่เซิร์ฟเวอร์อนุญาตให้บัญชีนี้อ่าน</p></div><Badge tone="blue">ไม่มีสิทธิ์เขียน</Badge></div>
        <p className={s.hint}>AI ช่วยเลือกเครื่องมืออ่านข้อมูล ผลลัพธ์อาจไม่ครบหรือคลาดเคลื่อน ตรวจงานอ้างอิงก่อนตัดสินใจ ไม่ส่งข้อมูลลับหรือคีย์เข้ามาในคำถาม</p>
        <form className={s.form} onSubmit={submit}>
          <div className={s.actions}>{PROMPTS.map(prompt => <button key={prompt} type="button" className={s.button} disabled={busy}
            onClick={() => { setMessage(prompt); input.current?.focus(); }}>{prompt.trim()}</button>)}</div>
          <label htmlFor="ai-question">ต้องการตรวจสอบเรื่องอะไร?</label>
          <textarea id="ai-question" ref={input} className={s.textarea} maxLength={4000} value={message} disabled={busy}
            placeholder="เช่น วันนี้มีงานใดต้องติดตาม และควรตรวจข้อมูลอะไรต่อ?"
            aria-describedby="ai-question-help" onChange={event => setMessage(event.target.value)}
            onKeyDown={event => {
              if ((event.ctrlKey || event.metaKey) && event.key === "Enter" && !event.nativeEvent.isComposing) {
                event.preventDefault(); void submit();
              }
            }} />
          <div className={s.formFooter}>
            <span className={s.hint} id="ai-question-help">{message.length.toLocaleString()} / 4,000 · Ctrl / ⌘ + Enter ส่งคำถาม</span>
            <div className={s.actions}>{busy && <button type="button" className={s.button} onClick={cancel}>หยุดรอ</button>}
              <button className={s.primary} type="submit" disabled={!ready.ready || busy || !message.trim()}>{busy ? "กำลังตรวจข้อมูล…" : "ถาม Operations AI →"}</button></div>
          </div>
          {!ready.ready && <p className={s.hint}>{ready.title} · {ready.detail}</p>}
        </form>
        <div aria-live="polite" aria-busy={busy}>
          {askError && <Failure message={askError} />}
          {reply && <div className={s.reply}>
            <Badge tone={reply.mock ? "amber" : "green"}>{reply.mock ? "MOCK · ไม่ได้อ่านงานจริง" : "คำตอบพร้อมหลักฐาน"}</Badge>
            <p className={s.summary}>{reply.summary}</p>
            <div className={s.meta}><span>Run ID: <span className={s.runId}>{reply.runId}</span></span>
              {canViewAudit && !reply.mock && <button className={s.link} onClick={() => {
                setSelectedRun(reply.runId); activityPanel.current?.scrollIntoView({ block: "start" });
              }}>ดู Audit ของคำตอบนี้</button>}</div>
            {reply.evidence && <>
              <div className={s.meta}><span>วันที่อ้างอิง {reply.evidence.asOfDate} · {reply.evidence.timeZone}</span>
                <span>ช่วงข้อมูล: {reply.evidence.window}</span>
                <span>อ่านเมื่อ {stamp(reply.evidence.retrievedAt)}</span>
                <span>ต้นทางปรับปรุงล่าสุด {stamp(reply.evidence.sourceUpdatedAt)}</span></div>
              <p className={s.hint}>{reply.evidence.basis}</p>
              <p className={s.hint}>แสดง {number(reply.evidence.returned)} จาก {number(reply.evidence.total)} งานในผลค้นหา
                {reply.evidence.truncated ? " · แสดงเพียงบางส่วน กรุณาถามให้เจาะจงขึ้น" : ""}</p>
              <p className={s.hint}>งาน active ที่อ่านวันไม่ได้: {number(reply.evidence.undatedActive)} · แถวข้อมูลผิดรูปแบบ: {number(reply.evidence.invalidRows)}</p>
              {reply.evidence.rows.length ? <div className={s.evidence} role="region" aria-label="หลักฐานข้อมูลงาน">
                <ZoomBox height="520px">
                <table><thead><tr><th>งานต้นทาง</th><th>หลักฐาน / สิ่งที่ควรตรวจต่อ</th></tr></thead>
                  <tbody>{reply.evidence.rows.map(row => <tr key={row.key}>
                    <td><button className={s.link} onClick={() => onOpenJob(row.key)}>{row.jobCode || row.key} ↗</button>
                      <p>{row.customer || "ไม่ระบุลูกค้า"} · {row.category}</p><p>{row.date || "ไม่ระบุวันที่"} {row.planTime}</p>
                      <p className={s.hint}>{row.container || "—"} · {row.trucker || "ไม่ระบุผู้ขนส่ง"}</p>
                      <p className={s.hint}>สถานะ: {row.status || "—"}</p></td>
                    <td>{row.risk && <Badge tone="amber">{row.risk}</Badge>}<p>{row.explanation}</p>
                      <p><strong>ตรวจต่อ:</strong> {row.suggestedAction}</p><span className={s.hint}>ที่มา: {row.source}</span></td>
                  </tr>)}</tbody></table>
                </ZoomBox>
              </div> : <Empty>ไม่พบงานในขอบเขตและช่วงวันที่ของคำถามนี้ ไม่ได้หมายความว่าไม่มีงานในระบบ</Empty>}
            </>}
          </div>}
        </div>
        <details className={s.agents}><summary>Agent ที่บัญชีนี้มองเห็น · ตามการตั้งค่าเซิร์ฟเวอร์</summary>
          <ul>{status.data?.agents.map(agent => <li key={agent.id}><span>{agent.name}</span>
            <Badge tone={agent.enabled && agent.connected ? "blue" : "muted"}>{!agent.enabled ? "ปิดอยู่" : !agent.connected ? "ยังไม่เชื่อมต่อ" : "เชื่อมต่อเครื่องมือ"}</Badge></li>)}</ul>
          <p className={s.hint}>การตั้งค่า provider ไม่ใช่ผลตรวจการเชื่อมต่อจริง Phase นี้เปิดให้ถามเฉพาะ Operations Agent</p>
          <button className={s.link} onClick={() => onNavigate("assistant")}>เปิดหน้า AI Assistant เดิม · สิทธิ์และคิวอนุมัติ</button>
        </details>
      </section>
    </div>

    <section ref={activityPanel} className={s.panel} aria-labelledby="ai-activity">
      <div className={s.sectionTitle}><div><h2 id="ai-activity">AI Activity</h2>
        <p>ประวัติถาวรจาก ai_audit_logs · ตามสิทธิ์ ViewAudit · ไม่มีการเก็บข้อความคำถามหรือคำตอบฉบับเต็ม</p></div>
        {canViewAudit && <button className={s.button} onClick={() => { activity.refresh(); detail.refresh(); }}>รีเฟรช Activity</button>}</div>
      {!canViewAudit ? <Empty>บัญชีนี้ไม่มีสิทธิ์ ViewAudit จึงไม่โหลดประวัติการทำงาน</Empty> :
        <div className={s.activityLayout}>
          <div>{activity.loading ? <Empty>กำลังอ่านประวัติ…</Empty> : activity.error ? <Failure message={activity.error} />
            : activity.data?.runs.length === 0 ? <Empty>ยังไม่มีรอบการทำงานที่บันทึกไว้ในหน้านี้</Empty>
              : <ul className={s.list}>{activity.data?.runs.map(run => <li key={run.runId}>
                <button className={s.run} aria-pressed={selectedRun === run.runId} onClick={() => setSelectedRun(run.runId)}>
                  <div className={s.findingTop}><strong>{run.agentId || "AI run"}</strong>
                    <Badge tone={run.status === "succeeded" ? "green" : run.status === "running" ? "blue" : "amber"}>{stateName(run.status)}</Badge></div>
                  <p>{stamp(run.startedAt)} · {run.userId}</p><p className={s.runId}>{run.runId}</p>
                </button></li>)}</ul>}
            <div className={s.pagination}><button className={s.button} disabled={beforeId === null || activity.loading}
              onClick={() => setBeforeId(null)}>กลับรายการล่าสุด</button>
              <button className={s.button} disabled={!activity.data?.nextBeforeId || activity.loading}
                onClick={() => { if (activity.data?.nextBeforeId) setBeforeId(activity.data.nextBeforeId); }}>รายการเก่ากว่า →</button></div>
          </div>
          <div className={s.detail}>{!selectedRun ? <Empty>เลือกรอบการทำงานเพื่อดูผู้เรียก ขอบเขตอ่าน และลำดับเหตุการณ์</Empty>
            : detail.loading ? <Empty>กำลังอ่านรายละเอียด…</Empty> : detail.error ? <Failure message={detail.error} />
              : detail.data && <RunDetail run={detail.data} onOpenJob={onOpenJob} />}</div>
        </div>}
    </section>
  </div>;
}
