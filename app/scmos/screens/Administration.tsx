"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";

/**
 * Who may sign in, and what each of them may do.
 *
 * The matrix and the people both come from `/api/staff`, which builds them from
 * the same table the API enforces against — so what is shown here cannot drift
 * from what is in force. That mattered enough to be worth the endpoint: a
 * printed permission list that has quietly gone stale is worse than none,
 * because people plan around it.
 */

type Role = { name: string; scopeEn: string; scopeTh: string; grants: string[] };
type Person = {
  id: string; email: string; name: string; account: string; role: string;
  active: boolean; note: string; jobs: number; can: string[];
  updatedBy: string; updatedAt: string;
};
type Directory = { people: Person[]; roles: Role[]; canManage: boolean; you: string };

const CAPABILITY_TH: Record<string, string> = {
  ViewDashboard: "ดูแดชบอร์ด",
  ViewTeam: "เห็นงานทั้งทีม",
  EditOwnJobs: "แก้งานของตัวเอง",
  EditAnyJob: "แก้งานของใครก็ได้",
  AssignJobs: "มอบหมายงาน",
  UploadDocuments: "อัปโหลดเอกสาร",
  ViewRates: "ดูตารางราคา",
  EditRates: "แก้ราคา",
  ManageSuppliers: "จัดการผู้ขนส่ง",
  CloseCarPar: "ปิด CAR/PAR",
  ApproveAi: "อนุมัติข้อเสนอ AI",
  ViewAudit: "อ่านประวัติการแก้ไข",
  ApproveRetention: "อนุมัติการเก็บ/ทำลายเอกสาร",
  AdministerData: "จัดการผู้ใช้และข้อมูลทั้งระบบ",
};

export function Administration({ onToast }: { onToast: (m: string) => void }) {
  const [dir, setDir] = useState<Directory | null>(null);
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ email: "", name: "", role: "Operation User", note: "" });
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState<{ role: string; email: string; note: string }>({ role: "", email: "", note: "" });

  const load = useCallback(async () => {
    const response = await apiFetch("/api/staff", { headers: { accept: "application/json" } });
    setDir(response.ok ? await response.json() as Directory : null);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/staff", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Directory : null;
      if (!cancelled) setDir(body);
    })();
    return () => { cancelled = true; };
  }, []);

  async function post(path: string, body: unknown) {
    if (busy) return false;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/staff${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
      return response.ok;
    } finally { setBusy(false); }
  }

  if (!dir) {
    return (
      <div style={css("background:#fff;border:1px solid #F3C9C4;border-left:3px solid #B42318;border-radius:5px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#B42318;margin-bottom:4px")}>อ่านทะเบียนผู้ใช้ไม่ได้</div>
        <div style={css("font-size:12.5px;color:#5A6B7D")}>
          หน้านี้ต้องมีสิทธิ์ระดับหัวหน้างานขึ้นไป หรือ API ยังติดต่อไม่ได้
        </div>
      </div>
    );
  }

  const roleOf = (name: string) => dir.roles.find((role) => role.name === name);
  const admins = dir.people.filter((p) => p.active && p.can.includes("AdministerData")).length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 15px;font-size:12px;color:#5A6B7D;line-height:1.65")}>
        ทะเบียนนี้และตารางสิทธิ์ด้านล่างอ่านจาก <code style={css("font-family:ui-monospace,monospace")}>Rules/Roles.cs</code> และตาราง{" "}
        <code style={css("font-family:ui-monospace,monospace")}>staff</code> ซึ่งเป็นตัวเดียวกับที่ API ใช้บังคับจริง
        {dir.canManage
          ? " — คุณมีสิทธิ์แก้ไขทะเบียนนี้"
          : " — คุณดูได้อย่างเดียว เพิ่มหรือแก้ผู้ใช้ได้เฉพาะผู้ดูแลระบบ"}
      </div>

      {/* ------------------------------------------------------------ people */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
          <div>
            <div style={css("font-size:13px;font-weight:650;color:#0A2240")}>ผู้ใช้ในระบบ · {dir.people.length} คน</div>
            <div style={css("font-size:11.5px;color:#94A3B8;margin-top:1px")}>
              ผู้ดูแลระบบที่ยังใช้งานอยู่ {admins} คน — ระบบไม่ยอมให้เหลือศูนย์
            </div>
          </div>
          {dir.canManage && (
            <button onClick={() => setAdding((v) => !v)}
              style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:" + (adding ? "#fff" : "#0A2240") + ";color:" + (adding ? "#0A2240" : "#fff") + ";border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
              {adding ? "ยกเลิก" : "+ เพิ่มผู้ใช้"}
            </button>
          )}
        </div>

        {adding && dir.canManage && (
          <div style={css("padding:14px 16px;border-bottom:1px solid #E9EFF5;background:#F8FAFC")}>
            <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:flex-end")}>
              <Field label="อีเมลที่ใช้ลงชื่อเข้าใช้ *" width="250px">
                <input value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })}
                  placeholder="somchai.p@leschaco.co.th" style={INPUT} />
              </Field>
              <Field label="ชื่อ (ตามที่ไฟล์แผนสะกด) *" width="180px">
                <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })}
                  placeholder="Somchai" style={INPUT} />
              </Field>
              <Field label="บทบาท" width="200px">
                <select value={form.role} onChange={(e) => setForm({ ...form, role: e.target.value })} style={SELECT}>
                  {dir.roles.map((role) => <option key={role.name} value={role.name}>{role.name} — {role.scopeTh}</option>)}
                </select>
              </Field>
              <Field label="หมายเหตุ" width="180px">
                <input value={form.note} onChange={(e) => setForm({ ...form, note: e.target.value })} style={INPUT} />
              </Field>
              <button
                onClick={async () => {
                  if (await post("", form)) { setForm({ email: "", name: "", role: "Operation User", note: "" }); setAdding(false); }
                }}
                disabled={busy || !form.email.trim() || !form.name.trim()}
                style={css("height:30px;padding:0 15px;border:1px solid #16794C;background:" +
                  (busy || !form.email.trim() || !form.name.trim() ? "#C3CFDB" : "#16794C") +
                  ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>สร้างบัญชี</button>
            </div>

            <div style={css("margin-top:11px;padding:10px 12px;background:#E7F0FA;border:1px solid #BBD5EE;border-radius:5px;font-size:12px;color:#1D4E80;line-height:1.6")}>
              <b>บทบาท {form.role}</b> จะได้สิทธิ์: {(roleOf(form.role)?.grants ?? []).map((g) => CAPABILITY_TH[g] ?? g).join(" · ") || "—"}
            </div>
            <div style={css("margin-top:7px;font-size:11.5px;color:#B45309;line-height:1.6")}>
              อีเมลต้องตรงกับที่ Microsoft 365 ส่งมาจริง — บัญชีที่เป็น guest ในองค์กรจะใช้รูป
              <code style={css("font-family:ui-monospace,monospace")}>ชื่อ_domain.com#EXT#@tenant.onmicrosoft.com</code> ไม่ใช่อีเมลเดิม
            </div>
          </div>
        )}

        <div style={css("overflow-x:auto")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["รหัส", "ชื่อ", "อีเมล", "บทบาท", "งาน", "สถานะ", ""].map((h, i) => (
              <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:" + (i === 4 ? "right" : "left") +
                ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {dir.people.map((person) => {
                const isYou = person.id === dir.you;
                const open = editing === person.id;
                return (
                  <tr key={person.id} style={css("border-bottom:1px solid #F1F5F9;background:" +
                    (!person.active ? "#FAFBFC" : isYou ? "#F4F8FC" : "#fff"))}>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0")}>{person.id}</td>
                    <td style={css(CELL + ";font-weight:600;color:" + (person.active ? "#0A2240" : "#94A3B8"))}>
                      {person.name}
                      {isYou && <span style={css("font-size:10px;font-weight:700;color:#fff;background:#1D5FA8;border-radius:3px;padding:1px 6px;margin-left:7px")}>คุณ</span>}
                    </td>
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D;max-width:250px;word-break:break-all")}>
                      {open
                        ? <input value={draft.email} onChange={(e) => setDraft({ ...draft, email: e.target.value })}
                            style={css("width:100%;height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:11.5px")} />
                        : person.email || <span style={css("color:#B45309")}>ยังไม่ได้ตั้ง</span>}
                    </td>
                    <td style={CELL_S}>
                      {open
                        ? <select value={draft.role} onChange={(e) => setDraft({ ...draft, role: e.target.value })}
                            style={css("height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 6px;font-size:11.5px;background:#fff")}>
                            {dir.roles.map((r) => <option key={r.name} value={r.name}>{r.name}</option>)}
                          </select>
                        : <>
                            <div style={css("color:" + (person.active ? "#16232F" : "#94A3B8"))}>{person.role}</div>
                            <div style={css("font-size:10.5px;color:#94A3B8")}>{roleOf(person.role)?.scopeTh ?? ""}</div>
                          </>}
                    </td>
                    <td style={css(CELL + ";text-align:right;font-family:ui-monospace,monospace;color:#7B8CA0")}>
                      {person.jobs.toLocaleString()}
                    </td>
                    <td style={CELL_S}>
                      <span style={css("font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:" +
                        (person.active ? "#16794C" : "#7B8CA0"))}>{person.active ? "ใช้งาน" : "ปิด"}</span>
                    </td>
                    <td style={css(CELL + ";white-space:nowrap")}>
                      {dir.canManage && (open ? (
                        <span style={css("display:flex;gap:5px")}>
                          <Mini label="บันทึก" tone="#16794C" busy={busy}
                            onClick={async () => { if (await post(`/${person.id}`, draft)) setEditing(null); }} />
                          <Mini label="ยกเลิก" tone="#7B8CA0" busy={busy} onClick={() => setEditing(null)} />
                        </span>
                      ) : (
                        <span style={css("display:flex;gap:5px")}>
                          <Mini label="แก้ไข" tone="#0A5FA8" busy={busy}
                            onClick={() => { setEditing(person.id); setDraft({ role: person.role, email: person.email, note: person.note }); }} />
                          <Mini label={person.active ? "ปิดบัญชี" : "เปิดใช้"} tone={person.active ? "#B42318" : "#16794C"} busy={busy}
                            onClick={() => void post(`/${person.id}`, { active: !person.active })} />
                        </span>
                      ))}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        <div style={css("padding:11px 16px;border-top:1px solid #E9EFF5;font-size:11.5px;color:#94A3B8;line-height:1.6")}>
          ไม่มีปุ่มลบ — คนที่ลาออกยังเป็นเจ้าของงานที่เคยทำ ลบแถวทิ้งจะทำให้งานเหล่านั้นไม่มีเจ้าของ
          <b> ปิดบัญชี</b> คือหยุดการเข้าใช้โดยเก็บประวัติไว้ครบ ·
          ระบบไม่ยอมให้ปิดหรือลดบทบาทผู้ดูแลคนสุดท้าย และไม่ยอมให้ใครเปลี่ยนบทบาทของตัวเอง
        </div>
      </div>

      {/* ------------------------------------------------------------ matrix */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          สิทธิ์ตามบทบาท · {dir.roles.length} บทบาท
        </div>
        <div style={css("overflow-x:auto")}>
          <table style={css("border-collapse:collapse;font-size:12px;min-width:100%")}>
            <thead>
              <tr>
                <th style={css("position:sticky;left:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;border-right:1px solid #E9EFF5;white-space:nowrap;z-index:1")}>
                  ความสามารถ
                </th>
                {dir.roles.map((role) => {
                  const held = dir.people.filter((p) => p.active && p.role === role.name).length;
                  return (
                    <th key={role.name} style={css("background:#F8FAFC;padding:8px 10px;text-align:center;font-size:10.5px;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap;min-width:92px")}>
                      <div style={css("color:#0A2240;font-size:11px")}>{role.name}</div>
                      <div style={css("font-weight:400;font-size:10px")}>{role.scopeTh}</div>
                      <div style={css("font-weight:400;font-size:9.5px;color:" + (held ? "#16794C" : "#B45309"))}>
                        {held ? `${held} คน` : "ยังไม่มีคนใช้"}
                      </div>
                    </th>
                  );
                })}
              </tr>
            </thead>
            <tbody>
              {(dir.roles[0]?.grants ? Object.keys(CAPABILITY_TH) : []).map((capability) => (
                <tr key={capability} style={css("border-bottom:1px solid #F1F5F9")}>
                  <td style={css("position:sticky;left:0;background:#fff;padding:7px 12px;border-right:1px solid #E9EFF5;white-space:nowrap")}>
                    <div style={css("color:#16232F")}>{CAPABILITY_TH[capability]}</div>
                    <div style={css("font-family:ui-monospace,monospace;font-size:10px;color:#94A3B8")}>{capability}</div>
                  </td>
                  {dir.roles.map((role) => {
                    const granted = role.grants.includes(capability);
                    return (
                      <td key={role.name} style={css("padding:7px 10px;text-align:center;font-size:13px;color:" +
                        (granted ? "#16794C" : "#D8E0E8"))}>{granted ? "✓" : "·"}</td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <div style={css("padding:11px 16px;border-top:1px solid #E9EFF5;font-size:11.5px;color:#94A3B8;line-height:1.6")}>
          บทบาทที่ระบบไม่รู้จักจะได้สิทธิ์เท่า Viewer ไม่ใช่ค่าเริ่มต้น — พิมพ์บทบาทผิดควรทำให้เสียสิทธิ์ตัวเอง ไม่ใช่ได้สิทธิ์คนอื่น
        </div>
      </div>
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:middle";
const CELL_S = css(CELL);
const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;width:100%");
const SELECT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;width:100%");

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <label style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`)}>
      <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
      {children}
    </label>
  );
}

function Mini({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={busy}
      style={css(`height:26px;padding:0 10px;border:1px solid ${tone};background:#fff;color:${tone};border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer`)}
    >{label}</button>
  );
}
