/** Phase E contracts. No credentials, persistent chat cache, business scoring or tool execution here. */
export type AiAgent = { id: string; name: string; enabled: boolean; connected: boolean };
export type OperationsControl = {
  available: boolean; enabled: boolean; revision: number; canManage: boolean;
  canEnable: boolean; emergencyDisabled: boolean; blockReason: string;
};
export type AiStatus = {
  enabled: boolean; chatEnabled: boolean; providerConfigured: boolean; mock: boolean;
  configurationValid: boolean; liveToolsReady: boolean; writeToolsReady: boolean; auditReady: boolean;
  agents: AiAgent[];
  operationsControl?: OperationsControl | null;
};
export type Figure = { id: string; english: string; thai: string; value: number | null; base: number; unit: string; note: string };
export type TodayBoard = { date: string; computedAt: string; volume: Figure[]; performance: Figure[]; attention: Figure[] };
export type Finding = { urgency: "Now" | "Soon" | "Watch" | "Records"; kind: string; headline: string; detail: string; count: number; screen: string };
export type Brief = { today: string; quiet: string; findings: Finding[] };
export type EvidenceRow = {
  key: string; category: string; jobCode: string; container: string; customer: string; trucker: string;
  date: string; planTime: string; status: string; hasOwner: boolean; hasDriver: boolean; hasPlate: boolean;
  arrivalRecorded: boolean; risk: string | null; explanation: string; suggestedAction: string; source: string;
};
export type OperationsAnswer = {
  view: string; asOfDate: string; timeZone: string; window: string;
  total: number; returned: number; truncated: boolean; undatedActive: number; invalidRows: number;
  retrievedAt: string; sourceUpdatedAt: string | null; basis: string; rows: EvidenceRow[];
};
export type Usage = { inputTokens: number; outputTokens: number };
export type AiReply = { runId: string; code: string; summary: string; agentId: string | null; mock: boolean; usage: Usage | null; evidence: OperationsAnswer | null };
export type AuditEvent = { event: string; status: string; at: string; total: number | null; returned: number | null };
export type AuditRun = {
  runId: string; userId: string; role: string; agentId: string; model: string;
  scope: { team: boolean; operatorId: string | null }; status: string; startedAt: string; completedAt: string | null;
  toolCallId: string | null; tool: string | null; toolStatus: string | null; view: string | null; limit: number | null;
  risk: string; approvalStatus: string; source: string; sourceKeys: string[];
  total: number | null; returned: number | null; usage: Usage | null; events: AuditEvent[];
};
export type AuditPage = { runs: AuditRun[]; nextBeforeId: number | null };

type Obj = Record<string, unknown>;
const obj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const text = (v: unknown, max = 12000): v is string => typeof v === "string" && v.length <= max;
const count = (v: unknown): v is number => typeof v === "number" && Number.isSafeInteger(v) && v >= 0;
const nullableCount = (v: unknown) => v === null || count(v);
const nullableText = (v: unknown) => v === null || text(v);
const strings = (v: Obj, names: string[]) => names.every(k => text(v[k]));
const bools = (v: Obj, names: string[]) => names.every(k => typeof v[k] === "boolean");
const id = (v: unknown): v is string => typeof v === "string" && /^[a-fA-F0-9]{32}$/.test(v);
const usage = (v: unknown) => v === null || obj(v) && count(v.inputTokens) && count(v.outputTokens);
function accept<T>(v: unknown, valid: boolean): T {
  if (!valid) throw new ControlError("invalid_response");
  return v as T;
}
export function parseStatus(v: unknown): AiStatus {
  return accept(v, obj(v) && bools(v, ["enabled", "chatEnabled", "providerConfigured", "mock", "configurationValid", "liveToolsReady", "writeToolsReady", "auditReady"])
    && Array.isArray(v.agents) && v.agents.length <= 8 && v.agents.every(a =>
      obj(a) && strings(a, ["id", "name"]) && bools(a, ["enabled", "connected"]))
    && (v.operationsControl == null || obj(v.operationsControl)
      && bools(v.operationsControl, ["available", "enabled", "canManage", "canEnable", "emergencyDisabled"])
      && count(v.operationsControl.revision) && text(v.operationsControl.blockReason)));
}
const figure = (f: unknown) => obj(f) && strings(f, ["id", "english", "thai", "unit", "note"])
  && count(f.base) && (f.value === null || typeof f.value === "number" && Number.isFinite(f.value) && f.value >= 0);
export function parseToday(v: unknown): TodayBoard {
  return accept(v, obj(v) && strings(v, ["date", "computedAt"]) && ["volume", "performance", "attention"].every(k =>
    Array.isArray(v[k]) && v[k].length <= 30 && v[k].every(figure)));
}
export function parseBrief(v: unknown): Brief {
  return accept(v, obj(v) && strings(v, ["today", "quiet"]) && Array.isArray(v.findings) && v.findings.length <= 50
    && v.findings.every(f => obj(f) && strings(f, ["kind", "headline", "detail", "screen"])
      && count(f.count) && ["Now", "Soon", "Watch", "Records"].includes(String(f.urgency))));
}
function evidence(v: unknown): boolean {
  return obj(v) && strings(v, ["view", "asOfDate", "timeZone", "window", "retrievedAt", "basis"])
    && ["today", "risk_today", "search", "delays"].includes(String(v.view))
    && ["total", "returned", "undatedActive", "invalidRows"].every(k => count(v[k]))
    && typeof v.truncated === "boolean" && nullableText(v.sourceUpdatedAt)
    && Array.isArray(v.rows) && v.rows.length <= 50 && v.returned === v.rows.length
    && Number(v.total) >= Number(v.returned) && v.truncated === (Number(v.total) > Number(v.returned))
    && v.rows.every(r => obj(r) && text(r.key, 80) && r.key.length > 0
      && strings(r, ["category", "jobCode", "container", "customer", "trucker", "date", "planTime", "status", "explanation", "suggestedAction"])
      && bools(r, ["hasOwner", "hasDriver", "hasPlate", "arrivalRecorded"])
      && nullableText(r.risk) && r.source === "operation_jobs");
}
export function parseReply(v: unknown): AiReply {
  return accept(v, obj(v) && id(v.runId) && v.code === "ok" && text(v.summary) && nullableText(v.agentId)
    && typeof v.mock === "boolean" && usage(v.usage)
    && (v.mock ? v.evidence === null : evidence(v.evidence)));
}
const auditRun = (v: unknown) => obj(v) && id(v.runId)
  && strings(v, ["userId", "role", "agentId", "model", "status", "startedAt", "risk", "approvalStatus", "source"])
  && obj(v.scope) && typeof v.scope.team === "boolean" && nullableText(v.scope.operatorId)
  && ["completedAt", "toolCallId", "tool", "toolStatus", "view"].every(k => nullableText(v[k]))
  && ["limit", "total", "returned"].every(k => nullableCount(v[k])) && usage(v.usage)
  && Array.isArray(v.sourceKeys) && v.sourceKeys.length <= 50 && v.sourceKeys.every(k => text(k, 80))
  && Array.isArray(v.events) && v.events.length >= 1 && v.events.length <= 4
  && v.events.every(e => obj(e) && strings(e, ["event", "status", "at"]) && nullableCount(e.total) && nullableCount(e.returned));
export function parseAuditRun(v: unknown): AuditRun { return accept(v, auditRun(v)); }
export function parseAuditPage(v: unknown): AuditPage {
  return accept(v, obj(v) && Array.isArray(v.runs) && v.runs.length <= 100 && v.runs.every(auditRun)
    && (v.nextBeforeId === null || count(v.nextBeforeId) && v.nextBeforeId > 0));
}

export const ISSUE_TARGETS = ["monitoring", "myjob"] as const;
export function issueTarget(value: string): typeof ISSUE_TARGETS[number] | null {
  return ISSUE_TARGETS.find(screen => screen === value) ?? null;
}
export function askBody(message: string) {
  const trimmed = message.trim();
  if (!trimmed || trimmed.length > 4000) throw new ControlError("invalid_request");
  return { message: trimmed, agentId: "operations-agent", context: { page: "operations" } };
}
export function availability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  if (status.operationsControl && (!status.operationsControl.available || !status.operationsControl.enabled || status.operationsControl.emergencyDisabled))
    return blocked("Operations AI ยังปิดอยู่", "Administrator ควบคุมสวิตช์ได้จากหน้านี้ · ไม่มีสิทธิ์เขียนข้อมูลงาน");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "ดูสรุปจากระบบเดิมได้ตามปกติ การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  const operations = status.agents.find(a => a.id === "operations-agent");
  if (!operations?.enabled || !operations.connected) return blocked("Operations Agent ยังไม่พร้อม", "Agent อาจปิดอยู่ หรือบัญชีนี้ไม่มีขอบเขตอ่านงานที่รองรับ");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์ ไม่ต้องใส่คีย์ในหน้านี้", "amber");
  if (status.mock) return { ready: true, title: "Development Mock", detail: "โหมดสาธิต ไม่อ่านหรือแก้ไขข้อมูลงานจริง", tone: "amber" };
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้ ตรวจ migration และการเชื่อมต่อ Audit ก่อน", "amber");
  if (!status.liveToolsReady) return blocked("เครื่องมืออ่านงานยังไม่พร้อม", "รีเฟรชสถานะหรือแจ้งผู้ดูแลระบบ", "amber");
  return { ready: true, title: "Operations AI พร้อมรับคำถาม", detail: "อ่านอย่างเดียว · มี Audit · ตรวจสอบคำตอบจากงานอ้างอิงได้", tone: "green" };
}
export const EVENT_LABEL: Record<string, string> = {
  run_started: "เริ่มรอบการทำงาน", tool_started: "เริ่มอ่านข้อมูล", tool_completed: "จบการอ่านข้อมูล", run_completed: "จบรอบการทำงาน",
};
export const STATUS_LABEL: Record<string, string> = {
  running: "กำลังทำงาน", succeeded: "สำเร็จ", incomplete: "ประวัติไม่ครบ / งานอาจถูกขัดจังหวะ",
  failed: "ไม่สำเร็จ", cancelled: "ยกเลิก", audit_failed: "บันทึก Audit ไม่สำเร็จ",
  provider_busy: "AI ติดข้อจำกัดการเรียกใช้งาน", provider_unavailable: "AI ไม่พร้อม",
  source_unavailable: "อ่านแหล่งข้อมูลไม่สำเร็จ", timeout: "หมดเวลารอ",
  invalid_tool: "คำสั่งไม่ผ่านการตรวจสอบ", clarification_required: "ต้องระบุคำถามใหม่", not_connected: "ยังไม่เชื่อมต่อ",
};
export function stamp(value: string | null | undefined) {
  if (!value) return "—";
  const time = new Date(value);
  return Number.isNaN(time.getTime()) ? value : new Intl.DateTimeFormat("th-TH-u-ca-gregory",
    { dateStyle: "short", timeStyle: "medium", timeZone: "Asia/Bangkok" }).format(time);
}
export function number(value: number | null | undefined): string { return value == null ? "N/A" : value.toLocaleString("en-US", { maximumFractionDigits: 1 }); }
export class ControlError extends Error {
  code: string;
  constructor(code: string) { super(code); this.code = code; }
}
const ERRORS: Record<string, string> = {
  control_unavailable: "ที่เก็บสวิตช์ยังไม่พร้อม ต้องติดตั้ง migration หรือกู้การเชื่อมต่อก่อน",
  control_conflict: "มีผู้เปลี่ยนสถานะแล้ว กรุณาตรวจสถานะล่าสุดก่อนลองใหม่",
  emergency_disabled: "เซิร์ฟเวอร์สั่งหยุดฉุกเฉินอยู่ จึงเปิดจากหน้านี้ไม่ได้",
  control_not_ready: "Provider เครื่องมืออ่าน หรือ Audit ยังไม่พร้อม จึงยังเปิดไม่ได้",
  unauthenticated: "กรุณาเข้าสู่ระบบอีกครั้ง", forbidden: "บัญชีนี้ไม่มีสิทธิ์ดูข้อมูลส่วนนี้",
  unavailable: "อ่านข้อมูลไม่สำเร็จ กรุณาลองใหม่", invalid_response: "ข้อมูลตอบกลับไม่ตรงรูปแบบ จึงไม่แสดงตัวเลข",
  disabled: "SCMOS AI ยังปิดอยู่", invalid_request: "กรอกคำถาม 1–4,000 ตัวอักษร",
  audit_not_ready: "Audit ไม่พร้อม ระบบจึงไม่ส่งผลลัพธ์ AI", configuration_invalid: "การตั้งค่า AI ยังไม่พร้อม",
  agent_disabled: "Agent นี้ยังปิดอยู่", not_connected: "เครื่องมือยังไม่เชื่อมต่อ",
  provider_unavailable: "บริการ AI ไม่พร้อมใช้งาน", provider_busy: "AI ติดข้อจำกัดการเรียกใช้งาน กรุณารอสักครู่",
  busy: "AI กำลังให้บริการคำขออื่น กรุณาลองอีกครั้งภายหลัง", timeout: "หมดเวลารอ กรุณาลองใหม่",
  invalid_tool: "AI ส่งคำสั่งที่ไม่ผ่านการตรวจสอบ จึงไม่ได้อ่านข้อมูล", source_unavailable: "อ่านข้อมูลงานไม่สำเร็จ",
  clarification_required: "รองรับงานวันนี้ งานเสี่ยง ค้นหางาน และงานล่าช้า กรุณาระบุคำถามให้ตรงกับหัวข้อเหล่านี้",
  cancelled: "ยกเลิกคำขอแล้ว", not_found: "ไม่พบประวัติรอบการทำงานนี้",
};
export function errorText(error: unknown) {
  const code = error instanceof ControlError ? error.code : "unavailable";
  return Object.hasOwn(ERRORS, code) ? ERRORS[code] : ERRORS.unavailable;
}
export async function controlRequest<T>(fetcher: (path: string, init?: RequestInit) => Promise<Response>,
  path: string, parse: (value: unknown) => T, signal: AbortSignal, body?: ReturnType<typeof askBody>): Promise<T> {
  const response = await fetcher(path, {
    method: body ? "POST" : "GET", signal, headers: { accept: "application/json", ...(body ? { "content-type": "application/json" } : {}) },
    ...(body ? { body: JSON.stringify(body) } : {}),
  });
  const value: unknown = await response.json().catch(() => null);
  if (!response.ok) {
    const fallback = response.status === 401 ? "unauthenticated" : response.status === 403 ? "forbidden"
      : response.status === 404 ? "not_found" : response.status === 429 ? "busy" : "unavailable";
    throw new ControlError(obj(value) && typeof value.code === "string" && Object.hasOwn(ERRORS, value.code) ? value.code : fallback);
  }
  return parse(value);
}
