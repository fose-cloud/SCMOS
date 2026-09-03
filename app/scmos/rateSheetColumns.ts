/**
 * The rate sheet's columns, in the order and the words the workbook uses.
 *
 * Taken from `Rate Inquiry.xlsx` rather than invented, so what the screen shows
 * can be checked against the file line for line — which is the only way anybody
 * will trust it enough to stop keeping the spreadsheet as well.
 *
 * A leaf module: it imports nothing, so the list can be checked against the
 * register's own vehicle codes by a test that reads both.
 */

/** A column that writes a field of the lane or of the request above it. */
export type SheetField =
  | "date" | "no" | "requestor" | "customer"
  | "fromPlace" | "toPlace" | "county" | "carriers"
  | "fcl" | "lcl" | "domestic" | "remark";

export type SheetColumn = {
  /** The heading, spelled as the workbook spells it. */
  head: string;
  /** A plain field, or a price against one vehicle. */
  kind: "field" | "price" | "tick";
  /** The field it writes, for `field` and `tick`. */
  field?: SheetField;
  /** The register's vehicle code, for `price`. */
  vehicle?: string;
  /** Narrower than the default, for the short ones. */
  width?: number;
};

/**
 * The eleven columns before the prices.
 *
 * Date, No., Requestor and Customer belong to the request rather than the lane,
 * and the sheet repeats them down every row of one — so editing any of them
 * moves every lane of that request, which is what the file does too.
 */
const DETAILS: SheetColumn[] = [
  { head: "Date", kind: "field", field: "date", width: 96 },
  { head: "No.", kind: "field", field: "no", width: 54 },
  { head: "Requestor", kind: "field", field: "requestor", width: 150 },
  { head: "Customer", kind: "field", field: "customer", width: 160 },
  { head: "From", kind: "field", field: "fromPlace", width: 170 },
  { head: "To", kind: "field", field: "toPlace", width: 170 },
  { head: "County", kind: "field", field: "county", width: 110 },
  { head: "Subcon", kind: "field", field: "carriers", width: 150 },
  { head: "FCL", kind: "tick", field: "fcl", width: 48 },
  { head: "LCL", kind: "tick", field: "lcl", width: 48 },
  { head: "Domestic", kind: "tick", field: "domestic", width: 66 },
];

/**
 * The price columns, in the sheet's order, each named as the sheet names it.
 *
 * Two departures from the file, both deliberate:
 *
 * "Side Curtain truck NON-DG" is written twice in the newest sheets, columns 29
 * and 30. The second was meant to be the DG variant and is headed that way here.
 * It is not a cosmetic difference: on August 2026 row 203 — BERICAP, No. 63 —
 * both are priced, 11,000 and 11,500, and while the file gives them one heading
 * the importer can only read them as one vehicle and the 11,500 is dropped.
 * The file still needs its own column 30 renamed for an import to tell them
 * apart; this screen can already hold both.
 *
 * "6WH Hiab Truck" is priced by the register and has no column in the file at
 * all — the sheet has the 10-wheel one only. It is added at the end rather than
 * left off, because a rate nobody can enter is a rate nobody can quote.
 */
const PRICES: SheetColumn[] = [
  ["4W NON-DG", "4W"], ["6W NON-DG", "6W"], ["10 W NON-DG", "10W"],
  ["4W DG", "4W DG"], ["6W DG", "6W DG"], ["10 W DG", "10W DG"],
  ["4W (Reefer)", "4W RF"], ["6W (Reefer)", "6W RF"], ["10W (Reefer)", "10W RF"],
  ["4W (Reefer) DG", "4W RF DG"], ["6W (Reefer) DG", "6W RF DG"], ["10W (Reefer) DG", "10W RF DG"],
  ["20' NON-DG", "20F"], ["40'/40'HQ NON-DG", "40F"],
  ["20' DG", "20F DG"], ["40'/40'HQ DG", "40F DG"],
  ["20' Reefer", "20RF"], ["40'/40'HQ Reefer", "40RF"],
  ["Side Curtain truck NON-DG", "SIDE"], ["Side Curtain truck DG", "SIDE DG"],
  ["Flat-bed Trailer Non-DG", "FBT"], ["Flat-bed Trailer DG", "FBT DG"],
  ["20'OT (IG)", "20OT"],
  ["ISO Tank", "20TK"], ["20’Isotank DG", "20TK DG"], ["40’Isotank", "40TK"],
  ["6WH Flatbed", "6W FB"], ["10WH Hiab Truck", "10W HIAB"],
  ["6WH Hiab Truck", "6W HIAB"],
].map(([head, vehicle]) => ({ head, kind: "price" as const, vehicle, width: 92 }));

export const SHEET_COLUMNS: SheetColumn[] = [
  ...DETAILS,
  ...PRICES,
  { head: "Remark", kind: "field", field: "remark", width: 220 },
];

/** Every vehicle the sheet prices, for the test that checks them against the register. */
export const SHEET_VEHICLES: string[] =
  SHEET_COLUMNS.filter((one) => one.kind === "price").map((one) => one.vehicle!);
