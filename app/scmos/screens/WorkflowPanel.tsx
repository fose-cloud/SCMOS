"use client";

import { useCallback, useEffect, useState } from "react";
import {
  advance, assignCarrier, readWorkflow, release, requestSupplier, respondSupplier,
  type CarrierPriority, type JobWorkflow,
} from "../flow";
import { css } from "../theme";

/**
 * Where a job stands in the process, and the one thing it is waiting for.
 *
 * The panel never decides anything. It asks the backend what state the job is
 * in, shows the gate that is open, and sends the answer back. When a rule
 * refuses — a second carrier asked while the first has not replied, a truck
 * assigned to somebody who never confirmed — the refusal is shown as written,
 * because the rule's own words explain the process better than a disabled
 * button does.
 */

type Props = {
  jobKey: string;
  canEdit: boolean;
  onToast: (message: string) => void;
  onChanged: () => void;
};

const HOLD_LABELS: Record<string, string> = {
  CsCorrection: "ตีกลับ CS ให้แก้ข้อมูล",
  CsClarification: "รอเคลียร์กับ CS",
  BlMismatch: "B/L ไม่ตรงกับการจอง",
  ImageUnclear: "ภาพเอกสารไม่ชัด",
  Incident: "มีเหตุผิดปกติ — CAR/PAR",
};

const OUTCOME_LABELS: Record<string, string> = {
  pending: "รอคำตอบ",
  confirmed: "ยืนยัน",
  rejected: "ปฏิเสธ",
  cancelled: "ยกเลิก",
  "no-response": "ไม่ตอบ",
};

const OUTCOME_TONE: Record<string, string> = {
  pending: "#B45309", confirmed: "#16794C", rejected: "#B42318",
  cancelled: "#7B8CA0", "no-response": "#7B8CA0",
};

export function WorkflowPanel({ jobKey, canEdit, onToast, onChanged }: Props) {
  const [state, setState] = useState<JobWorkflow | null>(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");
  const [skipReason, setSkipReason] = useState("");
  const [showHistory, setShowHistory] = useState(false);

  const load = useCallback(async () => {
    setState(await readWorkflow(jobKey));
  }, [jobKey]);

  useEffect(() => {
    // Guarded rather than calling `load` straight out: switching jobs quickly
    // would otherwise let a slow answer for the previous one land on top of the
    // new one.
    let cancelled = false;
    (async () => {
      const fresh = await readWorkflow(jobKey);
      if (!cancelled) setState(fresh);
    })();
    return () => { cancelled = true; };
  }, [jobKey]);

  /** Every action goes the same way: send, show what came back, reload. */
  async function run(action: () => Promise<{ ok: boolean; message: string }>) {
    if (busy) return;
    setBusy(true);
    try {
      const reply = await action();
      onToast(reply.message);
      await load();
      if (reply.ok) onChanged();
    } finally {
      setBusy(false);
      setNote("");
      setSkipReason("");
    }
  }

  if (!state) {
    return (
      <div style={css("padding:14px 16px;font-size:12.5px;color:#94A3B8")}>กำลังโหลดสถานะกระบวนการ…</div>
    );
  }

  const open = state.suppliers.find((s) => s.outcome === "pending");
  const confirmed = state.suppliers.find((s) => s.outcome === "confirmed");

  return (
    <div style={css("border-top:1px solid #E9EFF5")}>
      <div style={css("padding:12px 16px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:7px")}>
          ขั้นตอนในกระบวนการ · {state.position + 1} จาก 20
        </div>
        <div style={css("display:flex;align-items:center;gap:8px;flex-wrap:wrap")}>
          <span style={css(
            "font-size:13px;font-weight:650;color:" + (state.isHeld ? "#B42318" : "#0A2240"),
          )}>{stageThai(state.stage)}</span>
          {state.isHeld && (
            <span style={css("font-size:10.5px;font-weight:700;padding:2px 8px;border-radius:3px;background:#FCE9E7;color:#B42318")}>
              พักงาน · {HOLD_LABELS[state.hold] ?? state.hold}
            </span>
          )}
        </div>
        <Bar position={state.position} held={state.isHeld} />
      </div>

      {state.isHeld && canEdit && (
        <div style={css("padding:12px 16px;border-bottom:1px solid #E9EFF5;background:#FEF6F5")}>
          <div style={css("font-size:12.5px;color:#B42318;margin-bottom:8px")}>
            งานหยุดอยู่ตรงนี้จนกว่าปัญหาจะถูกแก้
          </div>
          <Note value={note} onChange={setNote} placeholder="แก้ไขอย่างไร / ใครยืนยัน" />
          <Action label="ปลดล็อกและเดินต่อ" tone="#16794C" busy={busy}
            onClick={() => void run(() => release(jobKey, note))} />
        </div>
      )}

      {!state.isHeld && state.pendingGateThai && canEdit && (
        <div style={css("padding:12px 16px;border-bottom:1px solid #E9EFF5;background:#F7FAFD")}>
          <div style={css("font-size:12.5px;font-weight:650;color:#0A2240;margin-bottom:8px")}>
            {state.pendingGateThai}
          </div>
          <Note value={note} onChange={setNote} placeholder="บันทึกเพิ่มเติม (ถ้ามี)" />
          <div style={css("display:flex;gap:7px")}>
            <Action label="ใช่ / ผ่าน" tone="#16794C" busy={busy}
              onClick={() => void run(() => advance(jobKey, true, note))} />
            <Action label="ไม่ / ไม่ผ่าน" tone="#B42318" busy={busy}
              onClick={() => void run(() => advance(jobKey, false, note))} />
          </div>
        </div>
      )}

      {!state.isHeld && !state.pendingGateThai && canEdit && state.position < 19 && (
        <div style={css("padding:12px 16px;border-bottom:1px solid #E9EFF5")}>
          <Note value={note} onChange={setNote} placeholder="บันทึกเพิ่มเติม (ถ้ามี)" />
          <Action label="ทำขั้นตอนนี้เสร็จแล้ว →" tone="#0A2240" busy={busy}
            onClick={() => void run(() => advance(jobKey, null, note))} />
        </div>
      )}

      <Suppliers
        state={state} open={open} confirmed={confirmed} canEdit={canEdit} busy={busy}
        skipReason={skipReason} onSkipReason={setSkipReason}
        onAsk={(carrier, price, skip) => void run(() => requestSupplier(jobKey, carrier, price, skip))}
        onRespond={(id, outcome, reason) => void run(() => respondSupplier(jobKey, id, outcome, reason))}
        onAssign={(carrier) => void run(() => assignCarrier(jobKey, carrier))}
      />

      <button
        onClick={() => setShowHistory((v) => !v)}
        style={css("width:100%;padding:10px 16px;background:#FBFCFD;border:none;border-top:1px solid #E9EFF5;text-align:left;font-size:12px;color:#5A6B7D;cursor:pointer")}
      >
        {showHistory ? "▾" : "▸"} ประวัติกระบวนการ ({state.history.length})
      </button>
      {showHistory && (
        <div style={css("padding:4px 16px 12px;max-height:220px;overflow-y:auto")}>
          {state.history.length === 0 && (
            <div style={css("font-size:12px;color:#94A3B8;padding:6px 0")}>ยังไม่มีการบันทึก</div>
          )}
          {state.history.slice().reverse().map((event) => (
            <div key={event.id} style={css("padding:6px 0;border-bottom:1px solid #F4F7FA;font-size:11.5px")}>
              <div style={css("color:#16232F")}>
                {stageThai(event.from)} → {stageThai(event.to)}
                {event.hold && <span style={css("color:#B42318")}> · พัก ({HOLD_LABELS[event.hold] ?? event.hold})</span>}
              </div>
              {event.note && <div style={css("color:#5A6B7D;margin-top:1px")}>{event.note}</div>}
              <div style={css("color:#94A3B8;margin-top:1px")}>{event.by} · {stamp(event.at)}</div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ pieces */

function Suppliers({ state, open, confirmed, canEdit, busy, skipReason, onSkipReason, onAsk, onRespond, onAssign }: {
  state: JobWorkflow;
  open: JobWorkflow["suppliers"][number] | undefined;
  confirmed: JobWorkflow["suppliers"][number] | undefined;
  canEdit: boolean; busy: boolean;
  skipReason: string; onSkipReason: (v: string) => void;
  onAsk: (carrier: string, price: number | null, skipReason: string) => void;
  onRespond: (id: number, outcome: string, reason: string) => void;
  onAssign: (carrier: string) => void;
}) {
  const [reason, setReason] = useState("");
  const next: CarrierPriority | null = state.nextToAsk;

  return (
    <div style={css("padding:12px 16px;border-bottom:1px solid #E9EFF5")}>
      <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:8px")}>
        การขอรถตามลำดับ ({state.suppliers.length})
      </div>

      {state.suppliers.length === 0 && (
        <div style={css("font-size:12px;color:#94A3B8;margin-bottom:9px")}>ยังไม่ได้ขอรถจากใคร</div>
      )}

      {state.suppliers.map((attempt) => (
        <div key={attempt.id} style={css("border:1px solid #E9EFF5;border-radius:4px;padding:8px 10px;margin-bottom:6px;background:#FBFCFD")}>
          <div style={css("display:flex;align-items:center;gap:7px;flex-wrap:wrap")}>
            <span style={css("font-family:ui-monospace,monospace;font-size:11px;color:#7B8CA0")}>#{attempt.rank}</span>
            <span style={css("font-size:12.5px;font-weight:650;color:#0A2240;flex:1")}>{attempt.carrier}</span>
            {attempt.quotedPrice !== null && (
              <span style={css("font-family:ui-monospace,monospace;font-size:12px;color:#16232F")}>
                {attempt.quotedPrice.toLocaleString()}
              </span>
            )}
            <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${OUTCOME_TONE[attempt.outcome] ?? "#7B8CA0"}`)}>
              {OUTCOME_LABELS[attempt.outcome] ?? attempt.outcome}
            </span>
          </div>
          <div style={css("font-size:11px;color:#94A3B8;margin-top:2px")}>
            ขอเมื่อ {stamp(attempt.requestedAt)}
            {attempt.responseMinutes !== null && ` · ตอบใน ${attempt.responseMinutes} นาที`}
            {attempt.reason && ` · ${attempt.reason}`}
          </div>

          {attempt.outcome === "pending" && canEdit && (
            <div style={css("margin-top:7px")}>
              <input
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="เหตุผลที่ปฏิเสธ (ถ้าปฏิเสธ)"
                style={css("width:100%;height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px;margin-bottom:6px")}
              />
              <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
                <Small label="ยืนยันรับงาน" tone="#16794C" busy={busy} onClick={() => onRespond(attempt.id, "confirmed", "")} />
                <Small label="ปฏิเสธ" tone="#B42318" busy={busy} onClick={() => onRespond(attempt.id, "rejected", reason)} />
                <Small label="ไม่ตอบ" tone="#7B8CA0" busy={busy} onClick={() => onRespond(attempt.id, "no-response", reason)} />
                <Small label="ยกเลิกคำขอ" tone="#7B8CA0" busy={busy} onClick={() => onRespond(attempt.id, "cancelled", reason)} />
              </div>
            </div>
          )}
        </div>
      ))}

      {confirmed && canEdit && (
        <Action label={`มอบหมายงานให้ ${confirmed.carrier}`} tone="#0A2240" busy={busy}
          onClick={() => onAssign(confirmed.carrier)} />
      )}

      {!confirmed && !open && canEdit && (
        <div style={css("margin-top:8px")}>
          {next ? (
            <>
              <div style={css("font-size:12px;color:#465A6E;margin-bottom:6px")}>
                ลำดับถัดไปตามกระบวนการ: <b style={css("color:#0A2240")}>{next.carrier}</b>
                <div style={css("font-size:11px;color:#94A3B8")}>{next.basis}</div>
              </div>
              <Action label={`ขอรถจาก ${next.carrier}`} tone="#0A2240" busy={busy}
                onClick={() => onAsk(next.carrier, next.price, "")} />
              <div style={css("margin-top:9px;padding-top:9px;border-top:1px dashed #E9EFF5")}>
                <div style={css("font-size:11px;color:#7B8CA0;margin-bottom:5px")}>ข้ามลำดับ — ต้องระบุเหตุผล</div>
                <input
                  value={skipReason}
                  onChange={(e) => onSkipReason(e.target.value)}
                  placeholder="เหตุผลที่ข้ามลำดับ"
                  style={css("width:100%;height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px")}
                />
                <div style={css("display:flex;flex-wrap:wrap;gap:5px;margin-top:6px")}>
                  {state.priority
                    // Already asked is already asked — the backend refuses a
                    // repeat, and offering one here would be a button that
                    // exists only to be told no.
                    .filter((p) => p.carrier !== next.carrier
                      && !state.suppliers.some((s) => s.carrier.toUpperCase() === p.carrier.toUpperCase()))
                    .slice(0, 6)
                    .map((p) => (
                      <Small
                        key={p.carrier}
                        label={p.carrier}
                        tone={skipReason.trim() ? "#B45309" : "#C3CFDB"}
                        busy={busy || !skipReason.trim()}
                        onClick={() => onAsk(p.carrier, p.price, skipReason)}
                      />
                    ))}
                </div>
              </div>
            </>
          ) : (
            <div style={css("font-size:12px;color:#94A3B8")}>
              ไม่มีลำดับผู้ขนส่งสำหรับลูกค้ารายนี้ — ยังไม่เคยมีประวัติงาน
            </div>
          )}
        </div>
      )}
    </div>
  );
}

function Bar({ position, held }: { position: number; held: boolean }) {
  const pct = Math.round(((position + 1) / 20) * 100);
  return (
    <div style={css("height:5px;background:#E9EFF5;border-radius:3px;margin-top:9px;overflow:hidden")}>
      <div style={css(`height:100%;width:${pct}%;background:${held ? "#B42318" : "#16794C"};border-radius:3px`)} />
    </div>
  );
}

function Note({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder: string }) {
  return (
    <input
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      style={css("width:100%;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:7px")}
    />
  );
}

function Action({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      disabled={busy}
      style={css(
        `height:30px;padding:0 13px;border:1px solid ${tone};background:${busy ? "#C3CFDB" : tone};` +
        "color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:" + (busy ? "default" : "pointer"),
      )}
    >{label}</button>
  );
}

function Small({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      disabled={busy}
      style={css(
        `height:25px;padding:0 9px;border:1px solid ${tone};background:#fff;color:${tone};` +
        "border-radius:4px;font-size:11.5px;font-weight:600;cursor:" + (busy ? "default" : "pointer"),
      )}
    >{label}</button>
  );
}

/** Stage names in Thai. The backend sends the id; this is presentation. */
const STAGE_THAI: Record<string, string> = {
  Received: "รับงานจาก CS", Reviewed: "ตรวจข้อมูลการจอง", DocumentVerification: "ตรวจสอบเอกสาร",
  SupplierSelection: "เลือกผู้ขนส่ง", CapacityRequested: "ขอกำลังรถ", SupplierAssigned: "มอบหมายรถ",
  PreRunVerification: "ตรวจก่อนออกงาน", ECardReceived: "รับ E-Card", DocumentCheck: "ตรวจ B/L",
  DocumentReleased: "ส่งเอกสารให้ผู้ขนส่ง", Dispatched: "จ่ายงาน", PickedUp: "รับตู้ / รับสินค้า",
  Loading: "ขนถ่ายสินค้า", InTransit: "ระหว่างขนส่ง", Delivered: "ส่งมอบ",
  ContainerReturned: "คืนตู้", PodCollected: "เก็บ POD", BillingVerified: "ตรวจการวางบิล",
  KpiCalculated: "คำนวณ KPI", Closed: "ปิดงาน",
};

export const stageThai = (id: string) => STAGE_THAI[id] ?? id;

export function stamp(iso: string) {
  const at = new Date(iso);
  return Number.isNaN(at.getTime()) ? "—" : at.toLocaleString("th-TH", {
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit",
  });
}
