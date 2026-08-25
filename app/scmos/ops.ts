import { dnum, lateMinutes, tmin } from "./util";
import { opIdForName } from "./nav";
import { normaliseJob, validateJob, type Fix, type Issue } from "./standard";
import { STATUS_RE } from "./theme";

export type HistEntry = { ts: string; user: string; field: string; old: string; neu: string };

export type ActEntry = {
  ts: string; user: string; job: string; cust: string;
  field: string; old: string; neu: string;
};

/**
 * One operational job. Import/export jobs come straight from the plan
 * workbooks; delivery jobs are normalised into the same shape by `prep`.
 */
export type Job = {
  key: string;
  id: string;
  cat: string;
  /** The operator's name as the plan workbooks spell it. Display only. */
  op: string;
  /**
   * Who the job belongs to, as a directory id (OP-01…). This is what every
   * ownership check reads; `op` is what the screen prints. Empty means nobody
   * owns it yet, which the workspace shows rather than hides.
   */
  opId: string;
  date: string;
  customer: string;
  trucker: string;
  jobCode: string;
  abs: string;
  booking: string;
  product: string;
  fclLcl: string;
  agent: string;
  destination: string;
  plant: string;
  planTime: string;
  type: string;
  cyYard: string;
  returnLoc: string;
  emptyReturn: string;
  weight: string;
  container: string;
  seal: string;
  tare: string;
  licence: string;
  driver: string;
  contact: string;
  arrDate: string;
  arrTime: string;
  closingDate: string;
  closingTime: string;
  reason: string;
  remark: string;
  ot: string;
  /** When the container is collected. The note the sheets carry holds a
   *  date and a time in one sentence; they are split into these two. */
  pickupPlan: string;
  pickupTime: string;
  cs: string;
  incident: string;
  freightType: string;
  status: string;

  /**
   * A job whose plan date moved, and a job that is not happening.
   *
   * Both were possible before and neither was visible. CANCELLED has always been
   * on the status ladder, so a job could be cancelled and then sat in PENDING
   * for the rest of its life looking like work. A postponement was an edit to
   * `date` — the old date survived only in the change history, so "this customer
   * has moved the same shipment four times" was a thing you could only learn by
   * opening four jobs and reading.
   *
   * `origDate` is written once, the first time the date moves, and never again:
   * it is where the job was *originally* meant to go, not where it was last
   * week. Every hop in between is in the history.
   */
  origDate: string;
  /** Why it moved, in the operator's words. */
  moveReason: string;
  /** Who asked for it to move — see MOVED_BY. Countable, unlike free text. */
  moveBy: string;
  /** Why it is not happening. Separate from moveReason so neither overwrites the other. */
  cancelReason: string;
  // delivery-only
  wh?: string;
  jobNo?: string;
  sid?: string;
  /**
   * The D-code the summary sheet carries beside the SID.
   *
   * Its own column headed "SID NUMBER" on the job sheet and "DCODE" on the
   * second one — the same value under two names, which is why it is stored
   * under the clearer of them.
   */
  dCode?: string;
  /** The SAP order and the delivery note the customer's own system issues. */
  sapOrder?: string;
  deliverNo?: string;
  /** A tail-lift truck, counted like the other vehicle types. */
  vtl?: string;
  province?: string;
  zip?: string;
  pallet?: string;
  kgs?: string;
  v4?: string;
  v6?: string;
  v10?: string;
  vtr?: string;
  cost?: string;
  // computed
  hist: HistEntry[];
  flags: string[];
  action: boolean;
  prio: "HIGH" | "MEDIUM" | "LOW";
  /** Values that break the data standard and need a person to resolve them. */
  issues: Issue[];
  /** Formatting corrections applied on load, kept so the change is auditable. */
  fixes: Fix[];
};

export type Masters = {
  customers: string[];
  truckers: string[];
  operators: string[];
  cyYards: string[];
  warehouses: string[];
  provinces: string[];
};

export type Ops = { jobs: Job[]; masters: Masters };

type RawDelivery = Record<string, string | undefined> & { id: string };

export type RawOps = {
  jobs?: Record<string, string>[];
  delivery?: RawDelivery[];
  masters?: Partial<Masters>;
};

const EDITABLE_BLANK = {
  booking: "", product: "", fclLcl: "", agent: "", plant: "", cyYard: "",
  returnLoc: "", emptyReturn: "", container: "", seal: "", tare: "", licence: "",
  driver: "", contact: "", arrDate: "", arrTime: "", closingDate: "", closingTime: "",
  reason: "", ot: "", pickupPlan: "", pickupTime: "", cs: "", incident: "", freightType: "",
  origDate: "", moveReason: "", moveBy: "", cancelReason: "",
};

/**
 * Who asked for the date to move.
 *
 * A fixed list, for the same reason the delay categories are fixed: free text
 * gives a thousand spellings of "the customer" and nothing anyone can count.
 * Which side keeps moving the dates is the question this field exists to
 * answer, and it can only answer it if the answers are comparable.
 */
export const MOVED_BY = ["ลูกค้า", "ผู้ขนส่ง", "LESCHACO", "อื่นๆ"];

/** Not happening. The status is the record; this is the one place that reads it. */
export function isCancelled(j: Pick<Job, "status">): boolean {
  return (j.status || "").trim().toUpperCase() === "CANCELLED";
}

/** Moved at least once from the date it was first planned for. */
export function wasMoved(j: Pick<Job, "origDate" | "date">): boolean {
  const from = (j.origDate || "").trim();
  return from.length > 0 && from !== (j.date || "").trim();
}

function uniqueSorted(values: (string | undefined)[]) {
  return [...new Set(values.filter((v): v is string => !!v && v.trim() !== ""))].sort((a, b) =>
    a.localeCompare(b),
  );
}

/**
 * The published ops.json may omit `masters` (and `delivery`). Derive whatever is
 * missing from the jobs themselves so every dropdown still has real options.
 */
function deriveMasters(jobs: Job[], supplied?: Partial<Masters>): Masters {
  const fallbackOperators = ["Watsana", "Uthai", "Ananya", "Jiratchaya", "Maliwan"];
  // The union, not just what the plan happens to contain: an operator with no
  // jobs yet — Maliwan today — still has to be assignable.
  const derivedOperators = uniqueSorted(jobs.map((j) => j.op).concat(fallbackOperators));
  return {
    customers: supplied?.customers?.length ? supplied.customers : uniqueSorted(jobs.map((j) => j.customer)),
    truckers: supplied?.truckers?.length ? supplied.truckers : uniqueSorted(jobs.map((j) => j.trucker)),
    operators: supplied?.operators?.length
      ? supplied.operators
      : derivedOperators.length
        ? derivedOperators
        : fallbackOperators,
    cyYards: supplied?.cyYards?.length ? supplied.cyYards : uniqueSorted(jobs.map((j) => j.cyYard)),
    warehouses: supplied?.warehouses?.length ? supplied.warehouses : uniqueSorted(jobs.map((j) => j.wh)),
    provinces: supplied?.provinces?.length ? supplied.provinces : uniqueSorted(jobs.map((j) => j.province)),
  };
}

/**
 * Re-derives everything the grid colours a job by: which fields are still
 * blank, which values break the data standard, and how urgent the job is.
 * Called on load and after every edit.
 */
function flagJob(j: Job) {
  const fl: string[] = [];
  if (j.cat !== "DELIVERY") {
    if (!j.trucker) fl.push("Trucking company missing");
    if (!j.licence) fl.push("Licence missing");
    if (!j.driver) fl.push("Driver missing");
    if (!j.contact) fl.push("Driver contact missing");
    if (!j.container && !/6WH|4WH|10W|COMBINE/i.test(j.type || "")) fl.push("Container missing");
    if (j.cat === "EXPORT" && !j.seal) fl.push("Seal missing");
    if (!j.arrTime) fl.push("Arrival time missing");
  }
  // Cancelled counts as settled alongside done: chasing a licence plate for a
  // job that is not happening is noise, and it was the reason the missing-value
  // flags stopped being worth reading.
  const done = STATUS_RE.done.test(j.status) || isCancelled(j);
  j.flags = done ? [] : fl;

  j.issues = validateJob(j as unknown as Record<string, unknown>);
  const blocking = j.issues.filter((i) => i.severity === "error").length;

  // A malformed value keeps the job out of the KPIs, so it needs attention even
  // when the job is otherwise finished.
  j.action = blocking > 0 || (!done && fl.length > 0);

  const risky =
    j.cat === "EXPORT" &&
    !!j.closingTime &&
    !!j.arrTime &&
    (tmin(j.closingTime) ?? 0) - (tmin(j.arrTime) ?? 0) < 0;
  j.prio =
    STATUS_RE.delayed.test(j.status) || risky || blocking > 0 ? "HIGH"
      : fl.length > 2 && !done ? "HIGH"
        : done ? "LOW" : "MEDIUM";
}

/** True when every value on the job parses, so it can feed the dashboard. */
export function isKpiReady(j: Job): boolean {
  return !j.issues.some((i) => i.severity === "error");
}

/** How a status maps onto the buckets every summary counts by. Defined in theme.ts. */
export { STATUS_RE };

/**
 * The operational summary behind the dashboard and its Excel export. Kept here
 * so the screen and the workbook cannot drift apart: both count the same way.
 */
export function opsStats(jobs: Job[]) {
  const cat = (c: string) => jobs.filter((j) => j.cat === c);

  // On-time only means something for jobs that recorded an arrival, so the
  // measured base travels with the figure instead of being hidden.
  const measurable = jobs.filter(
    (j) => tmin(j.planTime) !== null && tmin(j.arrTime) !== null && !!dnum(j.date) && !!dnum(j.arrDate),
  );
  // Zero tolerance, which is what this KPI has always meant and what is
  // reported upward. A customer whose contract allows a grace period is
  // measured against that separately — see `lateMinutes`, which both use.
  const onTime = measurable.filter((j) => (lateMinutes(j) ?? 1) <= 0);

  const dateCount: Record<string, number> = {};
  jobs.forEach((j) => { if (j.date) dateCount[j.date] = (dateCount[j.date] || 0) + 1; });

  return {
    jobs,
    dates: Object.keys(dateCount).sort((a, b) => dnum(a) - dnum(b)),
    dateCount,
    imports: cat("IMPORT"),
    exports: cat("EXPORT"),
    deliveries: cat("DELIVERY"),
    waiting: jobs.filter((j) => STATUS_RE.waiting.test(j.status)),
    confirmed: jobs.filter((j) => STATUS_RE.confirmed.test(j.status)),
    running: jobs.filter((j) => STATUS_RE.running.test(j.status)),
    delayed: jobs.filter((j) => STATUS_RE.delayed.test(j.status)),
    done: jobs.filter((j) => STATUS_RE.done.test(j.status)),
    action: jobs.filter((j) => j.action),
    formatErrors: jobs.filter((j) => !isKpiReady(j)),
    measurable,
    onTime,
    otpPct: measurable.length ? Math.round((onTime.length / measurable.length) * 100) : 0,
  };
}

export type OpsStats = ReturnType<typeof opsStats>;

export function prep(raw: RawOps): Ops {
  const jobs: Job[] = [];

  (raw.jobs || []).forEach((j) => {
    jobs.push({ ...EDITABLE_BLANK, ...(j as unknown as Job), key: j.id, hist: [], flags: [], action: false, prio: "MEDIUM", issues: [], fixes: [] });
  });

  (raw.delivery || []).forEach((d) => {
    jobs.push({
      ...EDITABLE_BLANK,
      ...(d as unknown as Job),
      key: d.id,
      opId: "",
      hist: [],
      flags: [],
      action: false,
      prio: "MEDIUM",
      issues: [],
      fixes: [],
      jobCode: d.jobNo || "",
      abs: d.sid || "",
      trucker: "LESCHACO DTT",
      destination: (d.province || "") + " " + (d.zip || ""),
      type: [
        d.v4 && d.v4 + "×4W",
        d.v6 && d.v6 + "×6W",
        d.v10 && d.v10 + "×10W",
        d.vtr && d.vtr + "×TRAILER",
      ].filter(Boolean).join(" "),
      weight: d.kgs || "",
      status: "Scheduled",
    });
  });

  // Clean up the unambiguous typing first, then judge what is left.
  jobs.forEach((j) => {
    // The plan workbooks name an operator, not an account. Every job that
    // arrives without an owner id gets one derived from that name, once, here —
    // so a register keyed before sign-in existed still belongs to somebody
    // afterwards.
    if (!j.opId) j.opId = opIdForName(j.op);
    j.fixes = normaliseJob(j as unknown as Record<string, unknown>);
    flagJob(j);
  });
  return { jobs, masters: deriveMasters(jobs, raw.masters) };
}

export { flagJob };
