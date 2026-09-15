import type { AiStatus } from "./aiControl";

/** Display-only projection. No permission grants or optimistic fallback for future agents. */
export function agentReadiness(status: AiStatus | null, id: string) {
  const state = (code: string, label: string, detail: string, ready = false) => ({ code, label, detail, ready });
  if (!status) return state("unknown", "ยังไม่ทราบสถานะ", "กรุณารีเฟรชสถานะ");
  const agent = status.agents.find(a => a.id === id);
  if (!agent) return state("unavailable", "ไม่พร้อมสำหรับบัญชีนี้", "ไม่มี Agent นี้ในขอบเขตสถานะที่เซิร์ฟเวอร์ส่งมา");
  const control = id === "operations-agent" ? status.operationsControl : null;
  if (control?.emergencyDisabled) return state("emergency", "หยุดฉุกเฉิน", "เซิร์ฟเวอร์หยุด Operations AI");
  if (control && !control.available) return state("control_unavailable", "อ่านสถานะสวิตช์ไม่ได้", "ยังยืนยันความพร้อมไม่ได้");
  if (!agent.enabled || !status.enabled || !status.chatEnabled || (control && !control.enabled))
    return state("disabled", "ปิดอยู่", "ยังไม่เปิดรับคำถามสำหรับ Agent นี้");
  if (!agent.connected || id !== "operations-agent")
    return state("not_connected", "ยังไม่เชื่อมต่อ", "ยังไม่มีตัวเรียกเครื่องมือที่รองรับ Agent นี้ใน runtime ปัจจุบัน");
  if (!status.configurationValid) return state("configuration_invalid", "ตั้งค่าไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์");
  if (!status.providerConfigured) return state("provider_unavailable", "Provider ไม่พร้อม", "ยังไม่ได้ตั้งค่า provider");
  if (status.mock) return state("mock", "โหมดสาธิต", "ไม่ได้อ่านหรือแก้ข้อมูลงานจริง");
  if (!status.auditReady) return state("audit_unavailable", "Audit ไม่พร้อม", "ไม่อนุญาตให้ส่งผลลัพธ์ที่ไม่มี Audit");
  if (!status.liveToolsReady) return state("tools_unavailable", "เครื่องมือยังไม่พร้อม", "มีการเชื่อมต่อแต่ runtime ยังไม่พร้อมรับคำถาม");
  return state("ready", "พร้อมรับคำถาม", "อ่านตามสิทธิ์ · ตรวจ Audit อีกครั้งเมื่อเรียกใช้งาน · ไม่ใช่ผลทดสอบ provider สด", true);
}
