"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

/**
 * The two things that happen to a supplier outside the daily work: getting
 * approved in the first place, and being scored once a year.
 *
 * Both write to the same register the workspace reads, so an approval here is
 * what lets work be given to that company, and a score here is the one the
 * annual meeting argues with.
 */

export type Summary = {
  id: number; code: string; name: string; status: string;
  serviceType: string; serviceArea: string;
  jobs: number; lanes: number; trucks: number; drivers: number;
  lastScore: number | null; lastEvaluatedPeriod: string;
  aliases: string[]; expiringDocuments: number;
};

const STATUS_TONE: Record<string, string> = {
  approved: "#16794C", draft: "#7B8CA0", "pending-audit": "#B45309",
  suspended: "#B42318", rejected: "#B42318",
};
const STATUS_TH: Record<string, string> = {
  approved: "อนุมัติแล้ว", draft: "ร่าง", "pending-audit": "รอตรวจ",
  suspended: "ระงับ", rejected: "ไม่ผ่าน",
};

/** Shared loader — both screens read the same register. */
function useSuppliers() {
  const [rows, setRows] = useRemembered<Summary[]>("supplier-flows");

  const load = useCallback(async () => {
    const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
    const body = response.ok ? await response.json() as Summary[] : null;
    setRows((held) => body ?? held ?? []);
  }, [setRows]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Summary[] : null;
      if (!cancelled) setRows((held) => body ?? held ?? []);
    })();
    return () => { cancelled = true; };
  }, [setRows]);

  return { rows, load };
}

async function postSupplier(path: string, body: unknown) {
  const response = await apiFetch(`/api/suppliers${path}`, {
    method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
  });
  const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
  return reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ";
}

/* ------------------------------------------------------------ add a vendor */

/**
 * Onboarding.
 *
 * A new vendor starts as a draft and stays there until somebody with authority
 * moves it. Nothing about that is decorative: only an approved supplier can be
 * given work, so the status is the control, and it is a supervisor's to set.
 */
export function Vendor({ canManage, onToast }: { canManage: boolean; onToast: (m: string) => void }) {
  const { rows, load } = useSuppliers();
  const [busy, setBusy] = useState(false);
  const [form, setForm] = useState({ name: "", code: "", serviceType: "", serviceArea: "" });

  async function act(path: string, body: unknown, onDone?: () => void) {
    if (busy) return;
    setBusy(true);
    try {
      onToast(await postSupplier(path, body));
      await load();
      onDone?.();
    } finally { setBusy(false); }
  }

  if (!rows) return <Loading />;

  const onboarding = rows.filter((row) => row.status === "draft" || row.status === "pending-audit");

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="ร่าง" value={rows.filter((r) => r.status === "draft").length} colour="#7B8CA0" />
        <Tile label="รอตรวจ" value={rows.filter((r) => r.status === "pending-audit").length} colour="#B45309" />
        <Tile label="อนุมัติแล้ว" value={rows.filter((r) => r.status === "approved").length} colour="#16794C" />
        <Tile label="ระงับ / ไม่ผ่าน" value={rows.filter((r) => r.status === "suspended" || r.status === "rejected").length} colour="#B42318" />
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px")}>
        <div style={css("font-size:12.5px;font-weight:650;color:#0A2240;margin-bottom:3px")}>ลงทะเบียนผู้ขนส่งใหม่</div>
        <div style={css("font-size:11.5px;color:#94A3B8;margin-bottom:10px")}>
          ลงทะเบียนแล้วจะอยู่สถานะ “ร่าง” — จ่ายงานให้ยังไม่ได้จนกว่าจะผ่านการตรวจและอนุมัติ
        </div>
        <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
          {([
            ["name", "ชื่อบริษัท *", "220px"],
            ["code", "รหัส (เว้นว่างให้ระบบตั้ง)", "150px"],
            ["serviceType", "ประเภทบริการ", "170px"],
            ["serviceArea", "พื้นที่ให้บริการ", "170px"],
          ] as [keyof typeof form, string, string][]).map(([key, label, width]) => (
            <label key={key} style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`)}>
              <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
              <input value={form[key]} onChange={(e) => setForm({ ...form, [key]: e.target.value })}
                style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px")} />
            </label>
          ))}
          <button
            onClick={() => void act("", form, () => setForm({ name: "", code: "", serviceType: "", serviceArea: "" }))}
            disabled={busy || !canManage || !form.name.trim()}
            style={css("height:30px;padding:0 15px;border:1px solid #0A2240;background:" +
              (busy || !canManage || !form.name.trim() ? "#C3CFDB" : "#0A2240") +
              ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
          >ลงทะเบียน</button>
        </div>
        {!canManage && (
          <div style={css("font-size:11.5px;color:#B45309;margin-top:8px")}>
            ลงทะเบียนและอนุมัติผู้ขนส่งได้เฉพาะระดับหัวหน้างานขึ้นไป
          </div>
        )}
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          อยู่ระหว่างขึ้นทะเบียน · {onboarding.length} ราย
        </div>
        {onboarding.length === 0 ? (
          <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ไม่มีรายที่ค้างอยู่ — ผู้ขนส่งทั้งหมดในทะเบียนผ่านการอนุมัติแล้ว
          </div>
        ) : onboarding.map((row) => (
          <div key={row.id} style={css("padding:10px 16px;border-bottom:1px solid #F1F5F9;display:flex;gap:12px;align-items:center;flex-wrap:wrap")}>
            <span style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0;min-width:64px")}>{row.code}</span>
            <span style={css("font-weight:600;color:#0A2240;font-size:12.5px;flex:1;min-width:150px")}>{row.name}</span>
            <StatusChip status={row.status} />
            {canManage && ["pending-audit", "approved", "rejected"].map((status) => (
              <button key={status} onClick={() => void act(`/${row.id}/status`, { status })} disabled={busy || row.status === status}
                style={css("height:27px;padding:0 11px;border:1px solid " + (STATUS_TONE[status] ?? "#C9D6E2") +
                  ";background:#fff;color:" + (STATUS_TONE[status] ?? "#5A6B7D") +
                  ";border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}>
                {STATUS_TH[status]}
              </button>
            ))}
          </div>
        ))}
      </div>
    </div>
  );
}

/* ------------------------------------------------------- annual evaluation */

/**
 * The annual score.
 *
 * On-time, confirmation and delay come from the KPI engine, so the meeting
 * argues with measured figures rather than remembered ones. Safety and
 * documents are the assessor's own judgement and are typed here. A carrier with
 * too few jobs to measure gets no operational component at all rather than a
 * flattering zero-delay hundred.
 */
export function Evaluation({ canManage, onToast }: { canManage: boolean; onToast: (m: string) => void }) {
  const { rows, load } = useSuppliers();
  const [period, setPeriod] = useState(String(new Date().getFullYear()));
  const [busy, setBusy] = useState(false);
  const [open, setOpen] = useState<number | null>(null);
  const [safety, setSafety] = useState("");
  const [documents, setDocuments] = useState("");
  const [note, setNote] = useState("");

  async function submit(id: number) {
    if (busy) return;
    setBusy(true);
    try {
      onToast(await postSupplier(`/${id}/evaluate`, {
        period,
        safety: safety ? Number(safety) : null,
        documents: documents ? Number(documents) : null,
        note,
      }));
      await load();
      setOpen(null);
      setSafety(""); setDocuments(""); setNote("");
    } finally { setBusy(false); }
  }

  if (!rows) return <Loading />;

  const active = rows.filter((row) => row.jobs > 0 || row.status === "approved");
  const done = active.filter((row) => row.lastEvaluatedPeriod === period).length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="ต้องประเมิน" value={active.length} colour="#0A2240" />
        <Tile label={`ประเมินรอบ ${period} แล้ว`} value={done} colour="#16794C" />
        <Tile label="ยังไม่ได้ประเมิน" value={active.length - done} colour="#B45309" />
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;gap:10px;align-items:center;flex-wrap:wrap")}>
          <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>รอบการประเมิน</span>
          <input value={period} onChange={(e) => setPeriod(e.target.value)}
            style={css("width:96px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12.5px")} />
          <span style={css("font-size:11.5px;color:#94A3B8")}>
            คะแนนตรงเวลา / ตอบยืนยัน / ความล่าช้า ดึงจาก KPI ให้เอง — กรอกเฉพาะความปลอดภัยและเอกสาร
          </span>
        </div>
        <div style={css("overflow-x:auto")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["ชื่อ", "สถานะ", "งาน", "คะแนนล่าสุด", "รอบล่าสุด", ""].map((h, i) => (
              <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:" + (i === 2 || i === 3 ? "right" : "left") + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {active.map((row) => (
                <tr key={row.id} style={css("border-bottom:1px solid #F1F5F9;background:" + (row.lastEvaluatedPeriod === period ? "#F7FBF8" : "#fff"))}>
                  <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{row.name}</td>
                  <td style={CELL_S}><StatusChip status={row.status} /></td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.jobs.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.lastScore ?? "—"}</td>
                  <td style={css(CELL + ";font-size:11.5px;color:#7B8CA0")}>{row.lastEvaluatedPeriod || "—"}</td>
                  <td style={css(CELL + ";white-space:nowrap")}>
                    {canManage && (open === row.id ? (
                      <span style={css("display:flex;gap:5px;align-items:center;flex-wrap:wrap")}>
                        <input value={safety} onChange={(e) => setSafety(e.target.value.replace(/\D/g, ""))} placeholder="ปลอดภัย"
                          style={css("width:78px;height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:11.5px")} />
                        <input value={documents} onChange={(e) => setDocuments(e.target.value.replace(/\D/g, ""))} placeholder="เอกสาร"
                          style={css("width:78px;height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:11.5px")} />
                        <input value={note} onChange={(e) => setNote(e.target.value)} placeholder="หมายเหตุ"
                          style={css("width:150px;height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:11.5px")} />
                        <Mini label="บันทึก" tone="#16794C" busy={busy} onClick={() => void submit(row.id)} />
                        <Mini label="ยกเลิก" tone="#7B8CA0" busy={busy} onClick={() => setOpen(null)} />
                      </span>
                    ) : (
                      <Mini label="ประเมิน" tone="#0A2240" busy={busy}
                        onClick={() => { setOpen(row.id); setSafety(""); setDocuments(""); setNote(""); }} />
                    ))}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------ bits shared */

const CELL = "padding:8px 12px;vertical-align:middle";
const CELL_S = css(CELL);

function Loading() {
  return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
}

function StatusChip({ status }: { status: string }) {
  return (
    <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;white-space:nowrap;background:${STATUS_TONE[status] ?? "#7B8CA0"}`)}>
      {STATUS_TH[status] ?? status}
    </span>
  );
}

function Mini({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={busy}
      style={css(`height:27px;padding:0 11px;border:1px solid ${tone};background:#fff;color:${tone};border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer`)}
    >{label}</button>
  );
}

function Tile({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
    </div>
  );
}
