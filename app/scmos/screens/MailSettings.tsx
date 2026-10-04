"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { mailboxKindLabel, ownerChoice } from "../mailInbox";
import { ZoomBox } from "../TableFrame";

/**
 * What the Communication Center reads (4 Oct 2026). Personal mailboxes are read only for the senders listed here —
 * full addresses, kept by Supervisor and above — and the Administrator declares each approved mailbox personal
 * (whose) or shared, and switches it on.
 */

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:5px";
const BUTTON = "height:28px;padding:0 12px;border-radius:4px;font-size:12px;font-family:inherit;cursor:pointer;border:1px solid";
const INPUT = "height:28px;padding:0 8px;border:1px solid #C9D6E2;border-radius:4px;font-size:12px;font-family:inherit;background:#fff";
const CELL = "padding:7px 10px;border-bottom:1px solid #EDF1F5;font-size:12px;color:#0F2B46;vertical-align:middle";

type Sender = { id: number; address: string; note: string; addedBy: string; addedAt: string };
type MailboxRow = {
  address: string; id: number | null; active: boolean; owner: string; ownerName: string;
  approved: boolean; lastSyncedAt: string | null; staffOwner: string;
};
type Person = { id: string; name: string; role: string };

export function MailSettings({ canMailboxes, onToast }: {
  /** Whether this account administers the mailboxes (AdministerMailbox). */
  canMailboxes: boolean;
  onToast: (message: string) => void;
}) {
  const [senders, setSenders] = useState<Sender[]>([]);
  const [canManage, setCanManage] = useState(false);
  const [form, setForm] = useState({ address: "", note: "" });
  const [boxes, setBoxes] = useState<MailboxRow[]>([]);
  const [staff, setStaff] = useState<Person[]>([]);
  const [edits, setEdits] = useState<Record<string, { owner: string; active: boolean }>>({});
  const [busy, setBusy] = useState(false);
  const [refresh, setRefresh] = useState(0);

  useEffect(() => {
    let alive = true;
    (async () => {
      const response = await apiFetch("/api/mail/senders", { headers: { accept: "application/json" } });
      if (!alive || !response.ok) return;
      const body = await response.json() as { senders: Sender[]; canManage: boolean };
      if (!alive) return;
      setSenders(body.senders);
      setCanManage(body.canManage);
    })();
    return () => { alive = false; };
  }, [refresh]);

  useEffect(() => {
    if (!canMailboxes) return;
    let alive = true;
    (async () => {
      const response = await apiFetch("/api/mail/mailboxes", { headers: { accept: "application/json" } });
      if (!alive || !response.ok) return;
      const body = await response.json() as { mailboxes: MailboxRow[]; staff: Person[] };
      if (!alive) return;
      setBoxes(body.mailboxes);
      setStaff(body.staff);
      setEdits(Object.fromEntries(body.mailboxes.map((box) => [box.address, { owner: ownerChoice(box), active: box.active }])));
    })();
    return () => { alive = false; };
  }, [canMailboxes, refresh]);

  async function send(path: string, method: string, body?: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(path, {
        method, headers: { "content-type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `ไม่สำเร็จ (${response.status})`);
      if (response.ok) { setForm({ address: "", note: "" }); setRefresh((n) => n + 1); }
    } finally { setBusy(false); }
  }

  async function test(address: string) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/integrations/graph/test", {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify({ mailbox: address }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? `ไม่สำเร็จ (${response.status})`);
    } finally { setBusy(false); }
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <section style={css(`${CARD};padding:12px 14px;display:flex;flex-direction:column;gap:10px`)}>
        <div style={css("display:flex;gap:10px;align-items:baseline;flex-wrap:wrap")}>
          <span style={css("font-size:13px;font-weight:650;color:#0A2240")}>ผู้ส่งที่อ่านจากกล่องส่วนตัว</span>
          <span style={css(LABEL)}>{senders.length} ราย</span>
        </div>
        {canManage && (
          <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:center")}>
            <input aria-label="อีเมลผู้ส่ง" placeholder="booking@customer.com" value={form.address}
              onChange={(e) => setForm({ ...form, address: e.target.value })} style={css(INPUT + ";width:260px")} />
            <input aria-label="หมายเหตุผู้ส่ง" placeholder="หมายเหตุ" value={form.note} maxLength={200}
              onChange={(e) => setForm({ ...form, note: e.target.value })} style={css(INPUT + ";flex:1;min-width:160px")} />
            <button type="button" disabled={busy || form.address.trim().length === 0}
              onClick={() => void send("/api/mail/senders", "POST", { address: form.address.trim(), note: form.note.trim() })}
              style={css(`${BUTTON};background:#0A2240;color:#fff;border-color:#0A2240`)}>เพิ่ม</button>
          </div>
        )}
        {senders.length > 0 && (
          <ZoomBox zoomable={false} capped={false}><table style={css("width:100%;border-collapse:collapse")}>
            <tbody>
              {senders.map((one) => (
                <tr key={one.id}>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace")}>{one.address}</td>
                  <td style={css(CELL)}>{one.note}</td>
                  <td style={css(CELL + ";color:#64748B;white-space:nowrap")}>{one.addedBy} · {one.addedAt.slice(0, 10)}</td>
                  {canManage && (
                    <td style={css(CELL + ";text-align:right")}>
                      <button type="button" disabled={busy}
                        onClick={() => { if (window.confirm(`เอา ${one.address} ออก?`)) void send(`/api/mail/senders/${one.id}`, "DELETE"); }}
                        style={css(`${BUTTON};background:#fff;color:#9B1C1C;border-color:#F5C2C2`)}>เอาออก</button>
                    </td>
                  )}
                </tr>
              ))}
            </tbody>
          </table></ZoomBox>
        )}
      </section>

      {canMailboxes && (
        <section style={css(`${CARD};padding:12px 14px;display:flex;flex-direction:column;gap:10px`)}>
          <div style={css("display:flex;gap:10px;align-items:baseline;flex-wrap:wrap")}>
            <span style={css("font-size:13px;font-weight:650;color:#0A2240")}>กล่องอีเมล</span>
            <span style={css(LABEL)}>Graph__Mailboxes · {boxes.length} กล่อง</span>
          </div>
          {boxes.length > 0 && (
            <ZoomBox zoomable={false} capped={false}><table style={css("width:100%;border-collapse:collapse")}>
              <tbody>
                {boxes.map((box) => {
                  const edit = edits[box.address] ?? { owner: ownerChoice(box), active: box.active };
                  const set = (change: Partial<typeof edit>) => setEdits({ ...edits, [box.address]: { ...edit, ...change } });
                  return (
                    <tr key={box.address}>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace")}>
                        {box.address}
                        {!box.approved && <div style={css("font-size:10.5px;color:#9B1C1C")}>ไม่อยู่ใน Graph__Mailboxes</div>}
                      </td>
                      <td style={css(CELL)}>
                        <select aria-label={`เจ้าของ ${box.address}`} value={edit.owner} onChange={(e) => set({ owner: e.target.value })}
                          style={css(INPUT + ";min-width:220px")}>
                          <option value="">{mailboxKindLabel("")}</option>
                          {staff.map((person) => <option key={person.id} value={person.id}>{mailboxKindLabel(person.name)}</option>)}
                        </select>
                      </td>
                      <td style={css(CELL)}>
                        <label style={css("display:flex;gap:6px;align-items:center;font-size:12px")}>
                          <input type="checkbox" checked={edit.active} onChange={(e) => set({ active: e.target.checked })} />เปิดอ่าน
                        </label>
                      </td>
                      <td style={css(CELL + ";color:#64748B;white-space:nowrap")}>{box.lastSyncedAt ? box.lastSyncedAt.slice(0, 16).replace("T", " ") : "—"}</td>
                      <td style={css(CELL + ";text-align:right;white-space:nowrap")}>
                        <button type="button" disabled={busy} onClick={() => void test(box.address)}
                          style={css(`${BUTTON};background:#fff;color:#0A2240;border-color:#D3DBE3;margin-right:6px`)}>ทดสอบ</button>
                        <button type="button" disabled={busy || !box.approved}
                          onClick={() => void send("/api/mail/mailboxes", "PUT", { address: box.address, owner: edit.owner, active: edit.active })}
                          style={css(`${BUTTON};background:#0A2240;color:#fff;border-color:#0A2240`)}>บันทึก</button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table></ZoomBox>
          )}
        </section>
      )}
    </div>
  );
}
