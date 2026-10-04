"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import {
  CERTIFICATE_STATE, HISTORY_SOURCE, latestCertificates, newestFirst, shown, type HistoryCertificate, type HistoryRow, type LegacyPreview,
} from "../annualEvaluation";
import { isoDay, registerDate } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { Badge, CELL, EMPTY, HEAD, INPUT, LABEL, MONO, Notice, OUTLINE, PANEL, PRIMARY, SAVE, TITLE } from "./ActionPlanParts";

const CERTIFICATE_TYPES = [["q-mark", "Q-Mark"], ["iso-9001", "ISO 9001"], ["iso-14001", "ISO 14001"], ["iso-39001", "ISO 39001"], ["iso-45001", "ISO 45001"]] as const;

type Editing = {
  id: number | null; supplierId: number; carrier: string; type: string; year: string; held: boolean; number: string; issuedOn: string; expiresOn: string;
  verified: boolean; note: string;
};

/**
 * The carriers' evaluations year by year and their ISO / Q-Mark certificates (2 Oct 2026, Annual Evaluation Phase 11).
 * Past years come in from the department's own workbooks: the file is read and shown — which carrier each row is, what
 * it says, what the register already holds — and imported only when somebody confirms, with the carriers they chose by
 * hand. The file is sent again on import, so what is stored is the file's, never a figure typed here.
 */
export function EvaluationHistory({ canManage, onToast }: { canManage: boolean; onToast: (message: string) => void }) {
  const [rows, setRows] = useState<HistoryRow[] | null>(null);
  const [failure, setFailure] = useState("");
  const [register, setRegister] = useState<{ id: number; name: string; code: string }[]>([]);
  const [file, setFile] = useState<File | null>(null);
  const [year, setYear] = useState(String(new Date().getFullYear() - 1));
  const [chosen, setChosen] = useState<Record<number, number>>({});
  const [preview, setPreview] = useState<LegacyPreview | null>(null);
  const [busy, setBusy] = useState(false);
  const [editing, setEditing] = useState<Editing | null>(null);
  const [text, setText] = useState("");

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/annual-evaluations/history", { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as HistoryRow[] | { error?: string } | null;
      if (!response.ok || !Array.isArray(body)) throw new Error((body as { error?: string } | null)?.error ?? `เปิดประวัติไม่ได้ (${response.status})`);
      setRows(body); setFailure("");
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, []);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  useEffect(() => {
    if (!canManage) return;
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers", { headers: { accept: "application/json" } });
      const list = response.ok ? await response.json() as { id: number; code: string; name: string; legalName?: string }[] : [];
      if (!cancelled) setRegister(list.map((one) => ({ id: one.id, code: one.code, name: one.legalName || one.name })).sort((a, b) => a.name.localeCompare(b.name)));
    })();
    return () => { cancelled = true; };
  }, [canManage]);

  /** Reads the file (preview) or imports it, with the carriers chosen so far. */
  async function send(mode: "preview" | "import", picks: Record<number, number>) {
    if (!file || busy) return;
    setBusy(true);
    try {
      const form = new FormData();
      form.append("file", file);
      form.append("year", year);
      form.append("chosen", JSON.stringify(picks));
      const response = await apiFetch(`/api/annual-evaluations/history/${mode}`, { method: "POST", body: form });
      const reply = await response.json().catch(() => ({})) as { ok?: boolean; message?: string; error?: string; preview?: LegacyPreview };
      if (mode === "import" || !response.ok) onToast(reply.message || reply.error || (response.ok ? "นำเข้าแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (mode === "import" && response.ok) { setPreview(null); setFile(null); setChosen({}); await load(); return; }
      if (reply.preview) setPreview(reply.preview);
    } finally { setBusy(false); }
  }

  async function saveCertificate() {
    if (!editing || busy) return;
    setBusy(true);
    try {
      const body = {
        supplierId: editing.supplierId, type: editing.type, year: Number(editing.year), held: editing.held, number: editing.number,
        issuedOn: registerDate(editing.issuedOn), expiresOn: registerDate(editing.expiresOn), verified: editing.verified, note: editing.note,
      };
      const response = await apiFetch(editing.id ? `/api/annual-evaluations/history/certificates/${editing.id}` : "/api/annual-evaluations/history/certificates", {
        method: editing.id ? "PUT" : "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `ไม่สำเร็จ (${response.status})`);
      if (response.ok) { setEditing(null); await load(); }
    } finally { setBusy(false); }
  }

  function edit(row: HistoryRow, certificate: HistoryCertificate | null, type: string) {
    setEditing({
      id: certificate?.id ?? null, supplierId: row.supplierId, carrier: row.name, type: certificate?.type ?? type,
      year: String(certificate?.year ?? new Date().getFullYear()), held: certificate?.held ?? true, number: certificate?.number ?? "",
      issuedOn: isoDay(certificate?.issuedOn ?? ""), expiresOn: isoDay(certificate?.expiresOn ?? ""),
      verified: certificate?.verification === "verified", note: certificate?.note ?? "",
    });
  }

  if (failure) return <Notice tone="#B45309">{failure}</Notice>;
  if (!rows) return <Notice tone="#7B8CA0">กำลังโหลด…</Notice>;

  const periods = newestFirst([...new Set(rows.flatMap((row) => row.evaluations.map((one) => one.period)))]);
  const shownRows = rows.filter((row) => !text || `${row.name} ${row.code}`.toLowerCase().includes(text.toLowerCase()));
  const importable = preview?.rows.filter((row) => !row.skip && row.supplierId !== null).length ?? 0;

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {canManage && (
        <div style={css(PANEL + "display:flex;flex-direction:column;gap:10px")}>
          <div style={css("display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap")}>
            <span style={css(TITLE + "align-self:center")}>นำเข้าผลย้อนหลัง</span>
            <label style={css("display:flex;flex-direction:column;gap:3px")}>
              <span style={css(LABEL)}>ปีของผลประเมิน</span>
              <input aria-label="ปีของผลประเมิน" value={year} onChange={(e) => { setYear(e.target.value.replace(/\D/g, "").slice(0, 4)); setPreview(null); }}
                style={css(INPUT + "width:80px")} />
            </label>
            <label style={css("display:flex;flex-direction:column;gap:3px")}>
              <span style={css(LABEL)}>ไฟล์ (.xlsx) — ผลประเมิน หรือ ISO / Q-Mark</span>
              <input aria-label="ไฟล์ผลประเมินย้อนหลัง" type="file" accept=".xlsx"
                onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPreview(null); setChosen({}); }} style={css("font-size:12px")} />
            </label>
            <button type="button" disabled={!file || busy || year.length !== 4} onClick={() => void send("preview", chosen)} style={css(OUTLINE)}>ตรวจไฟล์</button>
            {preview && (
              <button type="button" disabled={busy || importable === 0} style={css(SAVE + (busy || importable === 0 ? "opacity:.45;cursor:default" : ""))}
                onClick={() => {
                  if (window.confirm(`นำเข้า${preview.kind === "results" ? "ผลประเมิน" : "ใบรับรอง"}ปี ${preview.year} — ${importable} ราย?`)) void send("import", chosen);
                }}>นำเข้า {importable} ราย</button>
            )}
          </div>

          {preview && (
            <ZoomBox zoomable={false} capped={false}>
              <table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
                <thead><tr>{["แถว", "ในไฟล์", "ABS", "ผู้ขนส่งในทะเบียน", preview.kind === "results" ? "ผลประเมิน" : "ใบรับรอง", ""].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
                <tbody>
                  {preview.rows.map((row) => (
                    <tr key={row.row} style={css(row.skip ? "background:#FAFBFC" : "")}>
                      <td style={css(CELL + MONO)}>{row.row}</td>
                      <td style={css(CELL)}>{row.name}</td>
                      <td style={css(CELL + MONO)}>{row.number || "—"}</td>
                      <td style={css(CELL)}>
                        <select aria-label={`ผู้ขนส่งของแถว ${row.row}`} value={chosen[row.row] ?? row.supplierId ?? ""} style={css(INPUT + "min-width:200px")}
                          onChange={(e) => {
                            const picks = { ...chosen, [row.row]: Number(e.target.value || 0) };
                            setChosen(picks);
                            void send("preview", picks);
                          }}>
                          <option value="">— เลือก —</option>
                          <option value="0">ข้ามแถวนี้</option>
                          {register.map((one) => <option key={one.id} value={one.id}>{one.name} ({one.code})</option>)}
                        </select>
                        {row.matchedBy && row.matchedBy !== "skipped" && (
                          <div style={css(LABEL)}>{row.matchedBy === "abs" ? "ตรงเลข ABS" : row.matchedBy === "name" ? "ตรงชื่อ" : "เลือกเอง"}</div>
                        )}
                      </td>
                      <td style={css(CELL)}>
                        {preview.kind === "results"
                          ? <span style={css(MONO)}>{shown(row.finalPercent)}% · {row.result || "—"} · {row.evaluators} คน</span>
                          : <div style={css("display:flex;gap:3px;flex-wrap:wrap")}>
                            {preview.certificates.filter((one) => row.held && one.code in row.held).map((one) => (
                              <Badge key={one.code} label={`${one.label} ${row.held![one.code] === true ? "✓" : row.held![one.code] === false ? "✗" : "?"}`}
                                tone={row.held![one.code] ? "#6D28D9" : "#94A3B8"} background={row.held![one.code] ? "#F3EEFE" : "#F8FAFC"} />
                            ))}
                          </div>}
                      </td>
                      <td style={css(CELL + "font-size:11.5px")}>
                        {row.problems.map((problem) => <div key={problem} style={css("color:#B42318")}>{problem}</div>)}
                        {row.existing && <div style={css("color:#8A6D0B")}>{row.existing}</div>}
                        {!row.skip && row.problems.length === 0 && !row.existing && <span style={css("color:#16794C")}>พร้อมนำเข้า</span>}
                      </td>
                    </tr>
                  ))}
                  {preview.rows.length === 0 && <tr><td colSpan={6} style={css(EMPTY)}>ไม่มีแถวในไฟล์</td></tr>}
                </tbody>
              </table>
            </ZoomBox>
          )}
        </div>
      )}

      {editing && (
        <div style={css(PANEL + "display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;border-left:4px solid #6D28D9")}>
          <span style={css(TITLE + "align-self:center")}>{editing.carrier}</span>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>ใบรับรอง</span>
            <select aria-label="ประเภทใบรับรอง" value={editing.type} disabled={editing.id !== null} onChange={(e) => setEditing({ ...editing, type: e.target.value })} style={css(INPUT)}>
              {CERTIFICATE_TYPES.map(([code, label]) => <option key={code} value={code}>{label}</option>)}
            </select>
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>ปี</span>
            <input aria-label="ปีของใบรับรอง" value={editing.year} disabled={editing.id !== null}
              onChange={(e) => setEditing({ ...editing, year: e.target.value.replace(/\D/g, "").slice(0, 4) })} style={css(INPUT + "width:70px")} />
          </label>
          <label style={css(LABEL + "display:flex;gap:5px;align-items:center;height:30px")}>
            <input type="checkbox" aria-label="ถือใบรับรองนี้" checked={editing.held} onChange={(e) => setEditing({ ...editing, held: e.target.checked, verified: e.target.checked && editing.verified })} />มี
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>เลขที่</span>
            <input aria-label="เลขที่ใบรับรอง" value={editing.number} maxLength={80} onChange={(e) => setEditing({ ...editing, number: e.target.value })} style={css(INPUT + "width:150px")} />
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>วันที่ออก</span>
            <input aria-label="วันที่ออกใบรับรอง" type="date" value={editing.issuedOn} onChange={(e) => setEditing({ ...editing, issuedOn: e.target.value })} style={css(INPUT)} />
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>วันหมดอายุ</span>
            <input aria-label="วันหมดอายุใบรับรอง" type="date" value={editing.expiresOn} onChange={(e) => setEditing({ ...editing, expiresOn: e.target.value })} style={css(INPUT)} />
          </label>
          <label style={css(LABEL + "display:flex;gap:5px;align-items:center;height:30px")}>
            <input type="checkbox" aria-label="ยืนยันจากใบรับรองจริง" checked={editing.verified} disabled={!editing.held}
              onChange={(e) => setEditing({ ...editing, verified: e.target.checked })} />ยืนยันจากใบรับรองจริง
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px;flex:1;min-width:160px")}>
            <span style={css(LABEL)}>หมายเหตุ</span>
            <input aria-label="หมายเหตุใบรับรอง" value={editing.note} maxLength={500} onChange={(e) => setEditing({ ...editing, note: e.target.value })} style={css(INPUT)} />
          </label>
          <button type="button" disabled={busy} onClick={() => void saveCertificate()} style={css(PRIMARY)}>บันทึก</button>
          <button type="button" onClick={() => setEditing(null)} style={css(OUTLINE)}>ยกเลิก</button>
        </div>
      )}

      <div style={css(PANEL + "display:flex;gap:10px;align-items:center")}>
        <input aria-label="ค้นหาผู้ขนส่งในประวัติ" placeholder="ผู้ขนส่ง" value={text} onChange={(e) => setText(e.target.value)} style={css(INPUT + "width:220px")} />
        <span style={css(LABEL)}>{shownRows.length} / {rows.length}</span>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["ผู้ขนส่ง", ...periods, "ใบรับรอง ISO / Q-Mark"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {shownRows.map((row) => {
                const latest = latestCertificates(row.certificates);
                return (
                  <tr key={row.supplierId}>
                    <td style={css(CELL)}><div style={css("font-weight:600")}>{row.name}</div><div style={css(LABEL + MONO)}>{row.code}</div></td>
                    {periods.map((period) => {
                      const one = row.evaluations.find((evaluation) => evaluation.period === period);
                      return (
                        <td key={period} style={css(CELL)} title={one?.note || undefined}>
                          {one ? (
                            <>
                              <div style={css(MONO + "font-weight:700")}>{one.finalPercent != null ? `${shown(one.finalPercent)}%` : one.totalScore ?? "—"}</div>
                              <div style={css(LABEL)}>{one.result || one.grade || "—"} · {HISTORY_SOURCE[one.source] ?? one.source}</div>
                            </>
                          ) : <span style={css("color:#CBD5E1")}>—</span>}
                        </td>
                      );
                    })}
                    <td style={css(CELL)}>
                      <div style={css("display:flex;gap:4px;flex-wrap:wrap;align-items:center")}>
                        {latest.map((one) => {
                          const state = CERTIFICATE_STATE[one.state] ?? { label: one.state, tone: "#475569", background: "#F1F5F9" };
                          return (
                            <button key={one.id} type="button" disabled={!canManage} onClick={() => edit(row, one, one.type)}
                              title={`${one.label} · ${state.label} · ปี ${one.year}${one.number ? ` · ${one.number}` : ""}${one.expiresOn ? ` · ถึง ${one.expiresOn}` : ""}`}
                              style={css(`border:0;background:none;padding:0;cursor:${canManage ? "pointer" : "default"}`)}>
                              <Badge label={`${one.label}${one.held ? "" : " ✗"}`} tone={state.tone} background={state.background} />
                            </button>
                          );
                        })}
                        {canManage && <button type="button" onClick={() => edit(row, null, "iso-9001")} style={css("border:1px dashed #C9D6E2;background:#fff;border-radius:4px;font-size:11px;padding:1px 6px;cursor:pointer;color:#5A6B7D")}>+ ใบรับรอง</button>}
                      </div>
                    </td>
                  </tr>
                );
              })}
              {shownRows.length === 0 && <tr><td colSpan={periods.length + 2} style={css(EMPTY)}>ยังไม่มีประวัติการประเมิน</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}
