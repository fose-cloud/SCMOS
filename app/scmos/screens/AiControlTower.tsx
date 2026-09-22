"use client";

import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { apiFetch } from "../api";
import { ZoomBox } from "../TableFrame";
import type { Screen } from "../nav";
import {
  askBody, availability, communicationAvailability, controlRequest, ControlError, dataAvailability, documentAvailability, engineeringAvailability, errorText, EVENT_LABEL, issueTarget, managementAvailability, number, sreAvailability,
  parseAuditPage, parseAuditRun, parseBrief, parseReply, parseStatus, parseToday, stamp, STATUS_LABEL, WINDOW_LABEL,
  type AgentChoice, type AiReply, type AuditRun, type CollaborationAnswer, type DocumentsAnswer, type EngineeringAnswer, type Finding, type KpiAnswer, type MessagesAnswer, type PlatformAnswer, type SourceAnswer,
} from "../aiControl";
import s from "./AiControlTower.module.css";
import { OperationsChanges } from "./OperationsChanges";
import { agentReadiness } from "../agentReadiness";
import { CHANGE_EXAMPLE, isChangeCommand, parseChangeDraft, parseChangeClarification, type ChangeDraft } from "../operationsChangeCommand";

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
const PROMPTS = ["สรุปงานวันนี้", "งานเสี่ยงวันนี้มีอะไรบ้าง", "งานไหนยังไม่มีรถหรือคนขับ", "งานไหนยังไม่มีผู้ขนส่ง", "งานวันนี้ที่เลยเวลาแล้วยังเงียบ", "มีงานล่าช้าอะไรบ้าง", "ค้นหางานลูกค้า "];
const DATA_PROMPTS = ["KPI เดือนนี้", "KPI เดือนที่แล้ว", "จำนวนงานปีนี้แยกตามผู้ขนส่ง", "KPI เดือนที่แล้วของลูกค้า "];
const MESSAGE_PROMPTS = ["ข้อความวันนี้มีอะไรบ้าง", "ข้อความที่รออนุมัติ", "ข้อความที่จับคู่งานไม่ได้", "ผู้ขนส่งแจ้งอะไรเกี่ยวกับงาน "];
const DOCUMENT_PROMPTS = ["งานไหนเอกสารยังไม่ครบ", "ใบแจ้งหนี้ผู้ขนส่งของงานที่เสร็จแล้ว", "เอกสารผู้ขนส่งหรือคนขับที่ใกล้หมดอายุ", "เอกสารของงาน "];
const DOCUMENT_STATE_LABEL: Record<string, string> = {
  held: "มีแล้ว", unclear: "อ่านไม่ชัด", missing: "ยังไม่มี", blocking: "ยังไม่มี · หยุดงานได้",
  filed_in_time: "ใบแจ้งหนี้ตามกำหนด", filed_late: "ใบแจ้งหนี้เกินกำหนด", due: "ยังไม่มี · ในกำหนด", overdue: "ยังไม่มี · เกินกำหนด",
  expiring: "ใกล้หมดอายุ", expired: "หมดอายุแล้ว",
};
const DOCUMENT_WINDOW_LABEL: Record<string, string> = { all_dates_for_the_job: "ทุกวันที่ของงานนี้", expiry_within_60_days_or_expired: "หมดอายุภายใน 60 วัน หรือหมดแล้ว" };
const SRE_PROMPTS = ["ระบบเป็นอย่างไรบ้างตอนนี้", "การ deploy ล่าสุดผ่านไหม", "วันนี้มีข้อผิดพลาดอะไรบ้าง", "คำขอ API ชั่วโมงล่าสุดช้าตรงไหน", "ข้อผิดพลาด 7 วันที่ผ่านมา"];
const ENGINEERING_PROMPTS = ["GitHub มี issue ที่ยังเปิดอะไรบ้าง", "Pull request ที่ยังเปิดมีอะไรบ้าง", "Commit ล่าสุดของ SCMOS มีอะไรบ้าง",
  "อ่านโค้ดแล้วอธิบายว่ากฎ on-time ของ Lotus อยู่ตรงไหน", "ดูโฟลเดอร์ server/Scmos.Api/Rules มีไฟล์อะไรบ้าง", "อ่านโค้ดแล้ววิเคราะห์สาเหตุที่ "];
const MANAGEMENT_PROMPTS = ["สรุปงานเลขที่ ", "งานล่าช้างานไหนเอกสารยังไม่ครบ"];
const CHANNEL_LABEL: Record<string, string> = { line: "LINE", tms: "TMS", mail: "อีเมล" };
const MESSAGE_STATE_LABEL: Record<string, string> = {
  applied: "นำเข้าแล้ว", waiting: "รออนุมัติ", unmatched: "จับคู่ไม่ได้", ignored: "ไม่เกี่ยวกับงาน", failed: "ไม่สำเร็จ", pending: "ยังไม่ได้อ่าน",
  linked: "จับคู่แล้ว", suggested: "เสนอจับคู่", rejected: "ปฏิเสธการจับคู่",
};
const stateName = (value: string) => Object.hasOwn(STATUS_LABEL, value) ? STATUS_LABEL[value] : value;
const eventName = (value: string) => Object.hasOwn(EVENT_LABEL, value) ? EVENT_LABEL[value] : value;
function Badge({ children, tone = "muted" }: { children: React.ReactNode; tone?: string }) {
  return <span className={[s.badge, s[tone]].filter(Boolean).join(" ")}>{children}</span>;
}
function Empty({ children }: { children: React.ReactNode }) { return <div className={s.empty}>{children}</div>; }
function Failure({ message }: { message: string }) { return <p role="alert" className={s.error}>{message}</p>; }

/** The Data Agent's figure with its provenance: the base it was measured over, the rule, the register's last change. */
function KpiCard({ kpi }: { kpi: KpiAnswer }) {
  const scope = [kpi.filters.customer && "ลูกค้า " + kpi.filters.customer, kpi.filters.trucker && "ผู้ขนส่ง " + kpi.filters.trucker,
    kpi.filters.owner && "เฉพาะงานของ " + kpi.filters.owner].filter(Boolean).join(" · ");
  return <>
    <div className={s.meta}><span>งวด {kpi.periodLabel}{scope ? " · " + scope : ""}</span>
      <span>อ่านเมื่อ {stamp(kpi.retrievedAt)}</span>
      <span>ต้นทางปรับปรุงล่าสุด {stamp(kpi.sourceUpdatedAt)}</span></div>
    <div className={s.risk}>
      <div><strong>{number(kpi.total)}</strong>งานทั้งหมด</div>
      <div><strong>{number(kpi.measured)}</strong>วัดได้ (มีเวลาแผนและเวลาถึง)</div>
      <div><strong>{kpi.measured > 0 ? number(kpi.onTimePercent) + "%" : "N/A"}</strong>ตรงเวลา {number(kpi.onTime)} จาก {number(kpi.measured)}</div>
      <div><strong>{number(kpi.notAssessable)}</strong>วัดไม่ได้</div>
    </div>
    <p className={s.hint}>ไม่มีวันที่ {number(kpi.undated)} · ข้อมูลผิดรูปแบบ {number(kpi.formatErrors)} · ต้องดำเนินการ {number(kpi.actionRequired)}
      {kpi.byCategory.length ? " · " + kpi.byCategory.map(c => `${c.label} ${number(c.value)}`).join(" · ") : ""}</p>
    <p className={s.hint}>กฎ {kpi.rule.id} v{kpi.rule.version} ({kpi.rule.source}) — {kpi.rule.meaning}</p>
    <p className={s.hint}>ข้อมูลไม่ครบ: {kpi.rule.missingData} · สัญญาลูกค้า: {kpi.customerContract === "unknown" ? "ไม่ทราบ — ใช้กฎของแผนก" : kpi.customerContract}</p>
    <p className={s.hint}>{kpi.basis}</p>
    {kpi.carriers.length ? <div className={s.evidence} role="region" aria-label="KPI แยกตามผู้ขนส่ง">
      <ZoomBox height="420px">
      <table>
        <thead><tr><th>ผู้ขนส่ง</th><th>งาน</th><th>วัดได้</th><th>ตรงเวลา</th><th>%</th></tr></thead>
        <tbody>{kpi.carriers.map(row => <tr key={row.carrier}>
          <td>{row.carrier}</td><td>{number(row.total)}</td><td>{number(row.measured)}</td><td>{number(row.onTime)}</td>
          <td>{row.measured > 0 ? number(row.percent) + "%" : "N/A"}</td></tr>)}</tbody>
      </table>
      </ZoomBox>
    </div> : null}
    {kpi.truncated && <p className={s.hint}>แสดง {number(kpi.returned)} จาก {number(kpi.carriersTotal)} ผู้ขนส่ง · ถามให้เจาะจงขึ้นหรือระบุผู้ขนส่ง</p>}
  </>;
}

/** What the carriers said, as the ledger holds it: each message with what the parser read and what the system did. */
function MessagesCard({ answer, onOpenJob }: { answer: MessagesAnswer; onOpenJob: (key: string) => void }) {
  const stateName = (value: string) => Object.hasOwn(MESSAGE_STATE_LABEL, value) ? MESSAGE_STATE_LABEL[value] : value;
  return <>
    <div className={s.meta}><span>วันที่อ้างอิง {answer.asOfDate} · {answer.timeZone}</span>
      <span>ช่วงข้อมูล: {answer.window}</span>
      <span>อ่านเมื่อ {stamp(answer.retrievedAt)}</span></div>
    <div className={s.risk}>
      <div><strong>{number(answer.total)}</strong>ข้อความ</div>
      <div><strong>{number(answer.waiting)}</strong>รออนุมัติ</div>
      <div><strong>{number(answer.applied)}</strong>นำเข้าแล้ว</div>
      <div><strong>{number(answer.unmatched)}</strong>จับคู่ไม่ได้</div>
    </div>
    {answer.jobs.length > 0 && <div className={s.actions}>{answer.jobs.map(job => <button key={job.key} className={s.button} onClick={() => onOpenJob(job.key)}>
      {job.jobCode || job.key} · {job.customer} ↗</button>)}</div>}
    <p className={s.hint}>{answer.basis}</p>
    <p className={s.hint}>แสดง {number(answer.returned)} จาก {number(answer.total)} ข้อความ{answer.truncated ? " · แสดงเพียงบางส่วน กรุณาถามให้เจาะจงขึ้น" : ""}</p>
    {answer.rows.length ? <div className={s.evidence} role="region" aria-label="ข้อความจากผู้ขนส่ง">
      <ZoomBox height="520px">
      <table>
        <thead><tr><th>เมื่อ · จากใคร</th><th>งาน</th><th>ที่ระบบอ่านได้</th><th>ข้อความ</th></tr></thead>
        <tbody>{answer.rows.map(row => <tr key={row.id}>
          <td><Badge tone={row.channel === "mail" ? "blue" : "amber"}>{CHANNEL_LABEL[row.channel] ?? row.channel}</Badge>
            <p>{stamp(row.at)}</p><p className={s.hint}>{row.group || "—"}</p></td>
          <td>{row.jobKey ? <button className={s.link} onClick={() => onOpenJob(row.jobKey)}>{row.jobCode || row.jobKey} ↗</button> : "—"}
            <p className={s.hint}>{row.customer || ""}{row.trucker ? " · " + row.trucker : ""}</p></td>
          <td>
            <p>{[row.status && "สถานะ " + row.status, row.arrival && "ถึง " + row.arrival, row.eta && "คาดถึง " + row.eta, row.plate && "ทะเบียน " + row.plate,
              row.container && "ตู้ " + row.container, row.seal && "ซีล " + row.seal, row.delayed && "ล่าช้า" + (row.delayCategory ? " (" + row.delayCategory + ")" : ""),
              row.question && "เป็นคำถาม"].filter(Boolean).join(" · ") || "—"}</p>
            <p><Badge tone={row.state === "applied" || row.state === "linked" ? "green" : row.state === "waiting" || row.state === "suggested" ? "amber" : "muted"}>{stateName(row.state)}</Badge>
              <span className={s.hint}> {row.detail}</span></p></td>
          <td><p className={s.hint}>{row.excerpt || "—"}</p></td>
        </tr>)}</tbody>
      </table>
      </ZoomBox>
    </div> : <Empty>ไม่มีข้อความในขอบเขตและช่วงเวลาของคำถามนี้</Empty>}
  </>;
}

/** The paperwork as the documents table holds it: each file, each empty folder, each job's invoice standing, judged by the screens' own rules. */
function DocumentsCard({ answer, onOpenJob }: { answer: DocumentsAnswer; onOpenJob: (key: string) => void }) {
  const stateName = (value: string) => Object.hasOwn(DOCUMENT_STATE_LABEL, value) ? DOCUMENT_STATE_LABEL[value] : value;
  const tone = (state: string) => state === "held" || state === "filed_in_time" ? "green"
    : state === "blocking" || state === "overdue" || state === "expired" ? "red"
    : state === "missing" || state === "filed_late" || state === "due" || state === "expiring" || state === "unclear" ? "amber" : "muted";
  const counts = answer.view === "invoice"
    ? [[answer.total, "งานเสร็จ"], [answer.inTime, "ใบแจ้งหนี้ตามกำหนด"], [answer.late, "เกินกำหนด"], [answer.due + answer.overdue, "ยังไม่มีใบแจ้งหนี้"]]
    : answer.view === "expiring" ? [[answer.total, "เอกสาร"], [answer.expired, "หมดอายุแล้ว"], [answer.expiring, "ใกล้หมดอายุ"], [answer.returned, "แสดง"]]
    : [[answer.held, answer.view === "job" ? "ไฟล์ที่มี" : "งานที่ครบ"], [answer.missing, answer.view === "job" ? "โฟลเดอร์ที่ขาด" : "งานที่ยังขาด"], [answer.blocking, "หยุดงานได้"], [answer.unclear, "อ่านไม่ชัด"]];
  return <>
    <div className={s.meta}><span>วันที่อ้างอิง {answer.asOfDate} · {answer.timeZone}</span>
      <span>ช่วงข้อมูล: {Object.hasOwn(DOCUMENT_WINDOW_LABEL, answer.window) ? DOCUMENT_WINDOW_LABEL[answer.window] : answer.window}</span>
      <span>กฎ: {answer.rule}</span>
      <span>อ่านเมื่อ {stamp(answer.retrievedAt)}</span></div>
    <div className={s.risk}>{counts.map(([value, label]) => <div key={label}><strong>{number(value as number)}</strong>{label}</div>)}</div>
    {answer.jobs.length > 0 && answer.view === "job" && <div className={s.actions}>{answer.jobs.map(job => <button key={job.key} className={s.button} onClick={() => onOpenJob(job.key)}>
      {job.jobCode || job.key} · {job.customer} ↗</button>)}</div>}
    <p className={s.hint}>{answer.basis}</p>
    <p className={s.hint}>แสดง {number(answer.returned)} จาก {number(answer.total)} รายการ{answer.truncated ? " · แสดงเพียงบางส่วน กรุณาถามให้เจาะจงขึ้น" : ""}</p>
    {answer.rows.length ? <div className={s.evidence} role="region" aria-label="เอกสาร">
      <ZoomBox height="520px">
      <table>
        <thead><tr><th>{answer.view === "expiring" ? "ของใคร" : "งาน"}</th><th>เอกสาร</th><th>สถานะ</th><th>รายละเอียด</th></tr></thead>
        <tbody>{answer.rows.map(row => <tr key={row.id}>
          <td>{row.jobKey ? <button className={s.link} onClick={() => onOpenJob(row.jobKey)}>{row.jobCode || row.jobKey} ↗</button> : row.owner || "—"}
            <p className={s.hint}>{row.jobKey ? [row.customer, row.trucker, row.date].filter(Boolean).join(" · ") : row.folder}</p></td>
          <td>{row.kind === "document" ? <><p>{row.fileName || "—"}</p><p className={s.hint}>{[row.folder, row.docKind, row.uploadedBy, row.uploadedAt ? stamp(row.uploadedAt) : ""].filter(Boolean).join(" · ")}</p></>
            : <><p>{row.docKind || row.folder || "—"}</p><p className={s.hint}>{row.folder}</p></>}</td>
          <td><Badge tone={tone(row.state)}>{stateName(row.state)}</Badge>{row.daysLeft !== null && <p className={s.hint}>{row.expiryDate}</p>}</td>
          <td><p className={s.hint}>{row.detail || "—"}</p></td>
        </tr>)}</tbody>
      </table>
      </ZoomBox>
    </div> : <Empty>ไม่มีรายการในขอบเขตและช่วงเวลาของคำถามนี้</Empty>}
  </>;
}

function EngineeringCard({ answer }: { answer: EngineeringAnswer }) {
  const label = answer.view === "open_issues" ? "Issue ที่ยังเปิด" : answer.view === "open_prs" ? "Pull request ที่ยังเปิด" : "Commit ล่าสุด";
  return <>
    <div className={s.meta}><span>{answer.repository} · {label}</span><span>อ่านเมื่อ {stamp(answer.retrievedAt)}</span></div>
    <p className={s.hint}>{answer.basis}</p>
    <p className={s.hint}>แสดง {number(answer.returned)} รายการจากหน้าแรกของ GitHub · ไม่ใช่ยอดรวมทั้งหมด</p>
    {answer.rows.length ? <div className={s.evidence} role="region" aria-label="รายการ GitHub ของ SCMOS"><ZoomBox height="420px">
      <table><thead><tr><th>รายการ</th><th>ชื่อเรื่อง</th><th>อัปเดตเมื่อ</th></tr></thead>
        <tbody>{answer.rows.map(row => <tr key={row.id}>
          <td><a className={s.link} href={row.url} target="_blank" rel="noopener noreferrer">{row.id} ↗</a></td>
          <td>{row.title}</td><td>{stamp(row.at)}</td>
        </tr>)}</tbody></table>
    </ZoomBox></div> : <Empty>ไม่พบรายการในหน้าแรกตามมุมมองนี้</Empty>}
  </>;
}

/**
 * What the Engineering Agent read of the source, step by step, and what the
 * model made of it — the analysis is shown under its own label, as the
 * model's opinion, because that is what it is.
 */
function SourceCard({ answer }: { answer: SourceAnswer }) {
  return <>
    <div className={s.meta}><span>{answer.repository} @ {answer.ref}</span><span>อ่าน {number(answer.returned)} ครั้ง</span><span>อ่านเมื่อ {stamp(answer.retrievedAt)}</span></div>
    <p className={s.hint}>{answer.basis}</p>
    <div className={s.evidence} role="region" aria-label="การวิเคราะห์ของโมเดล">
      <p><Badge tone="amber">การวิเคราะห์ของโมเดล</Badge> <span className={s.hint}>ยังไม่ได้ตรวจสอบ ทดสอบ หรือแก้ไข — เป็นความเห็นจากโค้ดที่อ่าน ไม่ใช่ข้อเท็จจริงที่ระบบยืนยัน</span></p>
      <p className={s.summary}>{answer.analysis || "—"}</p>
    </div>
    {answer.steps.map(step => <div key={step.step} className={s.evidence} role="region" aria-label={`ขั้นที่ ${step.step}`}>
      <p><Badge tone="blue">ขั้นที่ {step.step} · {step.mode === "list" ? "ดูโฟลเดอร์" : "อ่านไฟล์"}</Badge> <code className={s.runId}>{step.path || "/"}</code>
        {step.mode === "file" && step.returned > 0 && <span className={s.hint}> · บรรทัด {number(step.from)}–{number(step.from + step.returned - 1)} จาก {number(step.totalLines)}{step.truncated ? " · ยังมีต่อ" : ""}</span>}
        {step.mode === "list" && <span className={s.hint}> · {number(step.returned)} จาก {number(step.totalLines)} รายการ{step.truncated ? " · แสดงบางส่วน" : ""}</span>}</p>
      {step.mode === "list"
        ? (step.entries.length ? <ul>{step.entries.map(entry => <li key={entry.path}><code className={s.runId}>{entry.kind === "dir" ? "[dir] " : ""}{entry.path}</code>{entry.kind === "file" ? <span className={s.hint}> · {number(entry.size)} B</span> : null}</li>)}</ul> : <p className={s.hint}>{step.text || "ไม่มีรายการ"}</p>)
        : <ZoomBox height="360px" zoomable={false}><pre className={s.runId} style={{ whiteSpace: "pre", margin: 0, padding: "8px 10px" }}>{step.text || "—"}</pre></ZoomBox>}
    </div>)}
  </>;
}

/** The platform's condition as the API measured it: each signal with its state, and the basis saying what was not measured and what was not done. */
function PlatformCard({ answer }: { answer: PlatformAnswer }) {
  const tone = (state: string) => state === "ok" ? "green" : state === "warn" ? "amber" : state === "bad" ? "red" : "muted";
  const stateName = (state: string) => state === "ok" ? "ปกติ" : state === "warn" ? "ควรดู" : state === "bad" ? "ผิดปกติ" : "ไม่ทราบ";
  const label = answer.view === "health" ? "สุขภาพระบบ" : answer.view === "deployments" ? "การ deploy ล่าสุด" : answer.view === "requests" ? "คำขอ API ชั่วโมงล่าสุด" : "ข้อผิดพลาด";
  return <>
    <div className={s.meta}><span>{label} · {answer.window}</span><span>อ่านเมื่อ {stamp(answer.retrievedAt)}</span></div>
    <div className={s.risk}>
      <div><strong>{number(answer.rows.filter(r => r.state === "ok").length)}</strong>ปกติ</div>
      <div><strong>{number(answer.rows.filter(r => r.state === "warn").length)}</strong>ควรดู</div>
      <div><strong>{number(answer.rows.filter(r => r.state === "bad").length)}</strong>ผิดปกติ</div>
      <div><strong>{number(answer.rows.filter(r => r.state === "unknown").length)}</strong>ไม่ทราบ</div>
    </div>
    <p className={s.hint}>{answer.basis}</p>
    {answer.rows.length ? <div className={s.evidence} role="region" aria-label={label}><ZoomBox height="420px" zoomable={false}>
      <table><thead><tr><th>สัญญาณ</th><th>ค่า</th><th>สถานะ</th><th>รายละเอียด</th></tr></thead>
        <tbody>{answer.rows.map(row => <tr key={row.id}>
          <td>{row.label}<p className={s.hint}>{row.id}</p></td>
          <td>{row.value}</td>
          <td><Badge tone={tone(row.state)}>{stateName(row.state)}</Badge>{row.at && <p className={s.hint}>{stamp(row.at)}</p>}</td>
          <td><p className={s.hint}>{row.detail || "—"}</p></td>
        </tr>)}</tbody></table>
    </ZoomBox></div> : <Empty>ไม่มีสัญญาณในมุมมองนี้</Empty>}
  </>;
}

function CollaborationCard({ answer, onOpenJob }: { answer: CollaborationAnswer; onOpenJob: (key: string) => void }) {
  return <div className={s.evidence} role="region" aria-label="แผนและหลักฐานข้ามผู้เชี่ยวชาญ">
    <h3>{answer.title} · {number(answer.steps)} ขั้น</h3>
    <p className={s.hint}>อ่านเมื่อ {stamp(answer.retrievedAt)} · สรุปนี้ประกอบจากข้อมูลที่แต่ละ Agent มีสิทธิ์อ่าน ไม่ใช่คำตอบที่โมเดลคำนวณเอง</p>
    <ol className={s.timeline}>{answer.trail.map(step => <li key={step.step}>
      ขั้น {step.step} · {step.agentId} · {step.purpose} · อ่าน {number(step.returned)} จาก {number(step.total)} รายการ{step.truncated ? " (บางส่วน)" : ""}
    </li>)}</ol>
    {answer.findings.map(finding => <div key={finding.id}>
      <strong>{finding.label}: {finding.value}</strong><p className={s.hint}>{finding.detail}</p>
      {!!finding.jobKeys.length && <div className={s.actions}>{finding.jobKeys.slice(0, 10).map(key => <button key={key} className={s.link} onClick={() => onOpenJob(key)}>{key} ↗</button>)}</div>}
    </div>)}
    <p className={s.hint}>{answer.basis}</p>
    <p className={s.hint}>งานที่อยู่ในสองรายการไม่ได้ยืนยันว่าสิ่งหนึ่งเป็นสาเหตุของอีกสิ่งหนึ่ง</p>
  </div>;
}

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
      <dt>เครื่องมือ</dt><dd>{run.tool || "ยังไม่มีการเรียกเครื่องมือ"}{run.view ? " · " + run.view : ""}{run.steps ? ` · ${run.steps} ขั้น` : ""}</dd>
      {run.correlationId && <><dt>Correlation</dt><dd className={s.runId}>{run.correlationId}</dd></>}
      <dt>ผลจากแหล่งข้อมูล</dt><dd>{number(run.returned)} / {number(run.total)} รายการ</dd>
      <dt>สิทธิ์เขียน</dt><dd>ไม่มี · อ่านอย่างเดียว</dd>
      <dt>Tokens</dt><dd>{run.usage ? number(run.usage.inputTokens) + " เข้า / " + number(run.usage.outputTokens) + " ออก" : "N/A"}</dd>
    </dl>
    <ol className={s.timeline}>
      {run.events.map((event, index) => <li key={index}>
        {eventName(event.event)} · {stateName(event.status)}{event.step && event.event !== "run_completed" ? ` · ขั้น ${event.step}` : ""}
        <small>{stamp(event.at)}</small>
      </li>)}
    </ol>
    {run.status === "incomplete" && <p className={s.error}>ไม่พบเหตุการณ์จบรอบ ห้ามถือว่ารอบนี้ทำงานสำเร็จ</p>}
    {!!run.sourceKeys.length && <>
      <p className={s.hint}>{["query_shipments", "search_shipment", "query_delays", "query_followup"].includes(run.tool || "")
        ? "งานอ้างอิงที่บันทึกไว้ · เปิดดูข้อมูลปัจจุบันตามสิทธิ์ ไม่ใช่ภาพข้อมูลย้อนหลัง" : "รหัสหลักฐานของขั้นสุดท้าย · ไม่ใช่ Job key"}</p>
      <div className={s.actions}>{run.sourceKeys.map(key => ["query_shipments", "search_shipment", "query_delays", "query_followup"].includes(run.tool || "")
        ? <button key={key} className={s.button} onClick={() => onOpenJob(key)}>{key}</button>
        : <span key={key} className={s.hint}>{key}</span>)}</div>
    </>}
  </>;
}

type Props = {
  canViewDashboard: boolean; canViewAudit: boolean; canViewMonitor: boolean;
  onNavigate: (screen: Screen) => void; onOpenJob: (key: string) => void;
  /**
   * A question brought from another screen — the dashboard's rail — placed in
   * the box and focused, not sent. Sending is still the person's click, so the
   * audit trail never carries a question nobody pressed the button on.
   */
  initialQuestion?: string;
  onQuestionTaken?: () => void;
};

export function AiControlTower({ canViewDashboard, canViewAudit, canViewMonitor, onNavigate, onOpenJob, initialQuestion, onQuestionTaken }: Props) {
  const status = useRemote("/api/ai/status", parseStatus);
  const board = useRemote(canViewDashboard ? "/api/dashboard/today" : null, parseToday);
  const brief = useRemote(canViewDashboard ? "/api/dashboard/briefing" : null, parseBrief);
  const [beforeId, setBeforeId] = useState<number | null>(null);
  const activity = useRemote(canViewAudit ? "/api/ai/audit?take=8" + (beforeId ? "&beforeId=" + beforeId : "") : null, parseAuditPage);
  const [selectedRun, setSelectedRun] = useState<string | null>(null);
  const detail = useRemote(canViewAudit && selectedRun ? "/api/ai/audit/" + selectedRun : null, parseAuditRun);
  const ready = availability(status.data);
  const dataReady = dataAvailability(status.data);
  const messagesReady = communicationAvailability(status.data);
  const documentsReady = documentAvailability(status.data);
  const engineeringReady = engineeringAvailability(status.data);
  const sreReady = sreAvailability(status.data);
  const managementReady = managementAvailability(status.data);
  // Which specialist the question goes to: Operations (the default) or, when
  // the account has one, the Data Agent (Phase 2), the Communication Agent
  // (Phase 4) or the Document & Invoice Agent (Phase 5). The server decides
  // what any may read; this only chooses the door.
  const [agent, setAgent] = useState<AgentChoice>("operations-agent");
  const asking = agent === "data-agent" ? dataReady : agent === "communication-agent" ? messagesReady
    : agent === "document-agent" ? documentsReady : agent === "engineering-agent" ? engineeringReady : agent === "sre-agent" ? sreReady
    : agent === "management-agent" ? managementReady : ready;
  const doors = (["operations-agent", "data-agent", "communication-agent", "document-agent", "engineering-agent", "sre-agent", "management-agent"] as const)
    .filter(id => id === "operations-agent" || status.data?.agents.some(a => a.id === id));
  const input = useRef<HTMLTextAreaElement>(null);
  const askPanel = useRef<HTMLElement>(null);
  const activityPanel = useRef<HTMLElement>(null);
  const request = useRef<AbortController | null>(null);
  const mounted = useRef(false);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const [reply, setReply] = useState<AiReply | null>(null);
  const [changeDraft, setChangeDraft] = useState<ChangeDraft | null>(null);
  const [draftRevision, setDraftRevision] = useState(0);
  const [changeBusy, setChangeBusy] = useState(false);
  const draftPanel = useRef<HTMLDivElement>(null);
  const [askError, setAskError] = useState("");
  const [clarification, setClarification] = useState("");
  const [confirmSwitch, setConfirmSwitch] = useState<boolean | null>(null);
  const [switchBusy, setSwitchBusy] = useState(false);
  const [switchMessage, setSwitchMessage] = useState("");
  const switchRequest = useRef<AbortController | null>(null);
  const control = status.data?.operationsControl;
  useEffect(() => {
    mounted.current = true;
    return () => { mounted.current = false; request.current?.abort(); request.current = null; switchRequest.current?.abort(); };
  }, []);

  // Placed in the box while rendering, the way Chrome closes its drawer: React
  // re-runs this pass before painting, so the box never shows empty first.
  const [seeded, setSeeded] = useState("");
  if (initialQuestion && initialQuestion !== seeded) {
    setSeeded(initialQuestion);
    setMessage(initialQuestion.slice(0, 4000));
  }
  useEffect(() => {
    if (!initialQuestion) return;
    onQuestionTaken?.();
    askPanel.current?.scrollIntoView({ block: "start" });
    input.current?.focus({ preventScroll: true });
  }, [initialQuestion, onQuestionTaken]);

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
    if (request.current || changeBusy || (!asking.ready && !(agent === "operations-agent" && isChangeCommand(message))) || !message.trim()) return;
    const controller = new AbortController();
    request.current = controller;
    setBusy(true); setReply(null); setAskError(""); setClarification("");
    // Server allows up to 60 seconds plus bounded audit cleanup. Never auto-retry a POST.
    const timer = setTimeout(() => controller.abort(), 80000);
    try {
      if (isChangeCommand(message) && agent === "operations-agent") {
        const response = await apiFetch("/api/ai/operations-changes/interpret", { method: "POST", signal: controller.signal,
          headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" }, body: JSON.stringify({ message }) });
        const result = await response.json();
        if (!mounted.current || request.current !== controller) return;
        if (response.ok && result?.code === "clarification_required") {
          setClarification(parseChangeClarification(result));
          return;
        }
        if (!response.ok || result.code !== "draft") {
          setAskError("สร้างร่างไม่ได้ กรุณาตรวจสิทธิ์ งาน วันที่ เวลา สถานะ และรหัสผู้รับผิดชอบ ตัวอย่าง: " + CHANGE_EXAMPLE);
          return;
        }
        setChangeDraft(parseChangeDraft(result)); setDraftRevision(v => v + 1);
        requestAnimationFrame(() => draftPanel.current?.scrollIntoView({ block: "start" }));
        return;
      }
      const result = await controlRequest(apiFetch, "/api/ai/chat", parseReply, controller.signal, askBody(message, agent));
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

    <div ref={draftPanel}><OperationsChanges key={draftRevision} initialDraft={changeDraft}
      onBusyChange={setChangeBusy} onOpenJob={onOpenJob} /></div>
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
          <p>{agent === "data-agent" ? "Data Agent · จำนวนงานและ KPI ตรงเวลาตามช่วงเวลา คำนวณโดย SCMOS"
            : agent === "communication-agent" ? "Communication Agent · ข้อความจากผู้ขนส่งตามที่ระบบอ่านไว้ · ไม่ส่ง ไม่แก้"
            : agent === "document-agent" ? "Document & Invoice Agent · เอกสารตาม checklist ใบแจ้งหนี้เทียบกำหนดวางบิล และเอกสารใกล้หมดอายุ · ไม่เปิดไฟล์ ไม่อนุมัติ"
            : agent === "engineering-agent" ? "Engineering Agent · รายการ GitHub ของ SCMOS และอ่านซอร์สแบบจำกัด อ่านอย่างเดียว · ไม่รันคำสั่ง ไม่แก้ไฟล์ ไม่ deploy · Administrator เท่านั้น"
            : agent === "sre-agent" ? "SRE Agent · สุขภาพระบบ การ deploy และข้อผิดพลาด วัดโดยเซิร์ฟเวอร์เอง · ไม่รีสตาร์ต ไม่แก้ไข · Administrator เท่านั้น"
            : agent === "management-agent" ? "Management Agent · แผนสรุปงาน/เอกสาร/ข้อความที่กำหนดไว้ · ตรวจสิทธิ์และ Audit ทุกขั้น · ไม่แก้ไขข้อมูล"
            : "Operations Agent · ใช้ขอบเขตงานที่เซิร์ฟเวอร์อนุญาตให้บัญชีนี้อ่าน"}</p></div><Badge tone="blue">ไม่มีสิทธิ์เขียน</Badge></div>
        <p className={s.hint}>AI ช่วยเลือกเครื่องมืออ่านข้อมูล ผลลัพธ์อาจไม่ครบหรือคลาดเคลื่อน ตรวจงานอ้างอิงก่อนตัดสินใจ ไม่ส่งข้อมูลลับหรือคีย์เข้ามาในคำถาม</p>
        <form className={s.form} onSubmit={submit}>
          {doors.length > 1 && <div className={s.actions} role="group" aria-label="เลือก Agent">
            {doors.map(choice => <button key={choice} type="button" disabled={busy}
              className={choice === agent ? s.primary : s.button} aria-pressed={choice === agent}
              onClick={() => { setAgent(choice); setReply(null); setAskError(""); }}>
              {choice === "data-agent" ? "ข้อมูล KPI" : choice === "communication-agent" ? "ข้อความ LINE · อีเมล"
                : choice === "document-agent" ? "เอกสาร · ใบแจ้งหนี้" : choice === "engineering-agent" ? "Engineering" : choice === "sre-agent" ? "ระบบ · SRE" : choice === "management-agent" ? "สรุปข้าม Agent" : "Operations"}</button>)}
          </div>}
          <div className={s.actions}>{(agent === "data-agent" ? DATA_PROMPTS : agent === "communication-agent" ? MESSAGE_PROMPTS
            : agent === "document-agent" ? DOCUMENT_PROMPTS : agent === "engineering-agent" ? ENGINEERING_PROMPTS : agent === "sre-agent" ? SRE_PROMPTS : agent === "management-agent" ? MANAGEMENT_PROMPTS : PROMPTS).map(prompt => <button key={prompt} type="button" className={s.button} disabled={busy}
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
              <button className={s.primary} type="submit" disabled={(!asking.ready && !(isChangeCommand(message) && agent === "operations-agent")) || changeBusy || busy || !message.trim()}>{busy ? "กำลังตรวจข้อมูล…" : isChangeCommand(message) && agent === "operations-agent" ? "เติมร่างแก้งาน →" : agent === "data-agent" ? "ถาม Data Agent →" : agent === "communication-agent" ? "ถาม Communication Agent →" : agent === "document-agent" ? "ถาม Document Agent →" : agent === "engineering-agent" ? "ถาม Engineering Agent →" : agent === "sre-agent" ? "ถาม SRE Agent →" : agent === "management-agent" ? "ถาม Management Agent →" : "ถาม Operations AI →"}</button></div>
          {agent === "operations-agent" && <p className={s.hint}>คำสั่งเติมร่าง (ยังไม่บันทึก): {CHANGE_EXAMPLE} · ผู้รับผิดชอบใช้รหัส เช่น OP-02</p>}
          </div>
          {!asking.ready && <p className={s.hint}>{asking.title}{asking.detail ? " · " + asking.detail : ""}</p>}
        </form>
        <div aria-live="polite" aria-busy={busy}>
          {askError && <Failure message={askError} />}
          {clarification && <div role="status" style={{ whiteSpace: "pre-line" }}>{clarification}</div>}
          {reply && <div className={s.reply}>
            <Badge tone={reply.mock ? "amber" : "green"}>{reply.mock ? "MOCK · ไม่ได้อ่านงานจริง" : "คำตอบพร้อมหลักฐาน"}</Badge>
            <p className={s.summary}>{reply.summary}</p>
            <div className={s.meta}><span>Run ID: <span className={s.runId}>{reply.runId}</span></span>
              {reply.correlationId && <span>Correlation: <span className={s.runId}>{reply.correlationId}</span></span>}
              {reply.contextUsed && <span>อ่านต่อจากคำถามก่อนหน้า</span>}
              {canViewAudit && !reply.mock && <button className={s.link} onClick={() => {
                setSelectedRun(reply.runId); activityPanel.current?.scrollIntoView({ block: "start" });
              }}>ดู Audit ของคำตอบนี้</button>}</div>
            {reply.kpi && <KpiCard kpi={reply.kpi} />}
            {reply.messages && <MessagesCard answer={reply.messages} onOpenJob={onOpenJob} />}
            {reply.documents && <DocumentsCard answer={reply.documents} onOpenJob={onOpenJob} />}
            {reply.engineering && <EngineeringCard answer={reply.engineering} />}
            {reply.source && <SourceCard answer={reply.source} />}
            {reply.platform && <PlatformCard answer={reply.platform} />}
            {reply.collaboration && <CollaborationCard answer={reply.collaboration} onOpenJob={onOpenJob} />}
            {reply.evidence && <>
              <div className={s.meta}><span>วันที่อ้างอิง {reply.evidence.asOfDate} · {reply.evidence.timeZone}</span>
                <span>ช่วงข้อมูล: {Object.hasOwn(WINDOW_LABEL, reply.evidence.window) ? WINDOW_LABEL[reply.evidence.window] : reply.evidence.window}</span>
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
          <ul>{status.data?.agents.map(agent => {
            const readiness = agentReadiness(status.data, agent.id);
            return <li key={agent.id}><span>{agent.name}<small className={s.hint}> · {readiness.detail}</small></span>
              <Badge tone={readiness.ready ? "green" : "muted"}>{readiness.label}</Badge></li>;
          })}</ul>
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
