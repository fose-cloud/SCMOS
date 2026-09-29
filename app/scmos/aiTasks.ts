/**
 * The AI Control Tower's summary cards and "My AI Tasks" (Agent Platform
 * spec §43, §44): what the agents hold for this person, counted by the server
 * in the decision list's own scope (`GET /api/ai/tasks`), and the person's own
 * open decisions grouped the way the spec asks — so nobody has to open every
 * normal job to find the few that need them. Parsing is strict.
 */

import type { Decision } from "./aiFindings";

export type AiTaskCounts = {
  jobsMonitored: number; aiHandling: number; needsMyDecision: number; highRisk: number; blocked: number;
  informationRequired: number; carrierEscalation: number; pendingApproval: number; agentFailure: number;
};

/** The cards, in the spec's order; the label is the figure's name, nothing more. */
export const TASK_CARDS: { key: keyof AiTaskCounts; label: string; tone: "" | "red" | "amber" }[] = [
  { key: "jobsMonitored", label: "งานที่ AI ดูอยู่", tone: "" },
  { key: "aiHandling", label: "รายการที่ AI พบ", tone: "" },
  { key: "needsMyDecision", label: "รอฉันตอบ", tone: "amber" },
  { key: "highRisk", label: "เสี่ยงสูง", tone: "red" },
  { key: "pendingApproval", label: "รออนุมัติ", tone: "amber" },
  { key: "carrierEscalation", label: "ผู้ขนส่งต้องตาม", tone: "amber" },
  { key: "informationRequired", label: "ข้อมูลไม่ครบ", tone: "amber" },
  { key: "agentFailure", label: "Agent ผิดปกติ", tone: "red" },
];

export type TaskSection = "blocked" | "high_risk" | "carrier" | "information" | "decision";

/** My AI Tasks' sections, most urgent first — the server's SectionOf, read the same way. */
export const SECTIONS: { id: TaskSection; label: string }[] = [
  { id: "blocked", label: "AI ถูกบล็อก" },
  { id: "high_risk", label: "เสี่ยงสูง" },
  { id: "carrier", label: "ผู้ขนส่งต้องตาม" },
  { id: "information", label: "ต้องการข้อมูล" },
  { id: "decision", label: "รอฉันตัดสินใจ" },
];

const KEYS: (keyof AiTaskCounts)[] = ["jobsMonitored", "aiHandling", "needsMyDecision", "highRisk", "blocked",
  "informationRequired", "carrierEscalation", "pendingApproval", "agentFailure"];
const count = (v: unknown) => typeof v === "number" && Number.isInteger(v) && v >= 0;

export function parseTasks(v: unknown): AiTaskCounts {
  if (!(typeof v === "object" && v !== null && !Array.isArray(v))) throw new Error("invalid_response");
  const o = v as Record<string, unknown>;
  if (!KEYS.every(key => count(o[key]))
    // What a person may answer is part of what they may see; the rest are parts of what the AI holds.
    || Number(o.needsMyDecision) > Number(o.aiHandling)
    || Number(o.highRisk) > Number(o.aiHandling) || Number(o.blocked) > Number(o.aiHandling)
    || Number(o.informationRequired) > Number(o.aiHandling) || Number(o.carrierEscalation) > Number(o.aiHandling))
    throw new Error("invalid_response");
  return Object.fromEntries(KEYS.map(key => [key, Number(o[key])])) as AiTaskCounts;
}

const CARRIER_REMINDER = "Template:CARRIER_CONFIRMATION_REMINDER";

/** Which section of My AI Tasks a decision belongs to — the first that fits, most urgent first. */
export function sectionOf(d: Pick<Decision, "resultStatus" | "riskLevel" | "agentId" | "ruleReferences">): TaskSection {
  if (d.resultStatus === "BLOCKED") return "blocked";
  if (d.riskLevel === "HIGH" || d.riskLevel === "CRITICAL") return "high_risk";
  if ((d.agentId === "vendor-agent" && d.ruleReferences.includes("Carrier.NoEligible"))
    || (d.agentId === "communication-agent" && d.ruleReferences.includes(CARRIER_REMINDER))) return "carrier";
  if (d.resultStatus === "INSUFFICIENT_INFORMATION") return "information";
  return "decision";
}

/** The person's own tasks: the decisions they may answer, by section, sections with nothing left out. */
export function myTasks<T extends Pick<Decision, "canAnswer" | "resultStatus" | "riskLevel" | "agentId" | "ruleReferences">>(
  items: readonly T[]): { id: TaskSection; label: string; items: T[] }[] {
  return SECTIONS.map(section => ({ ...section, items: items.filter(d => d.canAnswer && sectionOf(d) === section.id) }))
    .filter(section => section.items.length > 0);
}
