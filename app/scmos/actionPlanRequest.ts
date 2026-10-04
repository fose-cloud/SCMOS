/**
 * A plan asked for from somewhere else (1 Oct 2026, Action Plan round two): an alert opening its plan, or a new
 * plan started from a carrier's profile, an evaluation, an audit or a skill gap with what that screen already
 * knows filled in. The other screen leaves the request here and moves to Action Plan, which takes it — or, when
 * Action Plan is already open, is handed it at once.
 */

export type PlanPrefill = {
  developmentType?: "people" | "subcontractor";
  targetType?: string;
  employeeId?: string;
  supplierId?: number;
  category?: string;
  developmentArea?: string;
  currentLevel?: string;
  targetLevel?: string;
  title?: string;
  /* What an evaluation found (Annual Evaluation Phase 10, 2 Oct 2026): the gap, its measure and target, and first steps. */
  gap?: string;
  objective?: string;
  metric?: string;
  baseline?: number | null;
  targetValue?: number | null;
  priority?: string;
  /** First steps, each owned by whoever opens the plan until they hand it on. */
  items?: { action: string; expectedResult: string }[];
  /** The records the plan is opened from, kept on it as references. */
  references?: { kind: string; refId: string; label: string }[];
};

export type ActionPlanRequest = { open: number } | { create: PlanPrefill };

let pending: ActionPlanRequest | null = null;
let listener: ((request: ActionPlanRequest) => void) | null = null;

export function requestActionPlan(request: ActionPlanRequest): void {
  if (listener) listener(request);
  else pending = request;
}

/** Action Plan's own: takes what was left before it opened, and hears what arrives while it is open. */
export function listenForActionPlanRequests(handle: (request: ActionPlanRequest) => void): () => void {
  listener = handle;
  if (pending) { const request = pending; pending = null; handle(request); }
  return () => { if (listener === handle) listener = null; };
}
