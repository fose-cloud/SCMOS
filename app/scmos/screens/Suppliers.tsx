"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";

/**
 * The supplier register.
 *
 * One row per company, with every spelling the register and the rate cards use
 * pointing at it. That reconciliation is the point: TATIYAPOL's jobs and
 * TATIYAPON's jobs can only be counted together once somebody has said the two
 * spellings mean one firm, and this screen is where they say it.
 */

type Summary = {
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

export function Suppliers({ canManage, onToast }: { canManage: boolean; onToast: (m: string) => void }) {
  const [rows, setRows] = useState<Summary[] | null>(null);
  const [query, setQuery] = useState("");
  const [picked, setPicked] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
    setRows(response.ok ? await response.json() as Summary[] : []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Summary[] : [];
      if (!cancelled) setRows(body);
    })();
    return () => { cancelled = true; };
  }, []);

  async function post(path: string, body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/suppliers/${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  if (!rows) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const wanted = query.trim().toLowerCase();
  const shown = rows.filter((row) => !wanted
    || row.name.toLowerCase().includes(wanted)
    || row.aliases.some((alias) => alias.toLowerCase().includes(wanted)));

  const multi = rows.filter((row) => row.aliases.length > 1).length;
  const noRates = rows.filter((row) => row.jobs > 0 && row.lanes === 0).length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:11px")}>
        <Tile label="ผู้ขนส่งทั้งหมด" value={rows.length} note="จากทะเบียนงานและตารางราคา" colour="#0A2240" />
        <Tile label="รวมชื่อซ้ำแล้ว" value={multi} note="เจ้าที่มีมากกว่า 1 การสะกด" colour="#16794C" />
        <Tile label="มีงานแต่ไม่มีราคา" value={noRates} note="คิดต้นทุนไม่ได้" colour="#B45309" />
        <Tile label="เอกสารใกล้หมดอายุ" value={rows.reduce((n, r) => n + r.expiringDocuments, 0)} note="ภายใน 60 วัน" colour="#B42318" />
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;gap:10px;flex-wrap:wrap")}>
          <span style={css("font-size:12.5px;color:#465A6E")}><b style={css("color:#0A2240")}>{shown.length}</b> ราย</span>
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="ค้นหาชื่อหรือชื่อที่สะกดต่างกัน"
            style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;min-width:240px")} />
        </div>
        <div style={css("overflow-x:auto")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["รหัส", "ชื่อ", "สถานะ", "งาน", "เส้นทางราคา", "คะแนนล่าสุด", "ชื่อที่สะกดต่างกัน"].map((h, i) => (
              <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:" + (i >= 3 && i <= 5 ? "right" : "left") + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {shown.map((row) => (
                <tr key={row.id} onClick={() => setPicked(row.id === picked ? null : row.id)}
                  style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (row.id === picked ? "#F2F7FC" : "#fff"))}>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>{row.code}</td>
                  <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{row.name}</td>
                  <td style={CELL_S}>
                    <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${STATUS_TONE[row.status] ?? "#7B8CA0"}`)}>
                      {STATUS_TH[row.status] ?? row.status}
                    </span>
                  </td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.jobs.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace;color:" + (row.jobs > 0 && row.lanes === 0 ? "#B45309" : "#7B8CA0"))}>{row.lanes.toLocaleString()}</td>
                  <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace")}>{row.lastScore ?? "—"}</td>
                  <td style={css(CELL + ";font-size:11px;color:#7B8CA0")}>{row.aliases.join(" · ")}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {picked !== null && canManage && (
        <Manage supplier={rows.find((r) => r.id === picked)!} busy={busy}
          onAlias={(alias) => void post(`${picked}/alias`, { alias })}
          onStatus={(status) => void post(`${picked}/status`, { status })}
          onEvaluate={(period, safety, documents, note) =>
            void post(`${picked}/evaluate`, { period, safety, documents, note })} />
      )}
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:top";
const CELL_S = css(CELL);

function Manage({ supplier, busy, onAlias, onStatus, onEvaluate }: {
  supplier: Summary; busy: boolean;
  onAlias: (alias: string) => void;
  onStatus: (status: string) => void;
  onEvaluate: (period: string, safety: number | null, documents: number | null, note: string) => void;
}) {
  const [alias, setAlias] = useState("");
  const [period, setPeriod] = useState(String(new Date().getFullYear()));
  const [safety, setSafety] = useState("");
  const [documents, setDocuments] = useState("");
  const [note, setNote] = useState("");

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:14px 16px;display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:18px")}>
      <div>
        <Label>ผูกชื่อที่สะกดต่างกันเข้ากับ {supplier.name}</Label>
        <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
          เช่น TTP กับ TATIYAPON — ระบบไม่เดาให้ เพราะจ่ายผิดเจ้าแย่กว่าไม่มีข้อมูล
        </div>
        <div style={css("display:flex;gap:6px")}>
          <input value={alias} onChange={(e) => setAlias(e.target.value)} placeholder="ชื่อที่จะผูก"
            style={css("flex:1;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <Button label="ผูก" tone="#0A2240" busy={busy || !alias.trim()} onClick={() => onAlias(alias)} />
        </div>
      </div>

      <div>
        <Label>สถานะการอนุมัติ</Label>
        <div style={css("display:flex;gap:5px;flex-wrap:wrap;margin-top:7px")}>
          {["draft", "pending-audit", "approved", "suspended"].map((status) => (
            <button key={status} onClick={() => onStatus(status)} disabled={busy}
              style={css("height:27px;padding:0 10px;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;border:1px solid " +
                (supplier.status === status ? STATUS_TONE[status] : "#C9D6E2") +
                ";background:" + (supplier.status === status ? STATUS_TONE[status] : "#fff") +
                ";color:" + (supplier.status === status ? "#fff" : "#5A6B7D"))}>
              {STATUS_TH[status]}
            </button>
          ))}
        </div>
      </div>

      <div>
        <Label>ประเมินประจำปี</Label>
        <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
          คะแนนตรงเวลา / ตอบยืนยัน / ความล่าช้า ดึงจาก KPI อัตโนมัติ
        </div>
        <div style={css("display:flex;gap:6px;margin-bottom:6px;flex-wrap:wrap")}>
          <input value={period} onChange={(e) => setPeriod(e.target.value)} placeholder="รอบ"
            style={css("width:80px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <input value={safety} onChange={(e) => setSafety(e.target.value.replace(/\D/g, ""))} placeholder="ความปลอดภัย"
            style={css("flex:1;min-width:110px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          <input value={documents} onChange={(e) => setDocuments(e.target.value.replace(/\D/g, ""))} placeholder="เอกสาร"
            style={css("flex:1;min-width:90px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
        </div>
        <input value={note} onChange={(e) => setNote(e.target.value)} placeholder="หมายเหตุการประเมิน"
          style={css("width:100%;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:7px")} />
        <Button label="บันทึกการประเมิน" tone="#16794C" busy={busy}
          onClick={() => onEvaluate(period, safety ? Number(safety) : null, documents ? Number(documents) : null, note)} />
      </div>
    </div>
  );
}

function Label({ children }: { children: React.ReactNode }) {
  return <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{children}</div>;
}

function Button({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={busy}
      style={css(`height:29px;padding:0 13px;border:1px solid ${tone};background:${busy ? "#C3CFDB" : tone};color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer`)}
    >{label}</button>
  );
}

function Tile({ label, value, note, colour }: { label: string; value: number; note: string; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
      <div style={css("font-size:12px;color:#7B8CA0")}>{note}</div>
    </div>
  );
}
