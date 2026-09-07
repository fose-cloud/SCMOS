"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { DIESEL } from "../diesel";
import {
  averageFor, daysInMonth, describe, expand, monthKey,
  type DieselChange,
} from "../dieselMonth";

/**
 * เรทน้ำมัน — the diesel prices, and the monthly average every rate is read at.
 *
 * Built to the shape of the sheet the account team already keep: a month down
 * the side, a price per day, and the average in a green cell at the bottom. The
 * difference is what gets typed. Their sheet has thirty-one rows a month; PTT OR
 * publish a *change*, and July 2569 had four of them. So changes are what is
 * entered here and the days are worked out — see dieselMonth.ts, which
 * reproduces their May'25 average of 36.05 from four rows.
 *
 * The average is the figure that matters. A month is priced at its own average,
 * not at whatever the pump said on the day somebody looked, so the whole point
 * of this screen is the column on the right.
 */

type Row = { date: string; price: string; source: string };

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
const CELL = "padding:5px 10px;border-bottom:1px solid #EDF1F5;font-size:12.5px";
const HEAD = "padding:7px 10px;background:#F4F7FA;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;text-align:left;white-space:nowrap";

/** dd/MM/yyyy as yyyyMMdd, so the 30th does not sort before the 3rd. */
const sortable = (date: string) =>
  /^\d{2}\/\d{2}\/\d{4}$/.test(date) ? date.slice(6) + date.slice(3, 5) + date.slice(0, 2) : "";

export function OilRate({ canEdit, onToast }: {
  /** False for an account that may read a rate but not set one. */
  canEdit: boolean;
  onToast: (message: string) => void;
}) {
  const [rows, setRows] = useState<Row[] | null>(null);
  const [saving, setSaving] = useState(false);
  const [draft, setDraft] = useState<Row>({ date: "", price: "", source: DIESEL.source });

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/diesel", { headers: { accept: "application/json" } });
      if (!response.ok) { setRows([]); return; }
      const stored = await response.json() as { date: string; price: number; source: string }[];
      setRows(stored.map((one) => ({ date: one.date, price: String(one.price), source: one.source ?? "" })));
    } catch {
      setRows([]);
    }
  }, []);

  // Every setState is after an await, so it runs in a microtask rather than
  // while this body does — the rule cannot see past the await.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const changes: DieselChange[] = useMemo(
    () => (rows ?? [])
      .filter((row) => sortable(row.date) && Number(row.price) > 0)
      .map((row) => ({ date: row.date, price: Number(row.price) })),
    [rows],
  );

  /**
   * Every month the recorded changes can speak for, newest first.
   *
   * Expanded month by month rather than in one pass, because a month's opening
   * price comes from a change made before it started — May opens at April's
   * price — and only the expander knows to look back for that.
   */
  const months = useMemo(() => {
    if (changes.length === 0) return [];
    const first = [...changes].sort((a, b) => sortable(a.date).localeCompare(sortable(b.date)))[0];
    const startKey = monthKey(first.date.slice(3));
    const seen: string[] = [];
    // Every month from the first recorded change to the newest one.
    const last = [...changes].sort((a, b) => sortable(b.date).localeCompare(sortable(a.date)))[0];
    let year = Number(first.date.slice(6));
    let month = Number(first.date.slice(3, 5));
    const endKey = monthKey(last.date.slice(3));
    for (let guard = 0; guard < 240; guard++) {
      const key = `${String(month).padStart(2, "0")}/${year}`;
      if (monthKey(key) > endKey) break;
      if (monthKey(key) >= startKey) seen.push(key);
      month += 1;
      if (month > 12) { month = 1; year += 1; }
    }
    return seen
      .map((one) => averageFor(expand(changes, one), one))
      .sort((a, b) => monthKey(b.month).localeCompare(monthKey(a.month)));
  }, [changes]);

  function addRow() {
    const date = draft.date.trim();
    if (!/^\d{2}\/\d{2}\/\d{4}$/.test(date)) { onToast("วันที่ต้องเป็น dd/mm/yyyy"); return; }
    const price = Number(draft.price.replace(/,/g, ""));
    if (!Number.isFinite(price) || price <= 0 || price >= 200) { onToast("ราคาน้ำมันดูไม่ถูกต้อง"); return; }

    setRows((held) => {
      const kept = (held ?? []).filter((row) => row.date !== date);
      return [{ date, price: String(price), source: draft.source }, ...kept]
        .sort((a, b) => sortable(b.date).localeCompare(sortable(a.date)));
    });
    setDraft({ date: "", price: "", source: draft.source });
  }

  async function save() {
    if (!rows?.length) { onToast("ยังไม่มีราคาให้บันทึก"); return; }
    setSaving(true);
    try {
      const response = await apiFetch("/api/diesel", {
        method: "PUT",
        headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify(changes.map((one) => ({
          effectiveDate: one.date,
          price: one.price,
          source: rows.find((row) => row.date === one.date)?.source ?? "",
        }))),
      });
      const answer = await response.json().catch(() => null) as
        { added?: number; changed?: number; removed?: number; message?: string } | null;
      if (!response.ok) { onToast(answer?.message ?? `บันทึกไม่สำเร็จ (${response.status})`); return; }
      onToast(`บันทึกแล้ว · เพิ่ม ${answer?.added ?? 0} · แก้ ${answer?.changed ?? 0} · ลบ ${answer?.removed ?? 0}`);
    } catch (error) {
      onToast("บันทึกไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setSaving(false);
    }
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>วันที่ราคาเปลี่ยน</span>
          <input value={draft.date} placeholder="dd/mm/yyyy" disabled={!canEdit}
            onChange={(e) => setDraft((one) => ({ ...one, date: e.target.value }))}
            style={css(CONTROL + ";width:130px;font-family:'IBM Plex Mono',monospace")} />
        </label>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ราคา (บาท/ลิตร)</span>
          <input value={draft.price} placeholder={String(DIESEL.price)} disabled={!canEdit}
            onChange={(e) => setDraft((one) => ({ ...one, price: e.target.value }))}
            onKeyDown={(e) => { if (e.key === "Enter") addRow(); }}
            style={css(CONTROL + ";width:110px;font-family:'IBM Plex Mono',monospace")} />
        </label>
        <button onClick={addRow} disabled={!canEdit}
          style={css("height:30px;padding:0 15px;border-radius:4px;font-size:12.5px;font-weight:600;font-family:inherit;"
            + (canEdit ? "border:1px solid #0A6E8A;background:#fff;color:#0A6E8A;cursor:pointer"
                       : "border:1px solid #E7ECF2;background:#FAFBFC;color:#B4C0CC;cursor:default"))}>
          + เพิ่มราคา
        </button>

        <div style={css("margin-left:auto;display:flex;gap:8px;align-items:center")}>
          <span style={css("font-size:11px;color:#7B8CA0")}>
            อ่านจาก <a href="https://www.pttor.com/th/oil_price" target="_blank" rel="noreferrer"
              style={css("color:#0A6E8A")}>pttor.com/th/oil_price</a>
          </span>
          <button onClick={save} disabled={saving || !canEdit}
            title={canEdit ? "" : "ต้องใช้บัญชีที่มีสิทธิ์แก้ราคาจึงจะบันทึกได้"}
            style={css("height:32px;padding:0 16px;border-radius:4px;font-size:12.5px;font-weight:600;font-family:inherit;"
              + (saving || !canEdit ? "border:1px solid #E7ECF2;background:#FAFBFC;color:#B4C0CC;cursor:default"
                                    : "border:1px solid #0A2240;background:#0A2240;color:#fff;cursor:pointer"))}>
            {saving ? "กำลังบันทึก…" : "บันทึกเข้าระบบ"}
          </button>
        </div>
      </div>

      {/* Why only a handful of rows produce a month of prices. Said here rather
          than left for somebody to work out from an empty-looking table. */}
      <div style={css("font-size:11.5px;color:#7B8CA0;line-height:1.7;max-width:78ch")}>
        กรอกเฉพาะ<b>วันที่ราคาเปลี่ยน</b> ไม่ต้องกรอกทุกวัน — ราคาจะถือไปจนถึงการเปลี่ยนครั้งถัดไป
        เดือนหนึ่งมักเปลี่ยนแค่ 3–5 ครั้ง ระบบจะกระจายเป็นรายวันแล้วหาค่าเฉลี่ยทั้งเดือนให้เอง
        ซึ่งเป็นตัวเลขที่ใช้เลือกช่วงราคาน้ำมันในการ์ดค่าขนส่ง
      </div>

      <div style={css("display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap")}>
        {/* The averages. This is the column the rest of the system reads. */}
        <div style={css("flex:1 1 320px;background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
          <table style={css("width:100%;border-collapse:collapse")}>
            <thead>
              <tr>
                <th style={css(HEAD)}>เดือน</th>
                <th style={css(HEAD + ";text-align:right")}>ค่าเฉลี่ย</th>
                <th style={css(HEAD)}>สถานะ</th>
              </tr>
            </thead>
            <tbody>
              {months.length === 0 && (
                <tr><td colSpan={3} style={css(CELL + ";color:#94A3B8;text-align:center;padding:22px")}>
                  ยังไม่มีราคาน้ำมัน — เพิ่มวันที่ราคาเปลี่ยนด้านบน
                </td></tr>
              )}
              {months.map((one) => (
                <tr key={one.month}>
                  <td style={css(CELL + ";font-family:'IBM Plex Mono',monospace")}>{one.month}</td>
                  <td style={css(CELL + ";text-align:right;font-family:'IBM Plex Mono',monospace;font-weight:600;"
                    + (one.closed ? "color:#16794C" : "color:#B45309"))}>
                    {one.average ?? "—"}
                  </td>
                  <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>
                    {one.closed
                      ? `ครบ ${one.days} วัน`
                      : `${one.days}/${daysInMonth(one.month)} วัน — ยังไม่สิ้นเดือน`}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* The changes themselves, which is what gets typed. */}
        <div style={css("flex:1 1 340px;background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
          <table style={css("width:100%;border-collapse:collapse")}>
            <thead>
              <tr>
                <th style={css(HEAD)}>วันที่เปลี่ยน</th>
                <th style={css(HEAD + ";text-align:right")}>บาท/ลิตร</th>
                <th style={css(HEAD)}></th>
              </tr>
            </thead>
            <tbody>
              {rows === null && (
                <tr><td colSpan={3} style={css(CELL + ";color:#94A3B8;text-align:center;padding:22px")}>กำลังโหลด…</td></tr>
              )}
              {rows?.length === 0 && (
                <tr><td colSpan={3} style={css(CELL + ";color:#94A3B8;text-align:center;padding:22px")}>ยังไม่มีข้อมูล</td></tr>
              )}
              {(rows ?? []).map((row) => (
                <tr key={row.date}>
                  <td style={css(CELL + ";font-family:'IBM Plex Mono',monospace")}>{row.date}</td>
                  <td style={css(CELL + ";text-align:right;font-family:'IBM Plex Mono',monospace")}>{row.price}</td>
                  <td style={css(CELL + ";text-align:right")}>
                    {canEdit && (
                      <button onClick={() => setRows((held) => (held ?? []).filter((one) => one.date !== row.date))}
                        title="ลบราคาวันนี้"
                        style={css("border:none;background:transparent;color:#B45309;font-size:12px;cursor:pointer;font-family:inherit")}>
                        ลบ
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {months.length > 0 && (
        <div style={css("font-size:11.5px;color:#7B8CA0;line-height:1.7")}>
          เดือนล่าสุด: {describe(months[0])}
        </div>
      )}
    </div>
  );
}
