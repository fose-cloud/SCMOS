"use client";

import { useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";
import { QuoteCalculator } from "./QuoteCalculator";
import { RateInquiry } from "./RateInquiry";

/**
 * What this journey costs, by carrier.
 *
 * The prices come from Azure SQL rather than the browser's copy of the rate
 * book, so the same figures are available to the backend when it orders carrier
 * priority. A carrier only appears when they actually priced the vehicle the
 * job needs — an unpriced carrier is absent rather than shown at zero.
 */

type Quote = {
  carrier: string; supplierId: number | null; price: number;
  lane: {
    id: number; carrier: string; service: string; customer: string;
    from: string; to: string; county: string; remark: string;
  };
};

const VEHICLES = ["20F", "40F", "20F DG", "40F DG", "4W", "6W", "10W", "4W DG", "6W DG", "10W DG", "20RF", "40RF", "20TK"];

export function Quotation({ diesel, onDiesel, onToast }: {
  diesel: number; onDiesel: (v: number) => void; onToast: (m: string) => void;
}) {
  const [customer, setCustomer] = useState("");
  const [destination, setDestination] = useState("");
  const [vehicle, setVehicle] = useState("20F");
  const [quotes, setQuotes] = useRemembered<Quote[]>("quotation");
  const [busy, setBusy] = useState(false);
  /**
   * Two halves of the same job, on one screen.
   *
   * Raising an inquiry is asking a carrier what a journey will cost; the
   * comparison below is reading back what they have already agreed. The screen
   * opens on the question, because that is the one somebody arrives here to do
   * — the rate book answers itself.
   */
  const [view, setView] = useState<"inquiry" | "compare" | "calculate">("inquiry");

  async function ask() {
    if (busy) return;
    if (!customer.trim() && !destination.trim()) {
      onToast("ระบุลูกค้าหรือปลายทางอย่างน้อยหนึ่งอย่าง");
      return;
    }
    setBusy(true);
    try {
      const query = new URLSearchParams({
        customer: customer.trim(), destination: destination.trim(),
        vehicle, diesel: String(diesel),
      });
      const response = await apiFetch(`/api/rates/quotes?${query}`, { headers: { accept: "application/json" } });
      if (!response.ok) { onToast("ขอราคาไม่สำเร็จ · HTTP " + response.status); return; }
      const body = await response.json() as Quote[];
      setQuotes(body);
      onToast(body.length ? `พบ ${body.length} เจ้าที่เสนอราคาเส้นทางนี้` : "ยังไม่มีเจ้าไหนเสนอราคาเส้นทางนี้สำหรับรถประเภทนี้");
    } finally { setBusy(false); }
  }

  const cheapest = quotes?.length ? quotes[0].price : 0;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:flex;gap:7px;flex-wrap:wrap")}>
        {([
          ["calculate", "คำนวณราคา", "Rate Calculator"],
          ["inquiry", "ขอราคาใหม่", "Rate Inquiry"],
          ["compare", "เทียบราคาที่มีอยู่", "Rate Comparison"],
        ] as ["inquiry" | "compare" | "calculate", string, string][]).map(([key, th, en]) => {
          const on = view === key;
          return (
            <button key={key} onClick={() => setView(key)}
              style={css("height:34px;padding:0 15px;border:1px solid " + (on ? "#0A2240" : "#E2E8F0") +
                ";background:" + (on ? "#0A2240" : "#fff") + ";color:" + (on ? "#fff" : "#64748B") +
                ";border-radius:4px;font-size:12.5px;cursor:pointer;font-family:inherit;font-weight:" +
                (on ? "600" : "400"))}>
              {th} <span style={css("opacity:.7;font-size:11px")}>· {en}</span>
            </button>
          );
        })}
      </div>

      {view === "calculate" && <QuoteCalculator onToast={onToast} />}

      {view === "inquiry" && <RateInquiry onToast={onToast} />}

      {view === "compare" && (
        <>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
        <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
          <Field label="ลูกค้า" width="220px">
            <input value={customer} onChange={(e) => setCustomer(e.target.value)} placeholder="เช่น ALLNEX"
              onKeyDown={(e) => { if (e.key === "Enter") void ask(); }}
              style={INPUT} />
          </Field>
          <Field label="ปลายทาง" width="220px">
            <input value={destination} onChange={(e) => setDestination(e.target.value)} placeholder="เช่น LAEM CHABANG"
              onKeyDown={(e) => { if (e.key === "Enter") void ask(); }}
              style={INPUT} />
          </Field>
          <Field label="ประเภทรถ" width="130px">
            <select value={vehicle} onChange={(e) => setVehicle(e.target.value)}
              style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;width:100%")}>
              {VEHICLES.map((v) => <option key={v} value={v}>{v}</option>)}
            </select>
          </Field>
          <Field label="ราคาน้ำมัน (บาท/ลิตร)" width="150px">
            <input value={String(diesel)} inputMode="decimal"
              onChange={(e) => { const n = Number(e.target.value); if (!Number.isNaN(n)) onDiesel(n); }}
              style={INPUT} />
          </Field>
          <button onClick={() => void ask()} disabled={busy}
            style={css("height:30px;padding:0 16px;border:1px solid #0A2240;background:" + (busy ? "#C3CFDB" : "#0A2240") +
              ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
          >{busy ? "กำลังค้น…" : "ขอราคา"}</button>
        </div>
        <div style={css("font-size:11.5px;color:#94A3B8;margin-top:9px;line-height:1.6")}>
          ราคาปรับตามช่วงราคาน้ำมันที่ผู้ขนส่งเสนอไว้ — เจ้าที่เสนอไว้ถึงช่วงต่ำเท่านั้น
          จะใช้ราคาช่วงสุดท้ายที่เสนอ เพราะนั่นคือราคาตามสัญญา
        </div>
      </div>

      {quotes !== null && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
            ผลเปรียบเทียบ · {quotes.length} เจ้า · {vehicle} · น้ำมัน {diesel} บาท
          </div>
          {quotes.length === 0 ? (
            <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#94A3B8")}>
              ไม่มีผลลัพธ์ — ลองพิมพ์ชื่อลูกค้าหรือปลายทางให้ตรงกับที่อยู่ในตารางราคามากขึ้น
            </div>
          ) : (
            <div style={css("overflow-x:auto")}>
              <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
                <thead><tr>{["ผู้ขนส่ง", "บริการ", "ลูกค้าในตารางราคา", "จาก", "ถึง", "ราคา", "ส่วนต่าง"].map((h, i) => (
                  <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:" + (i >= 5 ? "right" : "left") + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
                ))}</tr></thead>
                <tbody>
                  {quotes.map((quote, index) => (
                    <tr key={quote.lane.id} style={css("border-bottom:1px solid #F1F5F9;background:" + (index === 0 ? "#F7FBF8" : "#fff"))}>
                      <td style={css(CELL + ";font-weight:600;color:#0A2240")}>
                        {quote.carrier}
                        {index === 0 && <span style={css("font-size:10px;font-weight:700;color:#fff;background:#16794C;border-radius:3px;padding:1px 6px;margin-left:7px")}>ถูกที่สุด</span>}
                        {quote.supplierId === null && <span style={css("font-size:10.5px;color:#B45309;margin-left:7px")}>ยังไม่ผูกกับทะเบียนผู้ขนส่ง</span>}
                      </td>
                      <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{quote.lane.service}</td>
                      <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{quote.lane.customer || "—"}</td>
                      <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{quote.lane.from || "—"}</td>
                      <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{quote.lane.to || "—"}</td>
                      <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace;font-weight:600;color:#0A2240")}>{quote.price.toLocaleString()}</td>
                      <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace;color:" + (index === 0 ? "#16794C" : "#B45309"))}>
                        {index === 0 ? "—" : "+" + (quote.price - cheapest).toLocaleString()}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </div>
      )}
        </>
      )}
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:middle";
const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;width:100%");

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <label style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`)}>
      <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
      {children}
    </label>
  );
}
