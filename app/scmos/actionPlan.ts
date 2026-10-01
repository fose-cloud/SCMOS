/**
 * Subcontract Management's Action Plan (1 Oct 2026): the shapes the API answers in and the words the screens
 * use for them. Progress, overdue and who may read a plan are the API's (Services/ActionPlanService.cs,
 * Rules/ActionPlanRules.cs) — this file only names and colours what it is sent.
 */

export type PlanRow = {
  id: number; number: string; title: string; developmentType: string; category: string; targetType: string; targetName: string;
  employeeId: string; supplierId: number | null; ownerId: string; ownerName: string; startDate: string; targetDate: string;
  actualCompletionDate: string; progress: number | null; status: string; overdue: boolean; priority: string; year: number;
  quarter: number | null; month: number | null; updatedAt: string;
};

export type PlanItem = {
  id: number; planId: number; sequence: number; action: string; description: string; ownerId: string; ownerName: string;
  supportingPerson: string; supportingDepartment: string; startDate: string; targetDate: string; actualCompletionDate: string;
  priority: string; status: string; progress: number; expectedResult: string; actualResult: string; remark: string;
  trainingTitle: string; trainingType: string; trainer: string; trainingProvider: string; trainingDate: string;
  participants: string; certificateExpiry: string;
};

export type Plan = {
  id: number; number: string; title: string; developmentType: string; category: string; period: string; year: number;
  quarter: number | null; month: number | null; department: string; priority: string; status: string; description: string;
  objective: string; expectedOutcome: string; startDate: string; targetDate: string; actualCompletionDate: string; ownerId: string;
  ownerName: string; targetType: string; employeeId: string; employeeName: string; position: string; team: string;
  supervisor: string; supplierId: number | null; targetName: string; developmentArea: string; currentLevel: string;
  targetLevel: string; gap: string; rootCause: string; method: string; coach: string; carrierContact: string;
  evaluationMethod: string; reviewDate: string; result: string; metric: string; baseline: number | null;
  targetValue: number | null; actualValue: number | null; cancelReason: string; createdBy: string; createdAt: string;
  updatedBy: string; updatedAt: string;
};

export type PlanUpdate = {
  id: number; itemId: number | null; comment: string; progressBefore: number | null; progressAfter: number | null;
  statusBefore: string; statusAfter: string; createdBy: string; createdAt: string;
};
export type PlanReview = { id: number; submittedBy: string; submittedAt: string; reviewer: string; reviewedAt: string | null; result: string; comment: string };
export type PlanScore = { id: number; dimension: string; previousScore: number | null; currentScore: number | null; targetScore: number | null; assessedBy: string; assessedAt: string };
export type PlanReference = { id: number; kind: string; refId: string; label: string };
export type Evidence = { id: number; fileName: string; kind: string; note: string; uploadedBy: string; uploadedAt: string; canShow: boolean };

export type PlanDetail = {
  plan: Plan; progress: number | null; overdue: boolean; canEdit: boolean; canReview: boolean; items: PlanItem[];
  updates: PlanUpdate[]; reviews: PlanReview[]; scores: PlanScore[]; references: PlanReference[]; evidence: Evidence[];
  supplierName: string;
};

export type Count = { key: string; count: number };
export type Dashboard = {
  total: number; active: number; completed: number; overdue: number; dueThisMonth: number; people: number; carrier: number;
  byStatus: Count[]; byCategory: Count[]; byPriority: Count[]; byOwner: Count[];
  monthly: { month: string; completed: number; due: number }[]; peopleProgress: number | null; carrierProgress: number | null;
};

export type Person = { id: string; name: string; role: string };
export type PlanType = { id: number; developmentType: string; name: string; position: number; active: boolean };
export type Meta = {
  types: PlanType[]; people: Person[]; targetTypes: Record<string, string[]>; methods: string[]; trainingTypes: string[];
  dimensions: string[]; canEdit: boolean; canReview: boolean; canConfigure: boolean; me: string;
};

export const DEVELOPMENT_TH: Record<string, string> = { people: "People Development", subcontractor: "Subcontractor Development" };
export const TARGET_TH: Record<string, string> = {
  employee: "พนักงาน", team: "ทีม", department: "แผนก", subcontractor: "ผู้รับเหมาช่วง", carrier: "ผู้ขนส่ง",
};
export const PERIOD_TH: Record<string, string> = { annual: "รายปี", quarterly: "รายไตรมาส", monthly: "รายเดือน", custom: "กำหนดเอง" };

/** The workflow's states, and Overdue — which is worked out, never stored — in the order the work runs. */
export const STATUS: Record<string, { label: string; tone: string; background: string }> = {
  draft: { label: "Draft", tone: "#475569", background: "#F1F5F9" },
  planned: { label: "Planned", tone: "#1D5FA8", background: "#E7F0FA" },
  "in-progress": { label: "In Progress", tone: "#0A5C97", background: "#DCEBFA" },
  waiting: { label: "Waiting", tone: "#8A6D0B", background: "#FFFBEB" },
  "pending-review": { label: "Pending Review", tone: "#6D28D9", background: "#F3EEFE" },
  completed: { label: "Completed", tone: "#16794C", background: "#EDF7F1" },
  overdue: { label: "Overdue", tone: "#B42318", background: "#FEF0EE" },
  cancelled: { label: "Cancelled", tone: "#94A3B8", background: "#F8FAFC" },
};

export const PRIORITY: Record<string, { label: string; tone: string; background: string }> = {
  low: { label: "Low", tone: "#475569", background: "#F1F5F9" },
  medium: { label: "Medium", tone: "#1D5FA8", background: "#E7F0FA" },
  high: { label: "High", tone: "#B45309", background: "#FFF8F0" },
  critical: { label: "Critical", tone: "#B42318", background: "#FEF0EE" },
};

/** The moves the workflow allows from a state, as buttons — Completed comes only from a review. */
export const MOVES: Record<string, { to: string; label: string }[]> = {
  draft: [{ to: "planned", label: "ยืนยันแผน (Planned)" }],
  planned: [{ to: "in-progress", label: "เริ่มดำเนินการ" }],
  "in-progress": [{ to: "waiting", label: "พักรอ (Waiting)" }, { to: "pending-review", label: "ส่ง Review" }],
  waiting: [{ to: "in-progress", label: "ดำเนินการต่อ" }],
};

/** The status a row shows: Overdue when the API says so, else its stored state. */
export function shownStatus(row: { status: string; overdue: boolean }): string {
  return row.overdue ? "overdue" : row.status;
}

/** Suggested development areas — the skills the department listed, and the carrier dimensions. Not a closed list. */
export const PEOPLE_AREAS = [
  "Trucking Operation", "FCL Operation", "LCL Operation", "Domestic Transportation", "Container Operation", "ISO Tank", "DG Transportation",
  "Carrier Management", "Sourcing", "Procurement", "Vendor Evaluation", "Rate Analysis", "Cost Analysis", "Negotiation",
  "Incident Management", "CAR / PAR", "Root Cause Analysis", "Customer Requirement", "EHSQ", "Safety", "Defensive Driver Requirement",
  "Risk Assessment", "Billing", "Additional Charge Validation", "Cargo Receipt", "POD", "Contract Rate Validation", "SCMOS", "Excel",
  "Power BI", "Data Analysis", "AI", "Automation", "Communication", "Problem Solving", "Leadership", "Presentation", "Coaching", "Decision Making",
];
export const CARRIER_AREAS = [
  "Capacity", "OTD", "Truck Availability", "Truck Quality", "Driver Quality", "Safety", "EHSQ", "Defensive Driver Training",
  "DG Compliance", "ISO Tank Capability", "Documentation", "POD", "Cargo Receipt", "Billing Accuracy", "Communication",
  "Emergency Response", "Customer Requirement Compliance", "Cost Competitiveness", "Carrier Portal Adoption", "API Integration", "Digital Readiness",
];

export const SKILL_LEVELS =["1 = Basic", "2 = Beginner", "3 = Competent", "4 = Advanced", "5 = Expert"];
export const SCORE_TH = ["", "1 = Poor", "2 = Needs Improvement", "3 = Acceptable", "4 = Good", "5 = Excellent"];
export const REFERENCE_KINDS: Record<string, string> = {
  evaluation: "Carrier Evaluation", kpi: "KPI", incident: "Incident", carpar: "CAR / PAR", audit: "Audit", customer: "ลูกค้า",
  training: "Training", risk: "Risk Assessment", project: "Process Improvement Project", plan: "Action Plan อื่น",
};

export type Filters = {
  year: string; quarter: string; month: string; developmentType: string; category: string; ownerId: string;
  employeeId: string; supplierId: string; status: string; priority: string; query: string;
};

export const NO_FILTERS: Filters = {
  year: "", quarter: "", month: "", developmentType: "", category: "", ownerId: "", employeeId: "", supplierId: "", status: "", priority: "", query: "",
};

/** The list's filters as the API's query string — empty ones left out. */
export function filterQuery(filters: Filters): string {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(filters)) if (value.trim()) params.set(key, value.trim());
  const text = params.toString();
  return text ? `?${text}` : "";
}
