import * as XLSX from "xlsx";
import { opIdForName } from "./nav";
import { opsStats, STATUS_RE, type Job } from "./ops";
import type { RateBook } from "./rates";
import { DEFAULT_STATUS, legacyStatus, normaliseJob, validateJob, clean, type Fix, type Issue } from "./standard";
import { inferImportCategory, sheetImportCategory, type ImportCategory } from "./excelImportCategory";
import { STATUS_LADDER, STATUS_TH } from "./theme";
import { dowOf, pad } from "./util";

/**
 * Excel is where the plan actually lives, so import and export are round-trip
 * partners: what we write out can be read back in without losing a field.
 * Everything read is pushed through the data standard on the way in, so a
 * workbook full of "9.00 น." and "25,670.00" lands normalised and the values
 * that need a person are flagged rather than silently accepted.
 */

/* ----------------------------------------------------------------- export */

type Column = { header: string; pick: (j: Job) => string };

const COMMON_HEAD: Column[] = [
  { header: "Category", pick: (j) => j.cat },
  { header: "Assigned To", pick: (j) => j.op },
  { header: "Priority", pick: (j) => j.prio },
];

const IMPORT_COLUMNS: Column[] = [
  { header: "Date", pick: (j) => j.date },
  { header: "Customer", pick: (j) => j.customer },
  { header: "Truck", pick: (j) => j.trucker },
  { header: "Job Code", pick: (j) => j.jobCode },
  { header: "Product / DG", pick: (j) => j.product },
  { header: "Destination", pick: (j) => j.destination },
  { header: "Plan Time", pick: (j) => j.planTime },
  { header: "Type", pick: (j) => j.type },
  { header: "CY Yard", pick: (j) => j.cyYard },
  { header: "Weight", pick: (j) => j.weight },
  { header: "Container No.", pick: (j) => j.container },
  { header: "Empty Return", pick: (j) => j.emptyReturn },
  { header: "Licence", pick: (j) => j.licence },
  { header: "Driver", pick: (j) => j.driver },
  { header: "Driver Contact", pick: (j) => j.contact },
  { header: "Status", pick: (j) => j.status },
  { header: "Arr. Date", pick: (j) => j.arrDate },
  { header: "Arr. Time", pick: (j) => j.arrTime },
  { header: "Reason / Delay", pick: (j) => j.reason },
  { header: "OT", pick: (j) => j.ot },
  { header: "CS", pick: (j) => j.cs },
];

const EXPORT_COLUMNS: Column[] = [
  { header: "Customer", pick: (j) => j.customer },
  { header: "Truck", pick: (j) => j.trucker },
  { header: "Booking", pick: (j) => j.booking },
  { header: "ABS No.", pick: (j) => j.abs },
  { header: "FCL/LCL", pick: (j) => j.fclLcl },
  { header: "Plant Loading", pick: (j) => j.plant },
  { header: "Plan Date", pick: (j) => j.date },
  { header: "Plan Time", pick: (j) => j.planTime },
  { header: "Type", pick: (j) => j.type },
  { header: "CY Yard", pick: (j) => j.cyYard },
  { header: "Return", pick: (j) => j.returnLoc },
  { header: "Closing Date", pick: (j) => j.closingDate },
  { header: "Closing Time", pick: (j) => j.closingTime },
  { header: "Container", pick: (j) => j.container },
  { header: "Seal", pick: (j) => j.seal },
  { header: "Tare", pick: (j) => j.tare },
  { header: "Licence", pick: (j) => j.licence },
  { header: "Driver", pick: (j) => j.driver },
  { header: "Driver Contact", pick: (j) => j.contact },
  // "Arrival" used to be this layout's only arrival column and it carried the
  // time, so an export sheet written here and read back came in with a time and
  // no date — which the standard then flagged on every row, because it judges
  // arrDate on an export job exactly as it does on an import one. The
  // operators' own export sheets have carried both columns all along.
  { header: "Arr. Date", pick: (j) => j.arrDate },
  { header: "Arr. Time", pick: (j) => j.arrTime },
  { header: "Status", pick: (j) => j.status },
  { header: "Remark", pick: (j) => j.remark },
];

const DELIVERY_COLUMNS: Column[] = [
  { header: "W/H", pick: (j) => j.wh ?? "" },
  { header: "Job No.", pick: (j) => j.jobNo ?? "" },
  { header: "Pickup Date", pick: (j) => j.date },
  // Same rule as the other two layouts: a category exports every date and time
  // the standard is willing to judge it on, or a round trip quietly drops the
  // value and the check fires against a blank the file never got the chance to
  // fill.
  { header: "Plan Time", pick: (j) => j.planTime },
  { header: "Arr. Date", pick: (j) => j.arrDate },
  { header: "Arr. Time", pick: (j) => j.arrTime },
  { header: "SID No.", pick: (j) => j.sid ?? "" },
  { header: "Customer", pick: (j) => j.customer },
  { header: "Province", pick: (j) => j.province ?? "" },
  { header: "ZIP", pick: (j) => j.zip ?? "" },
  { header: "Pallet", pick: (j) => j.pallet ?? "" },
  { header: "Weight KG", pick: (j) => j.weight || j.kgs || "" },
  { header: "4W", pick: (j) => j.v4 ?? "" },
  { header: "6W", pick: (j) => j.v6 ?? "" },
  { header: "10W", pick: (j) => j.v10 ?? "" },
  { header: "Trailer", pick: (j) => j.vtr ?? "" },
  { header: "Transport Cost", pick: (j) => j.cost ?? "" },
  { header: "Status", pick: (j) => j.status },
  { header: "Remark", pick: (j) => j.remark },
];

/** The mixed view has to carry every field or a round trip would drop data. */
const ALL_COLUMNS: Column[] = [
  ...IMPORT_COLUMNS,
  { header: "Booking", pick: (j) => j.booking },
  { header: "ABS No.", pick: (j) => j.abs },
  { header: "FCL/LCL", pick: (j) => j.fclLcl },
  { header: "Plant Loading", pick: (j) => j.plant },
  { header: "Return", pick: (j) => j.returnLoc },
  { header: "Closing Date", pick: (j) => j.closingDate },
  { header: "Closing Time", pick: (j) => j.closingTime },
  { header: "Seal", pick: (j) => j.seal },
  { header: "Tare", pick: (j) => j.tare },
  { header: "Remark", pick: (j) => j.remark },
  // What happened to the plan. Only on the mixed layout, which is the one that
  // has to carry every field or a round trip drops data; the three operator
  // layouts stay the shape their own sheets have always been.
  { header: "Original Date", pick: (j) => j.origDate },
  { header: "Moved By", pick: (j) => j.moveBy },
  { header: "Move Reason", pick: (j) => j.moveReason },
  { header: "Cancel Reason", pick: (j) => j.cancelReason },
];

export function columnsFor(layout: string): Column[] {
  if (layout === "IMPORT") return IMPORT_COLUMNS;
  if (layout === "EXPORT") return EXPORT_COLUMNS;
  if (layout === "DELIVERY") return DELIVERY_COLUMNS;
  return ALL_COLUMNS;
}

function autoWidth(rows: Record<string, string>[], headers: string[]) {
  return headers.map((h) => {
    const longest = rows.reduce((max, r) => Math.max(max, String(r[h] ?? "").length), h.length);
    return { wch: Math.min(38, Math.max(9, longest + 2)) };
  });
}

/**
 * Writes the jobs currently on screen. A second sheet lists every value that
 * breaks the standard so the file doubles as the correction worklist.
 */
/**
 * Writes the whole rate book out.
 *
 * One row per lane and vehicle with a column per diesel band, which is the
 * shape the carriers quoted in and the shape procurement negotiates in — the
 * screen's single price at today's diesel answers the daily question, this
 * answers the contract one. A second sheet carries the surcharges, because a
 * lane rate without them is not what a job costs.
 */
export function exportRates(book: RateBook): string {
  const bandLabels = book.bands.map((b) => b.label);
  const headers = ["Carrier", "Service", "Customer", "Origin", "Destination", "Province", "Type", ...bandLabels, "Remark"];

  const rows: Record<string, string>[] = [];
  for (const lane of book.lanes) {
    for (const [vehicle, prices] of Object.entries(lane.prices)) {
      const row: Record<string, string> = {
        Carrier: lane.carrier,
        Service: lane.service,
        Customer: lane.customer,
        Origin: lane.from,
        Destination: lane.to,
        Province: lane.county,
        Type: vehicle,
        Remark: lane.remark,
      };
      bandLabels.forEach((label, index) => {
        const price = prices[index];
        row[label] = price == null ? "" : String(price);
      });
      rows.push(row);
    }
  }

  const workbook = XLSX.utils.book_new();
  const sheet = XLSX.utils.json_to_sheet(rows, { header: headers });
  sheet["!cols"] = autoWidth(rows, headers);
  XLSX.utils.book_append_sheet(workbook, sheet, "Rates");

  const chargeHeaders = ["Service", "No.", "Charge", "Currency", "Rate", "Unit"];
  const chargeRows = book.surcharges.map((charge) => ({
    Service: charge.service, "No.": charge.no, Charge: charge.description,
    Currency: charge.currency, Rate: charge.rate, Unit: charge.unit,
  }));
  const chargeSheet = XLSX.utils.json_to_sheet(chargeRows, { header: chargeHeaders });
  chargeSheet["!cols"] = autoWidth(chargeRows, chargeHeaders);
  XLSX.utils.book_append_sheet(workbook, chargeSheet, "Surcharges");

  // What could not be read travels with the prices, so nobody negotiates from a
  // sheet believing it is complete.
  if (book.issues.length) {
    const issueHeaders = ["File", "Sheet", "Row", "Field", "Value", "Problem"];
    const issueRows = book.issues.map((issue) => ({
      File: issue.file, Sheet: issue.sheet, Row: String(issue.row),
      Field: issue.field, Value: issue.value, Problem: issue.message,
    }));
    const issueSheet = XLSX.utils.json_to_sheet(issueRows, { header: issueHeaders });
    issueSheet["!cols"] = autoWidth(issueRows, issueHeaders);
    XLSX.utils.book_append_sheet(workbook, issueSheet, "Not read");
  }

  const filename = `SCMOS_Transportation_Rates_${new Date().toISOString().slice(0, 10)}.xlsx`;
  XLSX.writeFile(workbook, filename);
  return filename;
}

export function exportJobs(jobs: Job[], layout: string, scopeLabel: string): string {
  const columns = [...COMMON_HEAD, ...columnsFor(layout)];
  const headers = columns.map((c) => c.header);

  const rows = jobs.map((j) => {
    const row: Record<string, string> = {};
    for (const c of columns) row[c.header] = c.pick(j) ?? "";
    row["Data Issues"] = j.issues.map((i) => i.label).join(", ");
    return row;
  });

  const book = XLSX.utils.book_new();
  const sheet = XLSX.utils.json_to_sheet(rows, { header: [...headers, "Data Issues"] });
  sheet["!cols"] = autoWidth(rows, [...headers, "Data Issues"]);
  XLSX.utils.book_append_sheet(book, sheet, layout.slice(0, 28) || "Jobs");

  const issueRows = jobs.flatMap((j) =>
    j.issues.map((issue) => ({
      "Job / ABS": j.jobCode || j.abs || j.jobNo || "",
      Customer: j.customer,
      "Assigned To": j.op,
      Field: issue.label,
      "Current Value": issue.value,
      Problem: issue.message,
      "Required Format": issue.expected,
      Example: issue.example,
      Severity: issue.severity,
    })),
  );
  if (issueRows.length) {
    const issueSheet = XLSX.utils.json_to_sheet(issueRows);
    issueSheet["!cols"] = autoWidth(
      issueRows as unknown as Record<string, string>[],
      Object.keys(issueRows[0]),
    );
    XLSX.utils.book_append_sheet(book, issueSheet, "Format issues");
  }

  const now = new Date();
  const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
  const filename = `SCMOS_${scopeLabel.replace(/[^\w-]+/g, "_")}_${stamp}.xlsx`;
  XLSX.writeFile(book, filename);
  return filename;
}

/* ------------------------------------------------------- dashboard export */

function sheetFrom(book: XLSX.WorkBook, name: string, rows: Record<string, string | number>[]) {
  if (!rows.length) return;
  const sheet = XLSX.utils.json_to_sheet(rows);
  sheet["!cols"] = autoWidth(rows as unknown as Record<string, string>[], Object.keys(rows[0]));
  XLSX.utils.book_append_sheet(book, sheet, name.slice(0, 28));
}

/** Counts jobs by one field, biggest first, so a sheet reads like the bar chart. */
function tally(jobs: Job[], pick: (j: Job) => string | undefined) {
  const counts: Record<string, number> = {};
  jobs.forEach((j) => {
    const key = (pick(j) || "").trim();
    if (key) counts[key] = (counts[key] || 0) + 1;
  });
  return Object.keys(counts)
    .map((k) => ({ key: k, n: counts[k] }))
    .sort((a, b) => b.n - a.n);
}

/**
 * Writes what the dashboard shows, one sheet per panel, from the same numbers
 * on screen — a management workbook rather than a dump of rows, with the job
 * list included last so every figure can be traced back.
 */
export function exportDashboard(jobs: Job[], scopeLabel: string): string {
  const s = opsStats(jobs);
  const book = XLSX.utils.book_new();
  const share = (n: number) => (s.jobs.length ? Math.round((n / s.jobs.length) * 100) + "%" : "—");

  sheetFrom(book, "Summary", [
    { Metric: "Jobs in plan", "ตัวชี้วัด": "งานทั้งหมดในแผน", Value: s.jobs.length, Share: "100%" },
    { Metric: "Operation days", "ตัวชี้วัด": "จำนวนวันที่มีงาน", Value: s.dates.length, Share: "" },
    { Metric: "Import", "ตัวชี้วัด": "งานนำเข้า", Value: s.imports.length, Share: share(s.imports.length) },
    { Metric: "Export", "ตัวชี้วัด": "งานส่งออก", Value: s.exports.length, Share: share(s.exports.length) },
    { Metric: "Delivery", "ตัวชี้วัด": "งานกระจายสินค้า", Value: s.deliveries.length, Share: share(s.deliveries.length) },
    { Metric: "Waiting truck", "ตัวชี้วัด": "รอรถ", Value: s.waiting.length, Share: share(s.waiting.length) },
    { Metric: "Truck confirmed", "ตัวชี้วัด": "ยืนยันรถ", Value: s.confirmed.length, Share: share(s.confirmed.length) },
    { Metric: "In operation", "ตัวชี้วัด": "กำลังปฏิบัติงาน", Value: s.running.length, Share: share(s.running.length) },
    { Metric: "Delayed", "ตัวชี้วัด": "ล่าช้า", Value: s.delayed.length, Share: share(s.delayed.length) },
    { Metric: "Completed", "ตัวชี้วัด": "เสร็จสิ้น", Value: s.done.length, Share: share(s.done.length) },
    { Metric: "Action required", "ตัวชี้วัด": "ต้องดำเนินการ", Value: s.action.length, Share: share(s.action.length) },
    { Metric: "Data error", "ตัวชี้วัด": "ข้อมูลผิดหรือไม่ครบ", Value: s.formatErrors.length, Share: share(s.formatErrors.length) },
    { Metric: "On-time arrival", "ตัวชี้วัด": "ถึงตรงเวลา", Value: s.otpPct + "%", Share: "วัดได้ " + s.measurable.length + "/" + s.jobs.length },
  ]);

  sheetFrom(book, "By Day", s.dates.map((d) => {
    const set = s.jobs.filter((j) => j.date === d);
    return {
      Date: d,
      Day: dowOf(d),
      Jobs: set.length,
      Import: set.filter((j) => j.cat === "IMPORT").length,
      Export: set.filter((j) => j.cat === "EXPORT").length,
      Delivery: set.filter((j) => j.cat === "DELIVERY").length,
      Delayed: set.filter((j) => STATUS_RE.delayed.test(j.status)).length,
    };
  }));

  const otpOf = (set: Job[]) => {
    const measured = set.filter((j) => s.measurable.indexOf(j) >= 0);
    if (!measured.length) return { pct: "—" as string | number, n: 0 };
    return { pct: Math.round((measured.filter((j) => s.onTime.indexOf(j) >= 0).length / measured.length) * 100) + "%", n: measured.length };
  };

  sheetFrom(book, "By Customer", tally(s.jobs, (j) => j.customer).map((row) => {
    const set = s.jobs.filter((j) => j.customer === row.key);
    const otp = otpOf(set);
    return { Customer: row.key, Jobs: row.n, Delayed: set.filter((j) => STATUS_RE.delayed.test(j.status)).length, "On-Time": otp.pct, "Measured On": otp.n };
  }));

  sheetFrom(book, "By Subcontractor", tally(s.jobs, (j) => j.trucker).map((row) => {
    const set = s.jobs.filter((j) => j.trucker === row.key);
    const otp = otpOf(set);
    return { Subcontractor: row.key, Jobs: row.n, Delayed: set.filter((j) => STATUS_RE.delayed.test(j.status)).length, "On-Time": otp.pct, "Measured On": otp.n };
  }));

  sheetFrom(book, "By Truck Type", tally(s.jobs, (j) => j.type).map((row) => ({ "Truck / Container Type": row.key, Jobs: row.n })));

  sheetFrom(book, "Status Pipeline", ["IMPORT", "EXPORT", "DELIVERY"].flatMap((c) => {
    const set = s.jobs.filter((j) => j.cat === c);
    return tally(set, (j) => j.status).map((row) => ({
      Category: c,
      Status: row.key,
      "สถานะ (ไทย)": STATUS_TH[row.key] ?? "",
      Jobs: row.n,
      "On Ladder": (STATUS_LADDER[c] ?? []).indexOf(row.key) >= 0 ? "Yes" : "No",
    }));
  }));

  sheetFrom(book, "Delay Reasons", tally(s.jobs, (j) => j.reason).map((row) => ({ Reason: row.key, Jobs: row.n })));

  sheetFrom(book, "Delayed Jobs", s.delayed.map((j) => ({
    Date: j.date, Category: j.cat, Customer: j.customer,
    "Job / ABS": j.jobCode || j.abs || j.jobNo || "",
    Subcontractor: j.trucker, Status: j.status, Reason: j.reason, Owner: j.op,
  })));

  sheetFrom(book, "Action Required", s.action.map((j) => ({
    Date: j.date, Category: j.cat, Customer: j.customer,
    "Job / ABS": j.jobCode || j.abs || j.jobNo || "",
    Status: j.status, Missing: j.flags.join(", "),
    "Data Issues": j.issues.map((i) => i.label).join(", "),
    Owner: j.op,
  })));

  sheetFrom(book, "Jobs", s.jobs.map((j) => ({
    Category: j.cat, Date: j.date, Customer: j.customer, Subcontractor: j.trucker,
    "Job / ABS": j.jobCode || j.abs || j.jobNo || "", Type: j.type, "Plan Time": j.planTime,
    Container: j.container, Licence: j.licence, Driver: j.driver,
    Status: j.status, "Arr. Date": j.arrDate, "Arr. Time": j.arrTime,
    "Reason / Delay": j.reason, Owner: j.op,
  })));

  const now = new Date();
  const stamp = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
  const filename = `SCMOS_Dashboard_${scopeLabel.replace(/[^\w-]+/g, "_")}_${stamp}.xlsx`;
  XLSX.writeFile(book, filename);
  return filename;
}

/* ----------------------------------------------------------------- import */

/**
 * Header spellings seen across the operators' plan files. Matching is done on
 * an upper-cased, whitespace-collapsed form, so "Plan  Loading Time" and
 * "PLANLOADING TIME" both land on planTime.
 */
const HEADER_ALIASES: Record<string, string[]> = {
  // Written by our own export so a mixed view survives a round trip: without it
  // every row inherits the sheet's category and export statuses read as invalid.
  cat: ["CATEGORY", "CAT", "ประเภทงาน"],
  date: ["DATE", "PLAN DATE", "PLAN LOADING DATE", "PICKUP DATE",
    // The delivery report hyphenates it, and a hyphen is not a space.
    "PICK-UP DATE", "PICK UP DATE", "วันที่"],
  customer: ["CUSTOMER", "CONSIGNEE", "CUSTOMER LIST", "ลูกค้า"],
  trucker: ["TRUCK", "TRUCKER", "TRUCKING", "TRUCKING COMPANY", "SUBCONTRACTOR", "SUB NAME", "ผู้ขนส่ง"],
  jobCode: ["JOB CODE", "JOBCODE", "JOB NO", "JOB NO."],
  abs: ["ABS", "ABS NO", "ABS NO.", "ABS.NO."],
  booking: ["BOOKING", "BOOKING NO", "BOOKING NO."],
  product: ["PRODUCT", "PRODUCT / DG", "DG", "PRODUCT/DG"],
  fclLcl: ["FCL/LCL", "FCL / LCL", "FCLLCL"],
  agent: ["AGENT", "SHIPPING AGENT"],
  destination: ["DESTINATION", "DELIVERY", "DELIVERY LOCATION", "ปลายทาง"],
  plant: ["PLANT", "PLANT LOADING", "LOADING PLANT"],
  planTime: ["PLAN TIME", "PLANLOADING TIME", "PLAN LOADING TIME", "TIME LOAD", "เวลา"],
  type: ["TYPE", "SIZE", "CONT TYPE", "CONTAINER TYPE"],
  cyYard: ["CY YARD", "CY", "YARD"],
  returnLoc: ["RETURN", "RETURN PLACE", "RETURN LOCATION"],
  emptyReturn: ["EMPTY RETURN", "RETURN EMPTY"],
  weight: ["WEIGHT", "TOTAL WEIGHT", "WEIGHT KG", "KGS", "GW", "น้ำหนัก"],
  container: ["CONTAINER", "CONTAINER NO", "CONTAINER NO.", "NO CONTAINER", "CONT NO"],
  // "NO SEAL" reads as an absence and is not one — it is how the export
  // sheets head the seal column, exactly as they head the container column
  // "NO CONTAINER". Without it every export job imported with no seal and
  // then raised a "Seal missing" flag against itself.
  seal: ["SEAL", "SEAL NO", "SEAL NO.", "NO SEAL"],
  tare: ["TARE", "TARE WEIGHT"],
  licence: ["LICENCE", "LICENSE", "PLATE", "TRUCK PLATE", "ทะเบียน", "ทะเบียนรถ"],
  driver: ["DRIVER", "DRIVER NAME", "คนขับ", "พนักงานขับรถ"],
  contact: ["CONTACT", "DRIVER CONTACT", "TEL", "PHONE", "เบอร์โทร"],
  arrDate: ["ARRIVAL DATE", "ARR DATE", "ARR. DATE", "DAET LOANDING", "ACTUAL DATE"],
  arrTime: ["ARRIVAL TIME", "ARR TIME", "ARR. TIME", "TIME LOANDING", "ACTUAL TIME", "ARRIVAL"],
  closingDate: ["CLOSING DATE", "CLOSING"],
  closingTime: ["CLOSING TIME"],
  status: ["STATUS", "สถานะ"],
  origDate: ["ORIGINAL DATE", "ORIG DATE", "PLAN DATE ORIGINAL", "วันเดิม"],
  moveBy: ["MOVED BY", "REQUESTED BY", "ผู้ขอเลื่อน"],
  moveReason: ["MOVE REASON", "POSTPONE REASON", "เหตุผลที่เลื่อน"],
  cancelReason: ["CANCEL REASON", "CANCELLATION REASON", "เหตุผลที่ยกเลิก"],
  reason: ["REASON", "REASON / DELAY", "DELAY", "DELAY REASON", "สาเหตุ",
    // The operators' own spelling, in every import sheet they keep.
    "REASON/DEALEY", "REASON/DELAY"],
  remark: ["REMARK", "REMARKS", "NOTE", "หมายเหตุ", "REMARKS 1", "REMARK 1"],
  ot: ["OT", "OVERTIME", "TIME/OT", "OT/TIME"],
  freightType: ["FREIGHT TYPE", "FREIGHT"],
  incident: ["INCIDENT REPORT", "INCIDENT"],
  cs: ["CS"],
  pickupPlan: ["PICKUP PLAN", "PICK UP PLAN"],
  op: ["OWNER", "ASSIGNED TO", "OPERATOR", "ผู้รับผิดชอบ"],
  wh: ["W/H", "WH", "WAREHOUSE", "คลัง"],
  jobNo: ["JOB NO", "JOB NO."],
  sid: ["SID", "SID NO", "SID NO."],
  province: ["PROVINCE", "จังหวัด"],
  zip: ["ZIP", "ZIPCODE", "ZIP CODE", "POSTCODE"],
  pallet: ["PALLET", "PALLETS"],
  // No kgs entry: "KGS" and "WEIGHT KG" both belong to `weight` above, and
  // earlier fields win, so a kilos column off any sheet arrives as `weight`.
  // `kgs` stays a field because the hand-entry form and the seed file fill
  // it, and both copy it into `weight` immediately afterwards.
  v4: ["4W", "4WH", "4-WHEEL"],
  v6: ["6W", "6WH", "6-WHEEL"],
  v10: ["10W", "10WH", "10-WHEEL"],
  vtr: ["TRAILER", "TRAILER QTY"],
  cost: ["COST", "TRANSPORT COST", "TRANSPORTATION", "ค่าขนส่ง"],
};

/**
 * One spelling of a column heading, used both to build the alias table and to
 * look a header up in it.
 *
 * Both sides have to strip the same things or an alias can never match, and
 * they did not: the lookup dropped a trailing dot while the table kept it, so
 * "ABS.NO." — the heading on every export sheet the operators keep — could
 * never be found, and every export job came in with no ABS number. That also
 * quietly broke duplicate matching, which falls back to customer and time when
 * a job has no number of its own.
 */
function normaliseHeader(header: unknown): string {
  return String(header ?? "").toUpperCase().replace(/\s+/g, " ").trim().replace(/[:.]+$/, "");
}

const NORMALISED_ALIASES = new Map<string, string>();
for (const [field, names] of Object.entries(HEADER_ALIASES)) {
  for (const name of names) {
    const key = normaliseHeader(name);
    // Earlier fields win, so "JOB NO" maps to jobCode rather than jobNo unless
    // it is the only claimant.
    if (!NORMALISED_ALIASES.has(key)) NORMALISED_ALIASES.set(key, field);
  }
}

/** Columns our export adds for reading convenience; not inputs on the way back. */
const DERIVED_HEADERS = new Set(["PRIORITY", "DATA ISSUES"]);

function headerToField(header: string): string | null {
  return NORMALISED_ALIASES.get(normaliseHeader(header)) ?? null;
}

function isDerivedHeader(header: string): boolean {
  return DERIVED_HEADERS.has(normaliseHeader(header));
}

/**
 * An Excel serial number as its calendar parts, in UTC arithmetic.
 *
 * `XLSX.SSF.parse_date_code` used to be called here and would have thrown the
 * moment it was reached: the package ships SSF in its CommonJS build and not in
 * the ESM one, which is the build the browser is given, so `XLSX.SSF` is
 * undefined there. It was never reached only because the workbook was being
 * read as formatted text, which is the thing this file no longer does.
 *
 * Day zero is 30 December 1899 — the offset that makes Excel's 1900 leap-year
 * bug come out right for every date since — and everything is read back in UTC
 * so no local timezone can move a plan date by a day.
 */
export function fromSerial(serial: number): { d: number; m: number; y: number; H: number; M: number } | null {
  if (!Number.isFinite(serial) || serial <= 0 || serial > 2958466) return null;
  const days = Math.floor(serial);
  // To the nearest minute. 08:00 is stored as 0.333333…, which is 07:59:59.99
  // if the remainder is simply truncated.
  const minutes = Math.round((serial - days) * 1440);
  const at = new Date(Date.UTC(1899, 11, 30) + days * 86_400_000 + minutes * 60_000);
  return {
    d: at.getUTCDate(), m: at.getUTCMonth() + 1, y: at.getUTCFullYear(),
    H: at.getUTCHours(), M: at.getUTCMinutes(),
  };
}

/**
 * Excel stores dates and times as numbers; render them in the house format.
 *
 * `raw` is the same cell unformatted, and for a date or a time column it is the
 * only trustworthy reading. The formatted text is whatever the column happens
 * to be *displaying*: the operators' export sheets are formatted `d/m/yy`, so
 * 1 July 2026 arrives as "1/7/26", which reads equally well as 7 January. The
 * data standard is right to refuse to guess at that — but it never should have
 * been asked, because the workbook knew the answer all along.
 */
function cellToText(value: unknown, field: string, raw?: unknown): string {
  // Only for dates and times, and only when the raw cell really is a value
  // rather than text somebody typed. Everywhere else the formatted reading is
  // the better one — it keeps a leading zero the number itself has lost.
  if (/date|time/i.test(field) && (typeof raw === "number" || raw instanceof Date)) value = raw;

  if (value === null || value === undefined) return "";
  if (value instanceof Date) {
    if (!/time/i.test(field)) {
      return `${pad(value.getDate())}/${pad(value.getMonth() + 1)}/${value.getFullYear()}`;
    }
    // An Excel time becomes a Date on an 1899 epoch, and in 1899 Bangkok ran on
    // local mean time — 6h42m04s off UTC. 08:00 comes back as 07:59:56, so the
    // minute has to be rounded rather than truncated.
    const seconds = value.getHours() * 3600 + value.getMinutes() * 60 + value.getSeconds();
    const minutes = Math.round(seconds / 60);
    return `${pad(Math.floor(minutes / 60) % 24)}:${pad(minutes % 60)}`;
  }
  if (typeof value === "number") {
    // Serial times are the fractional part of a day.
    if (value > 0 && value < 1) {
      const minutes = Math.round(value * 24 * 60);
      return `${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`;
    }
    const parsed = fromSerial(value);
    if (parsed && /date/i.test(field)) return `${pad(parsed.d)}/${pad(parsed.m)}/${parsed.y}`;
    if (parsed && /time/i.test(field)) return `${pad(parsed.H)}:${pad(parsed.M)}`;
    return String(value);
  }
  return clean(value);
}

export type ImportPreview = {
  fileName: string;
  sheets: string[];
  rows: number;
  jobs: Job[];
  fixes: Fix[];
  issues: Issue[];
  mappedHeaders: string[];
  unmappedHeaders: string[];
  /** What will actually be written, shown before the operator confirms. */
  categoryCounts: Record<ImportCategory, number>;
  /** Incoming rows that match a job already on the board, in row order. */
  dups: DupMatch[];
};

/* ----------------------------------------------------- duplicate matching */

export type DupDecision = "skip" | "overwrite" | "new";

export type DupDiff = { field: string; label: string; from: string; to: string };

/** An incoming row that matches a job already in the workspace. */
export type DupMatch = {
  /** The incoming job's key — how the operator's decision is recorded. */
  key: string;
  incoming: Job;
  existing: Job;
  /** What the file would change. Empty means the row carries nothing new. */
  diffs: DupDiff[];
};

const upper = (value: unknown) => clean(value).toUpperCase();

/**
 * What counts as the same job. The plan files identify one by its job code (ABS
 * on the export side), but a single code routinely covers several trucks on the
 * same day — 40 such groups exist in the July plan, one of them eight rows deep
 * — so the container has to be part of the key or a re-import would collapse
 * four trucks into one. Rows with no code at all (19 in July, all export) fall
 * back to the customer and the loading time.
 */
export function dupKey(j: Job): string {
  const id = upper(j.jobCode) || upper(j.abs) || upper(j.jobNo);
  const head = [upper(j.cat), upper(j.date), upper(j.container)];
  return (id ? [...head, id] : [...head, "~", upper(j.customer), upper(j.planTime)]).join("|");
}

/** The first alias doubles as the column's display name. */
export function fieldLabel(field: string): string {
  return HEADER_ALIASES[field]?.[0] ?? field;
}

/**
 * Compares only the fields the workbook actually supplied: a column the file
 * does not carry is missing information, not an instruction to blank the job.
 */
function diffAgainst(existing: Job, incoming: Job, supplied: Iterable<string>): DupDiff[] {
  const before = existing as unknown as Record<string, unknown>;
  const after = incoming as unknown as Record<string, unknown>;
  const diffs: DupDiff[] = [];
  for (const field of supplied) {
    if (field === "cat") continue;
    const to = clean(after[field]);
    if (!to) continue;
    const from = clean(before[field]);
    if (from === to) continue;
    diffs.push({ field, label: fieldLabel(field), from, to });
  }
  return diffs;
}

function matchDuplicates(
  incoming: Job[],
  existing: Job[],
  supplied: Map<Job, Set<string>>,
): DupMatch[] {
  if (!existing.length) return [];

  const buckets = new Map<string, Job[]>();
  for (const job of existing) {
    const bucket = buckets.get(dupKey(job));
    if (bucket) bucket.push(job); else buckets.set(dupKey(job), [job]);
  }

  const dups: DupMatch[] = [];
  for (const job of incoming) {
    const bucket = buckets.get(dupKey(job));
    if (!bucket?.length) continue;
    // An existing job can only be claimed once, so re-importing a group of four
    // trucks sharing a job code pairs them off one to one instead of matching
    // all four against the first.
    const existingJob = bucket.shift() as Job;
    dups.push({
      key: job.key,
      incoming: job,
      existing: existingJob,
      diffs: diffAgainst(existingJob, job, supplied.get(job) ?? []),
    });
  }
  return dups;
}

const BLANK_JOB: Omit<Job, "key" | "id" | "cat" | "op"> = {
  opId: "",
  date: "", customer: "", trucker: "", jobCode: "", abs: "", booking: "", product: "",
  fclLcl: "", agent: "", destination: "", plant: "", planTime: "", type: "", cyYard: "",
  returnLoc: "", emptyReturn: "", weight: "", container: "", seal: "", tare: "", licence: "",
  driver: "", contact: "", arrDate: "", arrTime: "", closingDate: "", closingTime: "",
  reason: "", remark: "", ot: "", pickupPlan: "", cs: "", incident: "", freightType: "",
  origDate: "", moveReason: "", moveBy: "", cancelReason: "",
  status: "", hist: [], flags: [], action: false, prio: "MEDIUM", issues: [], fixes: [],
};

/**
 * Reads every sheet whose name looks operational and turns each populated row
 * into a job. The category comes from the sheet name so one workbook can carry
 * both the import and export plan, which is how the operators keep them.
 *
 * `existing` is the board as it stands: rows that match a job already on it are
 * returned as `dups` rather than merged, because whether a re-import means
 * "skip", "update this job" or "another truck on the same booking" is the
 * operator's call, not something to guess at.
 */
export async function parseWorkbook(
  file: File,
  defaultOwner: string,
  existing: Job[] = [],
): Promise<ImportPreview> {
  // No `cellDates`: a date left as its serial number converts through
  // `fromSerial` below, which is exact. Turned into a Date first it goes
  // through the local timezone, which is where 08:00 became 07:59.
  const book = XLSX.read(await file.arrayBuffer(), { type: "array" });

  const jobs: Job[] = [];
  const allFixes: Fix[] = [];
  const mapped = new Set<string>();
  const unmapped = new Set<string>();
  /** Which fields each row actually carried, so a diff never reads a blank as a change. */
  const supplied = new Map<Job, Set<string>>();
  let rowCount = 0;

  for (const sheetName of book.SheetNames) {
    // Unknown tab names stay unknown until the header is available. Treating
    // every non-English name as Import hid Thai and abbreviated export plans in
    // the Import section even though their ABS/booking/closing columns made the
    // direction unambiguous.
    const namedCategory = sheetImportCategory(sheetName);
    const shape = { header: 1, defval: null, blankrows: false } as const;
    const matrix = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], { ...shape, raw: false });
    // The same cells as the workbook stores them, for the date and time columns.
    // Consulted only while the two readings line up row for row; if they ever
    // did not, the formatted one stands alone rather than pairing rows wrongly.
    const unformatted = XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[sheetName], { ...shape, raw: true });
    const raws = unformatted.length === matrix.length ? unformatted : [];
    if (!matrix.length) continue;

    // The header is the first row carrying several recognisable column names.
    let headerRow = -1;
    let fields: (string | null)[] = [];
    for (let i = 0; i < Math.min(matrix.length, 50); i++) {
      const candidate = (matrix[i] || []).map((c) => headerToField(String(c ?? "")));
      if (candidate.filter(Boolean).length >= 3) {
        headerRow = i;
        fields = candidate;
        (matrix[i] || []).forEach((c, idx) => {
          const label = clean(c);
          if (!label || isDerivedHeader(label)) return;
          if (candidate[idx]) mapped.add(label); else unmapped.add(label);
        });
        break;
      }
    }
    if (headerRow < 0) continue;

    // A heading that continues onto a second row.
    //
    // The delivery reports group columns under a merged heading — QTY over
    // PALLET and KGS., TYPE of Vehicle over 4W, 6W, 10W and TRAILER. A merged
    // cell fills only the first column of its group, so read as a single row
    // six of that report's columns have no heading at all and every one of them
    // imports empty: the pallets, the kilos and the whole vehicle count.
    //
    // Taken only when the row below names at least two columns this one left
    // unnamed, and carries neither a date nor a customer of its own. That last
    // part is what separates a sub-heading from the first row of data, and it is
    // the same question asked of every row further down.
    const below = matrix[headerRow + 1] || [];
    const sub = below.map((c) => headerToField(String(c ?? "")));
    const adds = sub.filter((field, idx) => field && !fields[idx]).length;
    const isData = fields.some((field, idx) =>
      (field === "date" || field === "customer") && clean(below[idx]) !== "");
    if (adds >= 2 && !isData) {
      sub.forEach((field, idx) => {
        if (!field || fields[idx]) return;
        fields[idx] = field;
        const label = clean(below[idx]);
        if (label && !isDerivedHeader(label)) mapped.add(label);
      });
      headerRow += 1;
    }

    for (let r = headerRow + 1; r < matrix.length; r++) {
      const cells = matrix[r] || [];
      const rawCells = raws[r] || [];
      if (cells.filter((c) => clean(c) !== "").length < 2) continue;

      const sheetFields = fields.filter((field): field is string => !!field);
      const fallbackCategory = namedCategory ?? inferImportCategory("", sheetName, sheetFields);
      const job = { ...BLANK_JOB, key: "", id: "", cat: fallbackCategory, op: defaultOwner } as Job;
      const record = job as unknown as Record<string, unknown>;
      const rowFields = new Set<string>();

      fields.forEach((field, idx) => {
        if (!field) return;
        const text = cellToText(cells[idx], field, rawCells[idx]);
        if (!text) return;
        record[field] = text;
        rowFields.add(field);
      });
      if (rowFields.size < 2) continue;
      if (!job.customer && !job.jobCode && !job.abs && !job.booking && !job.jobNo && !job.sid && !job.container) continue;
      // A job needs a day or a customer to be work at all. The July upload
      // brought in 28 rows off a reference sheet that carried nothing but a
      // code, and they sat at the top of every list looking like empty jobs.
      if (!job.date && !job.customer) continue;

      // A Category column overrides the sheet name, so one sheet can legitimately
      // hold import and export rows side by side.
      job.cat = inferImportCategory(record.cat, sheetName, rowFields);

      rowCount++;
      const key = `IMP${Date.now().toString(36)}-${rowCount}`;
      job.key = key;
      job.id = key;
      job.hist = [];
      // The status the file carries, read onto the ladder; a row that carries
      // none starts where an unworked plan row actually is — waiting on a
      // carrier. The old default was the words "Waiting Truck", which is not a
      // code on any ladder, so every row this importer has ever produced landed
      // carrying a status the app itself marked invalid.
      const onLadder = legacyStatus(job.status, job.cat);
      if (onLadder) job.status = onLadder;
      if (!job.status) job.status = DEFAULT_STATUS;
      if (!job.op) job.op = defaultOwner;
      // The workbook names an operator; ownership is decided on the id behind it.
      job.opId = opIdForName(job.op);

      job.fixes = normaliseJob(record);
      job.issues = validateJob(record);
      allFixes.push(...job.fixes);
      supplied.set(job, rowFields);
      jobs.push(job);
    }
  }

  const categoryCounts: Record<ImportCategory, number> = { IMPORT: 0, EXPORT: 0, DELIVERY: 0 };
  jobs.forEach((job) => { categoryCounts[job.cat as ImportCategory]++; });

  return {
    fileName: file.name,
    sheets: book.SheetNames,
    rows: rowCount,
    jobs,
    fixes: allFixes,
    issues: jobs.flatMap((j) => j.issues),
    mappedHeaders: [...mapped],
    unmappedHeaders: [...unmapped],
    categoryCounts,
    // Matched after normalising, so "9.00" and "09:00" are not two jobs.
    dups: matchDuplicates(jobs, existing, supplied),
  };
}
