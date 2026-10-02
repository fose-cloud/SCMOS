/**
 * Annual Carrier Evaluation (1 Oct 2026): the shapes the API answers in and the words the screens use for them.
 * Every rule — what may open, what a figure is, how a score is reached — is the API's (Rules/AnnualEvaluationRules.cs,
 * Rules/EvaluationEvidence.cs, Rules/EvaluationScoring.cs); this file only names and formats what it is sent.
 */

export type CampaignRow = {
  id: number; code: string; name: string; year: number; status: string; periodStart: string; periodEnd: string;
  openOn: string | null; dueOn: string | null; carriers: number; included: number; locked: boolean;
};

export type Campaign = {
  id: number; code: string; name: string; year: number; periodStart: string; periodEnd: string; openOn: string | null; dueOn: string | null;
  systemWeight: number; humanWeight: number; minimumJobs: number; minimumSystemCoverage: number; commentRequiredAtOrBelow: number;
  status: string; version: number; lockedAt: string | null; lockedBy: string;
};

export type KpiBand = { threshold: number; score: number };
export type Kpi = {
  id: number; code: string; name: string; nameTh: string; weight: number; enabled: boolean; method: string; direction: string;
  measure: string; fallbackScore: number; bands: KpiBand[];
};
export type QuestionDepartment = { departmentId: number; enabled: boolean; required: boolean; commentRequiredAtOrBelow: number | null };
export type Question = { id: number; code: string; text: string; textTh: string; weight: number; enabled: boolean; position: number; departments: QuestionDepartment[] };
export type CampaignDepartment = { departmentId: number; code: string; name: string; weight: number; enabled: boolean };
export type ScoreBand = { code: string; label: string; minScore: number };

export type CampaignView = {
  campaign: Campaign; locked: boolean; kpis: Kpi[]; questions: Question[]; departments: CampaignDepartment[]; scoreBands: ScoreBand[];
  problems: string[]; moves: string[]; carriers: number; included: number; canManage: boolean; canDecide: boolean;
};

export type CarrierRow = {
  id: number; supplierId: number; code: string; name: string; supplierStatus: string; isCarrier: boolean; included: boolean;
  excludedReason: string; totalJobs: number | null; completedJobs: number | null; eligibility: string; countedAt: string | null; decision: string;
};

export type Metric = {
  code: string; status: string; value: number | null; numerator: number | null; denominator: number | null; formula: string;
  note: string; sources: string[]; external: boolean;
};
export type SnapshotVersion = { id: number; version: number; current: boolean; reason: string; generatedBy: string; generatedAt: string };
export type Snapshot = { evaluationCarrierId: number; supplierId: number; carrier: string; snapshot: SnapshotVersion | null; metrics: Metric[]; versions: SnapshotVersion[] };

export type ResultRow = {
  evaluationCarrierId: number; supplierId: number; code: string; carrier: string; eligibility: string; totalJobs: number | null; version: number;
  systemScore: number | null; humanScore: number | null; finalScore: number | null; systemWeightAvailable: number; band: string; status: string;
  reason: string; calculatedAt: string | null; decision: string;
};
export type KpiLine = {
  code: string; name: string; weight: number; method: string; metric: string; metricStatus: string; value: number | null;
  score: number | null; counted: boolean; why: string;
};
export type DepartmentLine = { departmentId: number; weight: number; score: number | null; responses: number; scored: number };
export type ResultDetail = {
  row: ResultRow;
  detail: { weights: { system: number; human: number; minimumCoverage: number }; snapshotVersion: number; coverage: number; kpis: KpiLine[]; departments: DepartmentLine[]; why: string };
  versions: number[];
};

export type InvitationRow = {
  id: number; evaluatorId: number; evaluationCarrierId: number; carrier: string; state: string; expiresAt: string;
  sentAt: string | null; openedAt: string | null; submittedAt: string | null; revokeReason: string;
};
export type EvaluatorRow = { id: number; name: string; email: string; departmentId: number; department: string; invitations: InvitationRow[] };
/** A link as it was made: the only time its token is on a screen. */
export type IssuedLink = { invitationId: number; evaluator: string; department: string; carrier: string; token: string; expiresAt: string };

export const INVITATION_STATE: Record<string, { label: string; tone: string; background: string }> = {
  pending: { label: "ยังไม่ส่ง", tone: "#475569", background: "#F1F5F9" },
  sent: { label: "ส่งแล้ว", tone: "#1D5FA8", background: "#E7F0FA" },
  opened: { label: "เปิดแล้ว", tone: "#8A6D0B", background: "#FFFBEB" },
  submitted: { label: "ตอบแล้ว", tone: "#16794C", background: "#EDF7F1" },
  revoked: { label: "ยกเลิก", tone: "#94A3B8", background: "#F8FAFC" },
  expired: { label: "หมดอายุ", tone: "#B42318", background: "#FEF3F2" },
};

/**
 * The address an evaluator opens. The token rides in the fragment, which a browser never sends to a server, so no
 * access log, proxy or link preview ever holds it.
 */
export function evaluationLink(origin: string, token: string): string {
  return `${origin.replace(/\/+$/, "")}/evaluation#${token}`;
}

/** An API time as Bangkok's day and minute (YYYY-MM-DD HH:mm), or a dash. */
export function bangkokTime(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString("sv-SE", { timeZone: "Asia/Bangkok" }).slice(0, 16) : "—";
}

export const STATUS: Record<string, { label: string; tone: string; background: string }> = {
  draft: { label: "ร่าง", tone: "#475569", background: "#F1F5F9" },
  "data-preparation": { label: "เตรียมข้อมูล", tone: "#1D5FA8", background: "#E7F0FA" },
  ready: { label: "พร้อมเปิด", tone: "#0A5C97", background: "#DCEBFA" },
  open: { label: "เปิดรับประเมิน", tone: "#16794C", background: "#EDF7F1" },
  closed: { label: "ปิดรับประเมิน", tone: "#8A6D0B", background: "#FFFBEB" },
  "under-review": { label: "กำลังพิจารณา", tone: "#6D28D9", background: "#F3EEFE" },
  approved: { label: "อนุมัติแล้ว", tone: "#16794C", background: "#E3F3EA" },
  finalized: { label: "สรุปผลแล้ว", tone: "#0A2240", background: "#E9EFF5" },
  archived: { label: "เก็บถาวร", tone: "#94A3B8", background: "#F8FAFC" },
};

/** The button for a move to each state. Moving back needs a reason, which the API asks for. */
export const MOVE_LABEL: Record<string, string> = {
  draft: "กลับเป็นร่าง", "data-preparation": "เตรียมข้อมูล", ready: "พร้อมเปิด", open: "เปิดรับประเมิน", closed: "ปิดรับประเมิน",
  "under-review": "ส่งพิจารณา", approved: "อนุมัติ", finalized: "สรุปผล", archived: "เก็บถาวร",
};

/** The states in their order, so a move to an earlier one is known to be a step back. */
export const ORDER = ["draft", "data-preparation", "ready", "open", "closed", "under-review", "approved", "finalized", "archived"];

export const ELIGIBILITY: Record<string, string> = { full: "ครบเกณฑ์", "limited-data": "ข้อมูลน้อย", "no-activity": "ไม่มีงาน" };

export const METRIC_STATUS: Record<string, string> = { available: "", "not-available": "ไม่มีข้อมูล", "insufficient-data": "ข้อมูลไม่พอ" };

/** What each figure of a snapshot is, in the order a reader goes through them. */
export const METRIC_LABEL: Record<string, string> = {
  "total-jobs": "งานทั้งหมด", "completed-jobs": "งานที่เสร็จสิ้น",
  "otd-measured": "งานที่วัด OTD ได้", late: "งานที่ช้า", "late-carrier": "ช้าเพราะผู้ขนส่ง", "late-other": "ช้าด้วยสาเหตุอื่น",
  "late-unclassified": "ช้า ไม่ระบุสาเหตุ (ไม่นับเป็นความผิด)", "operational-otd": "OTD รวม %", "carrier-otd": "OTD ฝั่งผู้ขนส่ง %",
  "incidents-major": "อุบัติเหตุ Major", "incidents-minor": "อุบัติเหตุ Minor", "incidents-loading": "อุบัติเหตุขณะขนถ่าย",
  "incidents-ungraded": "อุบัติเหตุยังไม่ระบุชนิด", "incident-rate": "อุบัติเหตุต่อ 100 งาน", "critical-safety": "เหตุร้ายแรง (เสียชีวิต/บาดเจ็บ/แอลกอฮอล์)",
  claims: "เคลม", "claims-critical": "เคลมวิกฤต", "claims-major": "เคลม Major", "claims-minor": "เคลม Minor", "claim-points": "คะแนนผลกระทบเคลม",
  "claim-rate": "คะแนนเคลมต่อ 100 งาน", "claims-car-par": "เคลมที่มี CAR/PAR",
  "billing-submitted": "ใบแจ้งหนี้ที่ส่งตรวจ", "billing-returned": "ใบแจ้งหนี้ที่ถูกตีกลับ", "billing-accuracy": "ความถูกต้องการวางบิล %",
  "billing-sla": "วางบิลทันกำหนด %", "pod-compliance": "งานเสร็จที่มี POD %", "compliance-documents": "เอกสารบังคับยังไม่หมดอายุ %",
  "iso-certificates": "ใบรับรอง ISO / Q-Mark", pricing: "ความสามารถด้านราคา",
};

/** A score or a rate as the screens show it: two places, the API keeps four. */
export function shown(value: number | null | undefined, suffix = ""): string {
  return value === null || value === undefined ? "—" : `${Number(value).toFixed(2)}${suffix}`;
}

/** Whether moving to this state is a step back, which needs a reason. */
export function isBackward(from: string, to: string): boolean {
  return ORDER.indexOf(to) < ORDER.indexOf(from);
}

/** The API's date (YYYY-MM-DD) as a date input holds it, or empty. */
export function dayInput(value: string | null | undefined): string {
  return value ? value.slice(0, 10) : "";
}
