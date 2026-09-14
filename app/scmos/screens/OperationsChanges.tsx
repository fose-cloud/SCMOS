"use client";

import { useRef, useState, useEffect } from "react";
import { apiFetch } from "../api";
import { ZoomBox } from "../TableFrame";
import { filterOperationsQueue } from "../operationsQueue";
import { stamp } from "../aiControl";
import { parseChangePreview, type ChangeDraft, type ChangePreview as Preview } from "../operationsChangeCommand";
import s from "./AiControlTower.module.css";

type Fields = Record<string, string>;
type Proposal = { id: number; state: string; requestedBy: string; canApprove: boolean;
  requestedAt?: string; decidedBy?: string | null; decidedAt?: string | null;
  payload: { key: string; fingerprint: string; before: Fields; changes: Fields; reason: string; expiresAt: string } };
type Attention = { total: number; returned: number; undated: number; invalidRows: number; asOfDate: string;
  rows: { key: string; jobCode: string; customer: string; date: string; risk: string | null; missingDriver: boolean; missingPlate: boolean }[] };
const LABELS: Fields = { date: "DATE (วัน/เดือน/ปี ค.ศ.)", planTime: "PLAN LOADING TIME", status: "STATUS", opId: "ผู้รับผิดชอบ" };
const CODES: Fields = { pending: "บันทึกข้อเสนอแล้ว ยังไม่ได้แก้งาน", applied: "ยืนยันและบันทึกงานแล้ว",
  already_applied: "รายการนี้บันทึกไปแล้ว ไม่ได้แก้ซ้ำ", rejected: "ปฏิเสธข้อเสนอแล้ว", stale: "ข้อมูลเปลี่ยนแล้ว กรุณาสร้างข้อเสนอใหม่",
  expired: "ข้อเสนอหมดอายุ กรุณาสร้างใหม่", invalid_or_stale: "ข้อมูลเปลี่ยนหรือคำขอไม่ถูกต้อง กรุณาโหลดใหม่",
  workflow_required: "กรุณาเปลี่ยนสถานะผ่าน Workflow เดิม", write_disabled: "ยังไม่เปิดการเขียนแบบยืนยันที่เซิร์ฟเวอร์",
  write_unavailable: "ยังยืนยันการบันทึกไม่ได้ ให้รีเฟรชคิวก่อนลองอีกครั้ง", forbidden: "ไม่มีสิทธิ์ดำเนินการ",
  invalid_date: "วันที่ไม่ถูกต้อง", invalid_time: "เวลาไม่ถูกต้อง", invalid_assignee: "ผู้รับผิดชอบไม่พร้อมรับงาน",
  no_change: "ไม่มีค่าที่เปลี่ยน", reason_required: "กรุณาระบุเหตุผล" };

/** Human-reviewed changes are separate from the read-only model answer. No automatic POST retries. */
export function OperationsChanges({ onOpenJob, initialDraft, onBusyChange }: {
  onOpenJob: (key: string) => void; initialDraft?: ChangeDraft | null; onBusyChange?: (busy: boolean) => void;
}) {
  const [key, setKey] = useState(initialDraft?.preview.key ?? "");
  const [preview, setPreview] = useState<Preview | null>(initialDraft?.preview ?? null);
  const [fields, setFields] = useState<Fields>(initialDraft ? { ...initialDraft.preview.values, ...initialDraft.changes } : {});
  const [reason, setReason] = useState(initialDraft?.reason ?? "");
  const [queue, setQueue] = useState<Proposal[]>([]);
  const [queueLoaded, setQueueLoaded] = useState(false);
  const [queueState, setQueueState] = useState("all");
  const [queueSearch, setQueueSearch] = useState("");
  const visibleQueue = filterOperationsQueue(queue, queueState, queueSearch);
  const [attention, setAttention] = useState<Attention | null>(null);
  const [review, setReview] = useState<Proposal | null>(null);
  const [note, setNote] = useState("");
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  const controller = useRef<AbortController | null>(null);
  const lock = useRef(false);
  useEffect(() => () => controller.current?.abort(), []);
  async function action(work: (signal: AbortSignal) => Promise<void>) {
    if (lock.current) return;
    lock.current = true; setBusy(true); onBusyChange?.(true); setMessage("");
    const abort = new AbortController(); controller.current = abort;
    const timer = setTimeout(() => abort.abort(), 30000);
    try { await work(abort.signal); }
    catch { if (!abort.signal.aborted) setMessage("อ่านหรือบันทึกไม่สำเร็จ กรุณารีเฟรชคิวเพื่อตรวจสถานะก่อนลองอีกครั้ง");
      else setMessage("คำขอหมดเวลาหรือถูกยกเลิก กรุณารีเฟรชคิวตรวจสถานะ"); }
    finally { clearTimeout(timer); lock.current = false; setBusy(false); onBusyChange?.(false); }
  }
  async function loadQueue(signal: AbortSignal) {
    const response = await apiFetch("/api/ai/operations-changes", { signal });
    if (!response.ok) { setQueue([]); throw new Error(); }
    const body = await response.json();
    if (!Array.isArray(body.rows)) throw new Error();
    setQueue(body.rows); setQueueLoaded(true);
    if (!body.configured) setMessage(CODES.write_disabled);
  }
  async function post(path: string, body: unknown, signal: AbortSignal) {
    const response = await apiFetch("/api/ai/operations-changes" + path, { method: "POST", signal,
      headers: { "content-type": "application/json", "X-SCMOS-AI-Control": "1" }, body: JSON.stringify(body) });
    const result = await response.json();
    setMessage(CODES[result.code] ?? "ดำเนินการไม่สำเร็จ กรุณาตรวจข้อมูลและสิทธิ์");
    return response.ok;
  }
  return <section className={s.panel} aria-label="เสนอและยืนยันการแก้งาน">
    <h2>เสนอการแก้งาน · Supervisor ยืนยันก่อนบันทึก</h2>
    <p className={s.hint}>เลือกค่าเองหรือใช้คำสั่งแชตตามรูปแบบเพื่อเติมร่าง ตรวจให้ครบก่อนสร้างข้อเสนอ โมเดลยังไม่มีเครื่องมือเขียนงาน</p>
    <button className={s.button} disabled={busy} onClick={() => void action(async signal => {
      setAttention(null);
      const response = await apiFetch("/api/ai/operations-changes/attention", { signal });
      if (!response.ok) throw new Error();
      const value = await response.json();
      if (!Array.isArray(value.rows) || !Number.isInteger(value.total)) throw new Error();
      setAttention(value);
    })}>ตรวจงานเสี่ยง / ขาดชื่อคนขับหรือทะเบียน</button>
    {attention && <div role="region" aria-label="งานที่ต้องติดตาม">
      <p role="status">ต้องติดตาม {attention.total} งาน · แสดง {attention.returned} งาน · ณ {attention.asOfDate}</p>
      <p className={s.hint}>แผนภายใน 2 วันรวมงานเลยกำหนด เฉพาะงานยังไม่มีข้อมูล ARRIVAL · อ่านวันไม่ได้ {attention.undated} งาน · ข้อมูลผิดรูปแบบ {attention.invalidRows} งาน</p>
      {attention.rows.map(row => <div key={row.key} className={s.empty}>
        <button className={s.link} onClick={() => onOpenJob(row.key)}>{row.jobCode || row.key}</button>{" · "}{row.customer}{" · "}{row.date}
        <p>{row.risk ? "ความเสี่ยงเดิม: " + row.risk + " · " : ""}{row.missingDriver ? "ยังไม่มีชื่อคนขับ · " : ""}{row.missingPlate ? "ยังไม่มีทะเบียน" : ""}</p>
        <button className={s.button} disabled={busy} onClick={() => { setKey(row.key); setPreview(null); }}>เลือกงานนี้เพื่อเสนอแก้ไข</button>
      </div>)}
    </div>}
    <div className={s.actions}>
      <input aria-label="รหัส key ของงาน" placeholder="Job key จากงานอ้างอิง" value={key} maxLength={80}
        disabled={busy} onChange={e => { setKey(e.target.value); setPreview(null); }} />
      <button className={s.button} disabled={busy || !key.trim()} onClick={() => void action(async signal => {
        setPreview(null);
        const response = await apiFetch("/api/ai/operations-changes/preview?key=" + encodeURIComponent(key.trim()), { signal });
        if (!response.ok) throw new Error();
        const value = parseChangePreview(await response.json());
        setPreview(value); setFields({ ...value.values }); setReason("");
      })}>อ่านข้อมูลปัจจุบัน</button>
      <button className={s.button} disabled={busy} onClick={() => void action(loadQueue)}>รีเฟรชคิวรอยืนยัน</button>
    </div>
    {preview && <>
      {!preview.enabled && <p className={s.hint}>{CODES.write_disabled}</p>}
      <div className={s.actions}>{Object.entries(LABELS).map(([field, label]) => <label key={field}>{label}<br />
        {field === "opId" || field === "status" ? <select value={fields[field]} disabled={busy}
          onChange={e => setFields(f => ({ ...f, [field]: e.target.value }))}>
          <option value={preview.values[field]}>เดิม: {preview.values[field] || "—"}</option>
          {(field === "opId" ? preview.assignees.map(p => ({ value: p.id, label: p.name }))
            : preview.statuses.map(value => ({ value, label: value }))).filter(p => p.value !== preview.values[field])
            .map(p => <option key={p.value} value={p.value}>{p.label}</option>)}
        </select> : <input value={fields[field]} maxLength={field === "date" ? 10 : 5} disabled={busy}
          placeholder={field === "date" ? "14/09/2026" : "08:30"} onChange={e => setFields(f => ({ ...f, [field]: e.target.value }))} />}
      </label>)}</div>
      <label>เหตุผล <input aria-label="เหตุผลเสนอแก้งาน" value={reason} maxLength={400} disabled={busy} onChange={e => setReason(e.target.value)} /></label>
      <button className={s.button} disabled={busy || !preview.enabled || !reason.trim()} onClick={() => void action(async signal => {
        const changes = Object.fromEntries(Object.entries(fields).filter(([field, value]) => value !== preview.values[field]));
        if (await post("", { key: preview.key, version: preview.version, changes, reason }, signal)) {
          setPreview(null); await loadQueue(signal);
        }
      })}>สร้างข้อเสนอ (ยังไม่แก้งาน)</button>
    </>}
    {message && <p role="status">{message}</p>}
    {queueLoaded && <>
      <div className={s.actions}>
        <label>สถานะข้อเสนอ <select value={queueState} onChange={e => setQueueState(e.target.value)}>
          <option value="all">ทั้งหมด</option><option value="pending">รอยืนยัน</option>
          <option value="applied">บันทึกแล้ว</option><option value="rejected">ปฏิเสธ</option>
          <option value="expired">หมดอายุ</option><option value="stale">ข้อมูลเปลี่ยน</option>
        </select></label>
        <input aria-label="ค้นหาข้อเสนอ" placeholder="ค้นหารหัสงานหรือผู้เสนอ" value={queueSearch}
          onChange={e => setQueueSearch(e.target.value)} />
        <button className={s.button} onClick={() => { setQueueState("all"); setQueueSearch(""); }}>ล้างตัวกรองคิว</button>
      </div>
      <p className={s.hint}>แสดง {visibleQueue.length} จาก {queue.length} ข้อเสนอที่โหลด · เฉพาะ 100 รายการล่าสุดในสิทธิ์ของคุณ</p>
      {visibleQueue.length === 0 && <p role="status">{queue.length === 0 ? "ยังไม่มีข้อเสนอในคิวที่โหลด" : "ไม่พบข้อเสนอที่ตรงกับตัวกรอง"}</p>}
    </>}
    {visibleQueue.map(row => <div key={row.id} className={s.empty}>
      <button className={s.link} onClick={() => onOpenJob(row.payload.key)}>{row.payload.key}</button>
      {" · "}{CODES[row.state] ?? "สถานะไม่ทราบ"}{" · ผู้เสนอ: "}{row.requestedBy}
      <button className={s.button} disabled={busy}
        onClick={() => { setReview(row); setNote(""); }}>{row.state === "pending" && row.canApprove
          ? "ตรวจค่าเดิม → ค่าใหม่" : "ดูรายละเอียด / ประวัติ"}</button>
    </div>)}
    {review && <div className={s.panel} role="region" aria-label="ตรวจข้อเสนอก่อนบันทึก">
      <h3>รายละเอียดข้อเสนอ #{review.id} · งาน {review.payload.key}</h3>
      <p>{CODES[review.state] ?? "สถานะไม่ทราบ"} · ผู้เสนอ: {review.requestedBy}</p>
      {review.requestedAt && <p>เสนอเมื่อ: {stamp(review.requestedAt)} · เวลาไทย</p>}
      {review.decidedBy && <p>ผู้ตัดสินใจ: {review.decidedBy}
        {review.decidedAt ? " · " + stamp(review.decidedAt) + " · เวลาไทย" : ""}</p>}
      <p>เหตุผลที่เสนอ: {review.payload.reason}</p>
      <p>หมดอายุ: {stamp(review.payload.expiresAt)} · เวลาไทย</p>
      <ZoomBox><table><thead><tr><th>ช่อง</th><th>เดิม</th><th>ใหม่</th></tr></thead><tbody>
        {Object.entries(review.payload.changes).map(([field, value]) => <tr key={field}>
          <td>{LABELS[field] ?? field}</td><td>{review.payload.before[field] || "—"}</td><td>{value}</td>
        </tr>)}
      </tbody></table></ZoomBox>
      {review.state === "pending" && review.canApprove && <>
      <label>เหตุผลการตัดสินใจ <input value={note} maxLength={400} disabled={busy} onChange={e => setNote(e.target.value)} /></label>
      <div className={s.actions}>{[true, false].map(approve => <button key={String(approve)} className={s.button}
        disabled={busy || !note.trim()} onClick={() => void action(async signal => {
          await post(`/${review.id}/confirm`, { fingerprint: review.payload.fingerprint, approve, note }, signal);
          setReview(null); await loadQueue(signal);
        })}>{approve ? "ยืนยันและบันทึกจริง" : "ปฏิเสธ"}</button>)}
      </div></>}
      <button className={s.button} disabled={busy} onClick={() => setReview(null)}>กลับ</button>
    </div>}
  </section>;
}
