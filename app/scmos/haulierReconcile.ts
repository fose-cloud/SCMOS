/**
 * The haulier's own list of runs, held against ours.
 *
 * The transport company sends a workbook shaped like the Domestic grid —
 * the trips they say they ran and what they charge for each — and the
 * account team check it line by line against the register. The department
 * asked on 15 September 2026 for SCMOS to do the looking: each row of the
 * file is found in our Domestic table, and where the two disagree the row
 * says which column disagrees and what each side holds.
 *
 * Their price is held against what our cost card says the run should cost
 * (the ตรวจสอบค่าขนส่ง reading), not against the selling rate on the grid —
 * the grid's rate is what the customer is billed, and a haulier's invoice
 * is checked against what we agreed to pay them.
 *
 * Nothing here changes a job. It is a comparison, and a person acts on it.
 * No imports beyond the leaves, with the extension, so it loads under test.
 */

import { trucksOf, type CheckableJob, type CheckedTrip } from "./chemoursCheck.ts";
import { zipKey } from "./domesticRate.ts";

/** A row of the haulier's file, as the workbook reader hands it over. */
export type HaulierRow = CheckableJob & { rowNo?: number };

export type Difference = {
  field: "date" | "destination" | "vehicle" | "cost" | "wh";
  label: string;
  /** What the file says and what the register says, as a person would read them. */
  theirs: string;
  ours: string;
  /** For the cost: theirs minus ours, in baht. */
  gap?: number;
};

export type Reconciled = {
  row: HaulierRow;
  /** The run in our register this row was taken to be, or null. */
  job: CheckableJob | null;
  /** What the two were matched on. */
  matchedBy: "jobCode" | "dCode" | "tmsId" | "date+customer+zip" | "";
  /** Our card's reading of that run, when the check could price it. */
  check: CheckedTrip | null;
  theirCost: number | null;
  differences: Difference[];
  verdict: "match" | "differs" | "unmatched" | "unpriced";
};

export type Reconciliation = {
  lines: Reconciled[];
  /** Our runs of the month the file does not carry — not billed, or billed elsewhere. */
  unbilled: CheckableJob[];
  /** Sums over the lines the file priced and the runs our card priced. */
  theirTotal: number;
  ourTotal: number;
  counts: { rows: number; matched: number; match: number; differs: number; unmatched: number; unpriced: number };
};

const norm = (value: string | undefined | null) => String(value ?? "").trim().toUpperCase().replace(/\s+/g, " ");
const money = (value: string | undefined | null): number | null => {
  const text = String(value ?? "").replace(/[,\s฿]/g, "");
  if (!text) return null;
  const number = Number(text);
  return Number.isFinite(number) && number > 0 ? number : null;
};

/** The run in the register a file row is: by job code, then D-code, then TMS ID, then the day, the consignee and the postcode. */
export function matchRow(row: HaulierRow, jobs: readonly CheckableJob[], taken: Set<string>): { job: CheckableJob | null; by: Reconciled["matchedBy"] } {
  const free = jobs.filter((job) => !taken.has(job.key));
  const byCode = (field: "jobCode" | "dCode" | "tmsId") => {
    const wanted = norm(row[field]);
    return wanted ? free.find((job) => norm(job[field]) === wanted) ?? null : null;
  };
  for (const field of ["jobCode", "dCode", "tmsId"] as const) {
    const found = byCode(field);
    if (found) return { job: found, by: field };
  }
  // The same day, the same consignee, the same postcode: the row for a run
  // whose code the haulier did not copy. All three, so two runs to one
  // consignee on one day are not taken for each other by half a match.
  if (row.date && row.customer && zipKey(row.zip)) {
    const found = free.find((job) => job.date === row.date && norm(job.customer) === norm(row.customer) && zipKey(job.zip) === zipKey(row.zip));
    if (found) return { job: found, by: "date+customer+zip" };
  }
  return { job: null, by: "" };
}

/** The columns the two sides disagree on, each said both ways. */
export function differencesOf(row: HaulierRow, job: CheckableJob, check: CheckedTrip | null): Difference[] {
  const out: Difference[] = [];
  if (row.date && job.date && row.date !== job.date) {
    out.push({ field: "date", label: "วันที่", theirs: row.date, ours: job.date });
  }
  const theirZip = zipKey(row.zip);
  const ourZip = zipKey(job.zip);
  if (theirZip && ourZip && theirZip !== ourZip) {
    out.push({ field: "destination", label: "ปลายทาง", theirs: theirZip, ours: ourZip });
  } else if (!theirZip && row.destination && job.destination && norm(row.destination) !== norm(job.destination)
    && !norm(row.destination).includes(norm(job.destination)) && !norm(job.destination).includes(norm(row.destination))) {
    out.push({ field: "destination", label: "ปลายทาง", theirs: row.destination, ours: job.destination });
  }
  if (row.wh && job.wh && norm(row.wh) !== norm(job.wh) && !norm(row.wh).includes(norm(job.wh)) && !norm(job.wh).includes(norm(row.wh))) {
    out.push({ field: "wh", label: "W/H", theirs: row.wh, ours: job.wh });
  }
  const theirTrucks = trucksOf(row);
  const ourTrucks = trucksOf(job);
  if (theirTrucks && ourTrucks && theirTrucks !== ourTrucks) {
    out.push({ field: "vehicle", label: "ประเภทรถ", theirs: theirTrucks, ours: ourTrucks });
  }
  const theirs = money(row.cost);
  if (theirs !== null && check && check.total !== null && Math.round(theirs) !== Math.round(check.total)) {
    out.push({
      field: "cost", label: "ค่าขนส่ง",
      theirs: `฿${theirs.toLocaleString("en-US")}`, ours: `฿${check.total.toLocaleString("en-US")}`,
      gap: Math.round(theirs - check.total),
    });
  }
  return out;
}

/**
 * The file against the register, row by row, and what the register has
 * that the file does not.
 *
 * `checks` is the ตรวจสอบค่าขนส่ง reading of our runs, keyed by job, so the
 * cost comparison uses the same figure the tab shows. A run the card could
 * not price is reported as such rather than as agreeing or disagreeing on
 * price.
 */
export function reconcile(rows: readonly HaulierRow[], jobs: readonly CheckableJob[], checks: readonly CheckedTrip[]): Reconciliation {
  const checkOf = new Map(checks.map((check) => [check.job.key, check]));
  const taken = new Set<string>();
  const lines: Reconciled[] = rows.map((row) => {
    const { job, by } = matchRow(row, jobs, taken);
    const theirCost = money(row.cost);
    if (!job) {
      return { row, job: null, matchedBy: "", check: null, theirCost, differences: [], verdict: "unmatched" };
    }
    taken.add(job.key);
    const check = checkOf.get(job.key) ?? null;
    const differences = differencesOf(row, job, check);
    const priced = check !== null && check.total !== null;
    const verdict: Reconciled["verdict"] = differences.length ? "differs" : priced || theirCost === null ? "match" : "unpriced";
    return { row, job, matchedBy: by, check, theirCost, differences, verdict };
  });
  const unbilled = jobs.filter((job) => !taken.has(job.key));
  return {
    lines,
    unbilled,
    theirTotal: lines.reduce((sum, line) => sum + (line.theirCost ?? 0), 0),
    ourTotal: lines.reduce((sum, line) => sum + (line.check?.total ?? 0), 0),
    counts: {
      rows: lines.length,
      matched: lines.filter((line) => line.job).length,
      match: lines.filter((line) => line.verdict === "match").length,
      differs: lines.filter((line) => line.verdict === "differs").length,
      unmatched: lines.filter((line) => line.verdict === "unmatched").length,
      unpriced: lines.filter((line) => line.verdict === "unpriced").length,
    },
  };
}

/** What a line's verdict says, in the words the row carries. */
export function verdictWords(line: Reconciled): string {
  switch (line.verdict) {
    case "match": return "ตรงกัน";
    case "differs": return "ต่างกัน: " + line.differences.map((one) => one.label).join(", ");
    case "unmatched": return "ไม่พบงานนี้ในตาราง Domestic";
    case "unpriced": return "ตรงกัน แต่ตารางต้นทุนยังคิดราคาเที่ยวนี้ไม่ได้";
  }
}
