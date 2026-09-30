"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { isoDay, parseCarrierCapacity, registerDate, type CapacityCell, type CarrierCapacity as Capacity } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

type Draft = { day: string; vehicle: string; available: string; committed: string };

/**
 * The carrier's own Capacity (30 Sep 2026): how many trucks of each kind it
 * has free each day, what it has already promised, and beside them its own
 * Leschaco jobs for that day. Saying a day and vehicle again corrects it. The
 * API takes the company from the account and shows no other carrier's fleet.
 */
export function CarrierCapacity({ onToast }: { onToast: (message: string) => void }) {
  const [capacity, setCapacity] = useState<Capacity | null>(null);
  const [error, setError] = useState("");
  const [draft, setDraft] = useState<Draft>({ day: "", vehicle: "", available: "", committed: "0" });
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/carrier/capacity?days=14", { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(typeof body === "object" && body && "error" in body ? String(body.error) : `เปิดกำลังรถไม่ได้ (${response.status})`);
      const parsed = parseCarrierCapacity(body);
      setCapacity(parsed); setError("");
      setDraft(d => d.day || !parsed.dates[0] ? d : { ...d, day: isoDay(parsed.dates[0]), vehicle: d.vehicle || parsed.vehicleTypes[0] || "" });
    } catch (problem) {
      setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
    }
  }, []);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const available = Number(draft.available);
  const committed = Number(draft.committed || "0");
  const valid = registerDate(draft.day) !== "" && draft.vehicle !== "" && draft.available.trim() !== ""
    && Number.isInteger(available) && available >= 0 && Number.isInteger(committed) && committed >= 0;

  async function save() {
    if (busy || !valid) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/carrier/capacity", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ date: registerDate(draft.day), vehicleType: draft.vehicle, available, committed }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกไม่สำเร็จ");
      if (response.ok) { setDraft(d => ({ ...d, available: "", committed: "0" })); await load(); }
    } finally { setBusy(false); }
  }

  function edit(cell: CapacityCell) {
    setDraft({ day: isoDay(cell.date), vehicle: cell.vehicleType, available: cell.reported ? String(cell.available) : "",
      committed: String(cell.reported ? cell.committed : cell.jobs) });
  }

  if (error) return <Notice tone="#B45309">{error}</Notice>;
  if (!capacity) return <div style={css("padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>กำลังโหลด…</div>;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 17px")}>
        <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>กำลังรถของบริษัท</div>
        <div style={css("font-size:16px;font-weight:700;color:#0F2B46;margin-top:2px")}>{capacity.supplierName}</div>
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 14px;display:flex;gap:8px;flex-wrap:wrap;align-items:end")}>
        <Field label="วันที่">
          <input type="date" aria-label="วันที่" value={draft.day} onChange={(event) => setDraft({ ...draft, day: event.target.value })} style={css(INPUT)} />
        </Field>
        <Field label="ประเภทรถ">
          <select aria-label="ประเภทรถ" value={draft.vehicle} onChange={(event) => setDraft({ ...draft, vehicle: event.target.value })} style={css(INPUT)}>
            {capacity.vehicleTypes.map(one => <option key={one} value={one}>{one}</option>)}
          </select>
        </Field>
        <Field label="รถว่าง (คัน)">
          <input aria-label="รถว่าง (คัน)" inputMode="numeric" value={draft.available} onChange={(event) => setDraft({ ...draft, available: event.target.value.replace(/\D/g, "") })} style={css(INPUT + "width:90px")} />
        </Field>
        <Field label="รับงานไว้แล้ว (คัน)">
          <input aria-label="รับงานไว้แล้ว (คัน)" inputMode="numeric" value={draft.committed} onChange={(event) => setDraft({ ...draft, committed: event.target.value.replace(/\D/g, "") })} style={css(INPUT + "width:90px")} />
        </Field>
        <button onClick={() => void save()} disabled={busy || !valid}
          style={css("height:31px;padding:0 16px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;opacity:" + (busy || !valid ? ".55" : "1"))}>
          บันทึกกำลังรถ</button>
      </div>

      {capacity.cells.length === 0
        ? <Notice tone="#7B8CA0">ยังไม่มีกำลังรถหรืองานใน 14 วันข้างหน้า</Notice>
        : <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px")}><ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:620px")}>
            <thead><tr>{["วันที่", "ประเภทรถ", "รถว่าง", "รับไว้แล้ว", "เหลือ", "งาน Leschaco", "แจ้งโดย", ""].map(head =>
              <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>{capacity.cells.map(cell => {
              const short = cell.reported && cell.spare < 0;
              const unreported = !cell.reported;
              return <tr key={cell.date + cell.vehicleType}>
                <td style={css(CELL)}>{cell.date}</td>
                <td style={css(CELL)}>{cell.vehicleType}</td>
                <td style={css(CELL + NUMBER)}>{unreported ? "—" : cell.available}</td>
                <td style={css(CELL + NUMBER)}>{unreported ? "—" : cell.committed}</td>
                <td style={css(CELL + NUMBER + "font-weight:700;color:" + (short ? "#B42318" : unreported ? "#7B8CA0" : "#16794C"))}>{unreported ? "ยังไม่แจ้ง" : cell.spare}</td>
                <td style={css(CELL + NUMBER)}>{cell.jobs}</td>
                <td style={css(CELL + "color:#7B8CA0")}>{cell.updatedBy || "—"}</td>
                <td style={css(CELL)}><button onClick={() => edit(cell)}
                  style={css("height:26px;padding:0 10px;border:1px solid #9CC2E8;background:#F4F8FC;color:#0A5C97;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}>
                  {unreported ? "แจ้ง" : "แก้"}</button></td>
              </tr>;
            })}</tbody>
          </table>
        </ZoomBox></div>}
    </div>
  );
}

const INPUT = "height:31px;padding:0 8px;border:1px solid #D3DBE3;border-radius:4px;background:#fff;font-size:12.5px;font-family:inherit;";
const HEAD = "text-align:left;padding:9px 10px;background:#EEF3F8;color:#52657A;font-weight:650;white-space:nowrap;";
const CELL = "padding:8px 10px;border-top:1px solid #E6ECF3;vertical-align:middle;color:#0F2B46;";
const NUMBER = "text-align:right;font-family:'IBM Plex Mono',monospace;white-space:nowrap;";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:4px")}>
    <span style={css("font-size:10.5px;color:#7B8CA0;font-weight:600")}>{label}</span>{children}
  </label>;
}

function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid " + tone + ";border-radius:6px;padding:18px 20px;font-size:12.5px;color:#5A6B7D")}>{children}</div>;
}
