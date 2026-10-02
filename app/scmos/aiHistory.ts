/**
 * The AI history's search (Agent Platform spec §47): the agents' decisions of
 * every status (`GET /api/ai/decisions`) and the model runs
 * (`GET /api/ai/audit`), filtered by day, agent, action, result, job,
 * customer, carrier and person. The server validates every filter; this only
 * builds the query, leaving out what is blank.
 */

export type DecisionQuery = {
  from: string; to: string; agent: string; status: string; type: string;
  q: string; customer: string; carrier: string; decidedBy: string;
};
export type AuditQuery = { from: string; to: string; agent: string; tool: string; result: string; user: string; key: string };

export const EMPTY_DECISION_QUERY: DecisionQuery = { from: "", to: "", agent: "", status: "", type: "", q: "", customer: "", carrier: "", decidedBy: "" };
export const EMPTY_AUDIT_QUERY: AuditQuery = { from: "", to: "", agent: "", tool: "", result: "", user: "", key: "" };

/** What happened to a decision — the log's own statuses. */
export const DECISION_STATUS: Record<string, string> = {
  OPEN: "รอคำตอบ", ACCEPTED: "ยอมรับ", OVERRIDDEN: "ทำอย่างอื่น", DISMISSED: "ไม่เกี่ยว", SUPERSEDED: "ถูกแทนที่", RESOLVED: "ปิดแล้ว",
};

/** The kinds of decision the agents write — the spec's "Action". */
export const DECISION_TYPE: Record<string, string> = {
  otd_risk: "ความเสี่ยงล่าช้า", validation: "ตรวจข้อมูล", carrier_candidates: "ผู้ขนส่งที่ควรถาม",
  communication_draft: "ร่างข้อความ", booking_draft: "ร่าง booking", not_booking: "ไม่ใช่ booking",
};

/** How a model run ended — the audit's own words; "incomplete" is a run that never recorded its end. */
export const RUN_RESULTS = ["succeeded", "failed", "cancelled", "timeout", "provider_unavailable", "provider_busy",
  "source_unavailable", "clarification_required", "invalid_tool", "not_connected", "audit_failed", "incomplete"] as const;

export const AUDIT_AGENTS = ["operations-agent", "data-agent", "communication-agent", "document-agent", "engineering-agent",
  "sre-agent", "management-agent", "booking-agent"] as const;
export const AUDIT_TOOLS = ["query_shipments", "search_shipment", "query_delays", "query_followup", "query_kpi", "query_messages",
  "query_documents", "extract_document", "analyze_billing", "query_repository", "read_source", "query_platform", "draft_booking",
  "summarize_evaluation"] as const;

const DAY = /^\d{4}-\d{2}-\d{2}$/;

function params(entries: [string, string][]): string {
  const kept = entries.map(([key, value]) => [key, value.trim()] as const).filter(([, value]) => value.length > 0);
  return kept.map(([key, value]) => `${key}=${encodeURIComponent(value)}`).join("&");
}

/** Why the search cannot be asked as it stands, or "". */
export function queryProblem(q: { from: string; to: string }): string {
  if ((q.from && !DAY.test(q.from)) || (q.to && !DAY.test(q.to))) return "วันที่ไม่ถูกต้อง";
  if (q.from && q.to && q.to < q.from) return "วันสิ้นสุดอยู่ก่อนวันเริ่ม";
  return "";
}

export function decisionsUrl(q: DecisionQuery, page: number, pageSize = 25): string {
  const rest = params([["from", q.from], ["to", q.to], ["agent", q.agent], ["status", q.status], ["type", q.type],
    ["q", q.q], ["customer", q.customer], ["carrier", q.carrier], ["decidedBy", q.decidedBy]]);
  return `/api/ai/decisions?page=${Math.max(1, page)}&pageSize=${pageSize}` + (rest ? "&" + rest : "");
}

export function auditUrl(q: AuditQuery, beforeId: number | null, take = 8): string {
  const rest = params([["agent", q.agent], ["tool", q.tool], ["result", q.result], ["user", q.user], ["from", q.from], ["to", q.to], ["key", q.key]]);
  return `/api/ai/audit?take=${take}` + (beforeId ? `&beforeId=${beforeId}` : "") + (rest ? "&" + rest : "");
}
