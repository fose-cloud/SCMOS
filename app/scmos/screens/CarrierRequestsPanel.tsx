"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { parseRequests, requestSummary, type CarrierJobRequest } from "../carrierJobRequests";
import { css } from "../theme";

/**
 * Jobs carriers keyed in, waiting for the department (29 Sep 2026), above the
 * Operation Workspace — drawn only while something waits. "เปิดเป็นฟอร์ม"
 * opens the ordinary add-job form filled from the request; saving it there is
 * what confirms the request. "ไม่รับ" refuses it with a reason the carrier sees.
 */
export function CarrierRequestsPanel({ onOpen, onToast, refreshKey }: {
  onOpen: (request: CarrierJobRequest) => void; onToast: (message: string) => void; refreshKey: number;
}) {
  const [items, setItems] = useState<CarrierJobRequest[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/carrier-job-requests", { headers: { accept: "application/json" } });
      if (!response.ok) { setItems([]); return; }
      setItems(parseRequests(await response.json()).items);
    } catch { setItems([]); }
  }, []);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load, refreshKey]);

  async function reject(item: CarrierJobRequest) {
    const reason = window.prompt(`ไม่รับงานที่ ${item.supplierName} แจ้งเพราะอะไร?`);
    if (!reason || !reason.trim() || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier-job-requests/${item.id}/reject`, {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ reason: reason.trim(), revision: item.revision }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "บันทึกไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  if (items.length === 0) return null;
  return (
    <section aria-label="งานที่ผู้ขนส่งแจ้งเข้ามา" style={css("background:#FFF8EC;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:6px;padding:10px 14px;margin-bottom:10px")}>
      <div style={css("font-size:12.5px;font-weight:700;color:#8A4B06;margin-bottom:6px")}>ผู้ขนส่งแจ้งงานเข้ามา {items.length} รายการ</div>
      {items.map(item => <div key={item.id} style={css("display:flex;gap:10px;align-items:center;flex-wrap:wrap;padding:6px 0;border-top:1px solid #F3E4CC")}>
        <div style={css("flex:1;min-width:240px")}>
          <div style={css("font-size:12.5px;color:#0F2B46;font-weight:600")}>{item.supplierName} · {requestSummary(item)}</div>
          <div style={css("font-size:11px;color:#7B8CA0")}>{item.createdBy} · {new Date(item.createdAt).toLocaleString("th-TH")}{item.note ? ` · ${item.note}` : ""}</div>
        </div>
        <button onClick={() => onOpen(item)} disabled={busy}
          style={css("height:28px;padding:0 12px;border:1px solid #16794C;background:#16794C;color:#fff;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}>เปิดเป็นฟอร์มเพิ่มงาน</button>
        <button onClick={() => void reject(item)} disabled={busy}
          style={css("height:28px;padding:0 12px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}>ไม่รับ</button>
      </div>)}
    </section>
  );
}
