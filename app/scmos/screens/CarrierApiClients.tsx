"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

/**
 * The Carrier TMS API's keys: one row per credential, bound to one supplier.
 *
 * Whoever manages suppliers issues a key here for a carrier's TMS, the way
 * they bind a LINE room on the LINE screen. The key is shown once, in the
 * panel that opens when it is issued, with the header the TMS must send it
 * in; after that the register holds only its hash and the screen shows the
 * prefix. A key is retired, never deleted — the audit trail names it.
 *
 * Reads and writes /api/carrier-api/clients (the department's side). The
 * carrier's own door is /api/carrier/v1/, which this screen never calls.
 */

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
const CELL = "padding:6px 10px;border-bottom:1px solid #EDF1F5;font-size:12.5px;vertical-align:top";
const HEAD = "padding:7px 10px;background:#F4F7FA;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;text-align:left;white-space:nowrap";
const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:5px";
const BUTTON = "height:28px;padding:0 12px;border-radius:4px;font-size:12px;font-family:inherit;cursor:pointer;border:1px solid";
const MONO = "font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace";

type Client = {
  id: number; clientId: string; name: string; supplierId: number; supplier: string;
  keyPrefix: string; status: "active" | "revoked";
  createdAt: string; createdBy: string; revokedAt: string | null; revokedBy: string; lastSeenAt: string | null;
};
type Supplier = { id: number; name: string; code?: string };
type Issued = { clientId: string; name: string; supplier: string; key: string; message: string };
/** The department's list, and where a TMS calls — the API's own host, never the web's, whose proxy drops the key. */
type Listing = { baseUrl: string; requestsPerMinute: number; clients: Client[] };

export function CarrierApiClients({ canManage, onToast }: { canManage: boolean; onToast: (message: string) => void }) {
  const [rows, setRows] = useState<Client[] | null>(null);
  const [baseUrl, setBaseUrl] = useState("");
  const [status, setStatus] = useState<number | null>(null);
  const [suppliers, setSuppliers] = useState<Supplier[]>([]);
  const [form, setForm] = useState({ supplierId: "", name: "" });
  const [busy, setBusy] = useState(false);
  /** The key just issued — the only moment it is readable. Cleared when the panel is closed. */
  const [issued, setIssued] = useState<Issued | null>(null);
  /** The row whose "ยกเลิก" was pressed once; the second press revokes. */
  const [arming, setArming] = useState<number | null>(null);

  const load = useCallback(async () => {
    const response = await apiFetch("/api/carrier-api/clients", { headers: { accept: "application/json" } });
    setStatus(response.status);
    if (!response.ok) { setRows([]); return; }
    const body = await response.json().catch(() => null) as Listing | null;
    setRows(Array.isArray(body?.clients) ? body.clients : []);
    setBaseUrl(body?.baseUrl ?? "");
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/carrier-api/clients", { headers: { accept: "application/json" } });
      if (cancelled) return;
      setStatus(response.status);
      if (!response.ok) { setRows([]); return; }
      const body = await response.json().catch(() => null) as Listing | null;
      if (cancelled) return;
      setRows(Array.isArray(body?.clients) ? body.clients : []);
      setBaseUrl(body?.baseUrl ?? "");
    })();
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    if (!canManage) return;
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers?status=approved", { headers: { accept: "application/json" } });
      if (!response.ok) return;
      const body = await response.json().catch(() => null) as Supplier[] | null;
      if (!cancelled) setSuppliers(Array.isArray(body) ? body : []);
    })();
    return () => { cancelled = true; };
  }, [canManage]);

  const issue = async () => {
    setBusy(true);
    try {
      const response = await apiFetch("/api/carrier-api/clients", {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ supplierId: Number(form.supplierId), name: form.name.trim() }),
      });
      const answer = await response.json().catch(() => null) as (Issued & { error?: string }) | null;
      // A failure says its status: the first LINE binding looked like a dead
      // button while the database was waking (16 Sep 2026).
      onToast(answer?.message ?? answer?.error ?? `ออกคีย์ไม่สำเร็จ (${response.status})`);
      if (response.ok && answer?.key) {
        setIssued(answer);
        setForm({ supplierId: "", name: "" });
        await load();
      }
    } catch (error) {
      onToast("ออกคีย์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setBusy(false);
    }
  };

  const revoke = async (row: Client) => {
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier-api/clients/${row.id}/revoke`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ reason: "" }),
      });
      const answer = await response.json().catch(() => null) as { message?: string; error?: string } | null;
      onToast(answer?.message ?? answer?.error ?? `ยกเลิกไม่สำเร็จ (${response.status})`);
      if (response.ok) await load();
    } catch (error) {
      onToast("ยกเลิกไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally {
      setArming(null);
      setBusy(false);
    }
  };

  const copy = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      onToast("คัดลอกแล้ว");
    } catch {
      onToast("คัดลอกไม่สำเร็จ — เลือกข้อความแล้วคัดลอกเอง");
    }
  };

  const when = (iso: string | null) => iso
    ? new Date(iso).toLocaleString("th-TH", { timeZone: "Asia/Bangkok", day: "2-digit", month: "2-digit", year: "2-digit", hour: "2-digit", minute: "2-digit" })
    : "—";

  if (rows === null) return null;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {issued && (
        <div style={css(`${CARD};border-color:#16794C;overflow:hidden`)}>
          <div style={css("display:flex;align-items:center;gap:10px;padding:10px 12px;border-bottom:1px solid #E6EBF0;flex-wrap:wrap")}>
            <span style={css(LABEL)}>คีย์ใหม่ · {issued.supplier} · {issued.name}</span>
            <span style={css("font-size:12px;color:#B45309")}>แสดงครั้งนี้ครั้งเดียว — ปิดแล้วดูอีกไม่ได้</span>
            <span style={css("flex:1")} />
            <button onClick={() => setIssued(null)}
              style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>ปิด</button>
          </div>
          <div style={css("padding:12px;display:grid;grid-template-columns:max-content 1fr max-content;gap:8px 12px;align-items:center;font-size:12.5px")}>
            <span style={css("color:#5A6B7D")}>Client ID</span>
            <code style={css(`${MONO};font-size:12.5px`)}>{issued.clientId}</code>
            <span />
            <span style={css("color:#5A6B7D")}>Key</span>
            <code style={css(`${MONO};font-size:12.5px;word-break:break-all;user-select:all`)}>{issued.key}</code>
            <button onClick={() => void copy(issued.key)}
              style={css(`${BUTTON};border-color:#1E5B8F;background:#1E5B8F;color:#fff`)}>คัดลอกคีย์</button>
            <span style={css("color:#5A6B7D")}>Header</span>
            <code style={css(`${MONO};font-size:12px;word-break:break-all`)}>Authorization: Bearer {issued.key}</code>
            <button onClick={() => void copy(`Authorization: Bearer ${issued.key}`)}
              style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>คัดลอก header</button>
            <span style={css("color:#5A6B7D")}>Base URL</span>
            <code style={css(`${MONO};font-size:12px`)}>{baseUrl}</code>
            <button onClick={() => void copy(baseUrl)}
              style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>คัดลอก URL</button>
          </div>
        </div>
      )}

      <div style={css(`${CARD};overflow:hidden`)}>
        <div style={css("display:flex;align-items:center;gap:10px;padding:10px 12px;border-bottom:1px solid #E6EBF0;flex-wrap:wrap")}>
          <span style={css(LABEL)}>คีย์ Carrier API · {rows.length}</span>
          <span style={css("font-size:12px;color:#475569")}>
            <code style={css(`${MONO};font-size:11.5px`)}>{baseUrl || "/api/carrier/v1/"}</code>
            {" · GET me · assignments · assignments/{jobKey} · 120 ครั้ง/นาที ต่อคีย์"}
            {status !== null && status !== 200 && <span style={css("color:#B45309")}> · โหลดรายการไม่สำเร็จ ({status})</span>}
          </span>
        </div>
        <ZoomBox capped={false} zoomable={false}>
          <table style={css("width:100%;border-collapse:collapse;min-width:820px")}>
            <thead>
              <tr>
                <th style={css(HEAD)}>ชื่อคีย์</th>
                <th style={css(HEAD)}>ผู้ขนส่ง</th>
                <th style={css(HEAD)}>Client ID</th>
                <th style={css(HEAD)}>คีย์</th>
                <th style={css(HEAD)}>สถานะ</th>
                <th style={css(HEAD)}>ออกเมื่อ</th>
                <th style={css(HEAD)}>ใช้ล่าสุด</th>
                <th style={css(HEAD)}></th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.id}>
                  <td style={css(CELL)}>{row.name}</td>
                  <td style={css(CELL)}>{row.supplier || <em style={css("color:#B91C1C;font-style:normal")}>ไม่พบผู้ขนส่ง</em>}</td>
                  <td style={css(`${CELL};${MONO};font-size:11.5px;color:#64748B`)}>{row.clientId}</td>
                  <td style={css(`${CELL};${MONO};font-size:11.5px;color:#64748B`)}>{row.keyPrefix}</td>
                  <td style={css(`${CELL};white-space:nowrap`)}>
                    {row.status === "active"
                      ? <span style={css("color:#15803D")}>ใช้งาน</span>
                      : <span style={css("color:#B91C1C")}>ยกเลิกแล้ว {when(row.revokedAt)}{row.revokedBy ? ` · ${row.revokedBy}` : ""}</span>}
                  </td>
                  <td style={css(`${CELL};white-space:nowrap;color:#475569`)}>{when(row.createdAt)}{row.createdBy ? ` · ${row.createdBy}` : ""}</td>
                  <td style={css(`${CELL};white-space:nowrap;color:#475569`)}>{when(row.lastSeenAt)}</td>
                  <td style={css(`${CELL};white-space:nowrap;text-align:right`)}>
                    {canManage && row.status === "active" && (
                      arming === row.id
                        ? (
                          <>
                            <button disabled={busy} onClick={() => void revoke(row)}
                              style={css(`${BUTTON};border-color:#B42318;background:#B42318;color:#fff`)}>ยืนยันยกเลิก</button>
                            {" "}
                            <button disabled={busy} onClick={() => setArming(null)}
                              style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#465A6E`)}>ไม่</button>
                          </>
                        )
                        : (
                          <button disabled={busy} onClick={() => setArming(row.id)}
                            style={css(`${BUTTON};border-color:#D3DBE3;background:#fff;color:#B42318`)}>ยกเลิกคีย์</button>
                        )
                    )}
                  </td>
                </tr>
              ))}
              {rows.length === 0 && (
                <tr>
                  <td colSpan={8} style={css("padding:20px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                    ยังไม่ได้ออกคีย์ให้ผู้ขนส่งรายใด
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </ZoomBox>

        {canManage && (
          <div style={css("display:flex;gap:8px;padding:11px 12px;border-top:1px solid #E6EBF0;flex-wrap:wrap;align-items:center")}>
            <select value={form.supplierId} onChange={(e) => setForm({ ...form, supplierId: e.target.value })}
              style={css(`${CONTROL};min-width:220px`)}>
              <option value="">เลือกผู้ขนส่ง…</option>
              {suppliers.map((one) => <option key={one.id} value={one.id}>{one.name}</option>)}
            </select>
            <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="ชื่อคีย์ เช่น TMS ของ SHORE" maxLength={120} style={css(`${CONTROL};min-width:220px`)} />
            <button
              disabled={busy || form.supplierId === "" || form.name.trim() === ""}
              onClick={() => void issue()}
              style={css(`${BUTTON};border-color:#1E5B8F;background:#1E5B8F;color:#fff`)}>
              ออกคีย์
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
