"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";

/**
 * Fleet availability against planned demand.
 *
 * The demand side has always existed — it is the register. The supply side
 * needed somebody to say what they have, and until this screen nobody had a way
 * to, so the capacity-shortage alert could only report that it was unable to
 * judge. That was true and useless.
 */

type Cell = {
  supplierId: number; supplier: string; date: string; vehicleType: string;
  available: number; committed: number; demand: number;
  spare: number; short: boolean; updatedBy: string; updatedAt: string | null;
};
type Day = { date: string; available: number; committed: number; demand: number; short: boolean };
type Board = { days: Day[]; cells: Cell[]; vehicleTypes: string[]; anyReported: boolean };
type Supplier = { id: number; code: string; name: string; status: string };

export function CapacityBoard({ canEdit, onToast }: { canEdit: boolean; onToast: (m: string) => void }) {
  const [board, setBoard] = useState<Board | null>(null);
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ supplierId: "", date: "", vehicleType: "20F", available: "", committed: "" });

  const load = useCallback(async () => {
    const response = await apiFetch("/api/capacity?days=7", { headers: { accept: "application/json" } });
    setBoard(response.ok ? await response.json() as Board : null);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const [boardResponse, supplierResponse] = await Promise.all([
        apiFetch("/api/capacity?days=7", { headers: { accept: "application/json" } }),
        apiFetch("/api/suppliers?status=approved", { headers: { accept: "application/json" } }),
      ]);
      const data = boardResponse.ok ? await boardResponse.json() as Board : null;
      const list = supplierResponse.ok ? await supplierResponse.json() as Supplier[] : [];
      if (cancelled) return;
      setBoard(data);
      setSuppliers(list);
      setForm((prev) => ({
        ...prev,
        supplierId: prev.supplierId || String(list[0]?.id ?? ""),
        date: prev.date || (data?.days[0]?.date ?? ""),
      }));
    })();
    return () => { cancelled = true; };
  }, []);

  async function report() {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/capacity", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({
          supplierId: Number(form.supplierId), date: form.date, vehicleType: form.vehicleType,
          available: Number(form.available || 0), committed: Number(form.committed || 0),
        }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  if (!board) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {!board.anyReported && (
        <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-radius:5px;padding:12px 15px;font-size:12.5px;color:#7A4A16;line-height:1.6")}>
          ยังไม่มีผู้ขนส่งรายใดแจ้งจำนวนรถที่ว่าง — ตารางด้านล่างจึงแสดงได้เฉพาะฝั่งงานที่วางแผนไว้
          ระบบไม่เดาว่ารถพอหรือไม่พอ เพราะการบอกว่า “ไม่ขาด” ทั้งที่ไม่มีข้อมูล แย่กว่าการบอกว่ายังไม่รู้
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          7 วันข้างหน้า
        </div>
        <div style={css("display:grid;grid-template-columns:repeat(7,1fr);gap:1px;background:#E9EFF5")}>
          {board.days.map((day) => (
            <div key={day.date} style={css("background:" + (day.short ? "#FEF6F5" : "#fff") + ";padding:10px 12px")}>
              <div style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0")}>{day.date.slice(0, 5)}</div>
              <div style={css("font-family:ui-monospace,monospace;font-size:20px;font-weight:600;color:" + (day.short ? "#B42318" : "#0A2240"))}>
                {day.demand}
              </div>
              <div style={css("font-size:11px;color:#94A3B8")}>งานตามแผน</div>
              <div style={css("font-size:11.5px;margin-top:5px;color:" + (day.available === 0 ? "#94A3B8" : "#16794C"))}>
                {day.available === 0 ? "ยังไม่แจ้งรถ" : `ว่าง ${day.available - day.committed} / ${day.available}`}
              </div>
            </div>
          ))}
        </div>
      </div>

      {canEdit && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
          <div style={css("font-size:12.5px;font-weight:650;color:#0A2240;margin-bottom:3px")}>แจ้งจำนวนรถ</div>
          <div style={css("font-size:11.5px;color:#94A3B8;margin-bottom:10px")}>
            บันทึกซ้ำวันเดิมและรถประเภทเดิมคือการแก้ตัวเลข ไม่ใช่การบวกเพิ่ม
          </div>
          <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
            <Field label="ผู้ขนส่ง">
              <select value={form.supplierId} onChange={(e) => setForm({ ...form, supplierId: e.target.value })}
                style={css(SELECT + ";min-width:180px")}>
                {suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </Field>
            <Field label="วันที่">
              <select value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} style={SELECT_S}>
                {board.days.map((d) => <option key={d.date} value={d.date}>{d.date}</option>)}
              </select>
            </Field>
            <Field label="ประเภทรถ">
              <select value={form.vehicleType} onChange={(e) => setForm({ ...form, vehicleType: e.target.value })} style={SELECT_S}>
                {board.vehicleTypes.map((v) => <option key={v} value={v}>{v}</option>)}
              </select>
            </Field>
            <Field label="มีทั้งหมด">
              <input value={form.available} inputMode="numeric"
                onChange={(e) => setForm({ ...form, available: e.target.value.replace(/\D/g, "") })}
                style={css(INPUT + ";width:96px")} />
            </Field>
            <Field label="รับงานไว้แล้ว">
              <input value={form.committed} inputMode="numeric"
                onChange={(e) => setForm({ ...form, committed: e.target.value.replace(/\D/g, "") })}
                style={css(INPUT + ";width:110px")} />
            </Field>
            <button onClick={() => void report()} disabled={busy || !form.supplierId || !form.date}
              style={css("height:30px;padding:0 15px;border:1px solid #0A2240;background:" +
                (busy || !form.supplierId ? "#C3CFDB" : "#0A2240") +
                ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>บันทึก</button>
          </div>
        </div>
      )}

      {board.cells.length > 0 && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["ผู้ขนส่ง", "วันที่", "ประเภทรถ", "มี", "รับไว้", "ว่าง", "งานตามแผน", "แจ้งโดย"].map((h, i) => (
                <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:" + (i >= 3 && i <= 6 ? "right" : "left") + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {board.cells.map((cell) => (
                  <tr key={`${cell.supplierId}-${cell.date}-${cell.vehicleType}`}
                    style={css("border-bottom:1px solid #F1F5F9;background:" + (cell.short ? "#FEF6F5" : "#fff"))}>
                    <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{cell.supplier}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{cell.date}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{cell.vehicleType}</td>
                    <td style={css(CELL + NUM)}>{cell.available}</td>
                    <td style={css(CELL + NUM)}>{cell.committed}</td>
                    <td style={css(CELL + NUM + ";font-weight:600;color:" + (cell.short ? "#B42318" : "#16794C"))}>{cell.spare}</td>
                    <td style={css(CELL + NUM + ";color:#7B8CA0")}>{cell.demand}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:#94A3B8")}>{cell.updatedBy}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}

const CELL = "padding:8px 12px";
const NUM = ";text-align:right;font-family:ui-monospace,monospace";
const INPUT = "height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px";
const SELECT = "height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff";
const SELECT_S = css(SELECT);

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:3px")}>
      <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
      {children}
    </label>
  );
}
