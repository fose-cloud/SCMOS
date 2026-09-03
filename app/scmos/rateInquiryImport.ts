/**
 * The rate-inquiry workbook, read into the shape the register already holds.
 *
 * The team keeps every quote request in one workbook, a sheet per month, and
 * has since August 2025 — some three thousand lanes. The register was modelled
 * from that workbook and matches it column for column, and was empty, because
 * nothing could carry one into the other.
 *
 * Two generations of sheet live in the same file. The current one, from about
 * April 2026, carries a Date, a County and twenty-eight priced columns split by
 * DG, reefer and container. The older one has no Date, no County, a single
 * DG/Non-DG pair of ticks and four price columns. The header row is found
 * rather than assumed, so both read without the caller saying which is which.
 *
 * A price column this file does not recognise is reported, never dropped. Four
 * of them exist today — two Side Curtain columns and the two Flat-bed Trailer
 * ones — and a quiet import that lost those prices would be worse than one that
 * says it cannot place them.
 *
 * No imports, so `tests/rateInquiryImport.test.mjs` runs it on plain arrays.
 * The caller reads the workbook and hands the rows over.
 */

/** One lane of a request: where to where, priced per vehicle. */
export type ImportLane = {
  fromPlace: string;
  toPlace: string;
  county: string;
  carriers: string;
  fcl: boolean;
  lcl: boolean;
  remark: string;
  /** Vehicle code to price. Only the ones the sheet actually filled in. */
  prices: Record<string, number>;
};

export type ImportInquiry = {
  /** The sheet it came from, so a reader can find the row again. */
  sheet: string;
  /** True when the row carried no date and the sheet's month stood in. */
  datedFromSheet: boolean;
  /** "No." as the sheet numbers it — one request, however many lanes. */
  number: number;
  inquiredOn: string;
  requestor: string;
  customer: string;
  fuelBand: string;
  lanes: ImportLane[];
};

export type ImportRead = {
  inquiries: ImportInquiry[];
  /** Rows that named no customer and no lane — spacers and totals. */
  skipped: number;
  /** Price columns this file has no vehicle for, with what they were headed. */
  unmapped: string[];
  /**
   * Rows where two columns became one vehicle and disagreed about the price.
   *
   * Only 40' against 40'HQ does this, on the older sheets that gave them a
   * column each. The first value is kept and the row is named here, because
   * picking one silently is the same as deciding the two are the same price —
   * which is what the current sheets decided, but not what these rows say.
   */
  conflicts: string[];
};

/** Letters and digits only, upper case: "40'/40'HQ  DG" and "4040HQDG" are one. */
const key = (value: string) => value.toUpperCase().replace(/[^A-Z0-9]/g, "");

/**
 * Which vehicle each priced column is.
 *
 * Written out rather than derived from the heading. The headings carry three
 * spellings of reefer, two of isotank and a curly apostrophe, and a rule that
 * guessed would put a DG price in the plain column the first time somebody
 * typed "Reefer-DG" instead of "(Reefer) DG".
 */
const VEHICLE_BY_HEADING: Record<string, string> = {
  // Trucks, plain then dangerous goods then reefer.
  "4WNONDG": "4W", "6WNONDG": "6W", "10WNONDG": "10W",
  "4WDG": "4W DG", "6WDG": "6W DG", "10WDG": "10W DG",
  "4WREEFER": "4W RF", "6WREEFER": "6W RF", "10WREEFER": "10W RF",
  "4WREEFERDG": "4W RF DG", "6WREEFERDG": "6W RF DG", "10WREEFERDG": "10W RF DG",

  // Containers. 40' and 40'HQ are one price on this sheet and one code here.
  "20NONDG": "20F", "4040HQNONDG": "40F",
  "20DG": "20F DG", "4040HQDG": "40F DG",
  "20REEFER": "20RF", "4040HQREEFER": "40RF",
  "20OTIG": "20OT",

  // Tanks.
  "ISOTANK": "20TK", "20ISOTANKDG": "20TK DG", "40ISOTANK": "40TK",

  // The specials. The workbook spells the flatbed three ways.
  "6WHFLATBED": "6W FB", "6WFLATBED": "6W FB",
  "10WHHIABTRUCK": "10W HIAB", "6WHHIABTRUCK": "6W HIAB",
  "SIDECURTAINTRUCKNONDG": "SIDE", "SIDECURTAINTRUCK": "SIDE",
  "SIDECURTAINTRUCKDG": "SIDE DG",
  "FLATBEDTRAILERNONDG": "FBT", "FLATBEDTRAILERDG": "FBT DG",

  // The older sheets give 40' and 40'HQ a column each where the current ones
  // merge them, and the register followed the current ones — one code, labelled
  // "40' / 40'HQ". Both spellings land on it, and a row that prices them
  // differently is reported rather than silently resolved: see `conflicts`.
  "40NONDG": "40F", "40HQNONDG": "40F",
  "40DG": "40F DG", "40HQDG": "40F DG",
  "40REEFER": "40RF", "40HQREEFER": "40RF",
  "20ISOTANK": "20TK",

  // The older sheets price four columns and say DG with a tick elsewhere, so
  // these are the plain ones. The tick is read separately and moves the price
  // to the DG code when it is set.
  "4W": "4W", "6W": "6W", "10W": "10W", "20": "20F", "40": "40F",
};

/** Columns that are not prices, whatever else they are. */
const FIELD_BY_HEADING: Record<string, string> = {
  DATE: "date", NO: "no", REQUESTOR: "requestor", CUSTOMER: "customer",
  FROM: "from", TO: "to", COUNTY: "county",
  SUBCON: "subcon", SUNCON: "subcon", // the older sheets spell it Suncon
  FCL: "fcl", LCL: "lcl", DOMESTIC: "domestic", REMARK: "remark", REMARKS: "remark",
  DG: "dg", NONDG: "nondg",
};

const text = (value: unknown) => (value === null || value === undefined ? "" : String(value).trim());

/** A tick is anything written in the box — the sheets use x, X and ✓. */
const ticked = (value: unknown) => text(value).length > 0;

/**
 * A price, or null when the cell holds anything else.
 *
 * Blank means "not quoted for this vehicle", which is different from zero, and
 * the sheets also carry "-" and notes. Zero is refused with them: a lane quoted
 * at nothing is a mistake, not a free journey.
 */
function price(value: unknown): number | null {
  if (typeof value === "number") return Number.isFinite(value) && value > 0 ? Math.round(value) : null;
  const clean = text(value).replace(/,/g, "");
  if (!/^\d+(\.\d+)?$/.test(clean)) return null;
  const n = Math.round(Number(clean));
  return n > 0 ? n : null;
}

const EXCEL_EPOCH = Date.UTC(1899, 11, 30);
const pad = (n: number) => String(n).padStart(2, "0");

/** A sheet date as dd/MM/yyyy, from a serial or from text already in that shape. */
export function readDate(value: unknown): string {
  if (typeof value === "number" && value > 20000 && value < 60000) {
    const at = new Date(EXCEL_EPOCH + value * 86_400_000);
    return `${pad(at.getUTCDate())}/${pad(at.getUTCMonth() + 1)}/${at.getUTCFullYear()}`;
  }
  const found = /^(\d{1,2})\/(\d{1,2})\/(\d{2,4})$/.exec(text(value));
  if (!found) return "";
  const year = found[3].length === 2 ? 2000 + Number(found[3]) : Number(found[3]);
  return `${pad(Number(found[1]))}/${pad(Number(found[2]))}/${year}`;
}

const MONTHS = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

/**
 * The month a sheet is for, from its own name — "APR 2026", "Aug 2025".
 *
 * The older sheets have no Date column at all, and the register will not file
 * an inquiry without a date. The sheet is the month; the day is not in the file
 * and is not invented beyond the first, which the import says out loud rather
 * than letting 2,116 rows look precisely dated.
 */
export function monthOf(sheet: string): string {
  const found = /([A-Za-z]+)\s*(\d{4})/.exec(sheet.trim());
  if (!found) return "";
  const at = MONTHS.indexOf(found[1].slice(0, 3).toUpperCase());
  return at < 0 ? "" : `01/${pad(at + 1)}/${found[2]}`;
}

/** The fuel band a sheet quoted at, from the merged caption over the prices. */
export function readBand(bandRow: unknown[]): string {
  for (const cell of bandRow) {
    const found = /([\d.]+\s*[-–]\s*[\d.]+)/.exec(text(cell).replace(/\s+/g, " "));
    if (found) return found[1].replace(/\s+/g, "").replace("-", "–");
  }
  return "";
}

/**
 * One month's sheet.
 *
 * `rows` is the sheet as a grid, values as the workbook holds them — numbers as
 * numbers, so a date is still a serial and a price is still a price.
 */
/** A request in progress, and the lane ends its blank rows mean to repeat. */
type Carried = { inquiry: ImportInquiry; from: string; to: string };

export function readSheet(sheet: string, rows: unknown[][]): ImportRead {
  // The header is the row that names a requestor and a customer. Found rather
  // than assumed: it is row 2 on the current sheets and row 2 on the old ones,
  // and was row 1 on at least one month somebody edited.
  const headerAt = rows.findIndex((row) =>
    row.some((cell) => key(text(cell)) === "REQUESTOR")
    && row.some((cell) => key(text(cell)) === "CUSTOMER"));
  if (headerAt < 0) return { inquiries: [], skipped: 0, unmapped: [], conflicts: [] };

  const header = rows[headerAt];
  const band = readBand(headerAt > 0 ? rows[headerAt - 1] : []);

  const field: Record<string, number> = {};
  const priced: [number, string][] = [];
  const unmapped: string[] = [];
  const conflicts: string[] = [];

  header.forEach((cell, at) => {
    const label = text(cell);
    if (!label) return;
    const k = key(label);
    if (FIELD_BY_HEADING[k] !== undefined) { field[FIELD_BY_HEADING[k]] ??= at; return; }
    if (VEHICLE_BY_HEADING[k] !== undefined) { priced.push([at, VEHICLE_BY_HEADING[k]]); return; }
    // Anything else in the priced part of the row is a vehicle nobody has told
    // this file about. Reported with the heading as written, so somebody can
    // add it rather than wonder where the money went.
    unmapped.push(label.replace(/\s+/g, " "));
  });

  const at = (row: unknown[], name: string) =>
    field[name] === undefined ? "" : text(row[field[name]]);

  const inquiries: ImportInquiry[] = [];
  const byNumber = new Map<number, ImportInquiry>();
  let skipped = 0;
  /*
   * What the row above carried.
   *
   * Blank in this workbook rarely means "none" — it means "the same as the row
   * above". The number, the customer and both ends of the lane are all written
   * once and left empty down the rest of the request. Reading those blanks
   * literally produced lanes that started nowhere and ended nowhere, which the
   * register refused: 199 origins and 211 destinations, every single one of
   * which is written plainly a row or two higher.
   *
   * A blank end is only taken from a row belonging to the same request, so a
   * new customer never inherits the last one's port.
   */
  let above: Carried | null = null;

  for (const row of rows.slice(headerAt + 1)) {
    // A wholly empty row ends the block, and with it anything to inherit.
    if (!row.some((cell) => text(cell).length > 0)) { above = null; continue; }

    const customer = at(row, "customer");
    const written = { from: at(row, "from"), to: at(row, "to") };
    // A row with no customer and nowhere to go is a spacer or a total.
    if (!customer && !written.from && !written.to) { skipped++; continue; }

    const number = Number(at(row, "no")) || 0;

    // What this row may take from the one above it. A row continues the request
    // above when it names neither a new number nor a different customer — the
    // sheet's own way of saying "still this one". A row that starts a fresh
    // request carries nothing down, and its blanks stay blank.
    const carried: Carried | null =
      above !== null
      && (number === 0 || number === above.inquiry.number)
      && (!customer || customer === above.inquiry.customer)
        ? above
        : null;
    const from: string = written.from || carried?.from || "";
    const to: string = written.to || carried?.to || "";
    const prices: Record<string, number> = {};
    // The old sheets carry one set of prices and say DG with a tick, so the
    // same column means a different vehicle depending on that tick.
    const oldStyleDg = field.dg !== undefined && ticked(row[field.dg]);

    for (const [column, code] of priced) {
      const value = price(row[column]);
      if (value === null) continue;
      const wanted = oldStyleDg && !code.includes("DG") && DG_OF[code] ? DG_OF[code] : code;
      // First wins, and a disagreement is named rather than overwritten.
      if (prices[wanted] !== undefined && prices[wanted] !== value) {
        conflicts.push(`${sheet} No.${at(row, "no") || "?"} ${customer} · ${wanted}`
          + ` ${prices[wanted]} vs ${value} — kept ${prices[wanted]}`);
        continue;
      }
      prices[wanted] = value;
    }

    const lane: ImportLane = {
      fromPlace: from,
      toPlace: to,
      county: at(row, "county"),
      carriers: at(row, "subcon"),
      fcl: field.fcl !== undefined && ticked(row[field.fcl]),
      lcl: field.lcl !== undefined && ticked(row[field.lcl]),
      remark: at(row, "remark"),
      prices,
    };

    /*
     * Gathering the lanes of one request back together.
     *
     * The two generations of sheet continue a request differently, and both
     * have to work. The current one repeats the number, the requestor and the
     * customer down every row, so five lanes are five rows carrying "No. 2".
     * The older one fills them in once and leaves the rest blank, so a
     * continuation row has no number and no customer at all — and reading that
     * as a new request produced fourteen hundred inquiries with nobody to quote
     * to, which the register rightly refused.
     */
    const held = number > 0 ? byNumber.get(number) : undefined;
    if (held && held.customer === customer) {
      held.lanes.push(lane);
      above = { inquiry: held, from, to };
      continue;
    }
    if (carried) {
      carried.inquiry.lanes.push(lane);
      above = { inquiry: carried.inquiry, from, to };
      continue;
    }

    const onRow = readDate(field.date === undefined ? "" : row[field.date]);
    const inquiry: ImportInquiry = {
      sheet,
      number,
      datedFromSheet: onRow.length === 0,
      inquiredOn: onRow || monthOf(sheet),
      requestor: at(row, "requestor"),
      customer,
      fuelBand: band,
      lanes: [lane],
    };
    inquiries.push(inquiry);
    above = { inquiry, from, to };
    if (number > 0) byNumber.set(number, inquiry);
  }

  return { inquiries, skipped, unmapped: [...new Set(unmapped)], conflicts };
}

/** The dangerous-goods twin of a plain code, for the older sheets' tick. */
const DG_OF: Record<string, string> = {
  "4W": "4W DG", "6W": "6W DG", "10W": "10W DG",
  "20F": "20F DG", "40F": "40F DG",
  "SIDE": "SIDE DG",
};

/** Every month in the workbook, in the order the sheets sit. */
export function readWorkbook(sheets: { name: string; rows: unknown[][] }[]): ImportRead {
  const all: ImportRead = { inquiries: [], skipped: 0, unmapped: [], conflicts: [] };
  for (const { name, rows } of sheets) {
    const read = readSheet(name, rows);
    all.inquiries.push(...read.inquiries);
    all.skipped += read.skipped;
    all.conflicts.push(...read.conflicts);
    for (const one of read.unmapped) if (!all.unmapped.includes(one)) all.unmapped.push(one);
  }
  return all;
}
