/**
 * Turns the subcontractor rate folder into the file the app reads.
 *
 *   node migration/build-rates.mjs "D:/Leschaco/Dashboard/Transport cost subcon"
 *
 * The parsing rules live in app/scmos/rates.ts with the rest of the business
 * rules — this only walks the folder, decides which file is the current one for
 * each carrier, and writes migration/data/rates.json, which --seed-suppliers loads into Azure SQL.
 *
 * Node strips the TypeScript types on import, so there is one copy of the rules
 * and the browser and this script cannot disagree about them.
 */

import * as XLSX from "xlsx";
import { readFileSync, readdirSync, statSync, writeFileSync, mkdirSync } from "node:fs";
import { join, basename, dirname, relative } from "node:path";
import { fileURLToPath } from "node:url";

import { parseRateSheet, parseDgtSheet, parseSurcharges, serviceOf, canonicalCarrier } from "../app/scmos/rates.ts";

const here = dirname(fileURLToPath(import.meta.url));
const ROOT = process.argv[2] ?? "D:/Leschaco/Dashboard/Transport cost subcon";
const OUT = join(here, "data", "rates.json");

/**
 * Files that are superseded, and why. A carrier who sends three workbooks is
 * not sending three price lists — they are sending one, and two pieces of
 * history. Guessing by modified date would be wrong as often as right, so the
 * choice is written down here where it can be argued with.
 */
const SUPERSEDED = {
  "SANGJA/ราคา SANGJA.xlsx": "older workbook, sheets dated 2022; SANGJA.xlsx is the current form",
  "SANGJA/Transportation_List(LCB) (003).xlsx": "customer list, not a rate card",
  "SANGJA/Transportation_List(NPT Warehouse) (003).xlsx": "customer list, not a rate card",
  "SHORE/RATE LESCHACO.xls": "pre-form .xls; SHORE.xlsx is the current form",
  "SHORE/RATE LESCHACO SHORE.xls": "pre-form .xls; SHORE.xlsx is the current form",
  "WEALTHY/ราคาค่าขนส่ง WEALTHY LESCHACO.xlsx": "pre-form layout; WEALTHY.xlsx is the current form",
  "DGT/FM-OP-09 REV.0 5-11-64  รายงานตรวจความพร้อมของรถขนส่งปร.xlsx": "vehicle inspection form, not a rate card",
};

/**
 * Blank templates — the form itself, with nobody's prices in it. Only the
 * copies sitting at the top of the folder: FNP returned their quote without
 * renaming the file, so the same name inside a carrier's folder is that
 * carrier's prices and must be read.
 */
const TEMPLATE = /^แบบฟอร์มราคา LESCHACO/;
const isTemplate = (rel, name) => TEMPLATE.test(name) && !rel.includes("/");

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const full = join(dir, name);
    if (statSync(full).isDirectory()) walk(full, out);
    else out.push(full);
  }
  return out;
}

const files = walk(ROOT)
  .filter((f) => /\.(xlsx|xls|xlsm)$/i.test(f) && !basename(f).startsWith("~$"));

const bands = [];
const lanes = [];
const sources = [];
const issues = [];
const surcharges = [];
const skippedFiles = [];

for (const file of files) {
  const rel = relative(ROOT, file).replace(/\\/g, "/");
  const name = basename(file);

  if (isTemplate(rel, name)) {
    // The blank form still carries the surcharge table, which is LESCHACO's own
    // contract terms rather than any one carrier's — so it is read for that.
    const workbook = XLSX.read(readFileSync(file), { cellDates: true });
    for (const sheetName of workbook.SheetNames) {
      if (!/remark/i.test(sheetName)) continue;
      const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], { header: 1, blankrows: false, defval: "" });
      surcharges.push(...parseSurcharges(rows, serviceOf(name, sheetName)));
    }
    skippedFiles.push({ file: rel, reason: "blank template (surcharges read from it)" });
    continue;
  }

  if (SUPERSEDED[rel]) {
    skippedFiles.push({ file: rel, reason: SUPERSEDED[rel] });
    continue;
  }

  const carrier = canonicalCarrier(rel.split("/")[0]);

  let workbook;
  try {
    workbook = XLSX.read(readFileSync(file), { cellDates: true });
  } catch (error) {
    issues.push({ file: rel, sheet: "", row: 0, field: "file", value: "", message: `Unreadable: ${error.message}` });
    continue;
  }

  for (const sheetName of workbook.SheetNames) {
    const rows = XLSX.utils.sheet_to_json(workbook.Sheets[sheetName], { header: 1, blankrows: false, defval: "" });
    if (!rows.length) continue;

    if (/remark/i.test(sheetName)) {
      surcharges.push(...parseSurcharges(rows, serviceOf(name, sheetName)));
      continue;
    }

    const input = { carrier, fileName: rel, sheetName, rows };
    // The LESCHACO form first; DGT quote on their own and are read by their own
    // reader rather than being forced into a shape they never used.
    const parsed = parseRateSheet(input, bands, issues) ?? parseDgtSheet(input, bands, issues);
    if (!parsed || parsed.lanes.length === 0) continue;
    lanes.push(...parsed.lanes);
    sources.push(parsed.source);
  }
}

// Bands are met in file order, so sort them into fuel order before anything
// indexes into them — a lane's price array is positional.
const order = bands.map((band, index) => ({ band, index })).sort((a, b) => a.band.max - b.band.max);
const remap = new Map(order.map((entry, position) => [entry.index, position]));
const sortedBands = order.map((entry) => entry.band);

for (const lane of lanes) {
  for (const [vehicle, row] of Object.entries(lane.prices)) {
    const moved = new Array(sortedBands.length).fill(null);
    row.forEach((price, index) => {
      if (price == null) return;
      moved[remap.get(index)] = price;
    });
    lane.prices[vehicle] = moved;
  }
}

// One surcharge table, not twenty copies of the same contract terms.
const uniqueSurcharges = [];
const seen = new Set();
for (const charge of surcharges) {
  const key = `${charge.service}|${charge.no}|${charge.description}`;
  if (seen.has(key)) continue;
  seen.add(key);
  uniqueSurcharges.push(charge);
}

const book = {
  bands: sortedBands,
  lanes,
  sources: sources.sort((a, b) => b.lanes - a.lanes),
  issues,
  surcharges: uniqueSurcharges,
  skippedFiles,
  builtAt: new Date().toISOString(),
};

mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, JSON.stringify(book), "utf8");

const byCarrier = {};
for (const lane of lanes) byCarrier[lane.carrier] = (byCarrier[lane.carrier] ?? 0) + 1;

console.log(`Read     ${files.length} workbooks under ${ROOT}`);
console.log(`Wrote    ${relative(join(here, ".."), OUT)}  (${(JSON.stringify(book).length / 1024 / 1024).toFixed(2)} MB)`);
console.log(`Bands    ${sortedBands.map((b) => b.label).join(" · ")}`);
console.log(`Lanes    ${lanes.length} across ${Object.keys(byCarrier).length} carriers`);
console.log(`Charges  ${uniqueSurcharges.length} surcharge terms`);
console.log(`Issues   ${issues.length}`);
console.log();
console.log("Lanes by carrier:");
for (const [carrier, count] of Object.entries(byCarrier).sort((a, b) => b[1] - a[1])) {
  console.log(`   ${carrier.padEnd(18)} ${String(count).padStart(5)}`);
}
console.log();
console.log("Skipped:");
for (const entry of skippedFiles) console.log(`   ${entry.file}\n      ${entry.reason}`);
