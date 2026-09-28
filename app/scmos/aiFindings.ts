/**
 * What the rule-first agents concluded — the decision log as the Control Tower
 * shows it (Agent Platform, 28 Sep 2026). Facts, rule results, inferences and
 * recommendations stay four separate lists all the way to the screen; a
 * response that does not keep them apart is not drawn.
 */

export type Finding = { text: string; source: string | null };
export type Findings = {
  facts: Finding[]; ruleResults: Finding[]; observations: Finding[];
  inferences: Finding[]; recommendations: Finding[]; blockingIssues: Finding[];
};
export type Decision = {
  id: number; agentId: string; decisionType: string; entityType: string; entityId: string; summary: string;
  resultStatus: string; status: string; riskLevel: string; shadow: boolean; autonomy: number; findings: Findings;
  ruleReferences: string[]; createdAt: string; humanChoice: string; overrideReason: string; decidedBy: string;
};
export type DecisionPage = { items: Decision[]; total: number };

export const AGENT_LABEL: Record<string, string> = { "otd-agent": "OTD", "validation-agent": "ตรวจข้อมูล", "vendor-agent": "ผู้ขนส่ง" };

/** Most serious first; the OTD words and the validation words share one order. */
export const RISK_ORDER = ["CRITICAL", "HIGH", "WATCH", "MEDIUM", "LOW", "NORMAL", ""] as const;
export const RISK_TONE: Record<string, string> = { CRITICAL: "red", HIGH: "red", WATCH: "amber", MEDIUM: "amber", LOW: "blue", NORMAL: "muted" };
export const RISK_LABEL: Record<string, string> = {
  CRITICAL: "วิกฤต", HIGH: "สูง", WATCH: "เฝ้าดู", MEDIUM: "ข้อมูลไม่ครบ", LOW: "ควรตรวจ", NORMAL: "ปกติ",
};

export const ANSWER_ERRORS: Record<string, string> = {
  forbidden: "เจ้าของงานหรือหัวหน้างานเท่านั้นที่ตอบได้",
  not_found: "ไม่พบรายการนี้",
  already_answered: "รายการนี้มีคำตอบแล้ว หรือรอบตรวจใหม่ปิดไปแล้ว",
  override_needs_choice_and_reason: "ระบุสิ่งที่ทำแทนและเหตุผล",
  invalid_outcome: "คำตอบไม่ถูกต้อง",
  invalid_text: "ข้อความยาวเกินหรือมีอักขระที่ใช้ไม่ได้",
};

type Obj = Record<string, unknown>;
const obj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const text = (v: unknown, max = 400) => typeof v === "string" && v.length <= max;
const finding = (v: unknown) => obj(v) && text(v.text) && String(v.text).length > 0 && (v.source === null || text(v.source, 120));
const list = (v: unknown) => Array.isArray(v) && v.length <= 20 && v.every(finding);

function decision(v: unknown): v is Decision {
  if (!obj(v) || !obj(v.findings)) return false;
  const f = v.findings;
  return Number.isInteger(v.id) && Number(v.id) > 0 && text(v.agentId, 40) && text(v.decisionType, 40)
    && text(v.entityType, 20) && text(v.entityId, 80) && String(v.entityId).length > 0 && text(v.summary)
    && text(v.resultStatus, 30) && text(v.status, 20) && (RISK_ORDER as readonly string[]).includes(String(v.riskLevel))
    && typeof v.shadow === "boolean" && Number.isInteger(v.autonomy)
    && ["facts", "ruleResults", "observations", "inferences", "recommendations", "blockingIssues"].every(k => list(f[k]))
    // Evidence before inference: a fact or a rule result without its source is not one.
    && (f.facts as Finding[]).every(one => one.source !== null) && (f.ruleResults as Finding[]).every(one => one.source !== null)
    && Array.isArray(v.ruleReferences) && v.ruleReferences.length <= 50 && v.ruleReferences.every(r => text(r, 120))
    && text(v.createdAt, 40) && !Number.isNaN(Date.parse(String(v.createdAt)))
    && text(v.humanChoice) && text(v.overrideReason) && text(v.decidedBy, 160);
}

export function parseDecisions(v: unknown): DecisionPage {
  if (!(obj(v) && Array.isArray(v.items) && v.items.length <= 200 && v.items.every(decision)
    && Number.isInteger(v.total) && Number(v.total) >= v.items.length)) throw new Error("invalid_response");
  return { items: v.items as Decision[], total: Number(v.total) };
}

/** Most serious first, and within a level the newest. */
export function inReadingOrder(items: readonly Decision[]): Decision[] {
  const rank = (d: Decision) => (RISK_ORDER as readonly string[]).indexOf(d.riskLevel);
  return [...items].sort((a, b) => rank(a) - rank(b) || Date.parse(b.createdAt) - Date.parse(a.createdAt) || b.id - a.id);
}

/** An answer as the API takes it: an override names what was done instead and why. */
export function answerBody(outcome: "ACCEPTED" | "OVERRIDDEN" | "DISMISSED", choice = "", reason = "") {
  return { outcome, choice: choice.trim(), reason: reason.trim() };
}

export function answerError(code: unknown): string {
  return typeof code === "string" && Object.hasOwn(ANSWER_ERRORS, code) ? ANSWER_ERRORS[code] : "บันทึกไม่สำเร็จ ลองใหม่";
}
