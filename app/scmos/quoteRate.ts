/**
 * What to charge for a journey, worked out from its distance.
 *
 * The shape is the team's own: a per-kilometre rate and a fixed base for each
 * vehicle, a multiplier for the refrigerated ones, a flat surcharge when the
 * load is dangerous goods, and a margin on the lot.
 *
 *     (distance × perKm + base) × chill + dangerousGoods     ← what it costs
 *                                       × (1 + margin)       ← what it sells for
 *
 * Two things about that order are worth writing down, because both were checked
 * against the 13,042 prices the register holds rather than assumed.
 *
 * The dangerous-goods surcharge sits inside, before the margin. Those prices are
 * what carriers quoted — a cost — and the DG figures in the card are exactly the
 * gap between a carrier's plain and DG quote for the same journey: +300 on a 4W
 * across 595 pairs, and the middle half of them is 300 to 300, not a spread
 * around it. It is part of what the trip costs, so it is marked up like the rest
 * of it.
 *
 * The chill multiplier applies to the transport and not to the DG surcharge. A
 * refrigerated truck costs more to run; the fee for carrying something hazardous
 * does not change because the box is cold.
 *
 * Nothing here is fixed in code. Every number is a card the team edits, because
 * the same measurement said the ×1.5 on refrigerated trucks is high — 10W RF
 * came out at ×1.24 across 30 pairs — and a rate that wants tuning should not
 * need a deployment.
 *
 * A leaf module: it imports nothing, so it can be run directly under node --test.
 */

/** One row of the card: how a single vehicle is priced. */
export type VehicleRate = {
  /** The register's own code, so a quote and a job speak about the same truck. */
  code: string;
  /** What the team calls it — "4WH", "20' Reefer". */
  label: string;
  /** Baht per kilometre of the outbound journey. */
  perKm: number;
  /** Baht before a wheel turns. */
  baseCharge: number;
  /** Multiplies the transport cost. 1 for anything not refrigerated. */
  chill: number;
  /** Baht added when the load is dangerous goods. */
  dangerousGoods: number;
};

export type OptionBasis = "flat" | "perKm" | "perHour" | "percent";

/**
 * An extra charge.
 *
 * The basis is a controlled list and not free text, which is the whole point of
 * it. The register already carries surcharges whose unit is a typed string, and
 * a column like that ends up holding "ต่อชั่วโมง", "per hr" and "/hour" for the
 * same idea — three spellings nothing can add up.
 */
export type QuoteOption = {
  id: string;
  label: string;
  basis: OptionBasis;
  /** Baht, or percent when the basis says so. */
  rate: number;
  /** Hours, or how many times. Ignored by flat and percent. */
  quantity: number;
};

/** One line of the answer, in the order it is worked out. */
export type QuoteLine = {
  label: string;
  /** How this number was arrived at, in words. */
  detail: string;
  amount: number;
};

export type Quote = {
  lines: QuoteLine[];
  /** What the journey costs before any margin. */
  cost: number;
  margin: number;
  total: number;
  /** Anything that stopped a number being produced. */
  refusals: string[];
};

/**
 * The card as the team priced it.
 *
 * Eleven rows, one per vehicle, exactly as they were written down — including
 * that a 6W refrigerated is 20 baht a kilometre where a plain 6W is 18, and that
 * a 40' is priced the same as a 20'. That last one looks like a mistake and is
 * not: across 1,708 journeys where both were quoted, the middle half of the
 * ratio is 1.00 to 1.00.
 *
 * The refrigerated rows carry the dangerous-goods figure of the vehicle they are
 * built on, which the team's note did not state either way.
 */
export const DEFAULT_CARD: VehicleRate[] = [
  { code: "4W", label: "4WH", perKm: 8, baseCharge: 1500, chill: 1, dangerousGoods: 300 },
  { code: "4W RF", label: "4WH RF", perKm: 8, baseCharge: 1500, chill: 1.5, dangerousGoods: 300 },
  { code: "6W", label: "6WH", perKm: 18, baseCharge: 2700, chill: 1, dangerousGoods: 500 },
  { code: "6W RF", label: "6WH RF", perKm: 20, baseCharge: 2700, chill: 1.5, dangerousGoods: 500 },
  { code: "10W", label: "10WH", perKm: 25, baseCharge: 3500, chill: 1, dangerousGoods: 800 },
  { code: "10W RF", label: "10WH RF", perKm: 28, baseCharge: 3500, chill: 1.5, dangerousGoods: 800 },
  { code: "20F", label: "20'", perKm: 40, baseCharge: 4000, chill: 1, dangerousGoods: 500 },
  { code: "40F", label: "40'", perKm: 40, baseCharge: 4000, chill: 1, dangerousGoods: 500 },
  { code: "20RF", label: "20' Reefer", perKm: 40, baseCharge: 4000, chill: 1.5, dangerousGoods: 500 },
  { code: "40RF", label: "40' Reefer", perKm: 40, baseCharge: 4000, chill: 1.5, dangerousGoods: 500 },
  { code: "20TK", label: "20' Tank", perKm: 40, baseCharge: 4000, chill: 1.5, dangerousGoods: 500 },
];

/** The margin the team quotes at, until somebody sets another. */
export const DEFAULT_MARGIN = 10;

export const BASIS_TH: Record<OptionBasis, string> = {
  flat: "เหมาต่อเที่ยว",
  perKm: "ต่อกิโลเมตร",
  perHour: "ต่อชั่วโมง",
  percent: "เปอร์เซ็นต์ของต้นทุน",
};

/** Whole baht. A quotation with satang on it has never helped anybody. */
const baht = (value: number) => Math.round(value);

/**
 * A card line that is waiting for a rate rather than holding one.
 *
 * Exported because the screen asks it too — the picker marks these before they
 * are chosen, which is kinder than letting somebody select four trucks and
 * discover afterwards that one of them cannot be quoted. One rule, asked in two
 * places, rather than two rules that will eventually disagree about what "no
 * rate" means.
 */
export const isUnpriced = (rate: VehicleRate) => !(rate.perKm > 0);

export type QuoteRequest = {
  vehicle: string;
  /** The outbound journey, in kilometres. */
  km: number;
  dangerousGoods: boolean;
  marginPercent: number;
  options: QuoteOption[];
};

/**
 * Prices one journey, and shows its working.
 *
 * Every line is rounded to the baht and the total is the sum of the lines, so
 * the breakdown always adds up to the figure at the bottom. A quotation whose
 * parts do not reconcile is one nobody can defend to a carrier.
 */
export function quote(card: VehicleRate[], ask: QuoteRequest): Quote {
  const refusals: string[] = [];
  const rate = card.find((one) => one.code === ask.vehicle);

  if (!rate) {
    return {
      lines: [], cost: 0, margin: 0, total: 0,
      refusals: [`ไม่มีอัตราสำหรับรถ ${ask.vehicle} ในตาราง`],
    };
  }
  /*
   * A row of the card that is missing a number, before anything is worked out
   * with it.
   *
   * Not defensiveness for its own sake. The card arrives as JSON from the API,
   * and when a field there was named differently from the field here, every
   * lookup returned undefined and the quotation rendered as "NaN" from the
   * second line down — a broken answer that still looked like an answer, and
   * one no test caught because the tests build their card from the constant
   * below where both names always agreed. A price that cannot be worked out
   * has to say so.
   */
  for (const [field, value] of [["ราคาต่อกิโลเมตร", rate.perKm], ["ค่าเริ่มต้น", rate.baseCharge],
                                ["ตัวคูณห้องเย็น", rate.chill], ["ค่า DG", rate.dangerousGoods]] as const) {
    if (typeof value !== "number" || !Number.isFinite(value)) {
      refusals.push(`อัตราของ ${rate.label || ask.vehicle} ไม่สมบูรณ์ — ${field} ไม่มีค่า`);
    }
  }

  /*
   * A vehicle on the card that nobody has priced yet.
   *
   * The card now carries a line for every vehicle the sheet has a column for,
   * so that a rate which has never been agreed is visible as missing rather
   * than absent. Those lines hold nought, and nought is not a price: worked
   * through, it would quote the base charge alone — or nothing at all — and
   * hand somebody a figure to send a customer. The rate editor refuses to save
   * a nought, so this is the only way one can be here, and it means exactly one
   * thing.
   */
  // Only when the card row is otherwise whole. A field that is missing entirely
  // is a different fault with a better message, and the loop above already
  // named it — saying "nobody has priced this" on top of it would send somebody
  // to type a rate into a card that cannot hold one.
  if (refusals.length === 0 && isUnpriced(rate)) {
    refusals.push(
      `ยังไม่ได้ตั้งอัตราของ ${rate.label || ask.vehicle} — ตั้งราคาในตารางอัตราก่อนจึงจะเสนอราคาได้`);
  }

  // Zero kilometres is a question nobody asked, not a journey that is free.
  if (!(ask.km > 0)) refusals.push("ต้องระบุระยะทางมากกว่า 0 กิโลเมตร");
  if (ask.km > 3000) refusals.push("ระยะทางเกิน 3,000 กม. — ตรวจสอบตัวเลขอีกครั้ง");
  if (refusals.length > 0) return { lines: [], cost: 0, margin: 0, total: 0, refusals };

  const lines: QuoteLine[] = [];

  const travel = ask.km * rate.perKm;
  lines.push({
    label: "ค่าระยะทาง",
    detail: `${ask.km.toLocaleString()} กม. × ${rate.perKm} บาท`,
    amount: baht(travel),
  });
  lines.push({ label: "ค่าเริ่มต้น", detail: rate.label, amount: baht(rate.baseCharge) });

  if (rate.chill !== 1) {
    // Shown as the uplift rather than folded into the two lines above, so the
    // person quoting can see what the cold is costing and argue about it.
    const uplift = (travel + rate.baseCharge) * (rate.chill - 1);
    lines.push({
      label: "ห้องเย็น",
      detail: `× ${rate.chill} ของค่าขนส่ง`,
      amount: baht(uplift),
    });
  }

  if (ask.dangerousGoods) {
    lines.push({ label: "สินค้าอันตราย (DG)", detail: rate.label, amount: baht(rate.dangerousGoods) });
  }

  // Everything charged in baht, before anything charged as a share of it.
  for (const option of ask.options) {
    if (option.basis === "percent") continue;
    const amount =
      option.basis === "flat" ? option.rate
      : option.basis === "perKm" ? option.rate * ask.km
      : option.rate * option.quantity;
    if (!(amount > 0)) continue;
    lines.push({
      label: option.label,
      detail: option.basis === "flat" ? BASIS_TH.flat
        : option.basis === "perKm" ? `${ask.km.toLocaleString()} กม. × ${option.rate} บาท`
        : `${option.quantity} ชม. × ${option.rate} บาท`,
      amount: baht(amount),
    });
  }

  // A percentage option is a share of what the trip costs, so it is worked out
  // on the lines above it and never on another percentage.
  const beforeShares = lines.reduce((sum, line) => sum + line.amount, 0);
  for (const option of ask.options) {
    if (option.basis !== "percent") continue;
    const amount = beforeShares * (option.rate / 100);
    if (!(amount > 0)) continue;
    lines.push({
      label: option.label,
      detail: `${option.rate}% ของต้นทุน`,
      amount: baht(amount),
    });
  }

  const cost = lines.reduce((sum, line) => sum + line.amount, 0);
  const margin = baht(cost * (ask.marginPercent / 100));

  return { lines, cost, margin, total: cost + margin, refusals: [] };
}
