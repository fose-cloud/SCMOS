"use client";

import { useMemo, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { DIESEL } from "../diesel";
import {
  averageFor, expand, monthDays, monthKey, monthsCovered, describe,
  type DieselChange,
} from "../dieselMonth";
import { bandForDiesel, type FuelBand } from "../rates";
import { sheetToday } from "../rateSheetDrafts";
import { StatCard, StatGlyph } from "../StatCard";

/**
 * เรทน้ำมัน — the month, a line per day, and the average the rest reads.
 *
 * Redrawn on 15 September 2026 to the shape the account team's own sheet
 * has: a month down the side, the day's pump price beside it, the average
 * at the top. The department wants an operator to key the day's price as
 * it is published, so the month's average is there when the invoice is —
 * and that average is what chooses the band on the ค่าขนส่ง card and prices
 * the Domestic runs.
 *
 * What is stored is still the price that took effect on a day, not a copy
 * per day: PTT OR publish a change, and a day nobody keyed carries the last
 * price recorded (see dieselMonth.expand). A day somebody keyed is drawn
 * bold; a carried day is drawn grey with the figure it inherited; a day
 * after today is drawn empty, because it has not happened.
 *
 * Each day saves on its own the moment it is keyed — PUT /api/diesel/{date}
 * — so two operators keying two days never overwrite each other's month.
 */

const CELL = "padding:6px 10px;border-bottom:1px solid #EDF1F5;font-size:12.5px;vertical-align:middle";
const HEAD = "padding:8px 10px;text-align:left;font-size:10.5px;font-weight:700;color:#465A6E;"
  + "letter-spacing:.05em;text-transform:uppercase;background:#F4F7FA;border-bottom:1px solid #D8E0E8;white-space:nowrap";
const MONO = "font-family:'IBM Plex Mono',ui-monospace,monospace";
const WEEKDAY = ["อา", "จ", "อ", "พ", "พฤ", "ศ", "ส"];

/** dd/MM/yyyy → the weekday, as the sheet abbreviates it. */
function weekdayOf(date: string): string {
  const day = new Date(Date.UTC(Number(date.slice(6)), Number(date.slice(3, 5)) - 1, Number(date.slice(0, 2))));
  return WEEKDAY[day.getUTCDay()] ?? "";
}

/** The month before or after, as MM/yyyy. */
function stepMonth(month: string, by: 1 | -1): string {
  let m = Number(month.slice(0, 2)) + by;
  let y = Number(month.slice(3));
  if (m < 1) { m = 12; y -= 1; }
  if (m > 12) { m = 1; y += 1; }
  return `${String(m).padStart(2, "0")}/${y}`;
}

export function OilRate({ canRecord, changes, bands, onChanged, onToast }: {
  /** Whether this account may key a day's price — the operators may. */
  canRecord: boolean;
  /** The published changes, as the register holds them; null while loading. */
  changes: DieselChange[] | null;
  /** The cost card's fuel clause, so the average can say which band it lands in. */
  bands: FuelBand[];
  /** The register changed under this screen; the owner re-reads it. */
  onChanged: () => void;
  onToast: (message: string) => void;
}) {
  const today = sheetToday();
  const thisMonth = today.slice(3);
  const [month, setMonth] = useState(thisMonth);
  /** The day being typed into, and what is in the box. */
  const [editing, setEditing] = useState<{ date: string; value: string } | null>(null);
  const [saving, setSaving] = useState("");

  const held = useMemo(() => changes ?? [], [changes]);
  const days = useMemo(() => monthDays(held, month, today), [held, month, today]);
  const average = useMemo(() => averageFor(expand(held, month, today), month), [held, month, today]);
  const months = useMemo(() => monthsCovered(held, today).map((one) => averageFor(expand(held, one, today), one)), [held, today]);
  const previous = months.find((one) => one.month === stepMonth(month, -1));
  const band = average.average !== null && bands.length ? bandForDiesel(bands, average.average) : -1;
  const keyedDays = days.filter((day) => day.keyed).length;
  const latest = [...held].filter((one) => monthKey(one.date.slice(3)) <= monthKey(month))
    .sort((a, b) => (b.date.slice(6) + b.date.slice(3, 5) + b.date.slice(0, 2)).localeCompare(a.date.slice(6) + a.date.slice(3, 5) + a.date.slice(0, 2)))[0];

  async function saveDay(date: string, text: string) {
    setEditing(null);
    const price = Number(text.replace(/[,\s฿]/g, ""));
    const was = held.find((one) => one.date === date);
    if (!text.trim()) {
      if (was) await removeDay(date);
      return;
    }
    if (!Number.isFinite(price) || price <= 0 || price >= 200) { onToast("ราคาน้ำมันดูไม่ถูกต้อง (บาทต่อลิตร)"); return; }
    if (was && was.price === price) return;
    setSaving(date);
    try {
      const response = await apiFetch(`/api/diesel/${date}`, {
        method: "PUT",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ price, source: DIESEL.source }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
      if (response.ok) onChanged();
    } finally { setSaving(""); }
  }

  async function removeDay(date: string) {
    setSaving(date);
    try {
      const response = await apiFetch(`/api/diesel/${date}`, { method: "DELETE", headers: { accept: "application/json" } });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `ลบไม่สำเร็จ (${response.status})`);
      if (response.ok) onChanged();
    } finally { setSaving(""); }
  }

  const fmt = (n: number | null) => (n === null ? "—" : n.toFixed(2));

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <StatCard label={`ค่าเฉลี่ยเดือน ${month}`} value={fmt(average.average)} tone={average.closed ? "#16794C" : "#B45309"} icon="calendar"
          note={average.average === null ? "ยังไม่มีราคาในเดือนนี้"
            : average.closed ? `ครบ ${average.days} วัน — ใช้ได้กับใบแจ้งหนี้`
              : `${average.days} วันจนถึงวันนี้ · กรอกเอง ${keyedDays} วัน — ยังไม่สิ้นเดือน`} />
        <StatCard label="ช่วงราคาน้ำมันในการ์ด" value={band >= 0 ? bands[band].label : "—"} tone="#1668AB" icon="money"
          note={!bands.length ? "ยังไม่มีการ์ดต้นทุนในระบบ" : band >= 0 ? "ช่วงที่ค่าขนส่งและงาน Domestic อ่านราคา" : average.average === null ? "" : "เกินช่วงที่การ์ดระบุไว้"} />
        <StatCard label="ราคาล่าสุดที่กรอก" value={latest ? latest.price.toFixed(2) : "—"} tone="#0A2240" icon="fuel"
          note={latest ? `วันที่ ${latest.date}` : "ยังไม่มี"} />
        <StatCard label={`เดือนก่อน ${stepMonth(month, -1)}`} value={fmt(previous?.average ?? null)} tone="#5A6B7D" icon="chart"
          note={previous ? describe(previous).replace(/^[\d.]+ · /, "") : "ไม่มีราคาของเดือนก่อน"} />
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:10px 14px;border-bottom:1px solid #E9EFF5;display:flex;align-items:center;gap:8px;flex-wrap:wrap")}>
          <button type="button" onClick={() => setMonth(stepMonth(month, -1))} style={css(NAV_BTN)}>‹</button>
          <span style={css(`${MONO};font-size:14px;font-weight:700;color:#0A2240;min-width:76px;text-align:center`)}>{month}</span>
          <button type="button" onClick={() => setMonth(stepMonth(month, 1))} disabled={monthKey(month) >= monthKey(thisMonth)}
            style={css(NAV_BTN + (monthKey(month) >= monthKey(thisMonth) ? ";opacity:.4;cursor:not-allowed" : ""))}>›</button>
          {month !== thisMonth && (
            <button type="button" onClick={() => setMonth(thisMonth)}
              style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;border-radius:6px;background:#fff;font-size:12px;font-weight:600;font-family:inherit;cursor:pointer;color:#0A2240")}>
              เดือนนี้
            </button>
          )}
          <span style={css("margin-left:auto;font-size:11.5px;color:#7B8CA0;display:inline-flex;align-items:center;gap:6px")}>
            <StatGlyph icon="fuel" size={14} />
            อ่านจาก <a href="https://www.pttor.com/th/oil_price" target="_blank" rel="noreferrer" style={css("color:#0A6E8A")}>pttor.com/th/oil_price</a>
            {!canRecord && <span style={css("color:#B45309")}> · บัญชีนี้ดูได้อย่างเดียว</span>}
          </span>
        </div>

        <table style={css("width:100%;border-collapse:collapse")}>
          <thead><tr>
            <th style={css(HEAD)}>วันที่</th>
            <th style={css(HEAD)}>วัน</th>
            <th style={css(HEAD + ";text-align:right")}>ราคา (บาท/ลิตร)</th>
            <th style={css(HEAD)}>ที่มา</th>
            <th style={css(HEAD)}></th>
          </tr></thead>
          <tbody>
            {changes === null && (
              <tr><td colSpan={5} style={css(CELL + ";color:#94A3B8;text-align:center;padding:22px")}>กำลังโหลด…</td></tr>
            )}
            {changes !== null && days.map((day) => {
              const isToday = day.date === today;
              const editable = canRecord && !day.ahead;
              const open = editing?.date === day.date;
              return (
                <tr key={day.date} style={css("background:" + (isToday ? "#EEF5FC" : day.keyed ? "#fff" : "#FBFCFD"))}>
                  <td style={css(CELL + `;${MONO};white-space:nowrap;` + (isToday ? "font-weight:700;color:#0A2240" : ""))}>
                    {day.date}{isToday && <span style={css("margin-left:6px;font-size:10px;font-weight:700;color:#1668AB;letter-spacing:.06em")}>วันนี้</span>}
                  </td>
                  <td style={css(CELL + ";color:#7B8CA0;white-space:nowrap")}>{weekdayOf(day.date)}</td>
                  <td style={css(CELL + ";text-align:right;white-space:nowrap")} onClick={() => editable && !open && setEditing({ date: day.date, value: day.keyed ? String(day.price ?? "") : "" })}>
                    {open ? (
                      <input
                        // eslint-disable-next-line jsx-a11y/no-autofocus
                        autoFocus
                        value={editing.value}
                        placeholder={day.price === null ? "" : day.price.toFixed(2)}
                        onChange={(e) => setEditing({ date: day.date, value: e.target.value })}
                        onBlur={() => void saveDay(day.date, editing.value)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter") void saveDay(day.date, editing.value);
                          if (e.key === "Escape") setEditing(null);
                        }}
                        style={css(`width:96px;height:28px;border:1px solid #0A5FA8;border-radius:4px;padding:0 8px;${MONO};font-size:12.5px;text-align:right`)} />
                    ) : (
                      <span title={day.ahead ? "ยังไม่ถึงวัน" : day.keyed ? "กรอกไว้วันนี้ — คลิกเพื่อแก้" : day.price === null ? "ยังไม่มีราคาก่อนหน้านี้" : "ต่อจากวันก่อนหน้า — คลิกเพื่อกรอกราคาของวันนี้"}
                        style={css(`${MONO};font-size:12.5px;` + (day.keyed ? "font-weight:700;color:#0A2240" : "color:#94A3B8")
                          + (editable ? ";cursor:text" : "") + (saving === day.date ? ";opacity:.5" : ""))}>
                        {day.ahead ? "" : day.price === null ? "—" : day.price.toFixed(2)}
                      </span>
                    )}
                  </td>
                  <td style={css(CELL + ";font-size:11px;color:#7B8CA0;white-space:nowrap")}>
                    {day.ahead ? "" : day.keyed ? "กรอกเอง" : day.price === null ? "" : "ต่อจากวันก่อน"}
                  </td>
                  <td style={css(CELL + ";text-align:right;white-space:nowrap")}>
                    {editable && day.keyed && (
                      <button type="button" onClick={() => void removeDay(day.date)} disabled={saving === day.date} title="ลบราคาที่กรอกไว้วันนี้"
                        style={css("border:none;background:transparent;color:#B45309;font-size:11.5px;cursor:pointer;font-family:inherit")}>ลบ</button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {months.length > 1 && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <table style={css("width:100%;border-collapse:collapse")}>
            <thead><tr>
              <th style={css(HEAD)}>เดือน</th>
              <th style={css(HEAD + ";text-align:right")}>ค่าเฉลี่ย</th>
              <th style={css(HEAD)}>ช่วงในการ์ด</th>
              <th style={css(HEAD)}>สถานะ</th>
            </tr></thead>
            <tbody>
              {months.map((one) => {
                const at = one.average !== null && bands.length ? bandForDiesel(bands, one.average) : -1;
                return (
                  <tr key={one.month} style={css("cursor:pointer;background:" + (one.month === month ? "#EEF5FC" : "#fff"))} onClick={() => setMonth(one.month)}>
                    <td style={css(CELL + `;${MONO}`)}>{one.month}</td>
                    <td style={css(CELL + `;text-align:right;${MONO};font-weight:600;` + (one.closed ? "color:#16794C" : "color:#B45309"))}>{fmt(one.average)}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:#0A2240")}>{at >= 0 ? bands[at].label : "—"}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>
                      {one.average === null ? "ยังไม่มีราคา" : one.closed ? `ครบ ${one.days} วัน` : `${one.days} วัน — ยังไม่สิ้นเดือน`}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

const NAV_BTN = "width:30px;height:30px;border:1px solid #C9D6E2;border-radius:6px;background:#fff;font-size:15px;font-family:inherit;cursor:pointer;color:#0A2240";
