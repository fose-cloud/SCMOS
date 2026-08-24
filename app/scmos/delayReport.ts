import * as XLSX from "xlsx";
import type { Job } from "./ops";
import { monthNameEn } from "./period";
import { lateMinutes } from "./util";

/**
 * The delay report as a workbook, in the shape management already reviews.
 *
 * Built to match the four-month performance review the account team circulates:
 * a dashboard, the KPI worked out month by month, what could not be measured
 * and why, the governance the figures were computed under, and the carriers
 * ranked by how many late trips sat under them.
 *
 * Two things are deliberate throughout.
 *
 * A trip with no arrival recorded is never counted as on time and never counted
 * as late. It is "not assessable", it has its own column and its own sheet, and
 * the OTD percentage is taken over what could be measured rather than over
 * everything. A review that quietly treats missing data as success is the one
 * thing a review must not do.
 *
 * The carrier ranking counts late trips and says so. It does not say whose
 * fault they were — the sheet carries that warning in writing, because a
 * ranking read as blame is how a number becomes an accusation.
 */

/** What one month of the account's work came to. */
export type MonthRow = {
  key: string;
  label: string;
  short: string;
  raw: number;
  eligible: number;
  onTime: number;
  late: number;
  notAssessable: number;
  /** On time over what could be measured. Null when nothing could be. */
  otd: number | null;
};

export type CarrierRow = {
  rank: number;
  carrier: string;
  late: number;
  share: number;
  /** Late trips per month, in the same order as the month rows. */
  byMonth: number[];
};

export type DelayReport = {
  customer: string;
  from: string;
  to: string;
  grace: number;
  target: number;
  months: MonthRow[];
  carriers: CarrierRow[];
  totalLate: number;
  totalRaw: number;
  totalNotAssessable: number;
};

const monthKeyOf = (date: string): string => {
  const parts = (date ?? "").trim().split("/");
  return parts.length === 3 ? `${parts[1]}/${parts[2]}` : "";
};

/**
 * Works the report out from the jobs already on screen.
 *
 * Takes the same rows the screen is showing rather than filtering again, so the
 * workbook and the table can never disagree about what was in scope.
 */
export function buildDelayReport(jobs: Job[], options: {
  customer: string; from: string; to: string; grace: number; target: number;
}): DelayReport {
  const { customer, from, to, grace, target } = options;

  const byMonth = new Map<string, Job[]>();
  jobs.forEach((job) => {
    const key = monthKeyOf(job.date);
    if (!key) return;
    const held = byMonth.get(key) ?? [];
    held.push(job);
    byMonth.set(key, held);
  });

  const months: MonthRow[] = [...byMonth.entries()]
    .sort((a, b) => {
      const [am, ay] = a[0].split("/");
      const [bm, by] = b[0].split("/");
      return (ay + am).localeCompare(by + bm);
    })
    .map(([key, held]) => {
      const [mm, yyyy] = key.split("/");
      const measured = held.map((job) => lateMinutes(job)).filter((m): m is number => m !== null);
      const late = measured.filter((m) => m > grace).length;
      const onTime = measured.length - late;
      return {
        key,
        label: `${monthNameEn(mm)} ${yyyy}`,
        short: monthNameEn(mm, true),
        raw: held.length,
        eligible: measured.length,
        onTime,
        late,
        notAssessable: held.length - measured.length,
        otd: measured.length ? (onTime / measured.length) * 100 : null,
      };
    });

  // Late trips per carrier, and how they fell across the months.
  const perCarrier = new Map<string, number[]>();
  jobs.forEach((job) => {
    const minutes = lateMinutes(job);
    if (minutes === null || minutes <= grace) return;
    const carrier = (job.trucker ?? "").trim() || "ไม่ระบุผู้ขนส่ง";
    const index = months.findIndex((month) => month.key === monthKeyOf(job.date));
    if (index < 0) return;
    const held = perCarrier.get(carrier) ?? months.map(() => 0);
    held[index] += 1;
    perCarrier.set(carrier, held);
  });

  const totalLate = months.reduce((sum, month) => sum + month.late, 0);
  const ordered = [...perCarrier.entries()]
    .map(([carrier, byMonthCounts]) => ({
      carrier,
      byMonth: byMonthCounts,
      late: byMonthCounts.reduce((sum, n) => sum + n, 0),
    }))
    .sort((a, b) => b.late - a.late || a.carrier.localeCompare(b.carrier));

  // Equal counts share a rank, and the next rank skips — the way a league table
  // is read, and the way the reviewed workbook does it.
  let lastLate = -1;
  let lastRank = 0;
  const carriers: CarrierRow[] = ordered.map((row, index) => {
    const rank = row.late === lastLate ? lastRank : index + 1;
    lastLate = row.late;
    lastRank = rank;
    return {
      rank,
      carrier: row.carrier,
      late: row.late,
      share: totalLate ? (row.late / totalLate) * 100 : 0,
      byMonth: row.byMonth,
    };
  });

  return {
    customer,
    from,
    to,
    grace,
    target,
    months,
    carriers,
    totalLate,
    totalRaw: months.reduce((sum, month) => sum + month.raw, 0),
    totalNotAssessable: months.reduce((sum, month) => sum + month.notAssessable, 0),
  };
}

const pct = (value: number | null, places = 1): string =>
  value === null ? "—" : value.toFixed(places);

/**
 * Statements about the months, taken from the numbers rather than written.
 *
 * The reviewed workbook carries a "Management Highlights" block of prose. These
 * are the same kind of sentences, but each one is a reading of a figure on the
 * sheet beside it — no judgement about causes, because the register does not
 * hold causes, and a generated sentence that sounds like analysis is worse than
 * no sentence at all.
 */
function highlights(report: DelayReport): [string, string][] {
  const { months, target } = report;
  if (!months.length) return [["No data", "ไม่มีเที่ยวที่วัดได้ในช่วงที่เลือก"]];

  const lines: [string, string][] = [];
  const met = months.filter((month) => month.otd !== null && month.otd >= target);
  const missed = months.filter((month) => month.otd !== null && month.otd < target);

  if (missed.length) {
    lines.push([
      missed.map((month) => month.short).join(", "),
      `OTD below the ${pct(target)}% target (`
        + missed.map((month) => `${month.short} ${pct(month.otd)}%`).join(", ") + ").",
    ]);
  }
  if (met.length) {
    lines.push([
      met.map((month) => month.short).join(", "),
      `OTD met the target (`
        + met.map((month) => `${month.short} ${pct(month.otd)}%`).join(", ") + ").",
    ]);
  }

  const measurable = months.filter((month) => month.otd !== null);
  if (measurable.length > 1) {
    const best = measurable.reduce((x, y) => (y.otd! > x.otd! ? y : x));
    const worst = measurable.reduce((x, y) => (y.otd! < x.otd! ? y : x));
    lines.push(["Range",
      `Best ${best.short} ${pct(best.otd)}%, weakest ${worst.short} ${pct(worst.otd)}%`
      + ` — a spread of ${pct(best.otd! - worst.otd!)} points across ${measurable.length} months.`]);
  }

  if (report.totalRaw) {
    lines.push(["Data quality",
      `${report.totalNotAssessable} of ${report.totalRaw} trips`
      + ` (${pct((report.totalNotAssessable / report.totalRaw) * 100)}%) carry no arrival and`
      + " are excluded from the percentage rather than counted as on time."]);
  }

  const top = report.carriers.slice(0, 4);
  if (top.length && report.totalLate) {
    const held = top.reduce((sum, row) => sum + row.late, 0);
    lines.push(["Exposure",
      `${top.map((row) => row.carrier).join(", ")} account for ${held} of`
      + ` ${report.totalLate} late trips (${pct((held / report.totalLate) * 100)}%).`
      + " Delay count indicates exposure, not confirmed responsibility."]);
  }

  return lines;
}

/** Blank cells to pad a row out to the widest one on the sheet. */
const row = (...cells: (string | number)[]): (string | number)[] => cells;

export function delayReportWorkbook(report: DelayReport): XLSX.WorkBook {
  const { customer, from, to, grace, target, months, carriers } = report;
  const scope = customer || "ทุกลูกค้า";
  const period = months.length
    ? `${months[0].label} – ${months[months.length - 1].label}`
    : `${from || "—"} – ${to || "—"}`;
  const book = XLSX.utils.book_new();

  /* ---------------------------------------------------------- Dashboard */
  const dashboard: (string | number)[][] = [
    row(`${scope} Trucking Performance Review`),
    row(),
    row(`${period} | Grace Period ${grace} Minutes | Target ≥${pct(target)}%`),
    row(),
    row(...months.map((month) =>
      `${month.short} OTD\n${pct(month.otd)}%\n${month.otd === null ? "NO DATA" : month.otd >= target ? "PASS" : "FAIL"}`)),
    row(),
    row(),
    row(),
    row(),
    row("Month", "OTD %", "Target %", "Raw Trips"),
    ...months.map((month) => row(month.short, pct(month.otd), pct(target), month.raw)),
    row(),
    row(),
    row("Management Highlights"),
    ...highlights(report).map(([label, text]) => row(label, text)),
    row(),
    row(`Subcontractor Delay Comparison — ${period}`),
    row("Rank", "Subcontractor", "Late Trips", "Share of Late"),
    ...carriers.slice(0, 4).map((carrier) =>
      row(carrier.rank, carrier.carrier, carrier.late, pct(carrier.share) + "%")),
  ];
  const dash = XLSX.utils.aoa_to_sheet(dashboard);
  dash["!cols"] = [{ wch: 22 }, { wch: 62 }, { wch: 12 }, { wch: 12 }];
  XLSX.utils.book_append_sheet(book, dash, "Dashboard");

  /* --------------------------------------------------------- KPI Detail */
  const detail = XLSX.utils.aoa_to_sheet([
    row("Month", "Raw Trips", "Eligible", "On Time", "Late", "Not Assessable",
      "OTD %", "Target %", "Status"),
    ...months.map((month) => row(
      month.label, month.raw, month.eligible, month.onTime, month.late, month.notAssessable,
      pct(month.otd), pct(target),
      month.otd === null ? "NO DATA" : month.otd >= target ? "PASS" : "FAIL")),
    row("Total", report.totalRaw,
      months.reduce((s, m) => s + m.eligible, 0),
      months.reduce((s, m) => s + m.onTime, 0),
      report.totalLate, report.totalNotAssessable, "", "", ""),
  ]);
  detail["!cols"] = [{ wch: 16 }, ...Array(8).fill({ wch: 14 })];
  XLSX.utils.book_append_sheet(book, detail, "KPI Detail");

  /* ------------------------------------------------------- Data Quality */
  const quality = XLSX.utils.aoa_to_sheet([
    row("Month", "Raw Trips", "Not Assessable", "Not Assessable %"),
    ...months.map((month) => row(month.short, month.raw, month.notAssessable,
      pct(month.raw ? (month.notAssessable / month.raw) * 100 : null) + "%")),
    row("Total", report.totalRaw, report.totalNotAssessable,
      pct(report.totalRaw ? (report.totalNotAssessable / report.totalRaw) * 100 : null) + "%"),
    row(),
    row("Data Quality Finding", "Status / Limitation"),
    row("Not assessable",
      "A trip with no arrival date or time recorded. Counted as neither on time nor late."),
    row("Effect on OTD",
      "The percentage is taken over eligible trips only, so missing arrivals lower confidence in the figure rather than raising it."),
    row("Delay reason",
      "A late trip is required to carry a reason in REASON/DELAY (import) or REMARK (export); trips missing one are visible in the workspace."),
    row("Vendor naming",
      "Carriers are counted under the name recorded on the job. Names that differ in the source are kept separate; no automatic merge is applied."),
  ]);
  quality["!cols"] = [{ wch: 22 }, { wch: 26 }, { wch: 18 }, { wch: 18 }];
  XLSX.utils.book_append_sheet(book, quality, "Data Quality");

  /* ---------------------------------------------------- KPI Definition */
  const definition = XLSX.utils.aoa_to_sheet([
    row("KPI Governance", `${scope} OTD`),
    row("Reporting Period", period),
    row("Scope", customer ? `Customer: ${customer}` : "All customers in the register"),
    row("Measuring Point", "Truck arrival against the planned loading slot"),
    row("Planned", "Plan Loading Date + Plan Loading Time"),
    row("Actual", "Arrival Date + Arrival Time"),
    row("Grace Period", `${grace} minutes`),
    row("Target", `≥${pct(target)}%`),
    row("Formula", "On-Time Eligible Trips / Eligible Trips × 100"),
    row("Eligible", "A trip carrying both a planned slot and a recorded arrival"),
    row("Excluded", "Trips with no arrival recorded — reported separately under Data Quality"),
    row("Source", "SCMOS operation register, read at export time"),
  ]);
  definition["!cols"] = [{ wch: 20 }, { wch: 68 }];
  XLSX.utils.book_append_sheet(book, definition, "KPI Definition");

  /* ------------------------------------------------ Subcontractor Delay */
  const ranking: (string | number)[][] = [
    row(`${scope} Subcontractor Delay Comparison`),
    row(),
    row(`${period} | Ranking by Late Trips | Grace Period ${grace} Minutes`),
    row(),
    row("Rank", "Subcontractor", "Late Trips", "Share of Total Late",
      ...months.map((month) => `${month.short} Late`)),
    ...carriers.map((carrier) => row(
      carrier.rank, carrier.carrier, carrier.late, pct(carrier.share) + "%", ...carrier.byMonth)),
    row("", "TOTAL", report.totalLate, report.totalLate ? "100.0%" : "0.0%",
      ...months.map((month) => month.late)),
    row(),
    row("Interpretation & Data Governance"),
    row("Ranking basis",
      "Late trips only. It answers which carrier had the most delayed movements for this account."),
    row("Important",
      "A late trip under a carrier does not mean the carrier caused the delay."),
    row("Accountability",
      "Confirm the delay reason and its evidence before assigning responsibility or raising a CAR/PAR."),
    row("Vendor naming",
      "Names are taken as recorded on the job; differing spellings are not merged automatically."),
  ];
  const rank = XLSX.utils.aoa_to_sheet(ranking);
  rank["!cols"] = [{ wch: 8 }, { wch: 24 }, { wch: 12 }, { wch: 18 },
    ...months.map(() => ({ wch: 11 }))];
  XLSX.utils.book_append_sheet(book, rank, "Subcontractor Delay");

  return book;
}

/**
 * Adds the trips themselves as a further sheet.
 *
 * The reviewed workbook stops at the summaries, which is right for a meeting
 * and wrong the moment somebody asks "which ones". These are the rows behind
 * every figure on the other sheets, in the column order management's own
 * request used.
 */
export function appendTripDetail(book: XLSX.WorkBook, rows: (string | number)[][],
  heads: string[]): void {
  const sheet = XLSX.utils.aoa_to_sheet([heads, ...rows]);
  sheet["!cols"] = heads.map((head) => ({ wch: Math.max(12, head.length + 3) }));
  XLSX.utils.book_append_sheet(book, sheet, "Trip Detail");
}

/** The filename the reviewed workbook uses, with this report's scope. */
export function delayReportName(report: DelayReport): string {
  const scope = (report.customer || "ALL").replace(/[^A-Za-z0-9]+/g, "_");
  const span = report.months.length
    ? `${report.months[0].short}-${report.months[report.months.length - 1].short}_`
      + report.months[report.months.length - 1].key.split("/")[1]
    : "period";
  return `${scope}_Performance_Review_${span}.xlsx`;
}
