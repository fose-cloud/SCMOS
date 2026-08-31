"use client";

import { useCallback, useEffect, useState, type ChangeEvent, type DragEvent } from "react";
import { apiFetch } from "../api";
import { badge, css } from "../theme";
import type { Alert, WsTarget } from "../alerts";
import type { Account } from "../nav";
import { PER_PAGE_OPTIONS, type Prefs, type Profile } from "../settings";

/* ----------------------------------------------------------------- toast */

export function Toast({ message }: { message: string }) {
  if (!message) return null;
  return (
    <div style={css("position:fixed;bottom:24px;right:24px;z-index:70;background:#0A2240;color:#fff;padding:13px 18px;border-radius:5px;box-shadow:0 12px 32px rgba(7,26,49,.28);display:flex;align-items:center;gap:12px;animation:tin .18s ease")}>
      <span style={css("width:8px;height:8px;border-radius:50%;background:#3CB371")} />
      <span style={css("font-size:13px")}>{message}</span>
    </div>
  );
}

/* --------------------------------------------------------- notifications */

export function Notifications(p: {
  alerts: Alert[];
  scope: number;
  onOpen: (target: WsTarget) => void;
  onClose: () => void;
}) {
  const critical = p.alerts.filter((a) => a.level === "Critical").length;

  return (
    <aside style={css("position:fixed;top:60px;right:0;bottom:0;width:400px;background:#fff;border-left:1px solid #D8E0E8;box-shadow:-8px 0 28px rgba(10,34,64,.10);z-index:50;display:flex;flex-direction:column;animation:tin .18s ease")}>
      <div style={css("padding:15px 18px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;background:#0A2240")}>
        <div>
          <div style={css("font-size:13.5px;font-weight:600;color:#fff")}>Alert &amp; Notification Center</div>
          <div style={css("font-size:10.5px;color:#7FA5CC")}>
            ศูนย์แจ้งเตือน · {p.alerts.length} รายการ{critical ? " · วิกฤต " + critical : ""} · จากงานจริง {p.scope} งาน
          </div>
        </div>
        <button onClick={p.onClose} aria-label="Close alerts" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer;font-size:14px")}>✕</button>
      </div>
      <div style={css("flex:1;overflow-y:auto;padding:12px 14px;display:flex;flex-direction:column;gap:9px")}>
        {p.alerts.map((a) => (
          <button
            key={a.id}
            type="button"
            onClick={() => p.onOpen(a.target)}
            style={css(
              "font-family:inherit;text-align:left;width:100%;cursor:pointer;border:1px solid #E9EFF5;border-left:3px solid " +
              (a.level === "Critical" ? "#B42318" : a.level === "Warning" ? "#D89614" : "#2E7DD1") +
              ";border-radius:4px;padding:11px 13px;background:#FBFCFD",
            )}
          >
            <div style={css("display:flex;justify-content:space-between;align-items:center;gap:8px")}>
              <span style={css(badge(a.level, a.level === "Critical" ? "red" : a.level === "Warning" ? "amber" : "blue"))}>{a.level}</span>
              <span style={css(
                "font-size:16px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:" +
                (a.level === "Critical" ? "#B42318" : a.level === "Warning" ? "#B45309" : "#1D5FA8"),
              )}>
                {a.count}
              </span>
            </div>
            <div style={css("font-size:12.5px;font-weight:600;color:#0A2240;margin-top:7px")}>{a.title}</div>
            <div style={css("font-size:11.5px;color:#334155;margin-top:2px")}>{a.th}</div>
            <div style={css("font-size:11.5px;color:#64748B;margin-top:4px;line-height:1.45;text-wrap:pretty")}>{a.body}</div>
            <div style={css("font-size:10.5px;color:#2E7DD1;margin-top:7px;font-weight:600")}>เปิดงานชุดนี้ใน Workspace →</div>
          </button>
        ))}
        {!p.alerts.length && (
          <div style={css("border:1px solid #E3F4EB;background:#F4FBF7;border-radius:4px;padding:14px;font-size:12.5px;color:#16794C")}>
            ไม่มีเรื่องต้องแจ้งเตือนในแผนนี้ ✓
          </div>
        )}
      </div>
    </aside>
  );
}

/* --------------------------------------------------------------- profile */

export function ProfileMenu(p: {
  me: Account;
  profile: Profile;
  stats: { total: number; open: number; running: number; delayed: number; action: number; format: number };
  onOpen: (target: WsTarget) => void;
  onSettings: () => void;
  onLogout: () => void;
  onClose: () => void;
}) {
  const rows: [string, string, string][] = [
    ["Username", "ชื่อผู้ใช้", p.me.user],
    ["Full name", "ชื่อ-สกุล", p.profile.full || p.me.full],
    ["Employee ID", "รหัสพนักงาน", p.me.id],
    ["Role", "บทบาท", p.me.role],
    ["Email", "อีเมล", p.profile.email || "—"],
    ["Phone", "เบอร์โทร", p.profile.phone || "—"],
    ["Edit rights", "สิทธิ์แก้ไข", p.me.role === "Operation User" ? "เฉพาะงานที่ตัวเองรับผิดชอบ" : "แก้ไขได้ทุกงานในทีม"],
  ];
  const stats: [string, string, number, string][] = [
    ["MY JOBS", "งานของฉัน", p.stats.total, "#2E7DD1"],
    ["OPEN", "ยังไม่ปิด", p.stats.open, "#475569"],
    ["RUNNING", "กำลังทำ", p.stats.running, "#0A6E8A"],
    ["DELAYED", "ล่าช้า", p.stats.delayed, "#B42318"],
    ["ACTION", "ต้องจัดการ", p.stats.action, "#B45309"],
    ["FORMAT", "รูปแบบผิด", p.stats.format, "#B42318"],
  ];

  return (
    <>
      <button
        aria-label="Close profile"
        onClick={p.onClose}
        style={css("position:fixed;inset:0;z-index:54;border:none;background:transparent;cursor:default")}
      />
      <aside style={css("position:fixed;top:64px;right:16px;width:340px;background:#fff;border:1px solid #D8E0E8;border-radius:6px;box-shadow:0 18px 44px rgba(10,34,64,.22);z-index:55;overflow:hidden;animation:tin .16s ease")}>
        <div style={css("background:#0A2240;padding:15px 17px;display:flex;align-items:center;gap:12px")}>
          {p.profile.avatar ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={p.profile.avatar} alt={p.me.name} style={css("width:42px;height:42px;border-radius:5px;object-fit:cover;display:block;flex:none")} />
          ) : (
            <span style={css("width:42px;height:42px;border-radius:5px;background:#2E7DD1;color:#fff;display:flex;align-items:center;justify-content:center;font-size:15px;font-weight:600;flex:none")}>
              {(p.profile.init || p.me.init).toUpperCase()}
            </span>
          )}
          <span style={css("display:flex;flex-direction:column;line-height:1.3;min-width:0")}>
            <span style={css("font-size:14px;font-weight:600;color:#fff")}>{p.profile.full || p.me.name}</span>
            <span style={css("font-size:11px;color:#7FA5CC")}>{p.me.role} · {p.me.id}</span>
          </span>
          <span style={css("margin-left:auto;display:flex;align-items:center;gap:6px;font-size:10.5px;color:#8BE0A4")}>
            <span style={css("width:7px;height:7px;border-radius:50%;background:#3CB371")} />
            ONLINE
          </span>
        </div>

        <div style={css("padding:13px 16px;display:flex;flex-direction:column;gap:12px")}>
          <div style={css("display:grid;grid-template-columns:repeat(3,1fr);gap:8px")}>
            {stats.map(([label, th, value, colour]) => (
              <div key={label} style={css("border:1px solid #E9EFF5;border-radius:4px;padding:8px 9px")}>
                <div style={css("font-size:9.5px;color:#8496A8;letter-spacing:.05em;font-weight:600")}>{label}</div>
                <div style={css("font-size:9.5px;color:#94A3B8")}>{th}</div>
                <div style={css("font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;margin-top:3px;color:" + colour)}>{value}</div>
              </div>
            ))}
          </div>

          <div style={css("border:1px solid #E9EFF5;border-radius:4px;overflow:hidden")}>
            {rows.map(([label, th, value], i) => (
              <div
                key={label}
                style={css("display:flex;gap:10px;padding:8px 11px;font-size:11.5px;" + (i ? "border-top:1px solid #F1F5F9;" : ""))}
              >
                <span style={css("flex:none;width:118px;color:#64748B")}>{label} <span style={css("color:#94A3B8")}>· {th}</span></span>
                <span style={css("flex:1;min-width:0;color:#0A2240;font-weight:600;word-break:break-word")}>{value}</span>
              </div>
            ))}
          </div>

          <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
            <button
              onClick={() => p.onOpen({ tab: "MY JOBS" })}
              style={css("flex:1;min-width:130px;height:34px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
            >
              เปิดงานของฉัน
            </button>
            <button
              onClick={() => p.onOpen({ tab: "MY JOBS", kpi: "Act" })}
              style={css("flex:1;min-width:130px;height:34px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12px;cursor:pointer")}
            >
              งานที่ต้องจัดการ
            </button>
          </div>

          <div style={css("display:flex;gap:8px;border-top:1px solid #F1F5F9;padding-top:11px")}>
            <button
              onClick={p.onSettings}
              style={css("flex:1;height:32px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12px;cursor:pointer")}
            >
              ⚙ ตั้งค่า
            </button>
            <button
              onClick={p.onLogout}
              style={css("flex:1;height:32px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:12px;cursor:pointer")}
            >
              ออกจากระบบ
            </button>
          </div>
        </div>
      </aside>
    </>
  );
}

/* -------------------------------------------------------------- settings */

/**
 * Whether this sign-in belongs to somebody invited from outside.
 *
 * A guest's name carries `#EXT#`, or is a plain address on a domain the tenant
 * does not own; their password lives with their own Microsoft account. A member
 * of the organisation changes theirs on the organisation's page. Sending either
 * one to the other's address produces "we could not find an account with that
 * username", which reads as the account being broken.
 */
function guestAccount(user: string): boolean {
  const name = (user || "").toLowerCase();
  return name.includes("#ext#") || !name.endsWith(".onmicrosoft.com");
}

export function SettingsModal(p: {
  me: Account;
  profile: Profile;
  onProfile: (profile: Profile) => void;
  onAvatar: (file: File | undefined) => void;
  prefs: Prefs;
  onChange: (prefs: Prefs) => void;
  /** Supervisors and above may reload the whole plan from the delivered file. */
  canReload: boolean;
  onToast: (message: string) => void;
  onReloadPlan: () => void;
  onCleanup: () => void;
  onDuplicates: () => void;
  onClose: () => void;
}) {
  const chip = (active: boolean) =>
    "height:32px;padding:0 14px;border:1px solid " + (active ? "#0A2240" : "#D8E0E8") +
    ";background:" + (active ? "#0A2240" : "#fff") + ";color:" + (active ? "#fff" : "#475569") +
    ";border-radius:4px;font-size:12px;cursor:pointer;font-weight:" + (active ? "600" : "400");

  const field = "height:34px;width:100%;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12.5px;padding:0 10px;outline:none;font-family:inherit";
  const shownName = p.profile.full || p.me.full;
  const shownInit = (p.profile.init || p.me.init).toUpperCase();
  // Same rule the data standard applies to driver numbers, reused as a hint.
  const phoneOff = !!p.profile.phone && !/^0\d{1,2}-\d{7,8}$/.test(p.profile.phone);
  const emailOff = !!p.profile.email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(p.profile.email);

  const edits: [string, string, keyof Profile, string, string][] = [
    ["ชื่อที่แสดง", "Display name", "full", shownName, "ชื่อที่ปรากฏบนหัวจอและบันทึกการแก้ไข"],
    ["อักษรย่อ", "Initials", "init", shownInit, "ใช้เมื่อยังไม่ได้อัปโหลดรูป · 2–3 ตัวอักษร"],
    ["อีเมล", "Email", "email", p.profile.email, "สำหรับให้ทีมติดต่อกลับ"],
    ["เบอร์โทร", "Phone", "phone", p.profile.phone, "รูปแบบ 0XX-XXXXXXX"],
  ];

  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:66;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:600px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:15px 20px;background:#0A2240;display:flex;justify-content:space-between;align-items:center")}>
          <div>
            <div style={css("font-size:14px;font-weight:600;color:#fff")}>Settings · ตั้งค่า</div>
            <div style={css("font-size:11px;color:#7FA5CC")}>ข้อมูลส่วนตัวและการตั้งค่าการใช้งาน · เก็บไว้ในเครื่องนี้</div>
          </div>
          <button onClick={p.onClose} aria-label="Close settings" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>

        <div style={css("padding:18px 20px;display:flex;flex-direction:column;gap:18px")}>
          <div>
            <div style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em;margin-bottom:9px")}>รูปโปรไฟล์ · PHOTO</div>
            <div style={css("display:flex;align-items:center;gap:16px;flex-wrap:wrap")}>
              {p.profile.avatar ? (
                // eslint-disable-next-line @next/next/no-img-element
                <img src={p.profile.avatar} alt={shownName} style={css("width:64px;height:64px;border-radius:6px;object-fit:cover;border:1px solid #D8E0E8;display:block")} />
              ) : (
                <span style={css("width:64px;height:64px;border-radius:6px;background:#2E7DD1;color:#fff;display:flex;align-items:center;justify-content:center;font-size:20px;font-weight:600")}>
                  {shownInit}
                </span>
              )}
              <div style={css("display:flex;flex-direction:column;gap:7px")}>
                <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
                  <label style={css("height:32px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;display:flex;align-items:center")}>
                    อัปโหลดรูป
                    <input
                      type="file"
                      accept="image/*"
                      onChange={(e: ChangeEvent<HTMLInputElement>) => { p.onAvatar(e.target.files?.[0]); e.target.value = ""; }}
                      style={{ display: "none" }}
                    />
                  </label>
                  {!!p.profile.avatar && (
                    <button
                      onClick={() => p.onProfile({ ...p.profile, avatar: "" })}
                      style={css("height:32px;padding:0 14px;border:1px solid #F3C3BE;background:#FDF6F5;color:#B42318;border-radius:4px;font-size:12px;cursor:pointer")}
                    >
                      ลบรูป
                    </button>
                  )}
                </div>
                <span style={css("font-size:11px;color:#94A3B8")}>
                  JPG / PNG ไม่เกิน 8 MB · ระบบย่อเป็นสี่เหลี่ยมจัตุรัส 160px ให้อัตโนมัติ
                </span>
              </div>
            </div>
          </div>

          <div>
            <div style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em;margin-bottom:9px")}>ข้อมูลส่วนตัว · PROFILE</div>
            <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px")}>
              {edits.map(([label, en, key, value, hint]) => (
                <label key={key} style={css("display:flex;flex-direction:column;gap:4px")}>
                  <span style={css("font-size:11.5px;color:#0A2240;font-weight:600")}>{label} <span style={css("color:#94A3B8;font-weight:400")}>· {en}</span></span>
                  <input
                    value={value}
                    maxLength={key === "init" ? 3 : 80}
                    onChange={(e) => p.onProfile({
                      ...p.profile,
                      [key]: key === "init" ? e.target.value.toUpperCase() : e.target.value,
                    })}
                    style={css(field)}
                  />
                  <span style={css(
                    "font-size:10.5px;color:" +
                    ((key === "phone" && phoneOff) || (key === "email" && emailOff) ? "#B45309" : "#94A3B8"),
                  )}>
                    {key === "phone" && phoneOff ? "รูปแบบยังไม่ตรงมาตรฐาน (0XX-XXXXXXX) — บันทึกไว้แล้ว แก้ทีหลังได้"
                      : key === "email" && emailOff ? "อีเมลยังไม่ครบรูปแบบ — บันทึกไว้แล้ว แก้ทีหลังได้"
                        : hint}
                  </span>
                </label>
              ))}
            </div>

            <div style={css("border:1px solid #E9EFF5;border-radius:4px;overflow:hidden;margin-top:12px")}>
              {([
                ["ชื่อผู้ใช้ · Username", p.me.user],
                ["ชื่อในระบบงาน · Owner key", p.me.name],
                ["รหัสพนักงาน · Employee ID", p.me.id],
                ["บทบาท · Role", p.me.role],
              ] as [string, string][]).map(([label, value], i) => (
                <div key={label} style={css("display:flex;padding:9px 12px;font-size:12px;background:#F8FAFC;" + (i ? "border-top:1px solid #F1F5F9;" : ""))}>
                  <span style={css("width:190px;flex:none;color:#64748B")}>{label}</span>
                  <span style={css("color:#0A2240;font-weight:600")}>{value}</span>
                  <span style={css("margin-left:auto;font-size:10.5px;color:#94A3B8")}>อ่านอย่างเดียว</span>
                </div>
              ))}
            </div>
            <div style={css("font-size:11px;color:#94A3B8;margin-top:6px;line-height:1.5")}>
              สี่รายการนี้แก้ที่นี่ไม่ได้ — “ชื่อในระบบงาน” คือกุญแจที่ใช้จับคู่ว่างานไหนเป็นของใคร ({p.me.name} = <code>job.op</code>) ถ้าแก้จะทำให้งานของคุณหลุดทั้งหมด ส่วนชื่อผู้ใช้ รหัสพนักงาน และบทบาท เป็นเรื่องของสิทธิ์ ต้องแก้ที่หน้า Administration
            </div>
          </div>

          <div>
            <div style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em;margin-bottom:9px")}>การตั้งค่า · PREFERENCES</div>
            <div style={css("display:flex;flex-direction:column;gap:14px")}>
              <div>
                <div style={css("font-size:12px;color:#0A2240;font-weight:600")}>หน้าเริ่มต้นหลังเข้าสู่ระบบ</div>
                <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>Landing screen after sign-in</div>
                <div style={css("display:flex;gap:8px")}>
                  <button onClick={() => p.onChange({ ...p.prefs, landing: "myjob" })} style={css(chip(p.prefs.landing === "myjob"))}>Operation Workspace</button>
                  <button onClick={() => p.onChange({ ...p.prefs, landing: "dashboard" })} style={css(chip(p.prefs.landing === "dashboard"))}>Dashboard</button>
                </div>
              </div>

              {/*
                Changing a password is Microsoft's job, not this application's.
                SCMOS never sees one — the platform authenticates people and
                hands over a verified identity, which is why there is no field
                here to type a new password into. What this section can do, and
                what somebody looking for "change my password" actually needs,
                is to send them to the right page for the kind of account they
                signed in with, because the two live at different addresses and
                the wrong one simply says the account does not exist.
              */}
              {/*
                Handing your work to a colleague while you are away.

                Both dates are required and there is no "until further notice".
                A grant with no end is a permanent change of who works whose
                jobs wearing a holiday's clothes, and the person who set it will
                not remember to take it off. It expires by comparing its dates
                to today, so nothing has to run overnight for it to end.
              */}
              <div>
                <div style={css("font-size:12px;color:#0A2240;font-weight:600")}>มอบสิทธิ์แก้ไขงานของฉัน</div>
                <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
                  สำหรับช่วงลา — คนที่รับมอบจะแก้งานของคุณได้เฉพาะในช่วงวันที่กำหนด
                </div>
                <Delegations me={p.me} onToast={p.onToast} />
              </div>

              <div>
                <div style={css("font-size:12px;color:#0A2240;font-weight:600")}>รหัสผ่านและความปลอดภัย</div>
                <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>
                  จัดการโดย Microsoft — SCMOS ไม่เก็บรหัสผ่านของใครเลย
                </div>

                <div style={css("background:#F8FAFC;border:1px solid #E3E8EE;border-radius:5px;padding:11px 13px")}>
                  <div style={css("font-size:11.5px;color:#5A6B7D;line-height:1.7")}>
                    บัญชีที่ใช้อยู่: <b style={css("color:#0F2B46")}>{p.me.user || p.me.full}</b>
                    <br />
                    {guestAccount(p.me.user)
                      ? "บัญชีนี้เป็นบัญชีภายนอกที่ได้รับเชิญเข้ามา รหัสผ่านเป็นของบัญชี Microsoft ส่วนตัวของคุณ"
                      : "บัญชีนี้เป็นบัญชีขององค์กร เปลี่ยนรหัสผ่านได้ที่หน้าของ Microsoft"}
                  </div>

                  <div style={css("display:flex;gap:8px;margin-top:10px;flex-wrap:wrap")}>
                    <a href={guestAccount(p.me.user)
                        ? "https://account.live.com/password/change"
                        : "https://account.activedirectory.windowsazure.com/ChangePassword.aspx"}
                      target="_blank" rel="noreferrer"
                      style={css("display:inline-flex;align-items:center;height:32px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;font-weight:600;text-decoration:none")}>
                      เปลี่ยนรหัสผ่าน
                    </a>
                    <a href="https://mysignins.microsoft.com/security-info"
                      target="_blank" rel="noreferrer"
                      style={css("display:inline-flex;align-items:center;height:32px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12px;font-weight:600;text-decoration:none")}>
                      ตั้งค่าความปลอดภัย
                    </a>
                  </div>

                  {guestAccount(p.me.user) && (
                    <div style={css("margin-top:10px;font-size:11px;color:#8A5A12;background:#FFF8F0;border:1px solid #F0D8B8;border-radius:4px;padding:8px 10px;line-height:1.65")}>
                      ถ้าทุกครั้งที่เข้าระบบมีรหัสส่งไปที่อีเมล แปลว่าอีเมลนี้ยังไม่มีบัญชี Microsoft
                      สมัครฟรีที่ <b>signup.live.com</b> ด้วยอีเมลเดิม ตั้งรหัสที่ต้องการครั้งเดียว
                      แล้วจะไม่มีรหัสส่งเข้าอีเมลอีก — ไม่ต้องเปลี่ยนอีเมลและไม่ต้องรับคำเชิญใหม่
                    </div>
                  )}
                </div>
              </div>

              <div>
                <div style={css("font-size:12px;color:#0A2240;font-weight:600")}>จำนวนแถวต่อหน้า</div>
                <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>Rows per page · ใช้กับทุกตารางในระบบ</div>
                <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
                  {PER_PAGE_OPTIONS.map((n) => (
                    <button key={n} onClick={() => p.onChange({ ...p.prefs, perPage: n })} style={css(chip(p.prefs.perPage === n))}>{n}</button>
                  ))}
                </div>
              </div>

              <div>
                <div style={css("font-size:12px;color:#0A2240;font-weight:600")}>เมนูด้านซ้าย</div>
                <div style={css("font-size:11px;color:#94A3B8;margin-bottom:7px")}>เปิดแอปแบบย่อเมนูไว้ เพื่อให้ตารางกว้างขึ้น</div>
                <div style={css("display:flex;gap:8px")}>
                  <button onClick={() => p.onChange({ ...p.prefs, collapsed: false })} style={css(chip(!p.prefs.collapsed))}>แสดงเต็ม</button>
                  <button onClick={() => p.onChange({ ...p.prefs, collapsed: true })} style={css(chip(p.prefs.collapsed))}>ย่อเมนู</button>
                </div>
              </div>
            </div>
          </div>

          {p.canReload && (
            <div>
              <div style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.06em;margin-bottom:9px")}>ข้อมูล · DATA</div>

              <div style={css("border:1px solid #E9EFF5;border-radius:4px;padding:12px 13px;display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:10px")}>
                <span style={css("flex:1;min-width:220px;font-size:11.5px;color:#64748B;line-height:1.5")}>
                  <b style={css("color:#0A2240")}>ล้างข้อมูลย้อนหลัง</b><br />
                  จัดรูปแบบวันที่ · แปลงสถานะภาษาไทยเข้าลำดับงาน · ย้ายข้อความ “รับตู้ …” ออกจากช่องเวลานัดโหลด แล้วบันทึกทุกการแก้ลงประวัติของงาน
                </span>
                <button
                  onClick={p.onCleanup}
                  style={css("height:32px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
                >
                  ล้างข้อมูล
                </button>
              </div>

              <div style={css("border:1px solid #E9EFF5;border-radius:4px;padding:12px 13px;display:flex;align-items:center;gap:12px;flex-wrap:wrap;margin-bottom:10px")}>
                <span style={css("flex:1;min-width:220px;font-size:11.5px;color:#64748B;line-height:1.5")}>
                  <b style={css("color:#0A2240")}>รายงานงานซ้ำ</b><br />
                  หางานที่เป็นเที่ยวเดียวกันแต่ถูกคีย์เข้ามาหลายรอบ · เลือกรวมได้ทีละกลุ่ม
                </span>
                <button
                  onClick={p.onDuplicates}
                  style={css("height:32px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
                >
                  ดูรายงาน
                </button>
              </div>

              <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:4px;padding:12px 13px;display:flex;align-items:center;gap:12px;flex-wrap:wrap")}>
                <span style={css("flex:1;min-width:220px;font-size:11.5px;color:#64748B;line-height:1.5")}>
                  <b style={css("color:#B42318")}>โหลดแผนใหม่จากไฟล์ ops.json</b><br />
                  ลบงานทั้งหมดในฐานข้อมูลแล้วแทนที่ด้วยข้อมูลในไฟล์ · ใช้ตอนได้ไฟล์แผนฉบับสมบูรณ์มาใหม่
                </span>
                <button
                  onClick={p.onReloadPlan}
                  style={css("height:32px;padding:0 14px;border:1px solid #B42318;background:#fff;color:#B42318;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
                >
                  โหลดแผนใหม่
                </button>
              </div>
            </div>
          )}
        </div>

        <div style={css("padding:13px 20px;border-top:1px solid #E9EFF5;background:#FBFCFD;display:flex;justify-content:space-between;align-items:center;gap:12px")}>
          <span style={css("font-size:11.5px;color:#64748B")}>บันทึกอัตโนมัติเมื่อเลือก · มีผลทันที</span>
          <button onClick={p.onClose} style={css("height:34px;padding:0 20px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}>
            เสร็จสิ้น
          </button>
        </div>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------ field model */

export type Field = {
  label: string;
  kind: "select" | "text";
  value: string;
  options?: string[];
  ph?: string;
  onChange: (value: string) => void;
};

function FieldControl({ f, style }: { f: Field; style: string }) {
  return f.kind === "select" ? (
    <select value={f.value} onChange={(e) => f.onChange(e.target.value)} style={css(style)}>
      {(f.options || []).map((o) => <option key={o} value={o}>{o}</option>)}
    </select>
  ) : (
    <input value={f.value} onChange={(e) => f.onChange(e.target.value)} placeholder={f.ph} style={css(style)} />
  );
}

const MODAL_INPUT = "height:36px;border:1px solid #D8E0E8;border-radius:4px;background:#F8FAFC;font-size:13px;padding:0 10px;outline:none";

/* ------------------------------------------------------ new shipment modal */

export function NewShipmentModal({ fields, onClose, onSave }: { fields: Field[]; onClose: () => void; onSave: () => void }) {
  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:60;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:760px;max-width:100%;max-height:100%;overflow:auto;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:16px 22px;background:#0A2240;display:flex;justify-content:space-between;align-items:center;border-radius:6px 6px 0 0")}>
          <div>
            <div style={css("font-size:14.5px;font-weight:600;color:#fff")}>New Shipment</div>
            <div style={css("font-size:11px;color:#7FA5CC")}>สร้างงานขนส่งใหม่</div>
          </div>
          <button onClick={onClose} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer")}>✕</button>
        </div>
        <div style={css("padding:20px 22px;display:grid;grid-template-columns:1fr 1fr;gap:14px")}>
          {fields.map((f) => (
            <label key={f.label} style={css("display:flex;flex-direction:column;gap:5px")}>
              <span style={css("font-size:11px;font-weight:600;color:#475569;letter-spacing:.02em")}>{f.label}</span>
              <FieldControl f={f} style={MODAL_INPUT} />
            </label>
          ))}
        </div>
        <div style={css("padding:14px 22px;border-top:1px solid #E9EFF5;display:flex;justify-content:flex-end;gap:9px;background:#FBFCFD;border-radius:0 0 6px 6px")}>
          <button className="ghost-btn" onClick={onClose} style={css("height:36px;padding:0 18px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:13px;color:#475569;cursor:pointer")}>Cancel</button>
          <button className="dark-btn" onClick={onSave} style={css("height:36px;padding:0 22px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:13px;font-weight:500;cursor:pointer")}>Save Shipment</button>
        </div>
      </div>
    </div>
  );
}

/* -------------------------------------------------------- delay capture */

export function DelayModal({ reference, fields, onCancel, onSave }: {
  reference: string; fields: Field[]; onCancel: () => void; onSave: () => void;
}) {
  return (
    <div style={css("position:fixed;inset:0;background:rgba(7,26,49,.48);z-index:62;display:flex;align-items:center;justify-content:center;padding:40px")}>
      <div style={css("background:#fff;border-radius:6px;width:660px;max-width:100%;box-shadow:0 24px 60px rgba(7,26,49,.3);animation:tin .16s ease")}>
        <div style={css("padding:16px 22px;background:#B42318;display:flex;justify-content:space-between;align-items:center;border-radius:6px 6px 0 0")}>
          <div>
            <div style={css("font-size:14.5px;font-weight:600;color:#fff")}>Record Delay — reason is mandatory</div>
            <div style={css("font-size:11px;color:#F3C3BE")}>บันทึกสาเหตุความล่าช้า · {reference}</div>
          </div>
          <button onClick={onCancel} aria-label="Close" style={css("width:28px;height:28px;border:1px solid #D96C63;background:transparent;color:#fff;border-radius:4px;cursor:pointer")}>✕</button>
        </div>
        <div style={css("padding:20px 22px;display:grid;grid-template-columns:1fr 1fr;gap:14px")}>
          {fields.map((f) => (
            <label key={f.label} style={css("display:flex;flex-direction:column;gap:5px")}>
              <span style={css("font-size:11px;font-weight:600;color:#475569;letter-spacing:.02em")}>{f.label}</span>
              <FieldControl f={f} style={MODAL_INPUT} />
            </label>
          ))}
        </div>
        <div style={css("padding:14px 22px;border-top:1px solid #E9EFF5;display:flex;justify-content:flex-end;gap:9px;background:#FBFCFD;border-radius:0 0 6px 6px")}>
          <button className="ghost-btn" onClick={onCancel} style={css("height:36px;padding:0 18px;border:1px solid #D8E0E8;background:#fff;border-radius:4px;font-size:13px;color:#475569;cursor:pointer")}>Cancel</button>
          <button onClick={onSave} style={css("height:36px;padding:0 22px;border:1px solid #B42318;background:#B42318;color:#fff;border-radius:4px;font-size:13px;font-weight:600;cursor:pointer")}>Save Delay Record</button>
        </div>
      </div>
    </div>
  );
}

/* ------------------------------------------------------------- documents */

export type StoredDoc = {
  id: string; name: string; size: string; kind: string; type: string;
  module: string; record: string; recordLabel: string; by: string; at: string;
  preview: string; status: string;
};

export function DocsDrawer(p: {
  scopeLabel: string;
  docs: StoredDoc[];
  allCount: number;
  totals: { k: string; n: number }[];
  dragOver: boolean;
  onClose: () => void;
  onInput: (e: ChangeEvent<HTMLInputElement>) => void;
  onDrop: (e: DragEvent<HTMLLabelElement>) => void;
  onDragOver: (e: DragEvent<HTMLLabelElement>) => void;
  onDragLeave: (e: DragEvent<HTMLLabelElement>) => void;
  onRemove: (id: string) => void;
}) {
  return (
    <aside style={css("position:fixed;top:60px;right:0;bottom:0;width:430px;background:#fff;border-left:1px solid #D8E0E8;box-shadow:-8px 0 28px rgba(10,34,64,.12);z-index:58;display:flex;flex-direction:column;animation:tin .18s ease")}>
      <div style={css("padding:14px 18px;background:#0A2240;display:flex;justify-content:space-between;align-items:flex-start;gap:10px")}>
        <div style={css("display:flex;flex-direction:column;gap:3px;min-width:0")}>
          <span style={css("font-size:13.5px;font-weight:600;color:#fff")}>Document Store</span>
          <span style={css("font-size:11px;color:#7FA5CC")}>คลังเอกสารงาน · {p.scopeLabel}</span>
        </div>
        <button onClick={p.onClose} aria-label="Close documents" style={css("width:28px;height:28px;flex:none;border:1px solid #24476E;background:#0E2B4F;color:#B9CFE5;border-radius:4px;cursor:pointer;font-size:14px")}>✕</button>
      </div>

      <div style={css("padding:14px 18px;border-bottom:1px solid #E9EFF5")}>
        <label
          onDrop={p.onDrop}
          onDragOver={p.onDragOver}
          onDragLeave={p.onDragLeave}
          style={css(
            "border:2px dashed " + (p.dragOver ? "#2E7DD1" : "#C7D6E4") + ";background:" + (p.dragOver ? "#F4F8FC" : "#FBFCFD") +
            ";border-radius:5px;padding:18px 16px;display:flex;flex-direction:column;align-items:center;gap:7px;text-align:center;cursor:pointer",
          )}
        >
          <span style={css("font-size:20px;color:#2E7DD1")}>⬆</span>
          <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>Drop files here or click to upload</span>
          <span style={css("font-size:11px;color:#64748B;line-height:1.45")}>
            PDF · images · Excel · Word — stored as data against this module and the open record
          </span>
          <input type="file" multiple onChange={p.onInput} style={{ display: "none" }} />
        </label>
        <div style={css("display:flex;gap:8px;margin-top:10px;flex-wrap:wrap")}>
          {p.totals.map((t) => (
            <span key={t.k} style={css("font-size:11px;color:#64748B;background:#F1F5F9;border-radius:3px;padding:3px 8px")}>{t.k} · {t.n}</span>
          ))}
          <span style={css("margin-left:auto;font-size:11px;color:#94A3B8")}>{p.allCount} stored system-wide</span>
        </div>
      </div>

      <div style={css("flex:1;overflow-y:auto;padding:12px 16px;display:flex;flex-direction:column;gap:9px")}>
        {p.docs.map((d) => (
          <div key={d.id} style={css("border:1px solid #E9EFF5;border-radius:4px;padding:10px 12px;display:flex;flex-direction:column;gap:6px;background:#fff")}>
            <div style={css("display:flex;align-items:center;gap:9px")}>
              <span style={css(badge(d.kind, d.kind === "PDF" ? "red" : d.kind === "Image" ? "teal" : d.kind === "Excel" ? "green" : "gray"))}>{d.kind}</span>
              <span style={css("font-size:11px;color:#0A2240;font-weight:600")}>{d.type}</span>
              <button onClick={() => p.onRemove(d.id)} aria-label={"Remove " + d.name} style={css("margin-left:auto;width:22px;height:22px;border:1px solid #E9EFF5;background:#fff;border-radius:3px;color:#94A3B8;cursor:pointer;font-size:11px")}>✕</button>
            </div>
            {d.preview && (
              <div style={{ ...css("height:140px;border-radius:3px;border:1px solid #E9EFF5;background-size:cover;background-position:center"), backgroundImage: "url(" + d.preview + ")" }} />
            )}
            <span style={css("font-size:12px;color:#16232F;word-break:break-all")}>{d.name}</span>
            <span style={css("font-size:10.5px;color:#94A3B8")}>{d.size} · {d.by} · {d.at}</span>
            <span style={css("font-size:10.5px;color:#64748B")}>Linked to: {d.recordLabel}</span>
          </div>
        ))}
        {!p.docs.length && (
          <span style={css("font-size:11.5px;color:#94A3B8;padding:8px 0")}>
            No documents stored against this module yet.
          </span>
        )}
      </div>
    </aside>
  );
}

type Grant = {
  id: number; ownerId: string; ownerName: string; delegateId: string; delegateName: string;
  fromDate: string; toDate: string; reason: string; status: string;
};

/**
 * The grants you have made and the ones you have been given.
 *
 * Both directions in one list on purpose: knowing whose work you are covering
 * matters as much as knowing who is covering yours, and somebody who has been
 * given cover without being told would otherwise find rows they can edit and no
 * explanation for why.
 */
function Delegations({ me, onToast }: { me: Account; onToast: (message: string) => void }) {
  const [grants, setGrants] = useState<Grant[]>([]);
  const [people, setPeople] = useState<{ id: string; name: string }[]>([]);
  const [form, setForm] = useState({ delegateId: "", fromDate: "", toDate: "", reason: "", ownerId: "" });
  // Whose work this person may arrange cover for. Empty for everybody without
  // the authority to assign work, so the field simply does not appear for them.
  const [owners, setOwners] = useState<{ id: string; name: string }[]>([]);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch("/api/delegations", { headers: { accept: "application/json" } });
    if (response.ok) setGrants(((await response.json()) as { grants: Grant[] }).grants);
    // Not /api/staff: that needs ViewAudit, which an Operation User does not
    // have, so this list was empty for exactly the people who go on leave. The
    // API answers who may be handed work using the same rule it validates the
    // grant with, so nothing offered here can be refused on the way in.
    const mayArrange = await apiFetch("/api/delegations/owners", { headers: { accept: "application/json" } });
    if (mayArrange.ok) setOwners(await mayArrange.json() as { id: string; name: string }[]);

    const staff = await apiFetch("/api/delegations/candidates", { headers: { accept: "application/json" } });
    if (staff.ok) {
      const body = await staff.json() as { id: string; name: string }[];
      setPeople(body);
    }
    // Nothing from this component is read any more: the API decides whose name
    // may appear, including leaving out the caller. It used to filter by
    // me.opId here, and the dependency outlived the code that needed it.
  }, []);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function grant() {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/delegations", {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(form),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "มอบสิทธิ์ไม่สำเร็จ");
      if (response.ok) { setForm({ delegateId: "", fromDate: "", toDate: "", reason: "", ownerId: "" }); await load(); }
    } finally { setBusy(false); }
  }

  async function revoke(id: number) {
    if (!window.confirm("ยกเลิกการมอบสิทธิ์นี้ทันที?")) return;
    const response = await apiFetch(`/api/delegations/${id}/revoke`, { method: "POST" });
    const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
    onToast(reply.message ?? reply.error ?? "ยกเลิกไม่สำเร็จ");
    await load();
  }

  const tone = (status: string) =>
    status === "กำลังใช้งาน" ? "#16794C" : status === "รอถึงกำหนด" ? "#B45309" : "#94A3B8";

  const box = "height:31px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:12px;padding:0 8px;outline:none;font-family:inherit";

  return (
    <div>
      <div style={css("display:flex;gap:7px;flex-wrap:wrap;align-items:center")}>
        {owners.length > 0 && (
          <select value={form.ownerId} onChange={(e) => setForm({ ...form, ownerId: e.target.value })}
            title="เว้นไว้ = มอบงานของตัวเอง"
            style={css(box + ";min-width:150px")}>
            <option value="">— งานของฉัน —</option>
            {owners.map((person) => (
              <option key={person.id} value={person.id}>แทน {person.name}</option>
            ))}
          </select>
        )}
        <select value={form.delegateId} onChange={(e) => setForm({ ...form, delegateId: e.target.value })}
          style={css(box + ";min-width:150px")}>
          <option value="">— เลือกผู้รับมอบ —</option>
          {people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
        </select>
        <input value={form.fromDate} onChange={(e) => setForm({ ...form, fromDate: e.target.value })}
          placeholder="เริ่ม วว/ดด/ปปปป" style={css(box + ";width:130px")} />
        <input value={form.toDate} onChange={(e) => setForm({ ...form, toDate: e.target.value })}
          placeholder="ถึง วว/ดด/ปปปป" style={css(box + ";width:130px")} />
        <input value={form.reason} onChange={(e) => setForm({ ...form, reason: e.target.value })}
          placeholder="เหตุผล เช่น ลาพักร้อน" style={css(box + ";flex:1;min-width:150px")} />
        <button onClick={() => void grant()}
          disabled={busy || !form.delegateId || !form.fromDate || !form.toDate || form.reason.trim().length < 4}
          style={css("height:31px;padding:0 14px;border:1px solid #0A2240;background:" +
            (busy || !form.delegateId || !form.fromDate || !form.toDate || form.reason.trim().length < 4
              ? "#C3CFDB" : "#0A2240") +
            ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
          มอบสิทธิ์
        </button>
      </div>

      {grants.length > 0 && (
        <div style={css("margin-top:10px;display:flex;flex-direction:column;gap:5px")}>
          {grants.map((item) => {
            const outgoing = item.ownerId === me.opId;
            return (
              <div key={item.id}
                style={css("display:flex;gap:10px;align-items:center;flex-wrap:wrap;background:#F8FAFC;border:1px solid #E3E8EE;border-radius:4px;padding:7px 10px")}>
                <span style={css("font-size:11.5px;color:#0F2B46;flex:1;min-width:190px")}>
                  {outgoing
                    ? <>ให้ <b>{item.delegateName}</b> แก้งานของคุณ</>
                    : <>คุณแก้งานของ <b>{item.ownerName}</b> ได้</>}
                  {" "}{item.fromDate}–{item.toDate} · {item.reason}
                </span>
                <span style={css("font-size:11px;font-weight:600;color:" + tone(item.status))}>{item.status}</span>
                {outgoing && item.status !== "ยกเลิกแล้ว" && item.status !== "หมดอายุแล้ว" && (
                  <button onClick={() => void revoke(item.id)}
                    style={css("height:25px;padding:0 10px;border:1px solid #B42318;background:#fff;color:#B42318;border-radius:3px;font-size:11px;font-weight:600;cursor:pointer;font-family:inherit")}>
                    ยกเลิก
                  </button>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
