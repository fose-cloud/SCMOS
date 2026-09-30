"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import {
  CATEGORIES, FIELD_LABEL, missingFields, parseRequests, REQUEST_STATUS_LABEL, requestBody, requestSummary,
  type CarrierJobRequest, type RequestForm,
} from "../carrierJobRequests";
import { css } from "../theme";

const TONE: Record<string, string> = { PENDING: "#B45309", APPROVED: "#16794C", REJECTED: "#B42318", WITHDRAWN: "#7B8CA0" };

/**
 * The carrier's own jobs, keyed in for Leschaco to confirm (29 Sep 2026): the
 * add-job form's fields for the category chosen, and what it sent before with
 * where each stands. Nothing here creates a job — the department does, from
 * the request, through its own form.
 */
export function CarrierJobRequests({ onToast }: { onToast: (message: string) => void }) {
  const [items, setItems] = useState<CarrierJobRequest[]>([]);
  const [form, setForm] = useState<RequestForm | null>(null);
  const [error, setError] = useState("");
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState<string>("IMPORT");
  const [fields, setFields] = useState<Record<string, string>>({});
  const [note, setNote] = useState("");
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/carrier/job-requests", { headers: { accept: "application/json" } });
      const body: unknown = await response.json().catch(() => null);
      if (!response.ok) throw new Error(typeof body === "object" && body && "error" in body ? String(body.error) : `เปิดงานที่แจ้งไว้ไม่ได้ (${response.status})`);
      const parsed = parseRequests(body);
      setItems(parsed.items); setForm(parsed.form); setError("");
    } catch (problem) {
      setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
    }
  }, []);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const names = form?.fields[category] ?? [];
  const missing = form ? missingFields(category, fields, form) : [];

  async function send() {
    if (busy || !form || missing.length > 0) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/carrier/job-requests", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify(requestBody(category, fields, names, note)),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ส่งงานไม่สำเร็จ");
      if (response.ok) { setOpen(false); setFields({}); setNote(""); await load(); }
    } finally { setBusy(false); }
  }

  async function withdraw(item: CarrierJobRequest) {
    if (busy || !window.confirm("ถอนงานที่แจ้งไว้นี้?")) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/carrier/job-requests/${item.id}/withdraw`, { method: "POST" });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ถอนไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 17px;display:flex;flex-direction:column;gap:11px")}>
      <div style={css("display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#0F2B46")}>งานที่บริษัทแจ้งเข้ามา</div>
        {!open && <button onClick={() => setOpen(true)} disabled={!form}
          style={css("height:31px;padding:0 15px;border:1px solid #0A5C97;background:#0A5C97;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
          + แจ้งงานใหม่</button>}
      </div>
      {error && <div style={css("font-size:12.5px;color:#B45309")}>{error}</div>}

      {open && form && (
        <div style={css("border:1px solid #9CC2E8;border-radius:6px;padding:12px;display:flex;flex-direction:column;gap:10px")}>
          <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
            {CATEGORIES.map(one => <button key={one} onClick={() => { setCategory(one); setFields({}); }}
              style={css("height:29px;padding:0 12px;border:1px solid " + (category === one ? "#0A5C97" : "#D3DBE3") + ";background:" +
                (category === one ? "#EAF4FC" : "#fff") + ";color:" + (category === one ? "#0A5C97" : "#5A6B7D") +
                ";border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;font-family:inherit")}>{one}</button>)}
          </div>
          <div style={css("display:grid;grid-template-columns:repeat(auto-fill,minmax(190px,1fr));gap:8px")}>
            {names.map(name => {
              const required = form.essential[category]?.includes(name);
              return <label key={name} style={css("display:flex;flex-direction:column;gap:4px")}>
                <span style={css("font-size:10.5px;color:#7B8CA0;font-weight:600")}>{FIELD_LABEL[name] ?? name}{required ? " *" : ""}</span>
                <input aria-label={FIELD_LABEL[name] ?? name} value={fields[name] ?? ""} maxLength={120} onChange={(event) => setFields({ ...fields, [name]: event.target.value })}
                  style={css("height:31px;padding:0 9px;border:1px solid " + (required && !(fields[name] ?? "").trim() ? "#E8B4A8" : "#D3DBE3") +
                    ";border-radius:4px;font-size:12.5px;font-family:inherit")} />
              </label>;
            })}
          </div>
          <label style={css("display:flex;flex-direction:column;gap:4px")}>
            <span style={css("font-size:10.5px;color:#7B8CA0;font-weight:600")}>หมายเหตุถึง Leschaco</span>
            <textarea aria-label="หมายเหตุถึง Leschaco" value={note} maxLength={500} rows={2} onChange={(event) => setNote(event.target.value)}
              style={css("padding:7px 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;resize:vertical")} />
          </label>
          <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
            <button onClick={() => void send()} disabled={busy || missing.length > 0}
              style={css("height:31px;padding:0 16px;border:0;background:#16794C;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;opacity:" +
                (busy || missing.length > 0 ? ".55" : "1"))}>ส่งให้ Leschaco ยืนยัน</button>
            <button onClick={() => { setOpen(false); setFields({}); setNote(""); }}
              style={css("height:31px;padding:0 15px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>ยกเลิก</button>
            {missing.length > 0 && <span style={css("font-size:11.5px;color:#B45309")}>ต้องกรอก: {missing.map(name => FIELD_LABEL[name] ?? name).join(", ")}</span>}
          </div>
        </div>
      )}

      {items.length > 0 && <div style={css("display:flex;flex-direction:column")}>
        {items.map(item => <div key={item.id} style={css("display:flex;gap:10px;align-items:center;justify-content:space-between;flex-wrap:wrap;padding:8px 0;border-top:1px solid #EFF3F7")}>
          <div style={css("min-width:220px;flex:1")}>
            <div style={css("font-size:12.5px;color:#0F2B46;font-weight:600")}>{requestSummary(item)}</div>
            <div style={css("font-size:11.5px;color:#7B8CA0")}>
              {new Date(item.createdAt).toLocaleString("th-TH")}
              {item.status === "APPROVED" && item.jobKey ? ` · งาน ${item.fields.jobCode || item.fields.booking || item.fields.jobNo || item.jobKey}` : ""}
              {item.decisionNote ? ` · ${item.decisionNote}` : ""}
            </div>
          </div>
          <span style={css("font-size:11px;font-weight:700;color:" + TONE[item.status])}>{REQUEST_STATUS_LABEL[item.status]}</span>
          {item.status === "PENDING" && <button onClick={() => void withdraw(item)} disabled={busy}
            style={css("height:27px;padding:0 11px;border:1px solid #D3DBE3;background:#fff;color:#5A6B7D;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}>ถอน</button>}
        </div>)}
      </div>}
    </div>
  );
}
