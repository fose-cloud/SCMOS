"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { stamp } from "./WorkflowPanel";
import type { Job } from "../ops";
import { css } from "../theme";

/**
 * Pre-run verification.
 *
 * The day before a shipment runs, the carrier is sent what they are carrying and
 * asked to confirm the truck and driver. What matters on this screen is the
 * clock: who has not answered, and how long they have had.
 *
 * The list is generated from the plan, so a job reassigned during the day is on
 * the right carrier's list without anybody maintaining a second copy of it.
 */

type Check = {
  id: number; sentAt: string; sentBy: string; respondedAt: string | null;
  responseMinutes: number | null; metSla: boolean | null; isReady: boolean;
  outcome: string; escalation: string; escalationLabel: string; nextStep: string | null;
  confirmedBy: string; truckNo: string; driver: string; driverContact: string;
  correction: string; remark: string;
};

type Line = {
  jobKey: string; reference: string; customer: string; carrier: string;
  type: string; planTime: string; plannedTruck: string; plannedDriver: string;
  check: Check | null;
};

type List = {
  shipmentDate: string; slaMinutes: number;
  total: number; notSent: number; awaiting: number; ready: number; breached: number;
  lines: Line[];
};

const tomorrow = () => {
  const at = new Date();
  at.setDate(at.getDate() + 1);
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${pad(at.getDate())}/${pad(at.getMonth() + 1)}/${at.getFullYear()}`;
};

export function PreRun({ jobs, canEdit, onToast }: {
  jobs: Job[]; canEdit: (job: Job) => boolean; onToast: (m: string) => void;
}) {
  const [date, setDate] = useState(tomorrow());
  const [list, setList] = useRemembered<List>(`prerun.${date}`);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch(`/api/pre-run?date=${encodeURIComponent(date)}`, {
      headers: { accept: "application/json" },
    });
    const body = response.ok ? await response.json() as List : null;
    setList((held) => body ?? held);
  }, [date, setList]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch(`/api/pre-run?date=${encodeURIComponent(date)}`, {
        headers: { accept: "application/json" },
      });
      const body = response.ok ? await response.json() as List : null;
      if (!cancelled) setList((held) => body ?? held);
    })();
    return () => { cancelled = true; };
  }, [date, setList]);

  async function post(path: string, body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/pre-run/${path}`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
    } finally {
      setBusy(false);
    }
  }

  /** The date list — every day the plan actually has work on. */
  const dates = [...new Set(jobs.map((job) => job.date).filter(Boolean))].sort((a, b) => {
    const n = (d: string) => d.slice(6, 10) + d.slice(3, 5) + d.slice(0, 2);
    return n(a).localeCompare(n(b));
  });

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:4px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>วันที่ขนส่ง</span>
          <select value={date} onChange={(e) => setDate(e.target.value)}
            style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;min-width:150px")}>
            <option value={tomorrow()}>{tomorrow()} (พรุ่งนี้)</option>
            {dates.map((d) => <option key={d} value={d}>{d}</option>)}
          </select>
        </label>
        {list && (
          <div style={css("font-size:12.5px;color:#465A6E")}>
            SLA <b style={css("font-family:ui-monospace,monospace")}>{list.slaMinutes}</b> นาที
          </div>
        )}
      </div>

      {!list && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
          กำลังโหลด…
        </div>
      )}

      {list && (
        <>
          <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:11px")}>
            <Tile label="งานทั้งหมด" value={list.total} colour="#0A2240" />
            <Tile label="ยังไม่ได้ส่ง" value={list.notSent} colour="#7B8CA0" />
            <Tile label="รอผู้ขนส่งตอบ" value={list.awaiting} colour="#B45309" />
            <Tile label="พร้อมแล้ว" value={list.ready} colour="#16794C" />
            <Tile label="เกิน SLA" value={list.breached} colour="#B42318" />
          </div>

          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
            <div style={css("overflow-x:auto")}>
              <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
                <thead>
                  <tr>{["ลูกค้า", "ผู้ขนส่ง", "ประเภท", "เวลาแผน", "สถานะ", "ตอบใน", ""].map((h) => (
                    <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
                  ))}</tr>
                </thead>
                <tbody>
                  {list.lines.map((line) => (
                    <Row key={line.jobKey} line={line} busy={busy}
                      canEdit={canEdit(jobs.find((j) => j.key === line.jobKey) ?? ({} as Job))}
                      onSend={() => void post("send", { jobKey: line.jobKey })}
                      onRespond={(body) => void post("respond", { id: line.check?.id, ...body })}
                      onChase={() => void post("chase", { id: line.check?.id, note: "" })} />
                  ))}
                  {!list.lines.length && (
                    <tr><td colSpan={7} style={css("padding:30px;text-align:center;color:#94A3B8")}>
                      ไม่มีงานที่มีผู้ขนส่งในวันนี้
                    </td></tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        </>
      )}
    </div>
  );
}

function Row({ line, canEdit, busy, onSend, onRespond, onChase }: {
  line: Line; canEdit: boolean; busy: boolean;
  onSend: () => void;
  onRespond: (body: Record<string, string>) => void;
  onChase: () => void;
}) {
  const [open, setOpen] = useState(false);
  const [truckNo, setTruckNo] = useState("");
  const [driver, setDriver] = useState("");
  const [confirmedBy, setConfirmedBy] = useState("");
  const [correction, setCorrection] = useState("");

  const check = line.check;
  const tone = !check ? "#7B8CA0"
    : check.isReady ? "#16794C"
      : check.metSla === false ? "#B42318" : "#B45309";
  const label = !check ? "ยังไม่ส่ง"
    : check.outcome === "confirmed" ? "ยืนยันแล้ว"
      : check.outcome === "corrected" ? "แก้ไขแล้ว"
        : check.outcome === "no-response" ? "ไม่ตอบ"
          : check.metSla === false ? check.escalationLabel : "รอตอบ";

  return (
    <>
      <tr style={css("border-bottom:1px solid #F1F5F9")}>
        <td style={css("padding:8px 12px;font-weight:600;color:#0A2240")}>{line.customer || "—"}</td>
        <td style={css("padding:8px 12px;color:#5A6B7D")}>{line.carrier}</td>
        <td style={css("padding:8px 12px;font-family:ui-monospace,monospace;font-size:11.5px")}>{line.type || "—"}</td>
        <td style={css("padding:8px 12px;font-family:ui-monospace,monospace")}>{line.planTime || "—"}</td>
        <td style={css(`padding:8px 12px;font-weight:600;color:${tone}`)}>{label}</td>
        <td style={css("padding:8px 12px;font-family:ui-monospace,monospace;color:#7B8CA0")}>
          {check?.responseMinutes !== null && check ? `${check.responseMinutes} น.` : "—"}
        </td>
        <td style={css("padding:8px 12px;text-align:right;white-space:nowrap")}>
          {canEdit && !check && (
            <button onClick={onSend} disabled={busy}
              style={css("height:26px;padding:0 10px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:11.5px;cursor:pointer")}
            >ส่ง Pre-run</button>
          )}
          {canEdit && check?.outcome === "pending" && (
            <span style={css("display:inline-flex;gap:5px")}>
              <button onClick={() => setOpen((v) => !v)}
                style={css("height:26px;padding:0 10px;border:1px solid #16794C;background:#fff;color:#16794C;border-radius:4px;font-size:11.5px;cursor:pointer")}
              >บันทึกคำตอบ</button>
              {check.nextStep && (
                <button onClick={onChase} disabled={busy}
                  style={css("height:26px;padding:0 10px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:4px;font-size:11.5px;cursor:pointer")}
                >{check.nextStep === "follow-up" ? "ตาม" : "ส่งต่อหัวหน้า"}</button>
              )}
            </span>
          )}
        </td>
      </tr>

      {check && (check.correction || check.confirmedBy) && !open && (
        <tr><td colSpan={7} style={css("padding:0 12px 8px 12px;font-size:11.5px;color:#7B8CA0")}>
          {check.confirmedBy && `ยืนยันโดย ${check.confirmedBy} · `}
          {check.truckNo && `${check.truckNo} · `}
          {check.driver}
          {check.correction && <span style={css("color:#B45309")}> · แก้ไข: {check.correction}</span>}
        </td></tr>
      )}

      {open && (
        <tr><td colSpan={7} style={css("padding:10px 12px 14px;background:#FBFCFD")}>
          <div style={css("display:flex;gap:6px;flex-wrap:wrap;margin-bottom:7px")}>
            <input value={confirmedBy} onChange={(e) => setConfirmedBy(e.target.value)} placeholder="ผู้ยืนยันฝั่งผู้ขนส่ง"
              style={css("flex:1;min-width:150px;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
            <input value={truckNo} onChange={(e) => setTruckNo(e.target.value)} placeholder={line.plannedTruck || "ทะเบียนรถ"}
              style={css("flex:1;min-width:130px;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
            <input value={driver} onChange={(e) => setDriver(e.target.value)} placeholder={line.plannedDriver || "คนขับ"}
              style={css("flex:1;min-width:130px;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          </div>
          <input value={correction} onChange={(e) => setCorrection(e.target.value)}
            placeholder="ผู้ขนส่งแก้อะไร (เว้นว่างถ้ายืนยันตามเดิม)"
            style={css("width:100%;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:7px")} />
          <button
            onClick={() => { onRespond({ confirmedBy, truckNo, driver, correction, remark: "" }); setOpen(false); }}
            disabled={busy}
            style={css("height:29px;padding:0 13px;border:1px solid #16794C;background:#16794C;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
          >บันทึก</button>
        </td></tr>
      )}
    </>
  );
}

function Tile({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>
        {value.toLocaleString()}
      </div>
    </div>
  );
}

export { stamp };
