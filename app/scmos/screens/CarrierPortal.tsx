"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

/**
 * The carrier's own screen.
 *
 * A subcontractor is not a colleague with fewer buttons — they work for a
 * different company, and the register everyone else reads holds their
 * competitors' assignments and every customer's name. So this screen is fed by
 * `/api/carrier`, which answers for one supplier and refuses an account tied to
 * none. It never touches `/api/jobs`.
 *
 * Acceptance and operations are deliberately separate actions. After a carrier
 * accepts work, resource assignment is restricted to its active fleet registry;
 * every reassignment and operational transition is retained as history.
 */

type CarrierJob = {
  key: string; jobCode: string; customer: string; destination: string; type: string;
  cyYard: string; weight: string; container: string; date: string; pickupPlan: string;
  status: string; requestId: number | null; quotedPrice: number | null;
  requestedAt: string | null; licence: string; driver: string; contact: string;
  assignmentOutcome?: string; operationalAvailable?: boolean;
  operations?: Operation[]; pods?: Pod[];
};

type Operation = { id: number; kind: string; from: string; to: string; note: string; by: string; recordedAt: string; eventAt: string | null };
type Pod = { id: number; fileName: string; uploadedAt: string };
type FleetTruck = { id: number; plate: string; vehicleType: string; dgCapable: boolean; registrationExpiry: string };
type FleetDriver = { id: number; name: string; phone: string; licenceNo: string; licenceExpiry: string; trainingExpiry: string };

type Portal = {
  supplierId: number; supplierName: string;
  offered: CarrierJob[]; accepted: CarrierJob[]; schedule?: CarrierJob[];
  trucks: FleetTruck[]; drivers: FleetDriver[];
};

type Draft = { licence: string; driver: string; contact: string };
type ScheduleView = "today" | "tomorrow" | "week" | "calendar" | "unassigned" | "active" | "completed";
const EMPTY: Draft = { licence: "", driver: "", contact: "" };

function localDateKey(date: Date) {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, "0");
  const day = String(date.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function jobDateKey(value: string) {
  const clean = value.trim();
  const iso = /^(\d{4})-(\d{2})-(\d{2})/.exec(clean);
  if (iso) return `${iso[1]}-${iso[2]}-${iso[3]}`;
  const local = /^(\d{1,2})\/(\d{1,2})\/(\d{4})/.exec(clean);
  if (!local) return "";
  return `${local[3]}-${local[2].padStart(2, "0")}-${local[1].padStart(2, "0")}`;
}

export function CarrierPortal({ onToast }: { onToast: (message: string) => void }) {
  const [portal, setPortal] = useRemembered<Portal>("carrier-portal");
  const [refused, setRefused] = useState("");
  const [tab, setTab] = useState<"new" | "schedule">("new");
  const [scheduleView, setScheduleView] = useState<ScheduleView>("active");
  const [calendarDate, setCalendarDate] = useState("");
  const [open, setOpen] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [busy, setBusy] = useState(false);
  const [operate, setOperate] = useState<string | null>(null);
  const [truckId, setTruckId] = useState("");
  const [trailerId, setTrailerId] = useState("");
  const [driverId, setDriverId] = useState("");
  const [nextStatus, setNextStatus] = useState("dispatched");
  const [remark, setRemark] = useState("");

  const load = useCallback(async () => {
    const response = await apiFetch("/api/carrier", { headers: { accept: "application/json" } });
    if (response.ok) { setPortal(await response.json() as Portal); setRefused(""); return; }
    const body = await response.json().catch(() => ({})) as { error?: string };
    setRefused(body.error || `เปิดหน้างานไม่ได้ (${response.status})`);
  }, [setPortal]);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function answer(job: CarrierJob, path: "accept" | "decline", body: Record<string, unknown>) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier/${encodeURIComponent(job.key)}/${path}`, {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ requestId: job.requestId, ...body }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      if (response.ok) { setOpen(null); setDraft(EMPTY); await load(); }
    } finally { setBusy(false); }
  }

  async function saveResources(job: CarrierJob) {
    if (busy || !truckId || !driverId) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier/${encodeURIComponent(job.key)}/resources`, {
        method: "PUT", headers: { "content-type": "application/json" },
        body: JSON.stringify({ truckId: Number(truckId), trailerId: trailerId ? Number(trailerId) : null, driverId: Number(driverId) }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "จัดรถไม่สำเร็จ");
      if (response.ok) await load();
    } finally { setBusy(false); }
  }

  async function saveStatus(job: CarrierJob) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier/${encodeURIComponent(job.key)}/status`, {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ type: nextStatus, remark }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกสถานะไม่สำเร็จ");
      if (response.ok) { setRemark(""); await load(); }
    } finally { setBusy(false); }
  }

  async function uploadPod(job: CarrierJob, file: File | null) {
    if (!file || busy) return;
    const body = new FormData();
    body.append("jobKey", job.key); body.append("folder", "POD"); body.append("kind", "pod"); body.append("file", file);
    setBusy(true);
    try {
      const response = await apiFetch("/api/documents", { method: "POST", body });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "อัปโหลด POD ไม่สำเร็จ");
      if (response.ok) await load();
    } finally { setBusy(false); }
  }

  if (refused) {
    return (
      <div style={css("background:#fff;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:6px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#B45309;margin-bottom:4px")}>เปิดหน้างานของบริษัทไม่ได้</div>
        <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.7")}>{refused}</div>
      </div>
    );
  }

  if (!portal) {
    return <div style={css("padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>กำลังโหลด…</div>;
  }

  const schedule = portal.schedule ?? portal.accepted;
  const now = new Date();
  const today = localDateKey(now);
  const tomorrowAt = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 1);
  const tomorrow = localDateKey(tomorrowAt);
  const weekEndAt = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 6);
  const weekEnd = localDateKey(weekEndAt);
  const scheduleRows = schedule.filter((job) => {
    const date = jobDateKey(job.date);
    const status = job.status.trim().toUpperCase();
    if (scheduleView === "today") return date === today;
    if (scheduleView === "tomorrow") return date === tomorrow;
    if (scheduleView === "week") return date >= today && date <= weekEnd;
    if (scheduleView === "calendar") return date === calendarDate;
    if (scheduleView === "unassigned") return !job.licence.trim();
    if (scheduleView === "completed") return status === "COMPLETED";
    return status !== "COMPLETED" && status !== "CANCELLED";
  });
  const rows = tab === "new" ? portal.offered : scheduleRows;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 17px")}>
        <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>บริษัทของคุณ</div>
        <div style={css("font-size:16px;font-weight:700;color:#0F2B46;margin-top:2px")}>{portal.supplierName}</div>
        <div style={css("font-size:12px;color:#7B8CA0;margin-top:3px;line-height:1.6")}>
          หน้านี้แสดงเฉพาะงานที่ถูกส่งมาให้บริษัทนี้ และงานที่บริษัทนี้รับไปแล้วเท่านั้น
        </div>
      </div>

      <div style={css("display:flex;gap:7px")}>
        {([["new", "งานใหม่", portal.offered.length], ["schedule", "ตารางงาน", (portal.schedule ?? portal.accepted).length]] as const)
          .map(([id, label, count]) => {
            const on = tab === id;
            return (
              <button key={id} onClick={() => setTab(id)}
                style={css("height:33px;padding:0 15px;border:1px solid " + (on ? "#0A2240" : "#D3DBE3") +
                  ";background:" + (on ? "#0A2240" : "#fff") + ";color:" + (on ? "#fff" : "#3F5265") +
                  ";border-radius:5px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
                {label} <span style={css("opacity:.75")}>{count}</span>
              </button>
            );
          })}
      </div>

      {tab === "schedule" && (
        <div style={css("display:flex;gap:6px;flex-wrap:wrap;align-items:center;background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:9px 10px")}>
          {([
            ["today", "วันนี้"], ["tomorrow", "พรุ่งนี้"], ["week", "7 วัน"],
            ["calendar", "ปฏิทิน"], ["unassigned", "รอจัดรถ"], ["active", "กำลังดำเนินการ"], ["completed", "เสร็จแล้ว"],
          ] as const).map(([id, label]) => {
            const on = scheduleView === id;
            return <button key={id} onClick={() => {
                setScheduleView(id);
                if (id === "calendar" && !calendarDate) setCalendarDate(today);
              }}
              style={css("height:29px;padding:0 11px;border:1px solid " + (on ? "#0A5C97" : "#D3DBE3") +
                ";background:" + (on ? "#EAF4FC" : "#fff") + ";color:" + (on ? "#0A5C97" : "#5A6B7D") +
                ";border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit")}>{label}</button>;
          })}
          {scheduleView === "calendar" && <input type="date" value={calendarDate}
            onChange={(event) => setCalendarDate(event.target.value)}
            style={css("height:29px;padding:0 8px;border:1px solid #9CC2E8;border-radius:4px;font-size:11.5px;font-family:inherit")} />}
          <span style={css("margin-left:auto;font-size:11.5px;color:#7B8CA0")}>{scheduleRows.length} งาน</span>
        </div>
      )}

      {rows.length === 0 && (
        <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>
          {tab === "new" ? "ยังไม่มีงานใหม่ส่งเข้ามา" : "ไม่พบงานในมุมมองนี้"}
        </div>
      )}

      {rows.map((job) => {
        const editing = open === job.key;
        return (
          <div key={job.key} style={css("background:#fff;border:1px solid " + (editing ? "#9CC2E8" : "#E3E8EE") + ";border-radius:6px;padding:14px 17px")}>
            <div style={css("display:flex;gap:14px;justify-content:space-between;flex-wrap:wrap;align-items:flex-start")}>
              <div style={css("flex:1;min-width:250px")}>
                <div style={css("font-size:13.5px;font-weight:650;color:#0F2B46")}>
                  {job.customer} · {job.jobCode || job.key}
                </div>
                <div style={css("font-size:12.5px;color:#5A6B7D;margin-top:4px;line-height:1.75")}>
                  วันที่ {job.date || "—"} · {job.type || "—"} · ลานตู้ {job.cyYard || "—"} · ปลายทาง {job.destination || "—"}
                  <br />
                  ตู้ {job.container || "—"} · น้ำหนัก {job.weight || "—"}
                  {job.pickupPlan ? <> · {job.pickupPlan}</> : null}
                </div>
                {tab === "schedule" && (job.licence || job.driver) && (
                  <div style={css("font-size:12.5px;color:#16794C;margin-top:5px;font-weight:600")}>
                    {job.licence || "—"} · {job.driver || "—"} · {job.contact || "—"}
                  </div>
                )}
                {tab === "schedule" && !job.licence && !job.driver && (
                  <div style={css("font-size:12px;color:#B45309;margin-top:5px;font-weight:600")}>รอจัดรถและคนขับ</div>
                )}
              </div>

              {job.quotedPrice != null && (
                <div style={css("text-align:right")}>
                  <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ราคาที่เสนอ</div>
                  <div style={css("font-size:16px;font-weight:700;color:#0F2B46;font-family:'IBM Plex Mono',monospace")}>
                    {job.quotedPrice.toLocaleString("en-US")}
                  </div>
                </div>
              )}
            </div>

            {tab === "schedule" && (
              <div style={css("margin-top:11px")}>
                <button onClick={() => {
                    const opening = operate !== job.key;
                    setOperate(opening ? job.key : null);
                    if (opening) { setTruckId(""); setTrailerId(""); setDriverId(""); setRemark(""); }
                  }}
                  style={css("height:30px;padding:0 13px;border:1px solid #9CC2E8;background:#F4F8FC;color:#0A5C97;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}>
                  {operate === job.key ? "ปิดการจัดการงาน" : "จัดรถ · อัปเดตสถานะ · POD"}
                </button>
              </div>
            )}

            {tab === "schedule" && operate === job.key && (
              <div style={css("margin-top:12px;padding-top:12px;border-top:1px solid #E9EFF5;display:flex;flex-direction:column;gap:13px")}>
                <div>
                  <div style={css("font-size:12px;font-weight:650;color:#0F2B46;margin-bottom:7px")}>จัดรถและคนขับจากทะเบียนบริษัท</div>
                  <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:end")}>
                    <Pick label="รถ" value={truckId} onChange={setTruckId}>
                      <option value="">เลือกรถ</option>
                      {portal.trucks.map((one) => <option key={one.id} value={one.id}>{one.plate} · {one.vehicleType}</option>)}
                    </Pick>
                    <Pick label="หาง (ถ้ามี)" value={trailerId} onChange={setTrailerId}>
                      <option value="">ไม่ระบุ</option>
                      {portal.trucks.filter((one) => String(one.id) !== truckId).map((one) => <option key={one.id} value={one.id}>{one.plate} · {one.vehicleType}</option>)}
                    </Pick>
                    <Pick label="คนขับ" value={driverId} onChange={setDriverId}>
                      <option value="">เลือกคนขับ</option>
                      {portal.drivers.map((one) => <option key={one.id} value={one.id}>{one.name} · {one.phone || "ไม่มีเบอร์"}</option>)}
                    </Pick>
                    <button disabled={busy || !truckId || !driverId} onClick={() => void saveResources(job)}
                      style={css("height:31px;padding:0 14px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;opacity:" + (busy || !truckId || !driverId ? ".55" : "1"))}>
                      บันทึกการจัดรถ
                    </button>
                  </div>
                </div>

                <div>
                  <div style={css("font-size:12px;font-weight:650;color:#0F2B46;margin-bottom:7px")}>สถานะการขนส่ง</div>
                  <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:end")}>
                    <Pick label="สถานะถัดไป" value={nextStatus} onChange={setNextStatus}>
                      <option value="dispatched">รถออกแล้ว</option><option value="picked_up">รับตู้/สินค้าแล้ว</option>
                      <option value="loading">กำลังขนถ่าย</option><option value="in_transit">ระหว่างขนส่ง</option>
                      <option value="delivered">ส่งมอบแล้ว</option><option value="container_returned">คืนตู้แล้ว</option>
                      <option value="delivery_complete">Delivery Complete</option>
                    </Pick>
                    <Field label="หมายเหตุ" width="260px" value={remark} onChange={setRemark} placeholder="รายละเอียดเพิ่มเติม (ถ้ามี)" />
                    <button disabled={busy} onClick={() => void saveStatus(job)}
                      style={css("height:31px;padding:0 14px;border:0;background:#0A5C97;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;opacity:" + (busy ? ".55" : "1"))}>
                      บันทึกสถานะ
                    </button>
                  </div>
                </div>

                <div>
                  <div style={css("font-size:12px;font-weight:650;color:#0F2B46;margin-bottom:7px")}>POD / หลักฐานส่งมอบ</div>
                  <input type="file" disabled={busy} onChange={(event) => void uploadPod(job, event.target.files?.[0] ?? null)} />
                  {(job.pods?.length ?? 0) > 0 && <div style={css("font-size:11.5px;color:#5A6B7D;margin-top:6px")}>
                    {job.pods!.map((one) => <a key={one.id} href={`/api/documents/${one.id}/content`} target="_blank" rel="noreferrer"
                      style={css("color:#0A5C97;margin-right:10px")}>{one.fileName}</a>)}
                  </div>}
                </div>

                {(job.operations?.length ?? 0) > 0 && <div>
                  <div style={css("font-size:12px;font-weight:650;color:#0F2B46;margin-bottom:6px")}>ประวัติการปฏิบัติงาน</div>
                  {job.operations!.slice().reverse().map((one) => <div key={one.id}
                    style={css("font-size:11.5px;color:#5A6B7D;padding:4px 0;border-top:1px solid #EFF3F7")}>
                    {one.to} · {one.note || "—"} · {new Date(one.eventAt ?? one.recordedAt).toLocaleString("th-TH")}
                  </div>)}
                </div>}
              </div>
            )}

            {tab === "new" && !editing && (
              <div style={css("display:flex;gap:8px;margin-top:12px;flex-wrap:wrap")}>
                <button onClick={() => { setOpen(job.key); setDraft(EMPTY); }}
                  style={css("height:31px;padding:0 15px;border:1px solid #16794C;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                  รับงานนี้
                </button>
                <button onClick={() => {
                    const reason = window.prompt("รับงานนี้ไม่ได้เพราะอะไร?");
                    if (reason && reason.trim()) void answer(job, "decline", { reasonCode: "OTHER", remark: reason.trim() });
                  }}
                  style={css("height:31px;padding:0 15px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                  รับไม่ได้
                </button>
              </div>
            )}

            {editing && (
              <div style={css("margin-top:13px;padding-top:13px;border-top:1px solid #E9EFF5")}>
                <div style={css("font-size:12px;color:#5A6B7D;margin-bottom:9px;line-height:1.6")}>
                  ยืนยันรับงานได้ทันที หรือกรอกรถและคนขับไว้พร้อมกันก็ได้ งานที่รับแล้วจะเข้าตารางงานอัตโนมัติ
                </div>
                <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
                  <Field label="ทะเบียนรถ (ถ้ามี)" width="180px" value={draft.licence}
                    onChange={(v) => setDraft({ ...draft, licence: v })} placeholder="70-1234 กรุงเทพฯ" />
                  <Field label="ชื่อ-สกุลพนักงานขับรถ (ถ้ามี)" width="230px" value={draft.driver}
                    onChange={(v) => setDraft({ ...draft, driver: v })} placeholder="นายสมชาย ใจดี" />
                  <Field label="เบอร์โทร (ถ้ามี)" width="160px" value={draft.contact}
                    onChange={(v) => setDraft({ ...draft, contact: v })} placeholder="081-234-5678" />
                </div>
                <div style={css("display:flex;gap:8px;margin-top:11px;flex-wrap:wrap")}>
                  <button
                    onClick={() => void answer(job, "accept", draft)}
                    disabled={busy}
                    style={css("height:31px;padding:0 16px;border:1px solid #16794C;background:" +
                      (busy ? "#C3CFDB" : "#16794C") +
                      ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                    ยืนยันรับงาน
                  </button>
                  <button onClick={() => { setOpen(null); setDraft(EMPTY); }}
                    style={css("height:31px;padding:0 15px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                    ยกเลิก
                  </button>
                </div>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

function Field({ label, width, value, onChange, placeholder }: {
  label: string; width: string; value: string;
  onChange: (value: string) => void; placeholder: string;
}) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:4px;width:" + width)}>
      <span style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</span>
      <input value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value)}
        style={css("height:31px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;width:100%")} />
    </label>
  );
}

function Pick({ label, value, onChange, children }: {
  label: string; value: string; onChange: (value: string) => void; children: React.ReactNode;
}) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:4px;min-width:190px")}>
      <span style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</span>
      <select value={value} onChange={(event) => onChange(event.target.value)}
        style={css("height:31px;padding:0 8px;border:1px solid #D3DBE3;border-radius:4px;background:#fff;font-size:12px;font-family:inherit")}>
        {children}
      </select>
    </label>
  );
}
