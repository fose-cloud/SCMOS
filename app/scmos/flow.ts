import { apiFetch } from "./api";

/**
 * The backend's process, as the screens see it.
 *
 * Types only, and calls. No decisions: which carrier may be asked next, whether
 * a gate passes, whether a delay counts against a subcontractor — all of that is
 * decided in .NET and arrives here already settled. When a call is refused the
 * server sends the reason, and the screen's whole job is to show it.
 */

export type StageView = {
  id: string; english: string; thai: string; position: number;
  gate: string | null; gateThai: string | null;
};

export type SupplierAttempt = {
  id: number; rank: number; carrier: string; quotedPrice: number | null;
  outcome: string; reason: string;
  requestedAt: string; respondedAt: string | null; responseMinutes: number | null;
};

export type CarrierPriority = { rank: number; carrier: string; price: number | null; basis: string };

export type WorkflowEventView = {
  id: number; kind: string; from: string; to: string;
  hold: string; note: string; by: string; at: string;
};

export type JobWorkflow = {
  jobKey: string; reference: string; stage: string; position: number;
  hold: string; isHeld: boolean;
  pendingGate: string | null; pendingGateThai: string | null;
  suppliers: SupplierAttempt[];
  priority: CarrierPriority[];
  nextToAsk: CarrierPriority | null;
  history: WorkflowEventView[];
};

export type MilestoneView = {
  stage: string; english: string; thai: string;
  plannedAt: string; actualAt: string | null; status: string;
  carrier: string; truckNo: string; driver: string;
  remark: string; delayReason: string; photoKey: string;
  updatedBy: string; updatedAt: string | null;
};

export type DelayView = {
  id: number; stage: string; category: string; categoryThai: string; detail: string;
  responsible: string; responsibleThai: string; classifiedBy: string; classifierBasis: string;
  detectedAt: string; impactMinutes: number | null;
  notifiedAt: string | null; notifiedTeam: string;
  recoveryAction: string; resolvedAt: string | null; againstCarrier: boolean;
};

export type ShipmentTrack = {
  jobKey: string; reference: string; customer: string;
  milestones: MilestoneView[]; delays: DelayView[];
};

export type DelayReasonOption = {
  id: string; thai: string; responsible: string; responsibleThai: string; againstCarrier: boolean;
};

export type Suggestion = {
  category: string; categoryThai: string;
  responsible: string; responsibleThai: string;
  basis: string; confidence: number;
};

/** Every call answers the same way: what happened, or why it was refused. */
export type Reply<T> = { ok: boolean; message: string; state?: T };

async function call<T>(path: string, init?: RequestInit): Promise<Reply<T>> {
  try {
    const response = await apiFetch(path, init);
    const body = await response.json().catch(() => ({})) as
      { message?: string; error?: string; state?: T };
    return {
      ok: response.ok,
      message: body.message ?? body.error ?? (response.ok ? "" : "HTTP " + response.status),
      state: body.state,
    };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

async function read<T>(path: string): Promise<T | null> {
  try {
    const response = await apiFetch(path, { headers: { accept: "application/json" } });
    if (!response.ok) return null;
    return await response.json() as T;
  } catch {
    return null;
  }
}

const json = (body: unknown): RequestInit => ({
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify(body),
});

/* --------------------------------------------------------------- workflow */

export const stageDefinition = () => read<StageView[]>("/api/workflow/definition");
export const readWorkflow = (key: string) => read<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}`);

export const advance = (key: string, answer: boolean | null, note = "") =>
  call<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}/advance`, json({ answer, note }));

export const hold = (key: string, reason: string, note = "") =>
  call<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}/hold`, json({ reason, note }));

export const release = (key: string, note = "") =>
  call<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}/release`, json({ note }));

export const requestSupplier = (key: string, carrier: string, quotedPrice: number | null, skipReason = "") =>
  call<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}/supplier-request`,
    json({ carrier, quotedPrice, skipReason }));

export const respondSupplier = (key: string, requestId: number, outcome: string, reason = "") =>
  call<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}/supplier-response`,
    json({ requestId, outcome, reason }));

export const assignCarrier = (key: string, carrier: string) =>
  call<JobWorkflow>(`/api/workflow/${encodeURIComponent(key)}/assign-carrier`, json({ carrier }));

/* ------------------------------------------------------------- monitoring */

export const readTrack = (key: string) => read<ShipmentTrack>(`/api/shipment/${encodeURIComponent(key)}`);
export const delayReasons = () => read<DelayReasonOption[]>("/api/shipment/delay-reasons");
export const classifyDelay = (text: string) =>
  read<Suggestion>(`/api/shipment/classify-delay?text=${encodeURIComponent(text)}`);

export const saveMilestone = (key: string, body: {
  stage: string; status: string; actualAt?: string | null;
  truckNo?: string; driver?: string; remark?: string; delayReason?: string; photoKey?: string;
}) => call<never>(`/api/shipment/${encodeURIComponent(key)}/milestone`, json(body));

export const recordDelay = (key: string, body: {
  stage: string; category?: string | null; detail: string; impactMinutes?: number | null;
}) => call<never>(`/api/shipment/${encodeURIComponent(key)}/delay`, json(body));

export const updateDelay = (body: {
  id: number; notifiedTeam?: string; recoveryAction?: string; resolved?: boolean;
}) => call<never>("/api/shipment/delay/update", json(body));
