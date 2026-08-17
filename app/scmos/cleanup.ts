import { dupKey } from "./excel";
import { flagJob, type Job } from "./ops";
import { clean, normaliseDate, normaliseJob } from "./standard";
import { STATUS_LADDER } from "./theme";

/**
 * A pass over the plan already in the register.
 *
 * The import normalises what it can as rows arrive, but a batch has context a
 * single row does not: the operators confirmed their files are written
 * day/month, and a job's own plan date says which month an arrival belongs to.
 * This uses that context to rescue values the per-field rules must leave alone,
 * and records every change on the job so none of it is silent.
 */

export type CleanupChange = { key: string; field: string; from: string; to: string; note: string };

export type CleanupReport = {
  scanned: number;
  changedJobs: number;
  changes: CleanupChange[];
  byKind: Record<string, number>;
  /** Values still needing a person after the pass. */
  remaining: { field: string; value: string; job: string }[];
};

const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;
const DATE = /^\d{2}\/\d{2}\/\d{4}$/;

/** Reads a two-component date as day/month, which is how the operators write them. */
function asDayFirst(value: string): string | null {
  const m = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})$/.exec(value);
  if (!m) return null;
  const day = Number(m[1]);
  const month = Number(m[2]);
  if (day < 1 || day > 31 || month < 1 || month > 12) return null;
  let year = Number(m[3]);
  if (m[3].length === 2) year += 2000;
  if (year > 2400) year -= 543;
  if (year < 2000 || year > 2100) return null;
  return String(day).padStart(2, "0") + "/" + String(month).padStart(2, "0") + "/" + year;
}

/**
 * Free-text statuses seen in the July upload, mapped onto the ladder they mean.
 * The wording is kept on the job as a remark, so the operator's own note is not
 * lost and a wrong mapping can be spotted and corrected.
 */
const STATUS_MAP: [RegExp, string, string?][] = [
  [/^ได้รับงาน/, "Truck Confirmed"],
  [/^รอรถ|รถอัพเดท|รออัพเดท/, "Waiting Truck"],
  [/^กำลังรอรับตู้|^รอรับตู้/, "Truck Confirmed"],
  [/ได้ตู้แล้ว|กำลังเดินทาง|ออกจากท่า/, "In Transit"],
  [/^ถึงโรงงาน|^ถึงลูกค้า/, "Arrived Customer", "Arrived Plant"],
  [/^รอการ์ด|^รอเอกสาร|^รอข้อมูล/, "Waiting Information"],
  [/^ส่งมอบเสร็จ|^ส่งเสร็จ/, "Delivery Completed"],
  [/^เสร็จ|^ปิดงาน/, "Completed"],
  [/^ยกเลิก/, "Cancelled"],
  [/^ล่าช้า|^ดีเลย์/, "Delayed"],
];

function mapStatus(status: string, cat: string): string | null {
  const ladder = STATUS_LADDER[cat] ?? STATUS_LADDER.IMPORT;
  if (ladder.indexOf(status) >= 0) return null;
  for (const [test, importValue, exportValue] of STATUS_MAP) {
    if (!test.test(status)) continue;
    const target = cat === "EXPORT" && exportValue ? exportValue : importValue;
    return ladder.indexOf(target) >= 0 ? target : null;
  }
  return null;
}

/** Plan-time cells carrying the pickup note rather than a loading time. */
const PICKUP_TEXT = /รับตู้|น\.\s*$|\d{1,2}\.\d{2}\.\d{2}/;

/** How a moved note is labelled in the remark, so its origin stays readable. */
const FIELD_LABEL: Record<string, string> = {
  arrTime: "เวลาถึง",
  arrDate: "วันที่ถึง",
  closingTime: "Closing time",
  closingDate: "Closing date",
};

export function cleanupJobs(jobs: Job[]): { report: CleanupReport; changed: Job[] } {
  const changes: CleanupChange[] = [];
  const remaining: CleanupReport["remaining"] = [];
  const byKind: Record<string, number> = {};
  const changed = new Set<Job>();

  const record = (job: Job, field: string, from: string, to: string, note: string, kind: string) => {
    changes.push({ key: job.key, field, from, to, note });
    byKind[kind] = (byKind[kind] || 0) + 1;
    changed.add(job);
  };

  for (const job of jobs) {
    const record0 = job as unknown as Record<string, unknown>;
    const label = job.jobCode || job.abs || job.jobNo || job.customer;

    // ---- dates ---------------------------------------------------------
    for (const field of ["date", "arrDate", "closingDate"] as const) {
      const value = clean(record0[field]);
      if (!value || DATE.test(value)) continue;

      // What the standard can settle on its own: a 24 that cannot be a month,
      // a 14 that cannot be a day-first month, an ISO date.
      const sure = normaliseDate(value);
      if (sure) {
        record0[field] = sure.value;
        record(job, field, value, sure.value, sure.note, "date-unambiguous");
        continue;
      }

      // What is left reads both ways. The operators confirmed day/month.
      const dayFirst = asDayFirst(value);
      if (dayFirst) {
        record0[field] = dayFirst;
        record(job, field, value, dayFirst, "ตีความเป็น วัน/เดือน ตามรูปแบบที่ทีมใช้", "date-dayfirst");
        continue;
      }
      remaining.push({ field, value, job: label });
    }

    // ---- plan time that is really the pickup note -----------------------
    const planTime = clean(job.planTime);
    if (planTime && !TIME.test(planTime) && PICKUP_TEXT.test(planTime)) {
      const pickup = clean(job.pickupPlan);
      if (!pickup) {
        job.pickupPlan = planTime;
        record(job, "planTime", planTime, "", "ย้ายไปช่อง Pickup Plan — ไม่ใช่เวลานัดโหลด", "plantime-moved");
      } else if (pickup === planTime) {
        record(job, "planTime", planTime, "", "ซ้ำกับ Pickup Plan ที่มีอยู่แล้ว", "plantime-moved");
      } else {
        job.remark = [clean(job.remark), planTime].filter(Boolean).join(" · ");
        record(job, "planTime", planTime, "", "ย้ายไปหมายเหตุ — Pickup Plan มีข้อความอื่นอยู่แล้ว", "plantime-moved");
      }
      job.planTime = "";
    }

    // ---- notes written into time and date cells -------------------------
    // "รออกจากท่า" in the arrival time, "TBA" or "CHECK JWD" in a closing
    // date: real information, but not a time — so it moves to the remark and
    // the field is cleared, instead of sitting there failing the standard.
    for (const field of ["arrTime", "closingTime", "closingDate", "arrDate"] as const) {
      const value = clean(record0[field]);
      if (!value) continue;
      const wellFormed = field.endsWith("Time") ? TIME.test(value) : DATE.test(value);
      if (wellFormed) continue;
      // Anything that still holds digits is a malformed time or date rather
      // than a note; those stay flagged for a person to read.
      if (/\d/.test(value)) continue;

      job.remark = [clean(job.remark), FIELD_LABEL[field] + ": " + value].filter(Boolean).join(" · ");
      record0[field] = "";
      record(job, field, value, "", "ย้ายข้อความไปหมายเหตุ — ไม่ใช่วันที่/เวลา", "note-moved");
    }

    // ---- free-text status ----------------------------------------------
    const status = clean(job.status);
    const mapped = status ? mapStatus(status, job.cat) : null;
    if (mapped) {
      job.status = mapped;
      job.remark = [clean(job.remark), "สถานะเดิม: " + status].filter(Boolean).join(" · ");
      record(job, "status", status, mapped, "แปลงเป็นสถานะในลำดับงาน · เก็บข้อความเดิมไว้ในหมายเหตุ", "status-mapped");
    } else if (status && (STATUS_LADDER[job.cat] ?? STATUS_LADDER.IMPORT).indexOf(status) < 0) {
      remaining.push({ field: "status", value: status, job: label });
    }
  }

  // Everything the standard fixes on its own, then the flags and priorities.
  for (const job of changed) {
    const fixes = normaliseJob(job as unknown as Record<string, unknown>);
    fixes.forEach((fix) => record(job, fix.field, fix.from, fix.to, fix.note, "standard-fix"));
    flagJob(job);
  }

  return {
    report: { scanned: jobs.length, changedJobs: changed.size, changes, byKind, remaining },
    changed: [...changed],
  };
}

/* ------------------------------------------------------------ duplicates */

export type DupGroup = {
  key: string;
  jobs: Job[];
  owners: string[];
  statuses: string[];
  /** Which load each row arrived on — the plan file, or a particular import. */
  batches: string[];
  /**
   * True when the rows came from different loads, which is what a job keyed
   * twice looks like. Rows from a single load that share a code with no
   * container are more likely several trucks on one booking — the July plan
   * has 40 such groups legitimately — so those are not called duplicates.
   */
  reUploaded: boolean;
};

/** The load a job arrived on, read from how its key was minted. */
function batchOf(job: Job): string {
  const key = job.key || "";
  const imported = /^(IMP[a-z0-9]+)-\d+$/.exec(key);
  if (imported) return "นำเข้า " + imported[1].slice(3);
  if (/^X\d+$/.test(key)) return "ทำสำเนาในระบบ";
  return "แผนตั้งต้น";
}

/**
 * Jobs that describe the same trip. Run after the cleanup: before the dates are
 * in one shape the same job written 24/7/26 and 24/07/2026 does not match, which
 * is how the July upload slipped 106 repeats past the import check.
 */
export function duplicateGroups(jobs: Job[]): DupGroup[] {
  const groups = new Map<string, Job[]>();
  for (const job of jobs) {
    const key = dupKey(job);
    const bucket = groups.get(key);
    if (bucket) bucket.push(job); else groups.set(key, [job]);
  }
  return [...groups.entries()]
    .filter(([, list]) => list.length > 1)
    .map(([key, list]) => {
      const batches = [...new Set(list.map(batchOf))];
      return {
        key,
        jobs: list,
        owners: [...new Set(list.map((j) => j.op).filter(Boolean))],
        statuses: [...new Set(list.map((j) => j.status).filter(Boolean))],
        batches,
        reUploaded: batches.length > 1,
      };
    })
    // The ones that were loaded twice come first: those are the certain ones.
    .sort((a, b) => Number(b.reUploaded) - Number(a.reUploaded) || b.jobs.length - a.jobs.length);
}
