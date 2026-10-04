"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "../aiControl";
import { parsePolicyReport, type PolicyReport } from "../aiPolicy";
import { ZoomBox } from "../TableFrame";
import s from "./AiControlTower.module.css";

/** Read-only grant evidence. Existing controls may stop AI but never grant these permissions. */
export function AiPolicyPanel() {
  const [report, setReport] = useState<PolicyReport | null>(null);
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    const load = async () => {
      try {
        const response = await apiFetch("/api/ai/policies", { headers: { accept: "application/json" } });
        if (!response.ok) throw new Error("อ่าน AI policy ไม่สำเร็จ");
        const parsed = parsePolicyReport(await response.json());
        if (active) setReport(parsed);
      } catch { if (active) setError("อ่าน AI policy ไม่สำเร็จ — ไม่ยืนยันว่า Agent พร้อมทำงาน"); }
    };
    void load();
    return () => { active = false; };
  }, []);
  return <section aria-label="AI Permission Matrix" data-testid="ai-policy-read-only">
    <h3>AI Permission Matrix · อ่านอย่างเดียว</h3>
    <p>สิทธิ์ของผู้ใช้ไม่ใช่สิทธิ์ของ Agent · Action ที่ไม่อนุญาตชัดเจนถูกปฏิเสธ · การเปลี่ยน policy ต้องผ่านการทบทวนแยกต่างหาก</p>
    <p>ผู้รับผิดชอบหลัก/สำรองต้องเป็นบัญชีที่ใช้งานอยู่ ระดับ Supervisor ขึ้นไป และเป็นคนละบัญชี · สิทธิ์อนุมัติยังใช้เกณฑ์เดิม</p>
    <p>อีเมลที่ระบุเป็นการเสนอชื่อ ระบบต้องตรวจให้ตรงกับบัญชีจริงในทะเบียนผู้ใช้ก่อน Agent ทำงาน</p>
    {error ? <p role="alert" className={s.error}>{error}</p> : !report ? <p>กำลังอ่าน policy…</p> : <>
      <p>Policy {report.policyVersion} · {report.valid ? "โครงสร้างถูกต้อง" : "โครงสร้างไม่ถูกต้อง — ห้ามทำงาน"}</p>
      {report.sharedBudget ? <p>งบรวมทั้ง 14 Agent: {report.sharedBudget.dailyCostLimit ?? "ยังไม่กำหนด"} USD/วัน · {report.sharedBudget.monthlyCostLimit} USD/เดือน
        {" · "}สำรองเดือนนี้ {report.sharedBudget.reservedCostMonth ?? "ไม่ทราบ"} USD
        {" · "}วงเงินคงเหลือหลังสำรอง {report.sharedBudget.remainingCostMonth ?? "ไม่ทราบ"} USD (รอบ UTC)
        {" · "}ไม่ใช่งบแยกต่อ Agent และยอดสำรองไม่ใช่ยอดเรียกเก็บจริง</p> : <p>งบรวมยังไม่กำหนด — ไม่ยืนยันว่า Agent พร้อมทำงาน</p>}
      {!report.auditAvailable && <p role="alert" className={s.error}>Audit ไม่พร้อม — AI จะไม่ทำงาน ระบบ Manual ใช้งานได้ตามเดิม</p>}
      <ZoomBox><table className={s.table}>
        <thead><tr><th>Agent / สถานะ</th><th>Tools</th><th>Read / Analyze / Draft</th><th>Execute / Human approval</th><th>Forbidden</th><th>งบประมาณ</th><th>ล่าสุด</th></tr></thead>
        <tbody>{report.agents.map(agent => <tr key={agent.id}>
          <td><strong>{agent.name}</strong><div>{agent.id}</div><div>{agent.status}</div><div>{agent.reasonCode}</div>
            <div>ผู้รับผิดชอบ: {agent.humanOwner || "ยังไม่กำหนด"}</div><div>สำรอง: {agent.fallbackOwner || "ยังไม่กำหนด"}</div></td>
          <td>{agent.allowedTools.join(", ") || "—"}</td>
          <td><div>Read: {agent.read.join(", ") || "—"}</div><div>Analyze: {agent.analyze.join(", ") || "—"}</div><div>AI DRAFT: {agent.draft.join(", ") || "—"}</div></td>
          <td><div>Execute: {agent.execute.join(", ") || "—"}</div><div>Human approval: {agent.humanApproval.join(", ") || "—"}</div></td>
          <td><details><summary>{agent.forbidden.length} Actions</summary>{agent.forbidden.join(", ")}</details></td>
          <td>{agent.budget ? <><div>รายวัน {agent.budget.dailyCostLimit}</div><div>รายเดือน {agent.budget.monthlyCostLimit}</div><div>สำรองเดือนนี้ {agent.reservedCostMonth ?? "ไม่ทราบ"}</div></> : "CONFIGURATION_REQUIRED"}</td>
          <td><div>Execution: {agent.lastExecution ? stamp(agent.lastExecution) : "—"}</div><div>Security: {agent.lastSecurityEvent ? stamp(agent.lastSecurityEvent) : "—"}</div></td>
        </tr>)}</tbody>
      </table></ZoomBox>
    </>}
  </section>;
}
