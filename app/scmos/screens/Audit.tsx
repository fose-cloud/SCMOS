"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { stageThai, stamp } from "./WorkflowPanel";
import { css } from "../theme";

/**
 * Audit.
 *
 * Two append-only records, side by side. The trail is who changed what, from
 * what value to what value, why, and from where — the thing an auditor asks
 * for. The workflow log is every decision the process itself recorded, from
 * which a job's position is derived.
 *
 * Neither is a log written alongside the truth. Both are the truth.
 */

type Entry = {
  id: number; jobKey: string; kind: string; from: string; to: string;
  hold: string; note: string; by: string; at: string;
};

type Trail = {
  id: number; at: string; who: string; whoId: string; role: string;
  action: string; entity: string; entityId: string; entityLabel: string;
  field: string; oldValue: string; newValue: string; reason: string;
  ipAddress: string; sessionId: string; source: string;
};

const ACTION_TH: Record<string, string> = {
  update: "แก้ไข", assign: "มอบหมาย", status: "เปลี่ยนสถานะ", carrier: "เปลี่ยนผู้ขนส่ง",
  rate: "เปลี่ยนราคา", approve: "อนุมัติ", reject: "ปฏิเสธ", apply: "นำไปใช้",
  close: "ปิดเคส", upload: "อัปโหลด", register: "ลงทะเบียน", evaluate: "ประเมิน",
  "retention-review": "ทบทวนการเก็บรักษา", "bulk-replace": "แทนที่ทั้งชุด",
  create: "สร้าง", delete: "ลบ",
};

const ENTITY_TH: Record<string, string> = {
  job: "งาน", supplier: "ผู้ขนส่ง", rate: "ราคา", incident: "เคส",
  document: "เอกสาร", approval: "คำขออนุมัติ", register: "ทะเบียน",
};

/** Changes a person has to justify. Kept in step with Rules/AuditActions.cs. */
const NEEDS_REASON = ["carrier", "rate", "close", "retention-review", "bulk-replace"];

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

export function Audit({ canView }: { canView: boolean }) {
  const [entries, setEntries] = useState<Entry[] | null>(null);
  const [trail, setTrail] = useState<Trail[] | null>(null);
  const [denied, setDenied] = useState(false);
  const [view, setView] = useState<"trail" | "workflow">("trail");
  const [kind, setKind] = useState("All");
  const [query, setQuery] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const [trailResponse, flowResponse] = await Promise.all([
        apiFetch("/api/audit?take=500", { headers: { accept: "application/json" } }),
        apiFetch("/api/workflow/events?limit=500", { headers: { accept: "application/json" } }),
      ]);
      const rows = trailResponse.ok
        ? (await trailResponse.json() as { entries: Trail[] }).entries
        : [];
      const flow = flowResponse.ok ? await flowResponse.json() as Entry[] : [];
      if (cancelled) return;
      setDenied(trailResponse.status === 403);
      setTrail(rows);
      setEntries(flow);
    })();
    return () => { cancelled = true; };
  }, []);

  if (!entries || !trail) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  if (view === "trail") {
    return (
      <div style={css("display:flex;flex-direction:column;gap:14px")}>
        <Switcher view={view} onView={setView} trail={trail.length} flow={entries.length} />
        <TrailTable rows={trail} denied={denied || !canView} query={query} onQuery={setQuery} />
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <Switcher view={view} onView={setView} trail={trail.length} flow={entries.length} />
      <WorkflowLog entries={entries} kind={kind} onKind={setKind} query={query} onQuery={setQuery} />
    </div>
  );
}

function Switcher({ view, onView, trail, flow }: {
  view: string; onView: (v: "trail" | "workflow") => void; trail: number; flow: number;
}) {
  const tab = (id: "trail" | "workflow", label: string, count: number) => (
    <button key={id} onClick={() => onView(id)}
      style={css("height:31px;padding:0 14px;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;border:1px solid " +
        (view === id ? "#0A2240;background:#0A2240;color:#fff" : "#C9D6E2;background:#fff;color:#5A6B7D"))}>
      {label} {count.toLocaleString()}
    </button>
  );
  return (
    <div style={css("display:flex;gap:7px")}>
      {tab("trail", "ใครแก้อะไร", trail)}
      {tab("workflow", "ขั้นตอนงาน", flow)}
    </div>
  );
}

/**
 * The trail an auditor asks for.
 *
 * Old and new side by side, because a change without its previous value is
 * information rather than evidence. A change that should carry a reason and
 * does not is marked — an empty reason on a carrier swap is itself a finding.
 */
function TrailTable({ rows, denied, query, onQuery }: {
  rows: Trail[]; denied: boolean; query: string; onQuery: (v: string) => void;
}) {
  if (denied) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-left:3px solid #B45309;border-radius:5px;padding:20px 22px")}>
        <div style={css("font-size:13px;font-weight:650;color:#B45309;margin-bottom:4px")}>ดูประวัติการแก้ไขไม่ได้</div>
        <div style={css("font-size:12.5px;color:#5A6B7D")}>
          การอ่านว่าใครแก้อะไรเป็นสิทธิ์ของระดับหัวหน้างานขึ้นไป — ระบบที่ทุกคนอ่านประวัติของทุกคนได้เป็นคนละระบบกับที่แต่ละคนเห็นงานตัวเอง
        </div>
      </div>
    );
  }

  const wanted = query.trim().toLowerCase();
  const shown = rows.filter((row) => !wanted ||
    [row.who, row.entityId, row.entityLabel, row.field, row.oldValue, row.newValue, row.reason]
      .some((f) => (f ?? "").toLowerCase().includes(wanted)));

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:4px;flex:1;min-width:220px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ค้นหา ผู้ใช้ / งาน / ค่า / เหตุผล</span>
          <input value={query} onChange={(e) => onQuery(e.target.value)}
            style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px")} />
        </label>
        <div style={css("font-size:12.5px;color:#465A6E")}>
          <b style={css("color:#0A2240")}>{shown.length.toLocaleString()}</b> รายการ
        </div>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        {shown.length === 0 ? (
          <div style={css("padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ยังไม่มีการบันทึก — ประวัติจะเริ่มสะสมเมื่อมีการแก้ไขข้อมูล
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["เวลา", "ผู้ใช้", "การกระทำ", "รายการ", "เดิม → ใหม่", "เหตุผล", "IP / Session"].map((h) => (
                <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {shown.map((row) => {
                  const missing = NEEDS_REASON.includes(row.action) && !row.reason.trim();
                  return (
                    <tr key={row.id} style={css("border-bottom:1px solid #F1F5F9;vertical-align:top")}>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;white-space:nowrap;color:#7B8CA0")}>{stamp(row.at)}</td>
                      <td style={css(CELL + ";white-space:nowrap")}>
                        <div style={css("color:#0A2240")}>{row.who}</div>
                        <div style={css("font-size:11px;color:#94A3B8")}>{row.role}</div>
                      </td>
                      <td style={css(CELL + ";white-space:nowrap")}>
                        <span style={css("font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:" +
                          (row.action === "delete" || row.action === "bulk-replace" ? "#B42318"
                            : row.action === "approve" ? "#16794C" : "#1D5FA8"))}>
                          {ACTION_TH[row.action] ?? row.action}
                        </span>
                        {row.source !== "web" && <span style={css("font-size:10.5px;color:#B45309;margin-left:6px")}>{row.source}</span>}
                      </td>
                      <td style={CELL_S}>
                        <div style={css("color:#16232F")}>{row.entityLabel || row.entityId || "—"}</div>
                        <div style={css("font-size:11px;color:#94A3B8")}>
                          {ENTITY_TH[row.entity] ?? row.entity}{row.field && ` · ${row.field}`}
                        </div>
                      </td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>
                        <span style={css("color:#B42318")}>{row.oldValue || "—"}</span>
                        <span style={css("color:#94A3B8")}> → </span>
                        <span style={css("color:#16794C")}>{row.newValue || "—"}</span>
                      </td>
                      <td style={css(CELL + ";max-width:240px;color:" + (missing ? "#B45309" : "#5A6B7D"))}>
                        {row.reason || (missing ? "ไม่ได้ระบุเหตุผล" : "—")}
                      </td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11px;color:#94A3B8;white-space:nowrap")}>
                        <div>{row.ipAddress || "—"}</div>
                        <div>{row.sessionId ? row.sessionId.slice(0, 18) : "—"}</div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

const CELL = "padding:8px 12px";
const CELL_S = css(CELL);

function WorkflowLog({ entries, kind, onKind, query, onQuery }: {
  entries: Entry[]; kind: string; onKind: (v: string) => void; query: string; onQuery: (v: string) => void;
}) {
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
          <select value={kind} onChange={(e) => onKind(e.target.value)}
            style={css("height:32px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;min-width:150px")}>
            <option value="All">ทั้งหมด</option>
            {kinds.map((k) => <option key={k} value={k}>{KIND_THAI[k] ?? k}</option>)}
          </select>
        </label>
        <label style={css("display:flex;flex-direction:column;gap:4px;flex:1;min-width:200px")}>
          <span style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ค้นหา งาน / ผู้ใช้ / บันทึก</span>
          <input value={query} onChange={(e) => onQuery(e.target.value)}
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
