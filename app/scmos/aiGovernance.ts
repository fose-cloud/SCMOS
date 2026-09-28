/**
 * The AI platform's governance as the Control Tower shows it — every agent's
 * autonomy, shadow mode, status, health and cost, and the platform's execution
 * switch (Agent Platform foundation, 27 Sep 2026). Parsing is strict: a report
 * that does not match is not drawn, the way every other AI panel refuses a
 * response it cannot read. The server decides; this only reads and asks.
 */

export type AgentUsage = {
  inputTokens24h: number; outputTokens24h: number; cost24h: number | null;
  inputTokens30d: number; outputTokens30d: number; cost30d: number | null;
};

export type AgentGovernance = {
  id: string; name: string; flagEnabled: boolean; maxAutonomy: number; autonomy: number; effectiveAutonomy: number;
  shadowMode: boolean; status: string; effectiveStatus: string; reason: string; revision: number;
  updatedBy: string; updatedAt: string | null; stored: boolean;
  runs24h: number; failures24h: number; failureRate24h: number | null; consecutiveFailures: number;
  lastSuccess: string | null; lastFailure: string | null; averageMs: number | null; breaker: string;
  usage: AgentUsage;
  /** Decisions in 30 days where a person's action can be set against the agent's, and how many agreed. */
  compared30d: number; matched30d: number;
};

export type PlatformGovernance = {
  autonomy: number; executionEnabled: boolean; stopped: boolean; reason: string; revision: number;
  updatedBy: string; updatedAt: string | null; stored: boolean;
};

export type GovernanceReport = {
  available: boolean; canManage: boolean; platform: PlatformGovernance; agents: AgentGovernance[];
  promptVersion: string; priceCurrency: string; pricesConfigured: boolean;
  breakerDegradedAfter: number; breakerPauseAfter: number; breakerCoolDownMinutes: number;
};

/** The settable statuses; DEGRADED is the breaker's word and never stored. */
export const SETTABLE_STATUSES = ["ACTIVE", "PAUSED", "MAINTENANCE", "DISABLED"] as const;
export const PLATFORM_ID = "platform";

export const AUTONOMY_LABEL = ["L0 ปิด", "L1 อ่านอย่างเดียว", "L2 แนะนำ", "L3 ทำเมื่อมีผู้อนุมัติ", "L4 ทำตามกฎ"] as const;

export const GOVERNANCE_STATUS_LABEL: Record<string, string> = {
  ACTIVE: "ทำงาน", PAUSED: "หยุดชั่วคราว", DEGRADED: "ผิดปกติบางส่วน", MAINTENANCE: "ปิดปรับปรุง", DISABLED: "ปิด",
};

/** The platform's three positions: everything, read-and-recommend only, or nothing. */
export const PLATFORM_CHOICES = [
  { autonomy: 4, label: "เปิดการทำงาน" },
  { autonomy: 2, label: "อ่านและแนะนำเท่านั้น" },
  { autonomy: 0, label: "หยุด AI ทั้งหมด" },
] as const;

export const SAVE_ERRORS: Record<string, string> = {
  forbidden: "Administrator เท่านั้น",
  second_factor_required: "ต้องเข้าสู่ระบบด้วยการยืนยันสองขั้นตอนก่อน",
  reason_required: "ต้องระบุเหตุผล (ไม่เกิน 200 ตัวอักษร)",
  autonomy_above_design: "ระดับนี้สูงกว่าที่ Agent ออกแบบไว้",
  invalid_autonomy: "ระดับไม่ถูกต้อง",
  invalid_status: "สถานะไม่ถูกต้อง",
  unknown_agent: "ไม่พบ Agent นี้",
  conflict: "มีผู้เปลี่ยนค่าไปแล้ว — รีเฟรชแล้วลองใหม่",
  unavailable: "บันทึกไม่สำเร็จ ลองใหม่",
};

type Obj = Record<string, unknown>;
const obj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const str = (v: unknown, max = 400) => typeof v === "string" && v.length <= max;
const whole = (v: unknown, max = Number.MAX_SAFE_INTEGER) => typeof v === "number" && Number.isInteger(v) && v >= 0 && v <= max;
const money = (v: unknown) => v === null || typeof v === "number" && Number.isFinite(v) && v >= 0;
const when = (v: unknown) => v === null || str(v, 40) && !Number.isNaN(Date.parse(String(v)));
const level = (v: unknown) => whole(v, 4);

function usage(v: unknown): v is AgentUsage {
  return obj(v) && ["inputTokens24h", "outputTokens24h", "inputTokens30d", "outputTokens30d"].every(k => whole(v[k]))
    && money(v.cost24h) && money(v.cost30d);
}

function agent(v: unknown): v is AgentGovernance {
  return obj(v) && str(v.id, 40) && String(v.id).length > 0 && str(v.name, 80) && typeof v.flagEnabled === "boolean"
    && level(v.maxAutonomy) && level(v.autonomy) && level(v.effectiveAutonomy) && Number(v.effectiveAutonomy) <= Number(v.maxAutonomy)
    && typeof v.shadowMode === "boolean" && Object.hasOwn(GOVERNANCE_STATUS_LABEL, String(v.status))
    && Object.hasOwn(GOVERNANCE_STATUS_LABEL, String(v.effectiveStatus)) && str(v.reason, 200) && whole(v.revision)
    && str(v.updatedBy, 160) && when(v.updatedAt) && typeof v.stored === "boolean"
    && whole(v.runs24h) && whole(v.failures24h) && Number(v.failures24h) <= Number(v.runs24h)
    && (v.failureRate24h === null || typeof v.failureRate24h === "number" && v.failureRate24h >= 0 && v.failureRate24h <= 1)
    && whole(v.consecutiveFailures) && when(v.lastSuccess) && when(v.lastFailure)
    && (v.averageMs === null || whole(v.averageMs)) && ["Closed", "Degraded", "Open", "HalfOpen"].includes(String(v.breaker))
    && usage(v.usage) && whole(v.compared30d) && whole(v.matched30d) && Number(v.matched30d) <= Number(v.compared30d);
}

/** "8/10 (80%)", or a dash while nothing has been compared. */
export function agreementText(agent: Pick<AgentGovernance, "compared30d" | "matched30d">): string {
  return agent.compared30d > 0
    ? `${agent.matched30d}/${agent.compared30d} (${Math.round(agent.matched30d * 100 / agent.compared30d)}%)`
    : "—";
}

export function parseGovernance(v: unknown): GovernanceReport {
  const p = obj(v) ? v.platform : null;
  if (!(obj(v) && typeof v.available === "boolean" && typeof v.canManage === "boolean"
    && obj(p) && level(p.autonomy) && typeof p.executionEnabled === "boolean" && typeof p.stopped === "boolean"
    && p.executionEnabled === Number(p.autonomy) >= 3 && p.stopped === (Number(p.autonomy) === 0)
    && str(p.reason, 200) && whole(p.revision) && str(p.updatedBy, 160) && when(p.updatedAt) && typeof p.stored === "boolean"
    && Array.isArray(v.agents) && v.agents.length <= 20 && v.agents.every(agent)
    && new Set(v.agents.map(a => (a as AgentGovernance).id)).size === v.agents.length
    && str(v.promptVersion, 60) && str(v.priceCurrency, 8) && typeof v.pricesConfigured === "boolean"
    && whole(v.breakerDegradedAfter, 50) && whole(v.breakerPauseAfter, 50) && whole(v.breakerCoolDownMinutes, 240)))
    throw new Error("invalid_response");
  return v as unknown as GovernanceReport;
}

/** The levels an administrator may choose for one agent: up to what its code was built for. */
export function autonomyChoices(agent: Pick<AgentGovernance, "maxAutonomy">): number[] {
  return Array.from({ length: agent.maxAutonomy + 1 }, (_, i) => i);
}

/** A change as the API takes it; the reason is trimmed and required by the server. */
export function settingsBody(autonomy: number, shadowMode: boolean, status: string, reason: string, revision: number) {
  return { autonomy, shadowMode, status, reason: reason.trim(), revision };
}

/** A cost with its currency, "ไม่ได้ตั้งราคา" when none is configured, "ราคาไม่ครบ" when a model has none. */
export function costText(cost: number | null, currency: string, pricesConfigured: boolean): string {
  if (!pricesConfigured) return "ไม่ได้ตั้งราคา";
  if (cost === null) return "ราคาไม่ครบ";
  return `${cost.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;
}

export function saveError(code: unknown): string {
  return typeof code === "string" && Object.hasOwn(SAVE_ERRORS, code) ? SAVE_ERRORS[code] : SAVE_ERRORS.unavailable;
}
