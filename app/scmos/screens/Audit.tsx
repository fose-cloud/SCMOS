"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { stageThai, stamp } from "./WorkflowPanel";
import { css } from "../theme";

/**
 * Audit.
 *
 * Every workflow decision the system has recorded, newest first. The table is
 * append-only, so this is not a log written alongside the truth — it is the
 * truth, and a job's position is derived from it.
 */

type Entry = {
  id: number; jobKey: string; kind: string; from: string; to: string;
  hold: string; note: string; by: string; at: string;
};

const KIND_THAI: Record<string, string> = {
  advance: "เดินหน้า",
  hold: "พักงาน",
  release: "ปลดล็อก",
  "supplier-request": "ขอกำลังรถ",
  "supplier-response": "ผู้ขนส่งตอบ",
  "assign-carrier": "มอบหมายผู้ขนส่ง",
};

const KIND_TONE: Record<string, string> = {
  advance: "#1D5FA8", hold: "#B42318", release: "#16794C",
  "supplier-request": "#B45309", "supplier-response": "#7B8CA0", "assign-carrier": "#0A2240",
};

export function Audit() {
  const [entries, setEntries] = useState<Entry[] | null>(null);
  const [kind, setKind] = useState("All");
  const [query, setQuery] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/audit?limit=500", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Entry[] : [];
      if (!cancelled) setEntries(body);
    })();
    return () => { cancelled = true; };
  }, []);

  if (!entries) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const wanted = query.trim().toLowerCase();
  const shown = entries
    .filter((entry) => kind === "All" || entry.kind === kind)
    .filter((entry) => !wanted || [entry.jobKey, entry.by, entry.note].some((f) => f.toLowerCase().includes(wanted)));

  const kinds = [...new Set(entries.map((entry) => entry.kind))];

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:4px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ประเภท</span>
          <select value={kind} onChange={(e) => setKind(e.target.value)}
            style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;min-width:150px")}>
            <option value="All">ทั้งหมด</option>
            {kinds.map((k) => <option key={k} value={k}>{KIND_THAI[k] ?? k}</option>)}
          </select>
        </label>
        <label style={css("display:flex;flex-direction:column;gap:4px;flex:1;min-width:200px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ค้นหา งาน / ผู้ใช้ / บันทึก</span>
          <input value={query} onChange={(e) => setQuery(e.target.value)}
            style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px")} />
        </label>
        <div style={css("font-size:12.5px;color:#465A6E")}>
          <b style={css("color:#0A2240")}>{shown.length.toLocaleString()}</b> รายการ
        </div>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        {entries.length === 0 && (
          <div style={css("padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ยังไม่มีการบันทึก — ประวัติจะเริ่มสะสมเมื่อมีการใช้งาน Booking หรือ Shipment Monitor
          </div>
        )}
        {entries.length > 0 && (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead>
                <tr>{["เวลา", "ประเภท", "งาน", "จาก → ไป", "บันทึก", "โดย"].map((h) => (
                  <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
                ))}</tr>
              </thead>
              <tbody>
                {shown.map((entry) => (
                  <tr key={entry.id} style={css("border-bottom:1px solid #F1F5F9")}>
                    <td style={css("padding:8px 12px;font-family:ui-monospace,monospace;font-size:11.5px;white-space:nowrap;color:#7B8CA0")}>{stamp(entry.at)}</td>
                    <td style={css("padding:8px 12px;white-space:nowrap")}>
                      <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${KIND_TONE[entry.kind] ?? "#7B8CA0"}`)}>
                        {KIND_THAI[entry.kind] ?? entry.kind}
                      </span>
                    </td>
                    <td style={css("padding:8px 12px;font-family:ui-monospace,monospace;font-size:11.5px")}>{entry.jobKey}</td>
                    <td style={css("padding:8px 12px;color:#5A6B7D;white-space:nowrap")}>
                      {stageThai(entry.from)}{entry.from !== entry.to && ` → ${stageThai(entry.to)}`}
                      {entry.hold && <span style={css("color:#B42318")}> [{entry.hold}]</span>}
                    </td>
                    <td style={css("padding:8px 12px;color:#16232F")}>{entry.note || "—"}</td>
                    <td style={css("padding:8px 12px;color:#7B8CA0;white-space:nowrap")}>{entry.by}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * A screen the menu names but the system cannot honestly fill yet.
 *
 * It says what the backend already supports and what is missing, rather than
 * showing invented figures. A screen full of plausible demo numbers is how a
 * system starts being trusted for things it cannot do — the register spent a
 * month being read that way and the handover had to spell it out.
 */
export function NotBuilt({ detail }: { detail: { ready: string[]; missing: string[] } }) {
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-left:3px solid #B45309;border-radius:5px;padding:20px 22px;max-width:760px")}>
      <div style={css("font-size:14px;font-weight:650;color:#B45309;margin-bottom:4px")}>หน้าจอนี้ยังไม่ได้สร้าง</div>
      <div style={css("font-size:12.5px;color:#5A6B7D;margin-bottom:16px")}>
        ไม่แสดงข้อมูลตัวอย่าง เพราะตัวเลขที่ดูสมจริงแต่ไม่จริงคือสิ่งที่ทำให้ระบบถูกเชื่อผิดๆ
      </div>

      {detail.ready.length > 0 && (
        <div style={css("margin-bottom:14px")}>
          <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#16794C;font-weight:700;margin-bottom:6px")}>
            หลังบ้านพร้อมแล้ว
          </div>
          {detail.ready.map((item) => (
            <div key={item} style={css("font-size:12.5px;color:#16232F;padding:2px 0")}>· {item}</div>
          ))}
        </div>
      )}

      <div>
        <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#B45309;font-weight:700;margin-bottom:6px")}>
          ยังต้องทำ
        </div>
        {detail.missing.map((item) => (
          <div key={item} style={css("font-size:12.5px;color:#5A6B7D;padding:2px 0")}>· {item}</div>
        ))}
      </div>
    </div>
  );
}
