"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

/**
 * The carrier's own screen.
 *
 * A subcontractor is not a colleague with fewer buttons — they work for a
 * different company, and the register everyone else reads holds their
 * competitors' assignments and every customer's name. So this screen is fed by
 * `/api/carrier`, which answers for one supplier and refuses an account tied to
 * none. It never touches `/api/jobs`.
 *
 * Accepting is one action with three required fields. A carrier who confirms
 * without a plate has moved the job into a state that reads as arranged and
 * still cannot be dispatched — and somebody has to ring them again for the one
 * thing the operator was waiting for.
 */

type CarrierJob = {
  key: string; jobCode: string; customer: string; destination: string; type: string;
  cyYard: string; weight: string; container: string; date: string; pickupPlan: string;
  status: string; requestId: number | null; quotedPrice: number | null;
  requestedAt: string | null; licence: string; driver: string; contact: string;
};

type Portal = {
  supplierId: number; supplierName: string;
  offered: CarrierJob[]; accepted: CarrierJob[];
};

type Draft = { licence: string; driver: string; contact: string };
const EMPTY: Draft = { licence: "", driver: "", contact: "" };

export function CarrierPortal({ onToast }: { onToast: (message: string) => void }) {
  const [portal, setPortal] = useRemembered<Portal>("carrier-portal");
  const [refused, setRefused] = useState("");
  const [tab, setTab] = useState<"new" | "accepted">("new");
  const [open, setOpen] = useState<string | null>(null);
  const [draft, setDraft] = useState<Draft>(EMPTY);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch("/api/carrier", { headers: { accept: "application/json" } });
    if (response.ok) { setPortal(await response.json() as Portal); setRefused(""); return; }
    const body = await response.json().catch(() => ({})) as { error?: string };
    setRefused(body.error || `เปิดหน้างานไม่ได้ (${response.status})`);
  }, [setPortal]);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function answer(job: CarrierJob, path: "accept" | "decline", body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier/${encodeURIComponent(job.key)}/${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      if (response.ok) { setOpen(null); setDraft(EMPTY); await load(); }
    } finally { setBusy(false); }
  }

  if (refused) {
    return (
      <div style={css("background:#fff;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:6px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#B45309;margin-bottom:4px")}>เปิดหน้างานของบริษัทไม่ได้</div>
        <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.7")}>{refused}</div>
      </div>
    );
  }

  if (!portal) {
    return <div style={css("padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>กำลังโหลด…</div>;
  }

  const rows = tab === "new" ? portal.offered : portal.accepted;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 17px")}>
        <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>บริษัทของคุณ</div>
        <div style={css("font-size:16px;font-weight:700;color:#0F2B46;margin-top:2px")}>{portal.supplierName}</div>
        <div style={css("font-size:12px;color:#7B8CA0;margin-top:3px;line-height:1.6")}>
          หน้านี้แสดงเฉพาะงานที่ถูกส่งมาให้บริษัทนี้ และงานที่บริษัทนี้รับไปแล้วเท่านั้น
        </div>
      </div>

      <div style={css("display:flex;gap:7px")}>
        {([["new", "งานใหม่", portal.offered.length], ["accepted", "งานที่รับแล้ว", portal.accepted.length]] as const)
          .map(([id, label, count]) => {
            const on = tab === id;
            return (
              <button key={id} onClick={() => setTab(id)}
                style={css("height:33px;padding:0 15px;border:1px solid " + (on ? "#0A2240" : "#D3DBE3") +
                  ";background:" + (on ? "#0A2240" : "#fff") + ";color:" + (on ? "#fff" : "#3F5265") +
                  ";border-radius:5px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}>
                {label} <span style={css("opacity:.75")}>{count}</span>
              </button>
            );
          })}
      </div>

      {rows.length === 0 && (
        <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>
          {tab === "new" ? "ยังไม่มีงานใหม่ส่งเข้ามา" : "ยังไม่มีงานที่รับไว้"}
        </div>
      )}

      {rows.map((job) => {
        const editing = open === job.key;
        return (
          <div key={job.key} style={css("background:#fff;border:1px solid " + (editing ? "#9CC2E8" : "#E3E8EE") + ";border-radius:6px;padding:14px 17px")}>
            <div style={css("display:flex;gap:14px;justify-content:space-between;flex-wrap:wrap;align-items:flex-start")}>
              <div style={css("flex:1;min-width:250px")}>
                <div style={css("font-size:13.5px;font-weight:650;color:#0F2B46")}>
                  {job.customer} · {job.jobCode || job.key}
                </div>
                <div style={css("font-size:12.5px;color:#5A6B7D;margin-top:4px;line-height:1.75")}>
                  วันที่ {job.date || "—"} · {job.type || "—"} · ลานตู้ {job.cyYard || "—"} · ปลายทาง {job.destination || "—"}
                  <br />
                  ตู้ {job.container || "—"} · น้ำหนัก {job.weight || "—"}
                  {job.pickupPlan ? <> · {job.pickupPlan}</> : null}
                </div>
                {tab === "accepted" && (job.licence || job.driver) && (
                  <div style={css("font-size:12.5px;color:#16794C;margin-top:5px;font-weight:600")}>
                    {job.licence || "—"} · {job.driver || "—"} · {job.contact || "—"}
                  </div>
                )}
              </div>

              {job.quotedPrice != null && (
                <div style={css("text-align:right")}>
                  <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ราคาที่เสนอ</div>
                  <div style={css("font-size:16px;font-weight:700;color:#0F2B46;font-family:'IBM Plex Mono',monospace")}>
                    {job.quotedPrice.toLocaleString("en-US")}
                  </div>
                </div>
              )}
            </div>

            {tab === "new" && !editing && (
              <div style={css("display:flex;gap:8px;margin-top:12px;flex-wrap:wrap")}>
                <button onClick={() => { setOpen(job.key); setDraft(EMPTY); }}
                  style={css("height:31px;padding:0 15px;border:1px solid #16794C;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                  รับงานนี้
                </button>
                <button onClick={() => {
                    const reason = window.prompt("รับงานนี้ไม่ได้เพราะอะไร?");
                    if (reason && reason.trim()) void answer(job, "decline", { reason });
                  }}
                  style={css("height:31px;padding:0 15px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                  รับไม่ได้
                </button>
              </div>
            )}

            {editing && (
              <div style={css("margin-top:13px;padding-top:13px;border-top:1px solid #E9EFF5")}>
                <div style={css("font-size:12px;color:#5A6B7D;margin-bottom:9px;line-height:1.6")}>
                  กรอกรถและคนขับที่จะวิ่งงานนี้ — ข้อมูลนี้จะขึ้นที่หน้างานของเจ้าของงานทันที
                </div>
                <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
                  <Field label="ทะเบียนรถ *" width="180px" value={draft.licence}
                    onChange={(v) => setDraft({ ...draft, licence: v })} placeholder="70-1234 กรุงเทพฯ" />
                  <Field label="ชื่อ-สกุลพนักงานขับรถ *" width="230px" value={draft.driver}
                    onChange={(v) => setDraft({ ...draft, driver: v })} placeholder="นายสมชาย ใจดี" />
                  <Field label="เบอร์โทร *" width="160px" value={draft.contact}
                    onChange={(v) => setDraft({ ...draft, contact: v })} placeholder="081-234-5678" />
                </div>
                <div style={css("display:flex;gap:8px;margin-top:11px;flex-wrap:wrap")}>
                  <button
                    onClick={() => void answer(job, "accept", draft)}
                    disabled={busy || !draft.licence.trim() || !draft.driver.trim() || !draft.contact.trim()}
                    style={css("height:31px;padding:0 16px;border:1px solid #16794C;background:" +
                      (busy || !draft.licence.trim() || !draft.driver.trim() || !draft.contact.trim() ? "#C3CFDB" : "#16794C") +
                      ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                    ยืนยันรับงาน
                  </button>
                  <button onClick={() => { setOpen(null); setDraft(EMPTY); }}
                    style={css("height:31px;padding:0 15px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
                    ยกเลิก
                  </button>
                </div>
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

function Field({ label, width, value, onChange, placeholder }: {
  label: string; width: string; value: string;
  onChange: (value: string) => void; placeholder: string;
}) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:4px;width:" + width)}>
      <span style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</span>
      <input value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value)}
        style={css("height:31px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;width:100%")} />
    </label>
  );
}
