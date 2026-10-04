export type AgentPolicy = {
  id: string; name: string; status: string; reasonCode: string; policyVersion: string;
  allowedTools: string[]; read: string[]; analyze: string[]; draft: string[]; execute: string[];
  humanApproval: string[]; forbidden: string[]; budget: { dailyCostLimit: number; monthlyCostLimit: number } | null;
  humanOwner?: string | null; fallbackOwner?: string | null;
  reservedCostMonth: number | null; lastExecution: string | null; lastSecurityEvent: string | null;
};
export type SharedBudget = { scope: "all-registered-agents"; currency: "USD"; dailyCostLimit: number | null;
  monthlyCostLimit: number; reservedCostMonth: number | null; remainingCostMonth: number | null; periodTimeZone: "UTC" };
export type PolicyReport = { policyVersion: string; valid: boolean; auditAvailable: boolean; readOnly: true;
  agents: AgentPolicy[]; sharedBudget?: SharedBudget | null };
const object = (value: unknown): value is Record<string, unknown> => typeof value === "object" && value !== null && !Array.isArray(value);
const strings = (value: unknown): value is string[] => Array.isArray(value) && value.every(item => typeof item === "string");
const optionalStamp = (value: unknown) => value === null || (typeof value === "string" && Number.isFinite(Date.parse(value)));
const money = (value: unknown): value is number => typeof value === "number" && Number.isFinite(value) && value >= 0;
const agentIds = new Set(["operations-agent", "vendor-agent", "rate-agent", "data-agent", "incident-agent", "document-agent",
  "compliance-agent", "management-agent", "communication-agent", "engineering-agent", "otd-agent", "validation-agent", "booking-agent", "sre-agent"]);
export function parsePolicyReport(value: unknown): PolicyReport {
  if (!object(value) || typeof value.policyVersion !== "string" || typeof value.valid !== "boolean"
    || typeof value.auditAvailable !== "boolean" || value.readOnly !== true || !Array.isArray(value.agents)
    || value.agents.length !== 14) throw new Error("invalid_response");
  if (value.sharedBudget !== undefined && value.sharedBudget !== null) {
    const shared = value.sharedBudget;
    if (!object(shared) || shared.scope !== "all-registered-agents" || shared.currency !== "USD" || shared.periodTimeZone !== "UTC"
      || !money(shared.monthlyCostLimit) || shared.monthlyCostLimit <= 0
      || !(shared.dailyCostLimit === null || (money(shared.dailyCostLimit) && shared.dailyCostLimit > 0 && shared.dailyCostLimit <= shared.monthlyCostLimit))
      || !(shared.reservedCostMonth === null || money(shared.reservedCostMonth))
      || !(shared.remainingCostMonth === null || (money(shared.remainingCostMonth) && shared.remainingCostMonth <= shared.monthlyCostLimit)))
      throw new Error("invalid_response");
  }
  const ids = new Set<string>();
  for (const agent of value.agents) {
    if (!object(agent) || ![agent.id, agent.name, agent.reasonCode, agent.policyVersion].every(item => typeof item === "string")
      || !["POLICY_READY", "CONFIGURATION_REQUIRED"].includes(String(agent.status))
      || ![agent.allowedTools, agent.read, agent.analyze, agent.draft, agent.execute, agent.humanApproval, agent.forbidden].every(strings)
      || !optionalStamp(agent.lastExecution) || !optionalStamp(agent.lastSecurityEvent)
      || ![agent.humanOwner, agent.fallbackOwner].every(owner => owner === undefined || owner === null || typeof owner === "string")
      || !(agent.reservedCostMonth === null || money(agent.reservedCostMonth))
      || !(agent.budget === null || (object(agent.budget) && money(agent.budget.dailyCostLimit) && money(agent.budget.monthlyCostLimit))))
      throw new Error("invalid_response");
    if (!agentIds.has(agent.id as string) || ids.has(agent.id as string) || agent.policyVersion !== value.policyVersion)
      throw new Error("invalid_response");
    ids.add(agent.id as string);
  }
  return value as PolicyReport;
}
