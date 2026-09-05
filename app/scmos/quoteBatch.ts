import { quote, type QuoteRequest, type VehicleRate } from "./quoteRate";
import { SHEET_VEHICLES } from "./rateSheetColumns";

/** Never put a dangerous-goods price in a non-DG column. */
export function quoteSheetVehicle(vehicle: string, dg: boolean): string | null {
  const code = dg ? `${vehicle} DG` : vehicle;
  return SHEET_VEHICLES.includes(code) ? code : null;
}

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
  const results = vehicles.map((vehicle) => {
    const label = card.find((one) => one.code === vehicle)?.label ?? vehicle;
    const result = quote(card, { ...ask, vehicle });
    result.refusals.push(...invalid);
    if (!Number.isSafeInteger(result.total) || result.total < 0 || result.total > 2147483647) {
      result.refusals.push("ราคาที่คำนวณได้อยู่นอกช่วงที่บันทึกได้");
    }
    refusals.push(...result.refusals.map((reason) => `${label}: ${reason}`));
    const sheetVehicle = quoteSheetVehicle(vehicle, ask.dangerousGoods);
    if (!sheetVehicle) sheetRefusals.push(`${label}${ask.dangerousGoods ? " DG" : ""}: ตารางอัตรายังไม่มีคอลัมน์รองรับ กรุณานำประเภทนี้ออกก่อนบันทึก`);
    return { vehicle, label, quote: result, sheetVehicle };
  });
  const prices: Record<string, number> = {};
  if (!refusals.length && !sheetRefusals.length) {
    for (const result of results) prices[result.sheetVehicle!] = result.quote.total;
  }
  return { results, refusals: [...new Set(refusals)], sheetRefusals, prices };
}
