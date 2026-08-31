/**
 * The stages a CAR/PAR case moves through, and what they are called.
 *
 * Lifted out of the Incidents screen because the dashboard now counts cases by
 * stage as well. Two screens reading the same vocabulary from two copies of it
 * is how this project has gone wrong before: the copies drift, and the one
 * nobody is looking at drifts first.
 *
 * The order is the quality process the team already runs on paper, so a
 * breakdown reads as a pipeline rather than a bag of labels.
 */
export const STAGES = ["open", "analysis", "action", "follow-up", "monitoring", "approval", "closed"];

export const STAGE_TH: Record<string, string> = {
  open: "เปิดเคส",
  analysis: "วิเคราะห์",
  action: "กำหนดการแก้ไข",
  "follow-up": "ติดตาม",
  monitoring: "ติดตามประสิทธิผล",
  approval: "รออนุมัติ",
  closed: "ปิดแล้ว",
};

/** A stage in Thai, or the raw value when the API grows one this does not know. */
export const stageLabel = (stage: string): string => STAGE_TH[stage] ?? stage;

/**
 * How many cases sit at each stage, in process order.
 *
 * Every stage is present even at zero, because "nothing is waiting for
 * approval" is a thing worth seeing rather than a row that quietly vanishes —
 * and a pipeline with a gap in the middle reads differently from a short one.
 */
export function byStage(cases: { stage: string }[]): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const stage of STAGES) counts[stageLabel(stage)] = 0;
  for (const one of cases) counts[stageLabel(one.stage)] = (counts[stageLabel(one.stage)] ?? 0) + 1;
  return counts;
}
