import { quote, type QuoteRequest, type VehicleRate } from "./quoteRate";
import { SHEET_VEHICLES } from "./rateSheetColumns";

/** Never put a dangerous-goods price in a non-DG column. */
export function quoteSheetVehicle(vehicle: string, dg: boolean): string | null {
  const code = dg ? `${vehicle} DG` : vehicle;
  return SHEET_VEHICLES.includes(code) ? code : null;
}

/**
 * The variants one chosen lorry answers in.
 *
 * A dangerous load is quoted twice — the ordinary rate and the dangerous one —
 * because the sheet prices "4W NON-DG" and "4W DG" side by side and a customer
 * asking about dangerous goods is told both. Exported so the screen works out
 * which row it is showing with the same rule that produces the rows.
 */
export const quoteVariants = (dangerousGoods: boolean): boolean[] =>
  dangerousGoods ? [false, true] : [false];

/**
 * What identifies one row of the result.
 *
 * The sheet column where there is one, because that is the key the server
 * recalculates and checks the preview against. Where the sheet has no column
 * for that combination the row still needs to be drawn and told apart, so it
 * falls back to the code and the flag.
 */
export const quoteRowKey = (vehicle: string, dangerousGoods: boolean): string =>
  quoteSheetVehicle(vehicle, dangerousGoods) ?? `${vehicle}${dangerousGoods ? " DG" : ""}`;

/** Independent alternatives for one journey; totals are not added together. */
export function quoteMany(card: VehicleRate[], ask: Omit<QuoteRequest, "vehicle"> & { vehicles: string[] }) {
  const refusals: string[] = [];
  const sheetRefusals: string[] = [];
  const vehicles = [...new Set(ask.vehicles)];
  if (!vehicles.length) refusals.push("เลือกประเภทรถอย่างน้อย 1 แบบ");
  if (!Number.isFinite(ask.marginPercent) || ask.marginPercent < 0 || ask.marginPercent > 100) {
    refusals.push("กำไรต้องอยู่ระหว่าง 0 ถึง 100 เปอร์เซ็นต์");
  }
  if (ask.options.some((one) => !Number.isFinite(one.quantity) || one.quantity < 0 || one.quantity > 10000)) {
    refusals.push("จำนวนรายการเพิ่มเติมต้องอยู่ระหว่าง 0 ถึง 10,000");
  }
  const invalid = [...refusals];
  /*
   * Both columns when the load is dangerous, not just the DG one.
   *
   * The sheet prices "4W NON-DG" and "4W DG" beside each other, and somebody
   * quoting a dangerous load is asked for both — the ordinary rate and what the
   * goods add to it. Ticking DG used to replace the answer rather than add to
   * it, so the non-DG column of that quotation was left empty and nobody got
   * the comparison the two columns exist for.
   */
  const results = vehicles.flatMap((vehicle) => quoteVariants(ask.dangerousGoods).map((dangerous) => {
    const base = card.find((one) => one.code === vehicle)?.label ?? vehicle;
    const label = dangerous ? `${base} DG` : base;
    const result = quote(card, { ...ask, vehicle, dangerousGoods: dangerous });
    result.refusals.push(...invalid);
    if (!Number.isSafeInteger(result.total) || result.total < 0 || result.total > 2147483647) {
      result.refusals.push("ราคาที่คำนวณได้อยู่นอกช่วงที่บันทึกได้");
    }
    refusals.push(...result.refusals.map((reason) => `${label}: ${reason}`));
    const sheetVehicle = quoteSheetVehicle(vehicle, dangerous);
    if (!sheetVehicle) sheetRefusals.push(`${label}: ตารางอัตรายังไม่มีคอลัมน์รองรับ กรุณานำประเภทนี้ออกก่อนบันทึก`);
    // `key` identifies a row now that one lorry can produce two. It is the
    // sheet column where there is one, because that is what the server checks
    // the preview against; the code plus the flag only where there is not, so
    // an unquotable row still has something unique to be drawn with.
    return { vehicle, dangerous, label, quote: result, sheetVehicle,
      key: quoteRowKey(vehicle, dangerous) };
  }));
  const prices: Record<string, number> = {};
  if (!refusals.length && !sheetRefusals.length) {
    for (const result of results) prices[result.sheetVehicle!] = result.quote.total;
  }
  return { results, refusals: [...new Set(refusals)], sheetRefusals, prices };
}
