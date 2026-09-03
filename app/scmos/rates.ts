import * as XLSX from "xlsx";

/**
 * Subcontractor transport rates.
 *
 * Every carrier quotes on the same LESCHACO form: a lane down the page
 * (customer, from, to, province) and, across it, a price for each vehicle or
 * container type repeated once per diesel price band. The bands are the
 * contract's fuel clause — the rate rises about 3% each time diesel crosses
 * into the next band — so a rate is never one number, it is a row of seven.
 *
 * The forms are filled in by twenty-one different companies, so the layout
 * wobbles: some quote trucks and containers in two separate blocks with their
 * own band sets, some drop the province column, some write 4WH where others
 * write 4W. This reads the shape out of the header rather than assuming it, and
 * flags what it cannot read instead of guessing a price.
 */

/** One step of the fuel clause. `max` is Infinity on the open-ended top band. */
export type FuelBand = { label: string; min: number; max: number };

export type RateLane = {
  id: string;
  carrier: string;
  /** FCL · LCL · REEFER · ISO TANK — what the sheet is quoting. */
  service: string;
  customer: string;
  from: string;
  to: string;
  county: string;
  remark: string;
  /**
   * Price by vehicle type, one entry per band in `RateBook.bands`. `null` where
   * that carrier does not quote the vehicle at that band — which is normal, not
   * an error: the truck block on a mixed form only carries four bands.
   */
  prices: Record<string, (number | null)[]>;
  /**
   * Where the row came from.
   *
   * `carrier` is a price read off a carrier's own signed form. `quotation` is
   * one keyed into the rate sheet and spread up the bands by the fuel clause —
   * the API works those out from the single figure the sheet holds, so the two
   * cannot drift apart.
   *
   * Optional because a book built by the browser's own reader has no opinion;
   * absent means `carrier`, which is what those rows are.
   */
  source?: "carrier" | "quotation";
};

export type RateSource = {
  carrier: string;
  file: string;
  sheet: string;
  service: string;
  lanes: number;
  skipped: number;
};

export type RateIssue = {
  file: string;
  sheet: string;
  row: number;
  field: string;
  value: string;
  message: string;
};

export type RateBook = {
  bands: FuelBand[];
  lanes: RateLane[];
  sources: RateSource[];
  issues: RateIssue[];
  /** Extra charges from the form's Remark sheet — real contract terms. */
  surcharges: Surcharge[];
  builtAt: string;
};

export type Surcharge = {
  service: string;
  no: string;
  description: string;
  currency: string;
  rate: string;
  unit: string;
};

/* ------------------------------------------------------------------ naming */

/**
 * Carrier names as the register spells them, mapped to the folder that holds
 * their rates. Only spellings whose reading is beyond doubt are here — an
 * abbreviation that could be two companies is left alone and reported, because
 * paying the wrong subcontractor's rate is worse than having no rate.
 */
const CARRIER_ALIASES: Record<string, string> = {
  ACN: "A.C.N",
  "A.C.N": "A.C.N",
  NEXTGEN: "NEXT GEN",
  "NEXT GEN": "NEXT GEN",
  WAK: "W.A.K",
  "W.A.K": "W.A.K",
  NATNISA: "NATNISA",
  "GREAT TRANSPORT": "GREAT TRANSPORT",
};

/** The canonical carrier name for a spelling, or the trimmed input unchanged. */
export function canonicalCarrier(name: string): string {
  const key = (name ?? "").trim().toUpperCase().replace(/\s+/g, " ");
  return CARRIER_ALIASES[key] ?? key;
}

/* ------------------------------------------------------------------- bands */

const BASE_BAND = /based\s+diesel\s+price\s+at\s+([\d.]+)/i;
const RANGE_BAND = /^([\d]+\.[\d]{2})\s*[-–]\s*([\d]+\.[\d]{2})$/;

/** Reads a band header. Returns null for anything that is not one. */
export function parseBand(text: string): FuelBand | null {
  const value = (text ?? "").replace(/\s+/g, " ").trim();
  if (!value) return null;

  const base = value.match(BASE_BAND);
  if (base) return { label: `≤ ${base[1]}`, min: 0, max: Number(base[1]) };

  const range = value.match(RANGE_BAND);
  if (range) return { label: `${range[1]}–${range[2]}`, min: Number(range[1]), max: Number(range[2]) };

  return null;
}

/** Which band a diesel price falls in. -1 when it is above every quoted band. */
export function bandForDiesel(bands: FuelBand[], diesel: number): number {
  for (let i = 0; i < bands.length; i++) {
    if (diesel <= bands[i].max) return i;
  }
  return -1;
}

/**
 * Whether two band headers describe the same step of the fuel clause.
 *
 * The truck block on a mixed form ends its third band at 36.29 and the
 * container block ends it at 36.30. DGT write 39.92-43.90 where the form says
 * 39.95-43.94. A few satang apart is how the form was typed, not two different
 * contract terms, and treating them as separate bands would put two carriers'
 * prices for the same fuel price in different columns.
 *
 * A tenth of a baht is the line. DGT's lowest band starts at 27.00 where the
 * form starts at zero, and their per-customer contracts genuinely step at
 * 33.00-35.99 rather than 33.00-36.29 — those stay separate, because they are.
 */
export function sameBand(a: FuelBand, b: FuelBand): boolean {
  return Math.abs(a.min - b.min) <= 0.1 && Math.abs(a.max - b.max) <= 0.1;
}

/* ------------------------------------------------------------------ layout */

/**
 * A vehicle or container heading. Wheel counts (4W, 6WH, 10W), containers
 * (20F, 40HC), reefers (20RF) and tanks (20TK) all appear, with and without
 * a DG suffix — a carrier quoting reefers only writes 20RF, so a narrow list
 * loses their whole sheet rather than one column.
 */
const VEHICLE = /^\d{1,2}\s*(W|WH|F|RF|TK|HC|')|^(TRAILER|ISO|TANK)/i;

const LANE_LABELS = {
  customer: /customer/i,
  from: /^(from|form|origin)$/i,
  to: /^(to|destination)$/i,
  county: /^(county|country|province)$/i,
};

/** Normalises the vehicle spellings the carriers use onto one vocabulary. */
export function canonicalVehicle(text: string): string {
  const value = (text ?? "").replace(/\s+/g, " ").trim().toUpperCase().replace(/'/g, "");
  if (!value) return "";
  const dg = /\bDG\b/.test(value);
  const stem = value
    .replace(/\bDG\b/g, "")
    .replace(/\//g, " ")
    .trim()
    .split(" ")[0]
    .replace(/^(\d{1,2})WH$/, "$1W");
  return dg ? `${stem} DG` : stem;
}

type Layout = {
  bandRow: number;
  labelRow: number;
  vehicleRow: number;
  lane: { customer: number; from: number; to: number; county: number };
  remarkCol: number;
  /** Each quoted block: where it starts and which band it prices. */
  groups: { start: number; end: number; band: FuelBand }[];
};

function readLayout(rows: unknown[][]): Layout | null {
  let bandRow = -1;
  let labelRow = -1;

  for (let r = 0; r < Math.min(rows.length, 10); r++) {
    const joined = (rows[r] ?? []).map((c) => String(c ?? "")).join(" ");
    if (bandRow < 0 && /diesel/i.test(joined)) bandRow = r;
    // SHORE label their columns ORIGIN / CUSTOMER NAME / DESTINATION rather than
    // Customer / Form / TO, so the row is found by what it means, not its wording.
    if (labelRow < 0 && /customer/i.test(joined) && /\b(to|from|form|origin|destination)\b/i.test(joined)) labelRow = r;
  }
  if (bandRow < 0 || labelRow < 0) return null;

  const labels = rows[labelRow] ?? [];
  const find = (test: RegExp) => labels.findIndex((c) => test.test(String(c ?? "").trim()));
  const lane = {
    customer: find(LANE_LABELS.customer),
    from: find(LANE_LABELS.from),
    to: find(LANE_LABELS.to),
    county: find(LANE_LABELS.county),
  };
  if (lane.customer < 0 || lane.to < 0) return null;

  // The vehicle row is the first one after the labels that names two or more
  // vehicles. On most forms that is the very next row.
  let vehicleRow = -1;
  for (let r = labelRow + 1; r < Math.min(rows.length, labelRow + 4); r++) {
    const hits = (rows[r] ?? []).filter((c) => VEHICLE.test(String(c ?? "").trim())).length;
    if (hits >= 2) { vehicleRow = r; break; }
  }
  if (vehicleRow < 0) return null;

  const header = rows[bandRow] ?? [];
  const remarkCol = header.findIndex((c) => /^remark$/i.test(String(c ?? "").trim()));

  // Every non-empty band header opens a block; it runs to the next one. A form
  // that quotes trucks and containers separately simply has more blocks, and
  // the same band label appearing twice is that, not a mistake.
  const starts: { start: number; band: FuelBand }[] = [];
  header.forEach((cell, index) => {
    const band = parseBand(String(cell ?? ""));
    if (band) starts.push({ start: index, band });
  });
  if (!starts.length) return null;

  const limit = remarkCol >= 0 ? remarkCol : Math.max(header.length, (rows[vehicleRow] ?? []).length);
  const groups = starts.map((entry, i) => ({
    start: entry.start,
    end: i + 1 < starts.length ? starts[i + 1].start : limit,
    band: entry.band,
  }));

  return { bandRow, labelRow, vehicleRow, lane, remarkCol, groups };
}

/* ------------------------------------------------------------------ prices */

/** A quoted price, or null. Zero means "not quoted" on these forms, not "free". */
function priceAt(row: unknown[], column: number): number | null {
  const cell = row[column];
  if (typeof cell === "number") return cell > 0 ? Math.round(cell) : null;

  const text = String(cell ?? "").replace(/[,\s฿]/g, "").trim();
  if (!text) return null;
  const value = Number(text);
  return Number.isFinite(value) && value > 0 ? Math.round(value) : null;
}

function text(row: unknown[], column: number): string {
  if (column < 0) return "";
  return String(row[column] ?? "").replace(/\s+/g, " ").trim();
}

/** What the sheet is quoting, from its name and the file it came from. */
export function serviceOf(fileName: string, sheetName: string): string {
  const probe = `${fileName} ${sheetName}`.toUpperCase();
  if (/ISO ?TANK/.test(probe)) return "ISO TANK";
  if (/REEFER|COOL/.test(probe)) return "REEFER";
  if (/LCL/.test(probe)) return "LCL";
  if (/FCL/.test(probe)) return "FCL";
  return "FCL";
}

/* ------------------------------------------------------------------- parse */

export type SheetInput = {
  carrier: string;
  fileName: string;
  sheetName: string;
  rows: unknown[][];
};

/**
 * Reads one quoted sheet into lanes, against a shared band list that grows as
 * new bands are met. Bands are shared across every carrier on purpose: the fuel
 * clause is LESCHACO's, not theirs, so two carriers quoting the same band must
 * land in the same column of the comparison.
 */
export function parseRateSheet(
  input: SheetInput,
  bands: FuelBand[],
  issues: RateIssue[],
): { lanes: RateLane[]; source: RateSource } | null {
  const layout = readLayout(input.rows);
  const service = serviceOf(input.fileName, input.sheetName);

  // Declining is not a complaint. Three readers are tried in turn, so every
  // sheet this one does not own — DGT's form, the Chemours card, the data tabs
  // that are not rate cards at all — used to raise an issue here and then be
  // read perfectly well by the next reader. Six false alarms on a screen whose
  // whole job is to show what could not be read. The caller reports the sheet
  // when every reader has declined it, which is the only moment it is true.
  if (!layout) return null;

  const bandIndex = (band: FuelBand) => {
    const found = bands.findIndex((b) => sameBand(b, band));
    if (found >= 0) return found;
    bands.push(band);
    return bands.length - 1;
  };

  const vehicleRow = input.rows[layout.vehicleRow] ?? [];
  const lanes: RateLane[] = [];
  let skipped = 0;

  for (let r = layout.vehicleRow + 1; r < input.rows.length; r++) {
    const row = input.rows[r] ?? [];
    if (!/^\d+$/.test(String(row[0] ?? "").trim())) continue;

    const customer = text(row, layout.lane.customer);
    const to = text(row, layout.lane.to);
    const prices: Record<string, (number | null)[]> = {};
    let quoted = 0;

    for (const group of layout.groups) {
      const slot = bandIndex(group.band);
      for (let c = group.start; c < group.end; c++) {
        const vehicle = canonicalVehicle(text(vehicleRow, c));
        if (!vehicle) continue;
        const price = priceAt(row, c);
        if (price === null) continue;
        if (!prices[vehicle]) prices[vehicle] = [];
        prices[vehicle][slot] = price;
        quoted++;
      }
    }

    // A row with a lane but no price is a line the carrier left blank. It is
    // reported rather than dropped silently, because a missing lane is the
    // thing somebody has to chase.
    if (!quoted) {
      if (customer || to) {
        skipped++;
        issues.push({
          file: input.fileName, sheet: input.sheetName, row: r + 1, field: "price",
          value: [customer, to].filter(Boolean).join(" → "),
          message: "Lane is listed but no price is quoted",
        });
      }
      continue;
    }

    if (!customer && !to) {
      skipped++;
      continue;
    }

    lanes.push({
      id: `${input.carrier}|${service}|${r}`,
      carrier: input.carrier,
      service,
      customer,
      from: text(row, layout.lane.from),
      to,
      county: text(row, layout.lane.county),
      remark: layout.remarkCol >= 0 ? text(row, layout.remarkCol) : "",
      prices,
    });
  }

  return {
    lanes,
    source: {
      carrier: input.carrier,
      file: input.fileName,
      sheet: input.sheetName,
      service,
      lanes: lanes.length,
      skipped,
    },
  };
}

/* --------------------------------------------------------------- DGT form */

const DGT_HEADER = /ลำดับ/;

/**
 * DGT's container wording onto the shared vocabulary.
 *
 * They quote "DRY 20 ' / 40' (NON DG)" where the LESCHACO form says "20F". One
 * cell can cover both sizes, which stays as one entry rather than being split
 * into two guesses at what each size costs.
 */
export function dgtVehicle(text: string): string {
  const value = (text ?? "").replace(/\s+/g, " ").trim().toUpperCase();
  if (!value) return "";
  if (/ISO|TANK/.test(value)) return "ISO TANK";

  const dg = /\bDG\b/.test(value) && !/NON[ -]?DG/.test(value);
  const has20 = /\b20\b/.test(value);
  const has40 = /\b40\b/.test(value);
  const size = has20 && has40 ? "20F/40F" : has40 ? "40F" : has20 ? "20F" : value;
  return dg ? `${size} DG` : size;
}

/**
 * DGT quote their own way: blocks down one sheet, each with its own fuel bands
 * across the top, a destination and a container type, and continuation rows
 * that price a second container type for the destination above them.
 *
 * Their bands are close to the LESCHACO clause but not identical — 27.00-29.99
 * where the form says "at 29.99", 48.30-53.11 where it says 48.35-53.18 — so
 * they are kept as quoted rather than rounded into somebody else's contract.
 */
export function parseDgtSheet(
  input: SheetInput,
  bands: FuelBand[],
  issues: RateIssue[],
): { lanes: RateLane[]; source: RateSource } | null {
  const headers = input.rows
    .map((row, index) => ({ index, isHeader: DGT_HEADER.test(String(row?.[0] ?? "")) }))
    .filter((entry) => entry.isHeader)
    .map((entry) => entry.index);

  if (!headers.length) return null;

  const service = serviceOf(input.fileName, input.sheetName);
  const bandIndex = (band: FuelBand) => {
    const found = bands.findIndex((b) => sameBand(b, band));
    if (found >= 0) return found;
    bands.push(band);
    return bands.length - 1;
  };

  const lanes: RateLane[] = [];
  let skipped = 0;

  headers.forEach((headerRow, block) => {
    const header = input.rows[headerRow] ?? [];
    const slots: { column: number; slot: number }[] = [];
    header.forEach((cell, column) => {
      const band = parseBand(String(cell ?? ""));
      if (band) slots.push({ column, slot: bandIndex(band) });
    });
    if (!slots.length) return;

    const end = block + 1 < headers.length ? headers[block + 1] : input.rows.length;
    let destination = "";

    for (let r = headerRow + 1; r < end; r++) {
      const row = input.rows[r] ?? [];
      const numbered = /^\d+$/.test(String(row[0] ?? "").trim());

      // A continuation row has no number and no destination: its container type
      // sits where the number would be, and it belongs to the lane above.
      const offset = numbered ? 0 : -1;
      const vehicleCell = text(row, numbered ? 2 : 0);
      if (numbered) destination = text(row, 1);
      if (!destination || !vehicleCell) continue;

      const vehicle = dgtVehicle(vehicleCell);
      if (!vehicle) continue;

      const prices: (number | null)[] = [];
      let quoted = 0;
      for (const { column, slot } of slots) {
        const price = priceAt(row, column + offset);
        if (price === null) continue;
        prices[slot] = price;
        quoted++;
      }

      if (!quoted) {
        skipped++;
        issues.push({
          file: input.fileName, sheet: input.sheetName, row: r + 1, field: "price",
          value: `${destination} · ${vehicleCell}`,
          message: "Lane is listed but no price is quoted",
        });
        continue;
      }

      // A row that carries a word after the last band is a status — DGT mark
      // withdrawn lanes "Cancelled" rather than deleting them.
      const tail = text(row, slots[slots.length - 1].column + offset + 1);

      lanes.push({
        id: `${input.carrier}|${service}|${r}`,
        carrier: input.carrier,
        service,
        customer: destination,
        from: "",
        to: destination,
        county: "",
        remark: tail,
        prices: { [vehicle]: prices },
      });
    }
  });

  return {
    lanes,
    source: { carrier: input.carrier, file: input.fileName, sheet: input.sheetName, service, lanes: lanes.length, skipped },
  };
}

/** Reads the Remark sheet's extra charges — waiting time, cancellation, and so on. */
/** "4-Wheel Truck" as the rest of the book spells it: 4W. */
export function chemoursVehicle(label: string): string {
  const wheels = /(\d{1,2})\s*-?\s*wheel/i.exec(label ?? "");
  return wheels ? `${Number(wheels[1])}W` : "";
}

/**
 * The distribution rates behind the Chemours account, one truck size per sheet.
 *
 * Not the LESCHACO form and not DGT's either: a lane runs origin city, origin
 * postcode, destination city, destination postcode, and then one price per
 * diesel band straight across. The truck size is not in any column — it is
 * named once in the heading over those prices, so the whole sheet quotes one
 * vehicle and the three sheets together make the card.
 *
 * That heading is checked against the sheet's own tab name, and the sheet is
 * refused when they disagree. The workbook arrived with every Unithai tab
 * called 10-Wheel and every SCGJWD tab called 6-Wheel while the headings inside
 * said otherwise, and a rate card that prices the wrong truck is worse than one
 * that will not load: nobody would have seen it until an invoice came back.
 */
/**
 * Where a Chemours card keeps its headings and its bands.
 *
 * Shared with the workbook-level pass that reconciles the clause across sheets,
 * because two copies of "which row are the bands on" is two answers waiting to
 * disagree — and if they ever did, the reconciliation would rewrite cells the
 * parser was not reading.
 *
 * Returns -1 rows when this is not one of these cards at all.
 */
export function chemoursLayout(rows: unknown[][]): { headRow: number; bandRow: number } {
  const headRow = rows.findIndex((row, index) =>
    index < 6 && /^origin city$/i.test(String(row?.[0] ?? "").trim()));
  if (headRow < 0) return { headRow: -1, bandRow: -1 };

  // Not always the row under the heading — SSL put a row of "COST" between the
  // two. Two bands is the threshold: a stray number in a spacer row is not a
  // fuel clause, and every one of these cards quotes at least four.
  for (let r = headRow + 1; r < Math.min(headRow + 5, rows.length); r++) {
    const row = rows[r] ?? [];
    let found = 0;
    for (let c = 4; c < row.length; c++) if (parseBand(String(row[c] ?? ""))) found++;
    if (found >= 2) return { headRow, bandRow: r };
  }
  return { headRow, bandRow: -1 };
}

/**
 * One fuel clause per card, reconciled across its sheets.
 *
 * The clause is a contract term. It belongs to the agreement, not to the size
 * of the lorry, so all six sheets of a card should carry the same one — and
 * where they do not, the odd one out is a typing slip rather than a separate
 * deal. SSL's card proves both halves: its 10-wheel sheet reads 36.31-29.94, a
 * nine typed as a two, and its 4-wheel and 10-wheel sheets end at 48.01-50.00
 * where the other four end at 48.35-53.18.
 *
 * So each position takes what most of the card's sheets say there, and every
 * sheet that disagreed is reported. Nothing is invented: the replacement is
 * always a band written on that same card, at that same position, by more
 * sheets than wrote the one being replaced. A band whose ceiling sits below its
 * floor can never win, however many sheets carry it.
 *
 * Returns the label to use at each position, or null to leave a position alone.
 */
export function reconcileChemoursBands(
  sheets: { sheetName: string; labels: string[] }[],
  issues: RateIssue[],
  fileName: string,
): string[] {
  const width = Math.max(0, ...sheets.map((sheet) => sheet.labels.length));
  const agreed: string[] = [];

  for (let position = 0; position < width; position++) {
    const tally = new Map<string, number>();
    for (const sheet of sheets) {
      const label = (sheet.labels[position] ?? "").trim();
      if (!label) continue;
      const band = parseBand(label);
      if (!band || band.max < band.min) continue;
      tally.set(label, (tally.get(label) ?? 0) + 1);
    }
    if (!tally.size) { agreed.push(""); continue; }

    const [winner] = [...tally.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))[0];
    agreed.push(winner);

    for (const sheet of sheets) {
      const label = (sheet.labels[position] ?? "").trim();
      if (!label || label === winner) continue;
      issues.push({
        file: fileName, sheet: sheet.sheetName, row: position + 1, field: "band",
        value: `${label} → ${winner}`,
        message: `ช่วงราคาน้ำมันช่องที่ ${position + 1} ไม่ตรงกับชีตอื่นในการ์ดเดียวกัน — ใช้ตามที่ชีตส่วนใหญ่ระบุ`,
      });
    }
  }

  return agreed;
}

export function parseChemoursSheet(
  input: SheetInput,
  bands: FuelBand[],
  issues: RateIssue[],
): { lanes: RateLane[]; source: RateSource } | null {
  const { headRow, bandRow } = chemoursLayout(input.rows);
  if (headRow < 0) return null;

  const heading = (input.rows[headRow] ?? [])
    .map((cell) => String(cell ?? ""))
    .find((cell) => /transportation rate per trip/i.test(cell));
  if (!heading) return null;

  const vehicle = chemoursVehicle(heading);
  const tabbed = chemoursVehicle((/\(([^)]*)\)\s*$/.exec(input.sheetName)?.[1] ?? "").replace(/W$/i, "-Wheel"));
  if (!vehicle) {
    issues.push({
      file: input.fileName, sheet: input.sheetName, row: headRow + 1, field: "vehicle",
      value: heading, message: "The heading over the prices does not name a truck size",
    });
    return null;
  }
  if (tabbed && tabbed !== vehicle) {
    issues.push({
      file: input.fileName, sheet: input.sheetName, row: headRow + 1, field: "vehicle",
      value: `tab says ${tabbed}, heading says ${vehicle}`,
      message: "Tab name and heading disagree about the truck — refused rather than priced as a guess",
    });
    return null;
  }

  // The carrier is not on this sheet anywhere, and the tab name is not it.
  //
  // "Unithai (4W)" and "SCGJWD (4W)" name the warehouse the run starts from —
  // the same two names that appear in the W/H column of the job sheets — not
  // the company driving the truck. Reading them as carriers filed SSL's whole
  // card under two warehouses that have never quoted anything, which would have
  // been a rate attributed to a company that did not give it. The origin is
  // already in the first column, so the tab name is not needed for that either;
  // the carrier is whatever the caller was told the file belongs to.
  const carrier = input.carrier.trim() || "UNKNOWN";
  // A domestic distribution run, not a container move — the same word the
  // register uses for these jobs, so the filter on the rates screen means
  // something.
  const service = "DELIVERY";

  const bandIndex = (band: FuelBand) => {
    const found = bands.findIndex((b) => sameBand(b, band));
    if (found >= 0) return found;
    bands.push(band);
    return bands.length - 1;
  };

  const columns: { column: number; slot: number }[] = [];
  if (bandRow >= 0) {
    const row = input.rows[bandRow] ?? [];
    for (let c = 4; c < row.length; c++) {
      const band = parseBand(String(row[c] ?? ""));
      if (!band) continue;
      // A ceiling below its floor is a typo, and an expensive one: taken at
      // face value such a band sorts to the bottom of the clause, so a lorry
      // running at 30 baht diesel would be priced at the 36-to-40 rate. The
      // workbook pass above usually replaces it from the card's other sheets;
      // this is the backstop for a card that has only the one.
      if (band.max < band.min) {
        issues.push({
          file: input.fileName, sheet: input.sheetName, row: bandRow + 1, field: "band",
          value: String(row[c] ?? ""),
          message: "ช่วงราคาน้ำมันกลับหัวกลับหาง ปลายช่วงน้อยกว่าต้นช่วง — ข้ามคอลัมน์นี้ไว้ก่อน",
        });
        continue;
      }
      columns.push({ column: c, slot: bandIndex(band) });
    }
  }
  if (!columns.length) {
    issues.push({
      file: input.fileName, sheet: input.sheetName, row: headRow + 2, field: "bands",
      value: "", message: "No diesel bands under the rate heading",
    });
    return null;
  }

  const lanes: RateLane[] = [];
  let skipped = 0;

  for (let r = headRow + 2; r < input.rows.length; r++) {
    const row = input.rows[r] ?? [];
    const to = text(row, 2);
    if (!to) continue;

    const prices: (number | null)[] = [];
    let quoted = 0;
    for (const { column, slot } of columns) {
      const price = priceAt(row, column);
      if (price === null) continue;
      prices[slot] = price;
      quoted++;
    }
    if (!quoted) { skipped++; continue; }

    lanes.push({
      id: `${carrier}|${service}|${vehicle}|${r}`,
      carrier,
      service,
      // Every one of these lanes is quoted for this account and no other, and a
      // price that forgets whose it is would be offered on somebody else's job.
      customer: "CHEMOURS",
      // The warehouse the run leaves from, as the card writes it.
      from: text(row, 0),
      to,
      county: text(row, 3),
      remark: "",
      prices: { [vehicle]: prices },
    });
  }

  if (!lanes.length) return null;

  return {
    lanes,
    source: {
      carrier, file: input.fileName, sheet: input.sheetName, service,
      lanes: lanes.length, skipped,
    },
  };
}

export function parseSurcharges(rows: unknown[][], service: string): Surcharge[] {
  const out: Surcharge[] = [];
  for (const row of rows) {
    const no = String(row[0] ?? "").trim();
    if (!/^\d+$/.test(no)) continue;
    const description = String(row[1] ?? "").replace(/\s+/g, " ").trim();
    if (!description) continue;
    out.push({
      service,
      no,
      description,
      currency: String(row[2] ?? "").trim(),
      rate: String(row[3] ?? "").trim(),
      unit: String(row[4] ?? "").trim(),
    });
  }
  return out;
}

/** Reads a workbook the operators uploaded, in the browser. */
export function parseRateWorkbook(carrier: string, fileName: string, data: ArrayBuffer): {
  lanes: RateLane[];
  bands: FuelBand[];
  sources: RateSource[];
  issues: RateIssue[];
  surcharges: Surcharge[];
} {
  const workbook = XLSX.read(data, { cellDates: true });
  const bands: FuelBand[] = [];
  const lanes: RateLane[] = [];
  const sources: RateSource[] = [];
  const issues: RateIssue[] = [];
  const surcharges: Surcharge[] = [];

  for (const sheetName of workbook.SheetNames) {
    const rows = XLSX.utils.sheet_to_json<unknown[]>(workbook.Sheets[sheetName], {
      header: 1, blankrows: false, defval: "",
    });
    if (!rows.length) continue;

    if (/remark/i.test(sheetName)) {
      surcharges.push(...parseSurcharges(rows, serviceOf(fileName, sheetName)));
      continue;
    }

    // Subcontractor forms only — the Chemours card is that account's own and is
    // read on that account's screen, not into the book that compares carriers.
    const input = { carrier, fileName, sheetName, rows };
    const parsed = parseRateSheet(input, bands, issues) ?? parseDgtSheet(input, bands, issues);
    if (!parsed) {
      issues.push({
        file: fileName, sheet: sheetName, row: 0, field: "layout", value: "",
        message: "ไม่รู้จักรูปแบบของชีตนี้ — ไม่ตรงกับฟอร์ม LESCHACO หรือฟอร์ม DGT",
      });
      continue;
    }
    lanes.push(...parsed.lanes);
    sources.push(parsed.source);
  }

  return { lanes, bands, sources, issues, surcharges };
}

/* ------------------------------------------------------------------ lookup */

/** The price for a lane at a diesel price, or null when it is not quoted. */
export function priceFor(
  lane: RateLane,
  vehicle: string,
  bands: FuelBand[],
  diesel: number,
): number | null {
  // The stored key first. It is already canonical, and running it through
  // canonicalVehicle again would rewrite it: DGT quote one price for "20F/40F"
  // and that key reduces to "20F", which the lane does not have.
  const row = lane.prices[vehicle] ?? lane.prices[canonicalVehicle(vehicle)];
  if (!row) return null;

  // Chosen from the bands this lane actually quotes, not from the book's whole
  // list. Two hauliers on the same customer turned out to quote different fuel
  // clauses — 28.01-30.00 against 29.99-32.99 — and the book holds the union of
  // both. Picking the global band first and then walking down to the nearest
  // quoted one lands a step too low whenever the two clauses interleave: at
  // 33.50 the union's next band is 34.00, which the coarser haulier does not
  // quote, and walking back reaches their 29.99-32.99 when 33.50 plainly sits
  // in their 33.00-36.30. That is money, on a price somebody bills against.
  //
  // So: the cheapest band this lane quotes whose ceiling still covers the
  // diesel price. That is the band the contract means.
  let best = -1;
  for (let i = 0; i < row.length; i++) {
    if (row[i] == null || !bands[i]) continue;
    if (bands[i].max < diesel) continue;
    if (best < 0 || bands[i].max < bands[best].max) best = i;
  }
  if (best >= 0) return row[best];

  // Above every band this lane quotes: the top one it did quote is the
  // contracted rate, which is what the clause says happens past its last step.
  let top = -1;
  for (let i = 0; i < row.length; i++) {
    if (row[i] == null || !bands[i]) continue;
    if (top < 0 || bands[i].max > bands[top].max) top = i;
  }
  return top >= 0 ? row[top] : null;
}

/** Every vehicle type quoted anywhere in the book, in a stable order. */
export function vehiclesIn(lanes: RateLane[]): string[] {
  const seen = new Set<string>();
  for (const lane of lanes) for (const vehicle of Object.keys(lane.prices)) seen.add(vehicle);
  const order = ["4W", "6W", "10W", "4W DG", "6W DG", "10W DG", "20F", "40F", "20F DG", "40F DG"];
  return [...seen].sort((a, b) => {
    const ia = order.indexOf(a), ib = order.indexOf(b);
    if (ia >= 0 && ib >= 0) return ia - ib;
    if (ia >= 0) return -1;
    if (ib >= 0) return 1;
    return a.localeCompare(b);
  });
}
