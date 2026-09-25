"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

type CalendarDay = {
  date: string; kind: string; name: string; updatedBy: string; updatedAt: string;
};
type SlaRule = {
  id: number; code: string; startDay: "day0" | "day1"; targetWorkingDays: 3 | 4;
  effectiveFrom: string; effectiveTo: string | null; active: boolean;
  updatedBy: string; updatedAt: string;
};

const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden";
const HEAD = "padding:7px 10px;background:#F4F7FA;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;text-align:left;white-space:nowrap";
const CELL = "padding:7px 10px;border-bottom:1px solid #EDF1F5;font-size:12px;vertical-align:middle";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12px;font-family:inherit;background:#fff;color:#0A2240";
const BUTTON = "height:29px;padding:0 12px;border-radius:4px;font-size:12px;font-family:inherit;cursor:pointer;border:1px solid";
const KINDS = [
  ["working-day", "วันทำงาน"],
  ["weekend", "วันหยุดสุดสัปดาห์"],
  ["public-holiday", "วันหยุดราชการ"],
  ["company-holiday", "วันหยุดบริษัท"],
  ["special-working-day", "วันทำงานพิเศษ"],
] as const;

function range(year: number) {
  return { from: `${year}-01-01`, to: `${year}-12-31` };
}

function errorOf(value: unknown, fallback: string) {
  return value && typeof value === "object" && "error" in value && typeof value.error === "string"
    ? value.error : fallback;
}

/** Phase 1 administration only: calendar exceptions and effective SLA rules. */
export function CarrierBillingFoundation({ canManage, onToast }: {
  canManage: boolean; onToast: (message: string) => void;
}) {
  const [year, setYear] = useState(new Date().getFullYear());
  const [days, setDays] = useState<CalendarDay[] | null>(null);
  const [rules, setRules] = useState<SlaRule[] | null>(null);
  const [failure, setFailure] = useState("");
  const [busy, setBusy] = useState(false);
  const [day, setDay] = useState({ date: "", kind: "public-holiday", name: "" });
  const [sla, setSla] = useState({ code: "STANDARD", startDay: "", targetWorkingDays: "4",
    effectiveFrom: "", effectiveTo: "", active: true });

  const load = useCallback(async () => {
    const dates = range(year);
    try {
      const [calendarResponse, slaResponse] = await Promise.all([
        apiFetch(`/api/carrier-billing/foundation/calendar?from=${dates.from}&to=${dates.to}`,
          { headers: { accept: "application/json" } }),
        apiFetch("/api/carrier-billing/foundation/sla", { headers: { accept: "application/json" } }),
      ]);
      const [calendarBody, slaBody] = await Promise.all([
        calendarResponse.json().catch(() => null), slaResponse.json().catch(() => null),
      ]);
      if (!calendarResponse.ok || !slaResponse.ok) {
        setFailure(errorOf(!calendarResponse.ok ? calendarBody : slaBody,
          `อ่านการตั้งค่าไม่สำเร็จ (${!calendarResponse.ok ? calendarResponse.status : slaResponse.status})`));
        setDays([]); setRules([]);
        return;
      }
      setDays(Array.isArray(calendarBody) ? calendarBody as CalendarDay[] : []);
      setRules(Array.isArray(slaBody) ? slaBody as SlaRule[] : []);
      setFailure("");
    } catch (error) {
      setFailure("ติดต่อ API ไม่ได้: " + (error instanceof Error ? error.message : String(error)));
      setDays([]); setRules([]);
    }
  }, [year]);

  // The state writes happen after the API promises settle; this is the same
  // mount-load shape as the other SCMOS register screens.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function mutate(path: string, method: "PUT" | "DELETE", body?: unknown) {
    if (busy || !canManage) return false;
    setBusy(true);
    try {
      const response = await apiFetch(path, {
        method,
        headers: body === undefined ? { accept: "application/json" }
          : { "content-type": "application/json", accept: "application/json" },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      });
      const answer = await response.json().catch(() => null) as { message?: string; error?: string } | null;
      onToast(answer?.message ?? answer?.error ?? `ทำรายการไม่สำเร็จ (${response.status})`);
      if (response.ok) await load();
      return response.ok;
    } catch (error) {
      onToast("ติดต่อ API ไม่ได้: " + (error instanceof Error ? error.message : String(error)));
      return false;
    } finally { setBusy(false); }
  }

  const saveDay = async () => {
    if (!day.date) return;
    if (await mutate(`/api/carrier-billing/foundation/calendar/${day.date}`, "PUT",
      { kind: day.kind, name: day.name.trim() }))
      setDay({ date: "", kind: "public-holiday", name: "" });
  };

  const saveSla = async () => {
    const code = sla.code.trim().toUpperCase();
    if (!code || !sla.effectiveFrom || !sla.startDay) return;
    if (await mutate(`/api/carrier-billing/foundation/sla/${encodeURIComponent(code)}/${sla.effectiveFrom}`,
      "PUT", { startDay: sla.startDay, targetWorkingDays: Number(sla.targetWorkingDays),
        effectiveTo: sla.effectiveTo || null, active: sla.active }))
      setSla({ code: "STANDARD", startDay: "", targetWorkingDays: "4",
        effectiveFrom: "", effectiveTo: "", active: true });
  };

  return (
    <section style={css("display:flex;flex-direction:column;gap:12px")}
      aria-label="Carrier billing foundation configuration">
      <div style={css("border:1px solid #BBD5EE;background:#F3F8FC;border-radius:5px;padding:11px 14px;font-size:12px;color:#315A7D;line-height:1.65") }>
        <strong style={css("color:#0A2240")}>Carrier Billing · Foundation</strong><br />
        กำหนดวันทำการและกฎ SLA ที่จะใช้เมื่อเริ่ม Billing ใน Phase ถัดไป ระบบไม่สร้างค่า Day 0/Day 1 ให้เอง
        {!canManage && " — บัญชีนี้ดูได้อย่างเดียว"}
      </div>

      {failure && <div style={css("border:1px solid #F3C9C4;background:#FFF5F4;color:#B42318;border-radius:5px;padding:10px 13px;font-size:12px") }>
        {failure} <button onClick={() => void load()} style={css("margin-left:8px;border:0;background:transparent;color:#1E5B8F;text-decoration:underline;cursor:pointer")}>ลองใหม่</button>
      </div>}

      <div style={css(CARD)}>
        <div style={css("padding:10px 12px;border-bottom:1px solid #E6EBF0;display:flex;align-items:center;gap:9px;flex-wrap:wrap") }>
          <strong style={css("font-size:13px;color:#0A2240")}>Business Calendar · ข้อยกเว้นปี</strong>
          <input type="number" min={2020} max={2100} value={year}
            onChange={(event) => setYear(Number(event.target.value))} style={css(`${CONTROL};width:84px`)} />
          <span style={css("font-size:11.5px;color:#7B8CA0")}>วันที่ไม่อยู่ในรายการใช้ จันทร์–ศุกร์ เป็นวันทำงานตามปกติ</span>
        </div>
        <ZoomBox capped={false} zoomable={false}>
          <table style={css("width:100%;border-collapse:collapse;min-width:720px") }>
            <thead><tr><th style={css(HEAD)}>วันที่</th><th style={css(HEAD)}>ประเภท</th>
              <th style={css(HEAD)}>ชื่อ/เหตุผล</th><th style={css(HEAD)}>แก้ล่าสุด</th><th style={css(HEAD)} /></tr></thead>
            <tbody>
              {(days ?? []).map((row) => <tr key={row.date}>
                <td style={css(CELL)}>{row.date}</td>
                <td style={css(CELL)}>{KINDS.find(([key]) => key === row.kind)?.[1] ?? row.kind}</td>
                <td style={css(CELL)}>{row.name || "—"}</td>
                <td style={css(`${CELL};color:#64748B`)}>{row.updatedBy || "—"}</td>
                <td style={css(`${CELL};text-align:right`)}>{canManage && <button disabled={busy}
                  onClick={() => void mutate(`/api/carrier-billing/foundation/calendar/${row.date}`, "DELETE")}
                  style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#B42318`)}>ลบข้อยกเว้น</button>}</td>
              </tr>)}
              {days?.length === 0 && <tr><td colSpan={5} style={css("padding:18px;text-align:center;color:#94A3B8;font-size:12px")}>ยังไม่มีข้อยกเว้นในปีนี้</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
        {canManage && <div style={css("padding:10px 12px;border-top:1px solid #E6EBF0;display:flex;gap:8px;align-items:center;flex-wrap:wrap") }>
          <input aria-label="Calendar date" type="date" value={day.date} onChange={(event) => setDay({ ...day, date: event.target.value })} style={css(CONTROL)} />
          <select value={day.kind} onChange={(event) => setDay({ ...day, kind: event.target.value })} style={css(CONTROL)}>
            {KINDS.map(([key, label]) => <option key={key} value={key}>{label}</option>)}
          </select>
          <input aria-label="Calendar name" value={day.name} maxLength={160} placeholder="ชื่อวันหยุด / เหตุผล"
            onChange={(event) => setDay({ ...day, name: event.target.value })} style={css(`${CONTROL};min-width:240px`)} />
          <button disabled={busy || !day.date} onClick={() => void saveDay()}
            style={css(`${BUTTON};border-color:#1E5B8F;background:#1E5B8F;color:#fff`)}>บันทึกวัน</button>
        </div>}
      </div>

      <div style={css(CARD)}>
        <div style={css("padding:10px 12px;border-bottom:1px solid #E6EBF0") }>
          <strong style={css("font-size:13px;color:#0A2240")}>Billing SLA Rules</strong>
          <span style={css("font-size:11.5px;color:#7B8CA0;margin-left:9px")}>ต้องระบุ Day 0 หรือ Day 1 และ 3 หรือ 4 วันทำการอย่างชัดเจน</span>
        </div>
        <ZoomBox capped={false} zoomable={false}>
          <table style={css("width:100%;border-collapse:collapse;min-width:820px") }>
            <thead><tr><th style={css(HEAD)}>รหัส</th><th style={css(HEAD)}>เริ่มนับ</th><th style={css(HEAD)}>เป้าหมาย</th>
              <th style={css(HEAD)}>เริ่มใช้</th><th style={css(HEAD)}>สิ้นสุด</th><th style={css(HEAD)}>สถานะ</th><th style={css(HEAD)} /></tr></thead>
            <tbody>
              {(rules ?? []).map((row) => <tr key={row.id}>
                <td style={css(CELL)}>{row.code}</td><td style={css(CELL)}>{row.startDay === "day0" ? "Day 0" : "Day 1"}</td>
                <td style={css(CELL)}>{row.targetWorkingDays} วันทำการ</td><td style={css(CELL)}>{row.effectiveFrom}</td>
                <td style={css(CELL)}>{row.effectiveTo || "ไม่กำหนด"}</td>
                <td style={css(CELL)}><span style={css(row.active ? "color:#16794C" : "color:#94A3B8")}>{row.active ? "ใช้งาน" : "ปิด"}</span></td>
                <td style={css(`${CELL};text-align:right`)}>{canManage && <button disabled={busy} onClick={() => setSla({
                  code: row.code, startDay: row.startDay, targetWorkingDays: String(row.targetWorkingDays),
                  effectiveFrom: row.effectiveFrom, effectiveTo: row.effectiveTo ?? "", active: row.active,
                })} style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#1E5B8F`)}>แก้ไข</button>}</td>
              </tr>)}
              {rules?.length === 0 && <tr><td colSpan={7} style={css("padding:18px;text-align:center;color:#B45309;font-size:12px")}>ยังไม่มีกฎ SLA — ระบบจะไม่เดา Day 0/Day 1</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
        {canManage && <div style={css("padding:10px 12px;border-top:1px solid #E6EBF0;display:flex;gap:8px;align-items:center;flex-wrap:wrap") }>
          <input aria-label="SLA code" value={sla.code} maxLength={40} placeholder="รหัส เช่น STANDARD"
            onChange={(event) => setSla({ ...sla, code: event.target.value })} style={css(`${CONTROL};width:150px`)} />
          <select aria-label="SLA start day" value={sla.startDay} onChange={(event) => setSla({ ...sla, startDay: event.target.value })} style={css(CONTROL)}>
            <option value="">เลือกวันเริ่ม…</option><option value="day0">Day 0</option><option value="day1">Day 1</option>
          </select>
          <select aria-label="SLA working days" value={sla.targetWorkingDays} onChange={(event) => setSla({ ...sla, targetWorkingDays: event.target.value })} style={css(CONTROL)}>
            <option value="3">3 วันทำการ</option><option value="4">4 วันทำการ</option>
          </select>
          <label style={css("font-size:11px;color:#64748B")}>เริ่มใช้ <input type="date" value={sla.effectiveFrom}
            onChange={(event) => setSla({ ...sla, effectiveFrom: event.target.value })} style={css(CONTROL)} /></label>
          <label style={css("font-size:11px;color:#64748B")}>สิ้นสุด <input type="date" value={sla.effectiveTo}
            onChange={(event) => setSla({ ...sla, effectiveTo: event.target.value })} style={css(CONTROL)} /></label>
          <label style={css("font-size:12px;color:#465A6E;display:flex;gap:5px;align-items:center") }><input type="checkbox" checked={sla.active}
            onChange={(event) => setSla({ ...sla, active: event.target.checked })} /> ใช้งาน</label>
          <button disabled={busy || !sla.code.trim() || !sla.startDay || !sla.effectiveFrom} onClick={() => void saveSla()}
            style={css(`${BUTTON};border-color:#16794C;background:#16794C;color:#fff`)}>บันทึกกฎ</button>
        </div>}
      </div>
    </section>
  );
}
