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
/** The Data Agent's figure (Phase 2): SCMOS's own KPI for a period, with the rule and its version on it. */
export type KpiCarrier = { carrier: string; total: number; measured: number; onTime: number; percent: number };
export type KpiAnswer = {
  view: string; period: string; periodLabel: string; filters: { customer: string; trucker: string; owner: string };
  total: number; measured: number; onTime: number; onTimePercent: number; notAssessable: number;
  undated: number; formatErrors: number; actionRequired: number;
  byCategory: { label: string; value: number }[];
  carriers: KpiCarrier[]; carriersTotal: number; returned: number; truncated: boolean;
  rule: { id: string; version: string; source: string; meaning: string; missingData: string };
  customerContract: string; retrievedAt: string; sourceUpdatedAt: string | null; basis: string; source: string;
};
/** The Communication Agent's messages (Phase 4): what carriers said, as the LINE parser and the mail links already read it. */
export type MessageRow = {
  id: string; channel: string; at: string; group: string;
  jobKey: string; jobCode: string; customer: string; trucker: string;
  status: string | null; arrival: string | null; eta: string | null; plate: string | null; container: string | null; seal: string | null;
  delayed: boolean; delayCategory: string | null; question: boolean;
  state: string; detail: string; excerpt: string; source: string;
};
export type MessagesAnswer = {
  view: string; asOfDate: string; timeZone: string; window: string;
  total: number; returned: number; truncated: boolean;
  waiting: number; applied: number; unmatched: number; ignored: number; mails: number;
  jobs: { key: string; jobCode: string; customer: string; trucker: string; ownerId: string; category: string; date: string; status: string }[];
  retrievedAt: string; basis: string; rows: MessageRow[];
};
/** The Document & Invoice Agent's paperwork (Phase 5): what is filed and owed, by the screens' own rules — no file opened, nothing approved. */
export type DocumentRow = {
  id: string; kind: string; jobKey: string; jobCode: string; customer: string; trucker: string; category: string; date: string; status: string;
  folder: string; fileName: string; docKind: string; uploadedBy: string; uploadedAt: string | null; expiryDate: string; daysLeft: number | null; owner: string;
  state: string; detail: string; source: string;
};
export type DocumentsAnswer = {
  view: string; asOfDate: string; timeZone: string; window: string;
  total: number; returned: number; truncated: boolean;
  held: number; missing: number; blocking: number; unclear: number;
  inTime: number; late: number; due: number; overdue: number; expiring: number; expired: number;
  jobs: { key: string; jobCode: string; customer: string; trucker: string; ownerId: string; category: string; date: string; arrDate: string; status: string; container: string;
    missing: number; missingBlocking: number; unclear: number; missingFolders: string[] }[];
  retrievedAt: string; rule: string; basis: string; rows: DocumentRow[];
};
/** Phase 6 first read: fixed public GitHub repository metadata; bodies never enter this contract. */
export type EngineeringAnswer = {
  view: "open_issues" | "open_prs" | "recent_commits";
  repository: "fose-cloud/SCMOS"; total: number; returned: number;
  retrievedAt: string; basis: string;
  rows: { id: string; title: string; url: string; state: "open" | "commit"; at: string }[];
};
/**
 * Phase 6, second increment: the Engineering Agent's bounded, read-only source
 * read — each step a listing or a numbered window of a file at the fixed ref —
 * and the model's analysis of it, which is the model's opinion and labelled so.
 * Nothing was run, changed or deployed.
 */
export type SourceStep = {
  step: number; mode: "list" | "file"; path: string; from: number; lines: number; returned: number; totalLines: number; truncated: boolean;
  size: number; sha: string; entries: { name: string; path: string; kind: "file" | "dir"; size: number }[]; text: string; source: "github_public_repo";
};
export type SourceAnswer = {
  repository: "fose-cloud/SCMOS"; ref: "azure-dotnet-migration"; total: number; returned: number; retrievedAt: string;
  steps: SourceStep[]; analysis: string; basis: string;
};
/** Phase 7: the SRE Agent's platform signals — measured by the API about itself, read from GitHub for deployments; safe identifiers only, nothing acted on. */
export type PlatformSignal = {
  id: string; kind: "health" | "deployment" | "error"; label: string; value: string; detail: string; at: string | null;
  state: "ok" | "warn" | "bad" | "unknown"; source: string;
};
export type PlatformAnswer = {
  view: "health" | "deployments" | "errors" | "requests"; window: string; total: number; returned: number; truncated: boolean;
  retrievedAt: string; basis: string; rows: PlatformSignal[];
};
/** Phase 8: server-composed findings from fixed, independently authorised specialist reads. */
export type CollaborationAnswer = {
  plan: "summarise_job" | "summarise_late_paperwork"; title: string; steps: number;
  trail: { step: number; agentId: string; tool: string; view: string; purpose: string;
    total: number; returned: number; truncated: boolean; status: "succeeded" }[];
  findings: { id: string; label: string; value: string; detail: string; jobKeys: string[] }[];
  retrievedAt: string; basis: string;
};
export type AiReply = {
  runId: string; code: string; summary: string; agentId: string | null; mock: boolean; usage: Usage | null; evidence: OperationsAnswer | null;
  /** The API request's correlation id — the same one on every audit event of the run (1D). */
  correlationId?: string;
  /** Whether the previous question's facts were given to the model (1D context pilot). */
  contextUsed?: boolean;
  /** The Data Agent's figure (Phase 2); null or absent for every other agent. */
  kpi?: KpiAnswer | null;
  /** The Communication Agent's messages (Phase 4); null or absent for every other agent. */
  messages?: MessagesAnswer | null;
  /** The Document & Invoice Agent's paperwork (Phase 5); null or absent for every other agent. */
  documents?: DocumentsAnswer | null;
  engineering?: EngineeringAnswer | null;
  /** The Engineering Agent's source read and the model's analysis (Phase 6, second increment); null or absent for every other run. */
  source?: SourceAnswer | null;
  /** The SRE Agent's platform signals (Phase 7); null or absent for every other agent. */
  platform?: PlatformAnswer | null;
  collaboration?: CollaborationAnswer | null;
};
export type AgentChoice = "operations-agent" | "data-agent" | "communication-agent" | "document-agent" | "engineering-agent" | "sre-agent" | "management-agent";
export type AuditEvent = { event: string; status: string; at: string; total: number | null; returned: number | null; step?: number | null; tool?: string | null };
export type AuditRun = {
  runId: string; userId: string; role: string; agentId: string; model: string;
  scope: { team: boolean; operatorId: string | null }; status: string; startedAt: string; completedAt: string | null;
  toolCallId: string | null; tool: string | null; toolStatus: string | null; view: string | null; limit: number | null;
  risk: string; approvalStatus: string; source: string; sourceKeys: string[];
  total: number | null; returned: number | null; usage: Usage | null; events: AuditEvent[];
  /** The API request the run belongs to (1D); empty for older runs. */
  correlationId?: string;
  /** Tool steps completed (1D); a run may hold up to eight, today's dispatch allows one. */
  steps?: number;
};

/** The most events one run may carry: run_started, eight steps of two, run_completed. */
export const MAX_AUDIT_EVENTS = 18;
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
    // Administrators can see all ten registered agents. Keep a bounded response,
    // but do not reject the entire status (including the Operations switch).
    && Array.isArray(v.agents) && v.agents.length <= 16 && v.agents.every(a =>
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
function kpiAnswer(v: unknown): boolean {
  return obj(v) && v.view === "kpi" && strings(v, ["period", "periodLabel", "customerContract", "retrievedAt", "basis"]) && v.source === "operation_jobs"
    && obj(v.filters) && strings(v.filters, ["customer", "trucker", "owner"])
    && ["total", "measured", "onTime", "onTimePercent", "notAssessable", "undated", "formatErrors", "actionRequired", "carriersTotal", "returned"].every(k => count(v[k]))
    && (v.measured as number) <= (v.total as number) && (v.onTime as number) <= (v.measured as number) && (v.onTimePercent as number) <= 100
    && typeof v.truncated === "boolean" && nullableText(v.sourceUpdatedAt)
    && Array.isArray(v.byCategory) && v.byCategory.length <= 20 && v.byCategory.every(c => obj(c) && text(c.label, 80) && count(c.value))
    && Array.isArray(v.carriers) && v.carriers.length <= 50 && v.carriers.length === v.returned
    && v.carriers.every(c => obj(c) && text(c.carrier, 80) && ["total", "measured", "onTime", "percent"].every(k => count(c[k])))
    && obj(v.rule) && strings(v.rule, ["id", "version", "source", "meaning", "missingData"]);
}
const MESSAGE_STATES = ["applied", "waiting", "unmatched", "ignored", "failed", "pending", "linked", "suggested", "rejected"];
function messagesAnswer(v: unknown): boolean {
  return obj(v) && ["job", "waiting", "unmatched", "today"].includes(v.view as string) && strings(v, ["asOfDate", "timeZone", "window", "retrievedAt", "basis"])
    && ["total", "returned", "waiting", "applied", "unmatched", "ignored", "mails"].every(k => count(v[k])) && typeof v.truncated === "boolean"
    && Array.isArray(v.jobs) && v.jobs.length <= 5 && v.jobs.every(j => obj(j) && strings(j, ["key", "jobCode", "customer", "trucker", "ownerId", "category", "date", "status"]))
    && Array.isArray(v.rows) && v.rows.length <= 50 && v.rows.length === v.returned && (v.returned as number) <= (v.total as number)
    && v.rows.every(r => obj(r) && strings(r, ["id", "channel", "at", "group", "jobKey", "jobCode", "customer", "trucker", "state", "detail", "excerpt"])
      && ["line", "tms", "mail"].includes(r.channel as string) && MESSAGE_STATES.includes(r.state as string) && ["line_events", "emails"].includes(r.source as string)
      && ["status", "arrival", "eta", "plate", "container", "seal", "delayCategory"].every(k => nullableText(r[k])) && typeof r.delayed === "boolean" && typeof r.question === "boolean");
}
const DOCUMENT_STATES = ["held", "unclear", "missing", "blocking", "filed_in_time", "filed_late", "due", "overdue", "expiring", "expired"];
function documentsAnswer(v: unknown): boolean {
  return obj(v) && ["job", "missing", "invoice", "expiring"].includes(v.view as string) && strings(v, ["asOfDate", "timeZone", "window", "retrievedAt", "rule", "basis"])
    && ["total", "returned", "held", "missing", "blocking", "unclear", "inTime", "late", "due", "overdue", "expiring", "expired"].every(k => count(v[k]))
    && typeof v.truncated === "boolean"
    && Array.isArray(v.jobs) && v.jobs.length <= 50 && v.jobs.every(j => obj(j)
      && strings(j, ["key", "jobCode", "customer", "trucker", "ownerId", "category", "date", "arrDate", "status", "container"])
      && ["missing", "missingBlocking", "unclear"].every(k => count(j[k])) && Array.isArray(j.missingFolders) && j.missingFolders.every(f => text(f, 20)))
    && Array.isArray(v.rows) && v.rows.length <= 50 && v.rows.length === v.returned && (v.returned as number) <= (v.total as number)
    && v.rows.every(r => obj(r) && strings(r, ["id", "jobKey", "jobCode", "customer", "trucker", "category", "date", "status", "folder", "fileName", "docKind", "uploadedBy", "expiryDate", "owner", "state", "detail"])
      && ["document", "checklist", "job"].includes(r.kind as string) && DOCUMENT_STATES.includes(r.state as string) && r.source === "documents"
      && nullableText(r.uploadedAt) && nullableCount(r.daysLeft));
}
function engineeringAnswer(v: unknown): boolean {
  if (!obj(v) || !["open_issues", "open_prs", "recent_commits"].includes(String(v.view))
    || v.repository !== "fose-cloud/SCMOS" || !text(v.retrievedAt, 64) || !text(v.basis, 1000)
    || !count(v.total) || !count(v.returned) || v.total !== v.returned
    || !Array.isArray(v.rows) || v.rows.length > 20 || v.rows.length !== v.returned) return false;
  const kind = v.view === "open_issues" ? "issue" : v.view === "open_prs" ? "pr" : "commit";
  const path = kind === "issue" ? "issues" : kind === "pr" ? "pull" : "commit";
  const seen = new Set<string>();
  return v.rows.every(row => {
    if (!obj(row) || !text(row.id, 80) || !text(row.title, 160) || !row.title
      || !text(row.url, 200) || !text(row.at, 64) || row.state !== (kind === "commit" ? "commit" : "open")) return false;
    const suffix = row.id.startsWith(kind + ":") ? row.id.slice(kind.length + 1) : "";
    if (!(kind === "commit" ? /^[a-fA-F0-9]{40}$/.test(suffix) : /^[1-9]\d*$/.test(suffix))
      || row.url !== `https://github.com/fose-cloud/SCMOS/${path}/${suffix}` || seen.has(row.id)) return false;
    seen.add(row.id);
    return !Array.from(row.title).some(c => c.charCodeAt(0) < 32 || c.charCodeAt(0) === 127)
      && !Object.hasOwn(row, "body") && !Object.hasOwn(row, "diff");
  });
}
function sourceAnswer(v: unknown): boolean {
  if (!obj(v) || v.repository !== "fose-cloud/SCMOS" || v.ref !== "azure-dotnet-migration" || !text(v.retrievedAt, 64)
    || !text(v.basis, 1000) || typeof v.analysis !== "string" || v.analysis.length > 8200
    || !count(v.total) || !count(v.returned) || v.total !== v.returned
    || !Array.isArray(v.steps) || v.steps.length < 1 || v.steps.length > 4 || v.steps.length !== v.returned) return false;
  return v.steps.every((step, index) => obj(step) && step.step === index + 1 && ["list", "file"].includes(step.mode as string)
    && typeof step.path === "string" && step.path.length <= 200 && !/\.\.|^\/|\\/.test(step.path)
    && ["from", "lines", "returned", "totalLines", "size"].every(k => count(step[k]) || step[k] === 0) && typeof step.truncated === "boolean"
    && typeof step.sha === "string" && step.sha.length <= 64 && step.source === "github_public_repo"
    && typeof step.text === "string" && step.text.length <= 200000
    && Array.isArray(step.entries) && step.entries.length <= 50 && step.entries.every(entry => obj(entry) && text(entry.name, 200) && text(entry.path, 200)
      && ["file", "dir"].includes(entry.kind as string) && (count(entry.size) || entry.size === 0))
    && !Object.hasOwn(step, "diff") && !Object.hasOwn(step, "command"));
}
function platformAnswer(v: unknown): boolean {
  return obj(v) && ["health", "deployments", "errors", "requests"].includes(v.view as string) && strings(v, ["window", "retrievedAt", "basis"])
    && count(v.total) && count(v.returned) && typeof v.truncated === "boolean" && (v.returned as number) <= (v.total as number)
    && Array.isArray(v.rows) && v.rows.length <= 50 && v.rows.length === v.returned
    && v.rows.every(r => obj(r) && strings(r, ["id", "label", "value", "detail", "source"]) && ["health", "deployment", "error"].includes(r.kind as string)
      && ["ok", "warn", "bad", "unknown"].includes(r.state as string) && nullableText(r.at)
      && !/https?:\/\//.test(String(r.detail)) && !/\w@\w/.test(String(r.detail)) && !Object.hasOwn(r, "secret") && !Object.hasOwn(r, "connectionString"));
}
function collaborationAnswer(v: unknown): boolean {
  if (!obj(v) || !["summarise_job", "summarise_late_paperwork"].includes(String(v.plan))
    || !text(v.title, 120) || !text(v.retrievedAt, 64) || !text(v.basis, 1600)
    || !count(v.steps) || !Array.isArray(v.trail) || v.trail.length !== v.steps
    || v.trail.length < 2 || v.trail.length > 3 || !Array.isArray(v.findings) || v.findings.length > 4) return false;
  const expected = v.plan === "summarise_job"
    ? [["operations-agent", "search_shipment", "search"], ["document-agent", "query_documents", "job"], ["communication-agent", "query_messages", "job"]]
    : [["operations-agent", "query_delays", "delays"], ["document-agent", "query_documents", "missing"]];
  return v.trail.length === expected.length && v.trail.every((step, index) => obj(step) && step.step === index + 1
    && step.agentId === expected[index][0] && step.tool === expected[index][1] && step.view === expected[index][2]
    && text(step.purpose, 160) && count(step.total) && count(step.returned) && (step.returned as number) <= (step.total as number)
    && (step.returned as number) <= 50 && typeof step.truncated === "boolean" && step.truncated === (step.total !== step.returned)
    && step.status === "succeeded")
    && v.findings.every(f => obj(f) && strings(f, ["id", "label", "value", "detail"])
      && Array.isArray(f.jobKeys) && f.jobKeys.length <= 50 && f.jobKeys.every(k => text(k, 80)));
}
export function parseReply(v: unknown): AiReply {
  return accept(v, obj(v) && id(v.runId) && v.code === "ok" && text(v.summary) && nullableText(v.agentId)
    && typeof v.mock === "boolean" && usage(v.usage)
    // The Operations evidence for the Operations agent; the KPI figure for the Data Agent; the messages for the Communication Agent; a mock carries none.
    && (v.mock ? v.evidence === null
      : v.agentId === "data-agent" ? v.evidence === null && kpiAnswer(v.kpi)
      : v.agentId === "communication-agent" ? v.evidence === null && messagesAnswer(v.messages)
      : v.agentId === "document-agent" ? v.evidence === null && documentsAnswer(v.documents)
      // The Engineering Agent answers with metadata or with a source read — one, never both, never neither.
      : v.agentId === "engineering-agent" ? v.evidence === null
        && ((v.source === undefined || v.source === null) ? engineeringAnswer(v.engineering) : (v.engineering === undefined || v.engineering === null) && sourceAnswer(v.source))
      : v.agentId === "sre-agent" ? v.evidence === null && platformAnswer(v.platform)
      : v.agentId === "management-agent" ? collaborationAnswer(v.collaboration) && evidence(v.evidence)
        && documentsAnswer(v.documents) && ((v.collaboration as CollaborationAnswer).plan === "summarise_job"
          ? (v.evidence as OperationsAnswer).view === "search" && (v.documents as DocumentsAnswer).view === "job"
            && messagesAnswer(v.messages) && (v.messages as MessagesAnswer).view === "job"
          : (v.evidence as OperationsAnswer).view === "delays" && (v.documents as DocumentsAnswer).view === "missing"
            && v.messages === null)
      : evidence(v.evidence))
    && (v.kpi === undefined || v.kpi === null || kpiAnswer(v.kpi))
    && (v.messages === undefined || v.messages === null || messagesAnswer(v.messages))
    && (v.documents === undefined || v.documents === null || documentsAnswer(v.documents))
    && (v.engineering === undefined || v.engineering === null || engineeringAnswer(v.engineering))
    && (v.source === undefined || v.source === null || sourceAnswer(v.source))
    && (v.platform === undefined || v.platform === null || platformAnswer(v.platform))
    && (v.collaboration === undefined || v.collaboration === null || collaborationAnswer(v.collaboration))
    && (v.agentId === "management-agent" || v.collaboration == null)
    && (v.correlationId === undefined || text(v.correlationId, 64))
    && (v.contextUsed === undefined || typeof v.contextUsed === "boolean"));
}
const auditRun = (v: unknown) => obj(v) && id(v.runId)
  && strings(v, ["userId", "role", "agentId", "model", "status", "startedAt", "risk", "approvalStatus", "source"])
  && obj(v.scope) && typeof v.scope.team === "boolean" && nullableText(v.scope.operatorId)
  && ["completedAt", "toolCallId", "tool", "toolStatus", "view"].every(k => nullableText(v[k]))
  && ["limit", "total", "returned"].every(k => nullableCount(v[k])) && usage(v.usage)
  && Array.isArray(v.sourceKeys) && v.sourceKeys.length <= 50 && v.sourceKeys.every(k => text(k, 80))
  && Array.isArray(v.events) && v.events.length >= 1 && v.events.length <= MAX_AUDIT_EVENTS
  && v.events.every(e => obj(e) && strings(e, ["event", "status", "at"]) && nullableCount(e.total) && nullableCount(e.returned)
    && (e.step === undefined || nullableCount(e.step)) && (e.tool === undefined || nullableText(e.tool)))
  && (v.correlationId === undefined || text(v.correlationId, 64))
  && (v.steps === undefined || (count(v.steps) && v.steps <= 8));
export function parseAuditRun(v: unknown): AuditRun { return accept(v, auditRun(v)); }
export function parseAuditPage(v: unknown): AuditPage {
  return accept(v, obj(v) && Array.isArray(v.runs) && v.runs.length <= 100 && v.runs.every(auditRun)
    && (v.nextBeforeId === null || count(v.nextBeforeId) && v.nextBeforeId > 0));
}

export const ISSUE_TARGETS = ["monitoring", "myjob"] as const;
export function issueTarget(value: string): typeof ISSUE_TARGETS[number] | null {
  return ISSUE_TARGETS.find(screen => screen === value) ?? null;
}
export function askBody(message: string, agent: AgentChoice = "operations-agent") {
  const trimmed = message.trim();
  if (!trimmed || trimmed.length > 4000) throw new ControlError("invalid_request");
  return { message: trimmed, agentId: agent, context: { page: agent === "data-agent" ? "kpi" : agent === "communication-agent" ? "line"
    : agent === "document-agent" ? "documents" : agent === "engineering-agent" ? "engineering" : agent === "sre-agent" ? "sre" : agent === "management-agent" ? "management" : "operations" } };
}

/** Management plans are available only with the server-side Phase 8 flag, provider and durable audit. */
export function managementAvailability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  const agent = status.agents.find(a => a.id === "management-agent");
  if (!agent) return blocked("Management Agent ไม่มีในขอบเขตของบัญชีนี้", "");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  if (!agent.enabled || !agent.connected) return blocked("Management Agent ยังไม่เปิด", "เปิดด้วย AI:ManagementAgentEnabled และเปิด Agent ของทุกขั้นในแผน");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์", "amber");
  if (status.mock) return blocked("Development Mock", "Management Agent ไม่ทำงานในโหมดสาธิต", "amber");
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้", "amber");
  return { ready: true, title: "Management Agent พร้อมรับคำถาม", detail: "สรุปข้ามผู้เชี่ยวชาญตามแผนตายตัว · อนุญาตและบันทึก Audit ทุกขั้น · อ่านอย่างเดียว", tone: "green" };
}

/** Administrator-only Phase 6 reader; the server remains the authority for permission. */
/** Whether the SRE Agent (Phase 7) can take a question now — the same gates, on its own flag; absent from the status means the account is not an Administrator. */
export function sreAvailability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  const agent = status.agents.find(a => a.id === "sre-agent");
  if (!agent) return blocked("SRE Agent ไม่มีในขอบเขตของบัญชีนี้", "");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  if (!agent.enabled || !agent.connected) return blocked("SRE Agent ยังไม่เปิด", "เปิดด้วย AI:SreAgentEnabled ที่ฝั่งเซิร์ฟเวอร์");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์", "amber");
  if (status.mock) return blocked("Development Mock", "SRE Agent ไม่ทำงานในโหมดสาธิต", "amber");
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้", "amber");
  return { ready: true, title: "SRE Agent พร้อมรับคำถาม", detail: "สุขภาพระบบ · การ deploy · ข้อผิดพลาด — วัดและอ่านเท่านั้น ไม่รีสตาร์ต ไม่แก้ไข · มี Audit", tone: "green" };
}

export function engineeringAvailability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  const agent = status.agents.find(a => a.id === "engineering-agent");
  if (!agent) return blocked("Engineering Agent ไม่มีในขอบเขตของบัญชีนี้", "");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  if (!agent.enabled || !agent.connected) return blocked("Engineering Agent ยังไม่เปิด", "เปิดด้วย AI:EngineeringAgentEnabled ที่ฝั่งเซิร์ฟเวอร์");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์", "amber");
  if (status.mock) return blocked("Development Mock", "Engineering Agent ไม่ทำงานในโหมดสาธิต", "amber");
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้", "amber");
  return { ready: true, title: "Engineering Agent พร้อมรับคำถาม", detail: "อ่านรายการ GitHub ของ SCMOS เท่านั้น · ไม่อ่านโค้ด ไม่เขียน · มี Audit", tone: "green" };
}

/** Whether the Document & Invoice Agent (Phase 5) can take a question now — the same gates as the others, on its own flag. */
export function documentAvailability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  const agent = status.agents.find(a => a.id === "document-agent");
  if (!agent) return blocked("Document Agent ไม่มีในขอบเขตของบัญชีนี้", "");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  if (!agent.enabled || !agent.connected) return blocked("Document Agent ยังไม่เปิด", "เปิดด้วย AI:DocumentAgentEnabled ที่ฝั่งเซิร์ฟเวอร์");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์", "amber");
  if (status.mock) return blocked("Development Mock", "Document Agent ไม่ทำงานในโหมดสาธิต", "amber");
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้", "amber");
  return { ready: true, title: "Document Agent พร้อมรับคำถาม", detail: "เอกสารตาม checklist · ใบแจ้งหนี้เทียบกำหนดวางบิล · เอกสารใกล้หมดอายุ · ไม่เปิดไฟล์ ไม่อนุมัติ · มี Audit", tone: "green" };
}

/** Whether the Communication Agent (Phase 4) can take a question now — the same gates as the Data Agent, on its own flag. */
export function communicationAvailability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  const agent = status.agents.find(a => a.id === "communication-agent");
  if (!agent) return blocked("Communication Agent ไม่มีในขอบเขตของบัญชีนี้", "");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  if (!agent.enabled || !agent.connected) return blocked("Communication Agent ยังไม่เปิด", "เปิดด้วย AI:CommunicationAgentEnabled ที่ฝั่งเซิร์ฟเวอร์");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์", "amber");
  if (status.mock) return blocked("Development Mock", "Communication Agent ไม่ทำงานในโหมดสาธิต", "amber");
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้", "amber");
  return { ready: true, title: "Communication Agent พร้อมรับคำถาม", detail: "ข้อความจากผู้ขนส่งตามที่ระบบอ่านไว้ · ไม่ส่ง ไม่แก้ · มี Audit", tone: "green" };
}

/**
 * Whether the Data Agent (Phase 2) can take a question now: the same
 * server-side gates as Operations, minus the Operations switch, which
 * governs Operations alone. Absent from the status means the account has
 * no scope for it, and the screen offers nothing.
 */
export function dataAvailability(status: AiStatus | null): { ready: boolean; title: string; detail: string; tone: string } {
  const blocked = (title: string, detail: string, tone = "muted") => ({ ready: false, title, detail, tone });
  if (!status) return blocked("ยังไม่ทราบสถานะ AI", "รีเฟรชสถานะก่อนส่งคำถาม");
  const data = status.agents.find(a => a.id === "data-agent");
  if (!data) return blocked("Data Agent ไม่มีในขอบเขตของบัญชีนี้", "");
  if (!status.enabled || !status.chatEnabled) return blocked("SCMOS AI ยังปิดอยู่", "การเปิด AI ต้องตั้งค่าที่ฝั่งเซิร์ฟเวอร์");
  if (!status.configurationValid) return blocked("การตั้งค่า AI ยังไม่พร้อม", "ให้ผู้ดูแลตรวจการตั้งค่าเซิร์ฟเวอร์", "red");
  if (!data.enabled || !data.connected) return blocked("Data Agent ยังไม่เปิด", "เปิดด้วย AI:DataAgentEnabled ที่ฝั่งเซิร์ฟเวอร์");
  if (!status.providerConfigured) return blocked("ยังไม่ได้ตั้งค่า AI provider", "ให้ผู้ดูแลตรวจการตั้งค่าฝั่งเซิร์ฟเวอร์", "amber");
  if (status.mock) return blocked("Development Mock", "Data Agent ไม่ทำงานในโหมดสาธิต", "amber");
  if (!status.auditReady) return blocked("Audit ถาวรยังไม่พร้อม", "ยังส่งคำถามไม่ได้", "amber");
  return { ready: true, title: "Data Agent พร้อมรับคำถาม", detail: "จำนวนงานและ KPI ตรงเวลาตามช่วงเวลา · คำนวณโดย SCMOS · มี Audit", tone: "green" };
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
/** The windows a read may say it covered, as a person reads them. */
export const WINDOW_LABEL: Record<string, string> = {
  scheduled_today_active: "งานที่กำหนดไว้วันนี้", overdue_through_next_2_days: "งานเลยกำหนดถึงอีก 2 วันข้างหน้า",
  all_active_dates: "งาน active ทุกวันที่", scheduled_today_plan_time_passed: "งานวันนี้ที่เลยเวลาแผนแล้ว",
};
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
  second_factor_required: "นโยบายระบบกำหนดให้ Administrator เข้าสู่ระบบด้วยการยืนยันสองขั้นตอนก่อนเปลี่ยนสถานะ",
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
