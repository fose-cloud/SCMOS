"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { boardFromCarrier, type Board, type CapacityRow } from "../capacity";
import { parseCarrierCapacity } from "../carrierPortal";
import { useRemembered } from "../pageCache";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";
import { FleetRegister } from "./FleetRegister";
import { VehicleTypes } from "./VehicleTypes";

/**
 * Fleet availability against planned demand.
 *
 * The demand side has always existed — it is the register. The supply side
 * needed somebody to say what they have, and until this screen nobody had a way
 * to, so the capacity-shortage alert could only report that it was unable to
 * judge. That was true and useless.
 *
 * One screen for the department and for a carrier since 30 Sep 2026, as the
 * department asked: a carrier's reads `/api/carrier/capacity` — its own fleet,
 * with its own Leschaco jobs as the demand — and writes there without naming a
 * supplier, which the API takes from the account. Below both, the fleet tables:
 * heads, tails and drivers with their papers (FleetRegister).
 */

type Supplier = { id: number; code: string; name: string; status: string };

export function CapacityBoard({ carrier = false, canEdit, canAdmin, onToast }:
  { carrier?: boolean; canEdit: boolean; canAdmin: boolean; onToast: (m: string) => void }) {
  const [board, setBoard] = useRemembered<Board>(carrier ? "carrier-capacity" : "capacity");
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState("");
  const [form, setForm] = useState({ supplierId: "", date: "", vehicleType: "20F", available: "", committed: "" });

  /** The board as this account may see it: the department's whole one, or a carrier's own. */
  const read = useCallback(async (): Promise<{ board: Board | null; suppliers: Supplier[] }> => {
    if (carrier) {
      const response = await apiFetch("/api/carrier/capacity?days=14", { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(typeof body === "object" && body && "error" in body ? String(body.error) : `เปิดกำลังรถไม่ได้ (${response.status})`);
      const own = parseCarrierCapacity(body);
      return { board: boardFromCarrier(own), suppliers: [{ id: own.supplierId, code: "", name: own.supplierName, status: "approved" }] };
    }
    // The vehicle types are no longer fetched here — the panel reads its own,
    // so it can be mounted anywhere without its host remembering to feed it.
    const [boardResponse, supplierResponse] = await Promise.all([
      apiFetch("/api/capacity?days=7", { headers: { accept: "application/json" } }),
      apiFetch("/api/suppliers?status=approved", { headers: { accept: "application/json" } }),
    ]);
    return {
      board: boardResponse.ok ? await boardResponse.json() as Board : null,
      suppliers: supplierResponse.ok ? await supplierResponse.json() as Supplier[] : [],
    };
  }, [carrier]);

  const load = useCallback(async () => {
    try {
      const { board: data } = await read();
      setBoard((held) => data ?? held);
    } catch (problem) {
      onToast(problem instanceof Error ? problem.message : String(problem));
    }
  }, [read, setBoard, onToast]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const { board: data, suppliers: list } = await read();
        if (cancelled) return;
        setBoard((held) => data ?? held);
        setSuppliers(list);
        setFailure("");
        setForm((prev) => ({
          ...prev,
          supplierId: prev.supplierId || String(list[0]?.id ?? ""),
          date: prev.date || (data?.days[0]?.date ?? ""),
        }));
      } catch (problem) {
        if (!cancelled) setFailure(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
      }
    })();
    return () => { cancelled = true; };
  }, [read, setBoard]);

  async function report() {
    if (busy) return;
    setBusy(true);
    try {
      const counts = {
        date: form.date, vehicleType: form.vehicleType,
        available: Number(form.available || 0), committed: Number(form.committed || 0),
      };
      // A carrier never names the supplier: the API takes it from the account.
      const response = carrier
        ? await apiFetch("/api/carrier/capacity", {
          method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(counts),
        })
        : await apiFetch("/api/capacity", {
          method: "POST", headers: { "content-type": "application/json" },
          body: JSON.stringify({ supplierId: Number(form.supplierId), ...counts }),
        });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกไม่สำเร็จ");
      if (response.ok) setForm((prev) => ({ ...prev, available: "", committed: "" }));
      await load();
    } finally { setBusy(false); }
  }

  /** A row's figures into the form, so saying the day and vehicle again corrects it. */
  function edit(cell: CapacityRow) {
    setForm({
      supplierId: String(cell.supplierId), date: cell.date, vehicleType: cell.vehicleType,
      available: cell.reported === false ? "" : String(cell.available),
      committed: String(cell.reported === false ? cell.demand : cell.committed),
    });
  }

  if (failure) {
    return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid #B45309;border-radius:6px;padding:18px 20px;font-size:12.5px;color:#5A6B7D")}>{failure}</div>;
  }
  if (!board) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const dates = [...new Set([...board.days.map((day) => day.date), ...board.cells.map((cell) => cell.date)])];

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {!board.anyReported && (
        <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-radius:5px;padding:12px 15px;font-size:12.5px;color:#7A4A16;line-height:1.6")}>
          {carrier
            ? "ยังไม่ได้แจ้งจำนวนรถที่ว่าง"
            : <>ยังไม่มีผู้ขนส่งรายใดแจ้งจำนวนรถที่ว่าง — ตารางด้านล่างจึงแสดงได้เฉพาะฝั่งงานที่วางแผนไว้
              ระบบไม่เดาว่ารถพอหรือไม่พอ เพราะการบอกว่า “ไม่ขาด” ทั้งที่ไม่มีข้อมูล แย่กว่าการบอกว่ายังไม่รู้</>}
        </div>
      )}

      {/* Its own component since 2026-09-01, and mounted here alone. It briefly
          lived on Administration while Capacity was out of the menu; two copies
          of one panel is how this codebase has drifted before. A carrier reads
          it and changes nothing — the list is the department's. */}
      <VehicleTypes canAdmin={!carrier && canAdmin} onToast={onToast} />

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          7 วันข้างหน้า
        </div>
        <div style={css("display:grid;grid-template-columns:repeat(7,1fr);gap:1px;background:#E9EFF5;overflow-x:auto")}>
          {board.days.map((day) => (
            <div key={day.date} style={css("background:" + (day.short ? "#FEF6F5" : "#fff") + ";padding:10px 12px;min-width:84px")}>
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
              <select aria-label="ผู้ขนส่ง" value={form.supplierId} disabled={carrier}
                onChange={(e) => setForm({ ...form, supplierId: e.target.value })}
                style={css(SELECT + ";min-width:180px")}>
                {suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
              </select>
            </Field>
            <Field label="วันที่">
              <select aria-label="วันที่" value={form.date} onChange={(e) => setForm({ ...form, date: e.target.value })} style={SELECT_S}>
                {dates.map((date) => <option key={date} value={date}>{date}</option>)}
              </select>
            </Field>
            <Field label="ประเภทรถ">
              <select aria-label="ประเภทรถ" value={form.vehicleType} onChange={(e) => setForm({ ...form, vehicleType: e.target.value })} style={SELECT_S}>
                {board.vehicleTypes.map((v) => <option key={v} value={v}>{v}</option>)}
              </select>
            </Field>
            <Field label="มีทั้งหมด">
              <input aria-label="มีทั้งหมด" value={form.available} inputMode="numeric"
                onChange={(e) => setForm({ ...form, available: e.target.value.replace(/\D/g, "") })}
                style={css(INPUT + ";width:96px")} />
            </Field>
            <Field label="รับงานไว้แล้ว">
              <input aria-label="รับงานไว้แล้ว" value={form.committed} inputMode="numeric"
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
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["ผู้ขนส่ง", "วันที่", "ประเภทรถ", "มี", "รับไว้", "ว่าง", "งานตามแผน", "แจ้งโดย", ...(canEdit ? [""] : [])].map((h, i) => (
                <th key={h + i} style={css("background:#F8FAFC;padding:8px 12px;text-align:" + (i >= 3 && i <= 6 ? "right" : "left") + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {board.cells.map((cell) => {
                  const unreported = cell.reported === false;
                  return (
                    <tr key={`${cell.supplierId}-${cell.date}-${cell.vehicleType}`}
                      style={css("border-bottom:1px solid #F1F5F9;background:" + (cell.short ? "#FEF6F5" : "#fff"))}>
                      <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{cell.supplier}</td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{cell.date}</td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{cell.vehicleType}</td>
                      <td style={css(CELL + NUM)}>{unreported ? "—" : cell.available}</td>
                      <td style={css(CELL + NUM)}>{unreported ? "—" : cell.committed}</td>
                      <td style={css(CELL + NUM + ";font-weight:600;color:" + (unreported ? "#7B8CA0" : cell.short ? "#B42318" : "#16794C"))}>{unreported ? "ยังไม่แจ้ง" : cell.spare}</td>
                      <td style={css(CELL + NUM + ";color:#7B8CA0")}>{cell.demand}</td>
                      <td style={css(CELL + ";font-size:11.5px;color:#94A3B8")}>{cell.updatedBy || "—"}</td>
                      {canEdit && <td style={css(CELL)}>
                        <button onClick={() => edit(cell)}
                          style={css("height:26px;padding:0 10px;border:1px solid #9CC2E8;background:#F4F8FC;color:#0A5C97;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}>
                          {unreported ? "แจ้ง" : "แก้"}</button>
                      </td>}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </ZoomBox>
        </div>
      )}

      <FleetRegister carrier={carrier} onToast={onToast} />
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
