"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import type { Job } from "../ops";
import { clearOwnerJobs } from "../store";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

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
type Directory = {
  people: Person[]; roles: Role[]; canManage: boolean; you: string;
  /** Whether this API can create a sign-in, not only a register row. */
  signIn?: { ready: boolean; why: string };
};

/**
 * What an administrator is actually deciding when they add somebody.
 *
 * Adding a row to the directory says what a person may do. It does not give
 * them a way in — and the two were the same button until an administrator
 * created a colleague, told them to sign in, and watched it fail with nothing
 * on screen to explain why.
 */
const SIGN_IN_WAYS = [
  {
    id: "invite",
    label: "เชิญด้วยอีเมลที่เขามีอยู่",
    detail: "Gmail, Outlook.com, อีเมลบริษัท — Microsoft ส่งคำเชิญไปให้ เขากดรับแล้วเข้าด้วยรหัสเดิมของตัวเอง SCMOS ไม่เก็บรหัสผ่าน",
  },
  {
    id: "tenant",
    label: "สร้างบัญชีใหม่ในองค์กร",
    detail: "สำหรับคนที่ไม่มีอีเมลใช้ได้ ระบบสุ่มรหัสชั่วคราวให้ครั้งเดียว และบังคับเปลี่ยนรหัสเมื่อเข้าใช้ครั้งแรก",
  },
  {
    id: "none",
    label: "เขาเข้าระบบได้อยู่แล้ว",
    detail: "บันทึกลงทะเบียนอย่างเดียว ใช้เมื่อบัญชีนั้นลงชื่อเข้าใช้ได้อยู่ก่อนแล้ว",
  },
] as const;

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

export function Administration({ jobs, me, onToast }: {
  /** The register, so the clear panel can offer the months a person actually has. */
  jobs: Job[];
  me: string;
  onToast: (m: string) => void;
}) {
  const [dir, setDir] = useRemembered<Directory>("administration");
  const [busy, setBusy] = useState(false);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState({ email: "", name: "", role: "Operation User", note: "", signIn: "invite" });
  /**
   * The temporary password, held only long enough to be read.
   *
   * It is never stored — not in the staff row, not in the audit trail — so this
   * is the one and only time anybody sees it. A toast would slide away while
   * the administrator was still reaching for a pen.
   */
  const [issued, setIssued] = useState<{ name: string; signIn: string; password: string } | null>(null);
  const [editing, setEditing] = useState<string | null>(null);
  const [draft, setDraft] = useState<{ role: string; email: string; note: string }>({ role: "", email: "", note: "" });

  /**
   * Why the directory could not be read, in the caller's own words.
   *
   * "No permission or the API is unreachable" was one message for two
   * situations that need opposite responses: one is answered by asking an
   * administrator, the other by waiting ten seconds. It appeared during a
   * routine restart of the API and stayed on screen with nothing to press,
   * which reads as the account having lost its access.
   */
  const [failure, setFailure] = useState<{ retryable: boolean; message: string } | null>(null);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch("/api/staff", { headers: { accept: "application/json" } });
      if (response.ok) {
        setDir(await response.json() as Directory);
        setFailure(null);
        return;
      }
      const body = await response.json().catch(() => ({})) as { error?: string };
      setDir(null);
      setFailure(response.status === 401 || response.status === 403
        ? { retryable: false, message: body.error || "บัญชีนี้ไม่มีสิทธิ์เปิดทะเบียนผู้ใช้" }
        : { retryable: true, message: `API ตอบ ${response.status} — ` +
            (response.status >= 500 ? "เซิร์ฟเวอร์อาจกำลังรีสตาร์ท" : body.error || "ลองใหม่อีกครั้ง") });
    } catch (error) {
      setDir(null);
      setFailure({ retryable: true,
        message: "ติดต่อ API ไม่ได้: " + (error instanceof Error ? error.message : String(error)) });
    }
  }, [setDir]);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

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

  /**
   * Creating differs from every other action here: its answer carries something
   * that cannot be fetched again. `post` throws the body away after the toast,
   * which is right for a role change and wrong for a password issued once.
   */
  async function create(body: unknown) {
    if (busy) return null;
    setBusy(true);
    try {
      const response = await apiFetch("/api/staff", {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as
        { message?: string; error?: string; signIn?: string; tempPassword?: string };
      onToast(reply.message ?? reply.error ?? "สร้างบัญชีไม่สำเร็จ");
      await load();
      return response.ok ? reply : null;
    } finally { setBusy(false); }
  }

  /**
   * Removing a row. Separate from `post` because it is the one action here that
   * cannot be undone from this screen — the confirmation and the wording of the
   * refusal matter more than the request does.
   */
  async function remove(person: Person) {
    if (busy) return;
    if (!window.confirm(
      `ลบ ${person.name} (${person.id}) ออกจากทะเบียนถาวร?

` +
      `ประวัติการใช้งานใน Audit ยังอยู่ครบ แต่แถวนี้จะหายไปและกู้คืนไม่ได้`,
    )) return;

    setBusy(true);
    try {
      const response = await apiFetch(`/api/staff/${person.id}`, { method: "DELETE" });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ลบไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  /**
   * The same shape as `create`: some answers carry a value that exists nowhere
   * else and cannot be fetched again, so the body is kept rather than toasted
   * and thrown away.
   */
  async function create2(path: string) {
    if (busy) return null;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/staff${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: "{}",
      });
      const reply = await response.json().catch(() => ({})) as
        { message?: string; error?: string; tempPassword?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
      return response.ok ? reply : null;
    } finally { setBusy(false); }
  }

  if (!dir) {
    const retryable = failure?.retryable ?? true;
    return (
      <div style={css("background:#fff;border:1px solid " + (retryable ? "#F0D8B8" : "#F3C9C4") +
        ";border-left:3px solid " + (retryable ? "#B45309" : "#B42318") + ";border-radius:5px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:" + (retryable ? "#B45309" : "#B42318") + ";margin-bottom:4px")}>
          {retryable ? "ยังอ่านทะเบียนผู้ใช้ไม่ได้" : "บัญชีนี้เปิดทะเบียนผู้ใช้ไม่ได้"}
        </div>
        <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.7")}>
          {failure?.message ?? "กำลังโหลด…"}
          {retryable && <><br />ถ้าเพิ่งมีการอัปเดตระบบ ให้รอสักครู่แล้วกดลองใหม่</>}
        </div>
        {retryable && (
          <button onClick={() => void load()} disabled={busy}
            style={css("margin-top:14px;height:32px;padding:0 15px;border:1px solid #B45309;background:#fff;color:#B45309;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
            ลองใหม่
          </button>
        )}
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

        {/* What just happened, kept on screen until it is dismissed. The
            password below exists nowhere else — closing this panel is the last
            chance to read it. */}
        {issued && (
          <div style={css("padding:14px 16px;border-bottom:1px solid #E9EFF5;background:#F0F8F3")}>
            <div style={css("display:flex;justify-content:space-between;gap:12px;align-items:flex-start;flex-wrap:wrap")}>
              <div style={css("flex:1;min-width:260px")}>
                <div style={css("font-size:13px;font-weight:650;color:#16794C;margin-bottom:4px")}>
                  สร้างบัญชีให้ {issued.name} แล้ว
                </div>
                <div style={css("font-size:12.5px;color:#3F5265;line-height:1.65")}>{issued.signIn}</div>

                {issued.password && (
                  <div style={css("margin-top:10px;padding:11px 13px;background:#fff;border:1px solid #BBD5EE;border-radius:5px")}>
                    <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:5px")}>
                      รหัสผ่านชั่วคราว — แสดงครั้งเดียว
                    </div>
                    <div style={css("font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:16px;font-weight:600;color:#0F2B46;letter-spacing:.04em;word-break:break-all")}>
                      {issued.password}
                    </div>
                    <div style={css("margin-top:7px;font-size:11.5px;color:#B45309;line-height:1.6")}>
                      ระบบไม่ได้เก็บรหัสนี้ไว้ที่ไหนเลย ส่งให้เจ้าตัวโดยตรง แล้วเขาจะถูกบังคับให้ตั้งรหัสใหม่ทันทีที่เข้าครั้งแรก
                      ถ้าทำหาย ให้ออกรหัสใหม่แทนการตามหา
                    </div>
                  </div>
                )}
              </div>
              <button onClick={() => setIssued(null)}
                style={css("height:29px;padding:0 13px;border:1px solid #C3CFDB;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}>
                รับทราบแล้ว
              </button>
            </div>
          </div>
        )}

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
                  const reply = await create(form);
                  if (!reply) return;
                  setIssued({ name: form.name, signIn: reply.signIn ?? "", password: reply.tempPassword ?? "" });
                  setForm({ email: "", name: "", role: "Operation User", note: "", signIn: form.signIn });
                  setAdding(false);
                }}
                disabled={busy || !form.email.trim() || !form.name.trim()}
                style={css("height:30px;padding:0 15px;border:1px solid #16794C;background:" +
                  (busy || !form.email.trim() || !form.name.trim() ? "#C3CFDB" : "#16794C") +
                  ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>สร้างบัญชี</button>
            </div>

            <div style={css("margin-top:11px;padding:10px 12px;background:#E7F0FA;border:1px solid #BBD5EE;border-radius:5px;font-size:12px;color:#1D4E80;line-height:1.6")}>
              <b>บทบาท {form.role}</b> จะได้สิทธิ์: {(roleOf(form.role)?.grants ?? []).map((g) => CAPABILITY_TH[g] ?? g).join(" · ") || "—"}
            </div>
            <div style={css("margin-top:11px")}>
              <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:6px")}>
                เขาจะเข้าระบบด้วยวิธีไหน
              </div>
              <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
                {SIGN_IN_WAYS.map((way) => {
                  const on = form.signIn === way.id;
                  return (
                    <button key={way.id} onClick={() => setForm({ ...form, signIn: way.id })}
                      style={css("flex:1;min-width:210px;text-align:left;padding:9px 11px;border:1px solid " +
                        (on ? "#0A2240" : "#D3DBE3") + ";background:" + (on ? "#0A2240" : "#fff") +
                        ";color:" + (on ? "#fff" : "#0F2B46") + ";border-radius:5px;cursor:pointer")}>
                      <div style={css("font-size:12.5px;font-weight:650;margin-bottom:2px")}>{way.label}</div>
                      <div style={css("font-size:11.5px;line-height:1.55;color:" + (on ? "#C8D6E5" : "#7B8CA0"))}>
                        {way.detail}
                      </div>
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Said before the form is filled in, not after it fails. Without
                directory permission the API can still write the row, and that
                row is exactly the half that looks like success. */}
            {form.signIn !== "none" && dir.signIn && !dir.signIn.ready && (
              <div style={css("margin-top:9px;padding:10px 12px;background:#FFF8F0;border:1px solid #F0D8B8;border-left:3px solid #B45309;border-radius:5px;font-size:12px;color:#8A5A12;line-height:1.6")}>
                <b>ตอนนี้ยังสร้างบัญชีลงชื่อเข้าใช้ไม่ได้</b> — {dir.signIn.why}
                <br />
                เพิ่มผู้ใช้ตอนนี้จะได้แค่แถวในทะเบียน ซึ่งยังล็อกอินไม่ได้ จนกว่าจะให้สิทธิ์นั้นก่อน
              </div>
            )}
          </div>
        )}

        <ZoomBox>
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
                          {/* Offered for everyone, because an administrator
                              cannot tell a guest from a member by looking at the
                              row — the API answers with the reason when it is a
                              guest, which is more use than a button that is not
                              there. */}
                          {/* Only on a closed account, and only then: the API
                              still refuses if the person owns any job, and says
                              how many rather than just refusing. */}
                          {!person.active && (
                            <Mini label="ลบ" tone="#B42318" busy={busy}
                              onClick={() => void remove(person)} />
                          )}
                          {/* Offered whenever the row has an email. Whether an
                              invitation is the right thing for this account is
                              the API's judgement — it refuses a directory
                              account with the reason, which is more use than a
                              button that is quietly missing. */}
                          {!!person.email && (
                            <Mini label="ส่งคำเชิญ" tone="#1D4E80" busy={busy}
                              onClick={async () => {
                                if (!window.confirm(`ส่งอีเมลคำเชิญเข้าใช้งานระบบไปที่
${person.email}?`)) return;
                                await create2(`/${person.id}/invite`);
                              }} />
                          )}
                          <Mini label="รหัสใหม่" tone="#B45309" busy={busy}
                            onClick={async () => {
                              if (!window.confirm(`ออกรหัสผ่านชั่วคราวใหม่ให้ ${person.name}?

รหัสเดิมจะใช้ไม่ได้ทันที และเขาต้องตั้งรหัสใหม่เมื่อเข้าครั้งถัดไป`)) return;
                              const reply = await create2(`/${person.id}/reset-password`);
                              if (reply?.tempPassword) {
                                setIssued({ name: person.name, signIn: reply.message ?? "", password: reply.tempPassword });
                              }
                            }} />
                        </span>
                      ))}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </ZoomBox>

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
        <ZoomBox>
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
        </ZoomBox>
        <div style={css("padding:11px 16px;border-top:1px solid #E9EFF5;font-size:11.5px;color:#94A3B8;line-height:1.6")}>
          บทบาทที่ระบบไม่รู้จักจะได้สิทธิ์เท่า Viewer ไม่ใช่ค่าเริ่มต้น — พิมพ์บทบาทผิดควรทำให้เสียสิทธิ์ตัวเอง ไม่ใช่ได้สิทธิ์คนอื่น
        </div>
      </div>

      {dir?.canManage && (
        <ClearJobs people={dir.people} jobs={jobs} me={me} onToast={onToast} onDone={load} />
      )}
    </div>
  );
}

/**
 * Removing the work one account holds.
 *
 * There is no history table behind the register. What this deletes is not
 * recoverable from inside the system, so the screen is built to make an
 * administrator slow down rather than to be quick: the person is chosen by name,
 * the count they hold is shown before the decision, the scope is stated in
 * words, and the name has to be typed back before the button will do anything.
 *
 * The API refuses this to anyone without the capability regardless of what is
 * on screen — the two halves are deliberate, because a hidden button is not a
 * permission.
 */
function ClearJobs({ people, jobs, me, onToast, onDone }: {
  people: Person[];
  jobs: Job[];
  me: string;
  onToast: (m: string) => void;
  onDone: () => void;
}) {
  const [who, setWho] = useState("");
  const [scope, setScope] = useState("ALL");
  const [reason, setReason] = useState("");
  const [typed, setTyped] = useState("");
  const [busy, setBusy] = useState(false);

  const person = people.find((p) => p.id === who) ?? null;

  /**
   * The months this person actually has work in, counted off the register
   * rather than offered as a list of every month — an administrator should not
   * be able to pick a month that would delete nothing and be told it worked.
   */
  const months = useMemo(() => {
    if (!person) return [] as [string, number][];
    const tally = new Map<string, number>();
    jobs.forEach((job) => {
      if (job.opId !== person.id) return;
      const date = (job.date ?? "").trim();
      if (date.length < 10) return;
      const key = date.slice(3);
      tally.set(key, (tally.get(key) ?? 0) + 1);
    });
    return [...tally.entries()].sort((a, b) => {
      const [am, ay] = a[0].split("/");
      const [bm, by] = b[0].split("/");
      return (ay + am).localeCompare(by + bm);
    });
  }, [jobs, person]);

  const going = scope === "ALL"
    ? person?.jobs ?? 0
    : months.find(([key]) => key === scope)?.[1] ?? 0;
  const armed = !!person && typed.trim() === person.name && !busy && going > 0;

  function pick(id: string) {
    setWho(id);
    setScope("ALL");
    setTyped("");
  }

  async function run() {
    if (!person || !armed) return;
    setBusy(true);
    const result = await clearOwnerJobs(person.id, scope === "ALL" ? "" : scope, me, reason.trim());
    setBusy(false);
    if (!result.ok) { onToast("ล้างข้อมูลไม่สำเร็จ — " + result.message); return; }
    onToast(`ล้างงานของ ${person.name} แล้ว ${result.removed} รายการ`);
    setWho("");
    setTyped("");
    setReason("");
    onDone();
  }

  return (
    <div style={css("background:#fff;border:1px solid #F0D2D2;border-radius:6px;overflow:hidden")}>
      <div style={css("padding:12px 16px;border-bottom:1px solid #F5E2E2;background:#FDF7F7")}>
        <div style={css("font-size:13px;font-weight:600;color:#8C2F2F")}>ล้างข้อมูลงานของผู้ใช้</div>
        <div style={css("font-size:11.5px;color:#A46A6A;margin-top:3px")}>
          ลบถาวร — ทะเบียนงานไม่มีตารางประวัติ ข้อมูลที่ลบแล้วกู้คืนจากในระบบไม่ได้
        </div>
      </div>

      <div style={css("padding:14px 16px;display:flex;flex-direction:column;gap:13px")}>
        <div style={css("display:flex;gap:12px;flex-wrap:wrap")}>
          <Field label="ผู้ใช้" width="230px">
            <select value={who} onChange={(e) => pick(e.target.value)} style={SELECT}>
              <option value="">— เลือกผู้ใช้ —</option>
              {people.filter((p) => p.jobs > 0).map((p) => (
                <option key={p.id} value={p.id}>{p.name} · {p.jobs} งาน</option>
              ))}
            </select>
          </Field>

          <Field label="ขอบเขต" width="230px">
            <select value={scope} onChange={(e) => { setScope(e.target.value); setTyped(""); }}
              style={SELECT} disabled={!person}>
              <option value="ALL">ทั้งหมด{person ? ` · ${person.jobs} งาน` : ""}</option>
              {months.map(([key, held]) => (
                <option key={key} value={key}>เดือน {key} · {held} งาน</option>
              ))}
            </select>
          </Field>

          <Field label="เหตุผล (บันทึกลง audit)" width="260px">
            <input value={reason} onChange={(e) => setReason(e.target.value)} style={INPUT}
              placeholder="เช่น อัปโหลดผิดไฟล์" />
          </Field>
        </div>

        {person && (
          <div style={css("display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end")}>
            <Field label={`พิมพ์ "${person.name}" เพื่อยืนยัน`} width="260px">
              <input value={typed} onChange={(e) => setTyped(e.target.value)} style={INPUT} />
            </Field>
            <button onClick={run} disabled={!armed}
              style={css("height:30px;padding:0 16px;border-radius:4px;font-size:12.5px;font-weight:600;font-family:inherit;border:1px solid "
                + (armed ? "#B3261E;background:#B3261E;color:#fff;cursor:pointer" : "#E0C6C6;background:#F6EDED;color:#C39A9A;cursor:not-allowed"))}>
              {busy ? "กำลังลบ…" : `ลบ ${going} งาน`}
            </button>
            <span style={css("font-size:11.5px;color:#8C2F2F;padding-bottom:7px")}>
              {going === 0
                ? "ขอบเขตนี้ไม่มีงานให้ลบ"
                : scope === "ALL"
                  ? `จะลบงานทั้งหมดของ ${person.name}`
                  : `จะลบเฉพาะงานเดือน ${scope} ของ ${person.name}`}
            </span>
          </div>
        )}
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
