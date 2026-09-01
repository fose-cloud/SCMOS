"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";

/**
 * The list of vehicle and container types the TYPE column may hold.
 *
 * Lived on the Capacity screen until 2026-09-01, because that is where the
 * fleet was being talked about. Capacity was taken out of the menu — the team
 * had not started using it — and this came away with it rather than going too:
 * it is the only place a type can be added or retired, and the TYPE dropdown on
 * My Job offers exactly what is here. Removing it would have left every
 * spelling frozen as it stands, with the ones retired in August gone for good
 * and no way to add the next one.
 *
 * It sits under Administration now, which is where a master list belongs and
 * which is already the right audience: the panel was Admin-only where it was.
 */

type VType = { id: number; code: string; label: string; sort: number; active: boolean; inUse: number };

export function VehicleTypes({ canAdmin, onToast }: {
  canAdmin: boolean;
  onToast: (message: string) => void;
}) {
  const [types, setTypes] = useState<VType[]>([]);
  const [busy, setBusy] = useState(false);
  const [newType, setNewType] = useState({ code: "", label: "" });

  /** Bumped after an add or a retire, so the list is re-read rather than guessed. */
  const [revision, setRevision] = useState(0);

  // Inline and guarded by `alive`, the shape the other screens use: a callback
  // invoked from the effect would put its setState on the render that
  // scheduled it.
  useEffect(() => {
    let alive = true;
    (async () => {
      const response = await apiFetch("/api/vehicle-types", { headers: { accept: "application/json" } });
      if (!response.ok || !alive) return;
      const list = await response.json() as VType[];
      if (alive) setTypes(list);
    })();
    return () => { alive = false; };
  }, [revision]);

  const live = types.filter((type) => type.active);
  const retired = types.filter((type) => !type.active);

  const send = async (run: () => Promise<Response>) => {
    setBusy(true);
    try {
      const response = await run();
      const body = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(body.message ?? body.error ?? (response.ok ? "บันทึกแล้ว" : "ทำรายการไม่สำเร็จ"));
      if (response.ok) setRevision((turn) => turn + 1);
    } finally {
      setBusy(false);
    }
  };

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>ประเภทรถและตู้</span>
        <span style={css("font-size:11.5px;color:#7B8CA0")}>
          {live.length} รายการที่ใช้งาน · คอลัมน์ TYPE ในเมนู My Job เลือกได้จากรายการนี้เท่านั้น
        </span>
      </div>

      <div style={css("display:flex;flex-wrap:wrap;gap:7px;padding:12px 16px")}>
        {live.map((type) => (
          <span key={type.id}
            style={css("display:inline-flex;align-items:center;gap:7px;border:1px solid #C9D6E2;"
              + "border-radius:4px;padding:4px 6px 4px 10px;background:#F7FAFD")}>
            <span style={css("font-family:ui-monospace,monospace;font-size:12px;font-weight:600;color:#0A2240")}>
              {type.code}
            </span>
            <span style={css("font-size:11px;color:#7B8CA0")}>
              {type.inUse > 0 ? type.inUse + " งาน" : "ยังไม่มีงาน"}
            </span>
            {canAdmin && (
              <button type="button" disabled={busy}
                title={type.inUse > 0
                  ? "นำออกจากตัวเลือก · " + type.inUse + " งานเดิมยังแสดงค่านี้ตามปกติ"
                  : "นำออกจากตัวเลือก"}
                onClick={() => send(() => apiFetch("/api/vehicle-types/" + type.id, { method: "DELETE" }))}
                style={css("border:none;background:none;cursor:pointer;color:#94A3B8;font-size:14px;line-height:1;padding:0 2px")}>
                ×
              </button>
            )}
          </span>
        ))}
        {live.length === 0 && (
          <span style={css("font-size:12px;color:#94A3B8")}>ยังไม่มีรายการ</span>
        )}
      </div>

      {retired.length > 0 && (
        <div style={css("padding:0 16px 12px;font-size:11.5px;color:#94A3B8")}>
          นำออกแล้ว: {retired.map((t) => t.code + (t.inUse > 0 ? " (" + t.inUse + " งาน)" : "")).join(" · ")}
          {canAdmin && " — พิมพ์รหัสเดิมในช่องด้านล่างเพื่อนำกลับมาใช้"}
        </div>
      )}

      {canAdmin && (
        <div style={css("display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;padding:12px 16px;border-top:1px solid #E9EFF5;background:#FAFCFE")}>
          <Field label="รหัส เช่น 1X20'">
            <input value={newType.code} disabled={busy}
              onChange={(e) => setNewType({ ...newType, code: e.target.value })}
              style={css(INPUT + ";width:150px;font-family:ui-monospace,monospace")} />
          </Field>
          <Field label="คำอธิบาย (ไม่ใส่ก็ได้)">
            <input value={newType.label} disabled={busy}
              onChange={(e) => setNewType({ ...newType, label: e.target.value })}
              style={css(INPUT + ";width:230px")} />
          </Field>
          <button type="button" disabled={busy || newType.code.trim().length === 0}
            onClick={async () => {
              await send(() => apiFetch("/api/vehicle-types", {
                method: "POST",
                headers: { "content-type": "application/json" },
                body: JSON.stringify({ code: newType.code, label: newType.label }),
              }));
              setNewType({ code: "", label: "" });
            }}
            style={css("height:30px;padding:0 14px;border-radius:4px;border:1px solid #0A2240;background:#0A2240;"
              + "color:#fff;font-size:12.5px;font-weight:600;cursor:pointer")}>
            เพิ่มประเภท
          </button>
          <span style={css("font-size:11px;color:#94A3B8;max-width:340px;line-height:1.5")}>
            รหัสจะถูกจัดรูปแบบให้ตรงกับที่ทะเบียนใช้อยู่ — พิมพ์ 1x20 จะได้ 1X20&#39;
          </span>
        </div>
      )}
    </div>
  );
}

const INPUT = "height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:3px")}>
      <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
      {children}
    </label>
  );
}
