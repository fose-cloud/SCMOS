"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";

/**
 * Who may do what.
 *
 * Read from `/api/roles`, which builds it from the same table the API enforces
 * against — so this screen shows the permissions that are actually in force
 * rather than a description of them somebody has to remember to update. A
 * printed matrix that has drifted from the code is worse than none: people plan
 * around it.
 */

type Role = { name: string; scopeEn: string; scopeTh: string; grants: string[] };
type Person = { id: string; name: string; account: string; role: string };
type Matrix = { capabilities: string[]; roles: Role[]; people: Person[] };

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
  AdministerData: "จัดการข้อมูลทั้งระบบ",
};

export function Administration() {
  const [matrix, setMatrix] = useState<Matrix | null>(null);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/roles", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Matrix : null;
      if (!cancelled) setMatrix(body);
    })();
    return () => { cancelled = true; };
  }, []);

  if (!matrix) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const occupied = new Set(matrix.people.map((person) => person.role));

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 15px;font-size:12px;color:#5A6B7D;line-height:1.65")}>
        ตารางนี้อ่านจาก <code style={css("font-family:ui-monospace,monospace")}>Rules/Roles.cs</code> ซึ่งเป็นตัวเดียวกับที่ API ใช้บังคับจริง —
        ไม่ใช่เอกสารที่ต้องมาไล่แก้ให้ตรงทีหลัง สิทธิ์เป็น <b style={css("color:#0A2240")}>ความสามารถรายข้อ</b> ไม่ใช่ระดับอาวุโส
        เพราะคำถามที่โค้ดถามจริงคือ “คนนี้ทำสิ่งนี้ได้ไหม” ไม่ใช่ “คนนี้ใหญ่แค่ไหน”
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          สิทธิ์ตามบทบาท · {matrix.roles.length} บทบาท
        </div>
        <div style={css("overflow-x:auto")}>
          <table style={css("border-collapse:collapse;font-size:12px;min-width:100%")}>
            <thead>
              <tr>
                <th style={css("position:sticky;left:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;border-right:1px solid #E9EFF5;white-space:nowrap;z-index:1")}>
                  ความสามารถ
                </th>
                {matrix.roles.map((role) => (
                  <th key={role.name} style={css("background:#F8FAFC;padding:8px 10px;text-align:center;font-size:10.5px;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap;min-width:92px")}>
                    <div style={css("color:#0A2240;font-size:11px")}>{role.name}</div>
                    <div style={css("font-weight:400;font-size:10px")}>{role.scopeTh}</div>
                    {!occupied.has(role.name) && (
                      <div style={css("font-weight:400;font-size:9.5px;color:#B45309")}>ยังไม่มีคนใช้</div>
                    )}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {matrix.capabilities.map((capability) => (
                <tr key={capability} style={css("border-bottom:1px solid #F1F5F9")}>
                  <td style={css("position:sticky;left:0;background:#fff;padding:7px 12px;border-right:1px solid #E9EFF5;white-space:nowrap")}>
                    <div style={css("color:#16232F")}>{CAPABILITY_TH[capability] ?? capability}</div>
                    <div style={css("font-family:ui-monospace,monospace;font-size:10px;color:#94A3B8")}>{capability}</div>
                  </td>
                  {matrix.roles.map((role) => {
                    const granted = role.grants.includes(capability);
                    return (
                      <td key={role.name} style={css("padding:7px 10px;text-align:center;font-size:13px;color:" +
                        (granted ? "#16794C" : "#D8E0E8"))}>
                        {granted ? "✓" : "·"}
                      </td>
                    );
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          บัญชีในระบบ · {matrix.people.length} คน
        </div>
        <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
          <thead><tr>{["รหัส", "ชื่อ", "บัญชี", "บทบาท"].map((h) => (
            <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5")}>{h}</th>
          ))}</tr></thead>
          <tbody>
            {matrix.people.map((person) => (
              <tr key={person.id} style={css("border-bottom:1px solid #F1F5F9")}>
                <td style={css("padding:8px 12px;font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0")}>{person.id}</td>
                <td style={css("padding:8px 12px;color:#0A2240;font-weight:600")}>{person.name}</td>
                <td style={css("padding:8px 12px;font-family:ui-monospace,monospace;font-size:11.5px;color:#5A6B7D")}>{person.account}</td>
                <td style={css("padding:8px 12px;color:#5A6B7D")}>{person.role}</td>
              </tr>
            ))}
          </tbody>
        </table>
        <div style={css("padding:11px 16px;border-top:1px solid #E9EFF5;font-size:11.5px;color:#94A3B8;line-height:1.6")}>
          บัญชีมาจาก <code style={css("font-family:ui-monospace,monospace")}>StaffDirectory</code> ซึ่งจับคู่กับ Entra ตอนล็อกอินจริง —
          บทบาทที่ระบบไม่รู้จักจะได้สิทธิ์เท่า Viewer ไม่ใช่เท่าค่าเริ่มต้น เพราะพิมพ์บทบาทผิดควรทำให้เสียสิทธิ์ตัวเอง ไม่ใช่ได้สิทธิ์คนอื่น
        </div>
      </div>
    </div>
  );
}
