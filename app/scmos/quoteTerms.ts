/**
 * The conditions attached to a quotation — what is charged on top of the rate.
 *
 * A rate is a number for a journey. What the customer actually pays also
 * depends on how long the truck waited, whether the container was opened for
 * X-ray, whether the booking was cancelled and how late — and none of that was
 * anywhere in this app. It lived in the workbook's Remarks sheet and in the
 * heads of the people who quote.
 *
 * <h3>Which copy is right</h3>
 *
 * This one. Asked directly on 3 September 2026, the team ruled that what they
 * supplied is the schedule and that `Rate Inquiry.xlsx` is to be corrected to
 * match it — not the other way round. That is worth writing down, because the
 * file is the older artefact and looks like the source: the next person to
 * find the two disagreeing would reasonably assume the spreadsheet won.
 *
 * It is behind in five places, taken line by line on the same day. The
 * three-axle chassis threshold is 23 tonnes in the file and 25 here; the BMT
 * return list is missing Siam River; the trailer-head overnight charge reads
 * "1 /NIGHT/TRIP" where it is a percentage of the truck rate; and two charges
 * are absent altogether — cargo handling alongside vessel, and the reefer
 * genset idling charge on FCL. The file has no LCL block at all, so all ten of
 * those conditions are here and nowhere else.
 *
 * `exportQuoteTerms` in excel.ts writes this back out in the Remarks sheet's
 * own shape, so the file can be brought up to date by pasting rather than by
 * retyping twenty-nine rows — which is how the next disagreement would start.
 *
 * <h3>Wording</h3>
 *
 * Verbatim, in whichever language the schedule uses — English lines in English,
 * Thai lines in Thai. These are chargeable terms that end up in front of a
 * customer, and a translation of one is a second wording of it: "free time 3
 * hours including lunch break" is a clause somebody may have to stand behind.
 *
 * A leaf module: it imports nothing, so the numbers can be checked by a test
 * that reads them without pulling in the app.
 */

/**
 * How the amount is read.
 *
 * Separated from the number because they are checked differently: 80 as a
 * percentage is most of the truck rate, 80 as baht is nothing, and the workbook
 * holds that very confusion — it stores the cancellation charges as 1 and 0.8
 * where the schedule says 100% and 80%.
 */
export type ChargeBasis = "baht" | "percent" | "free";

export type Charge = {
  /** The condition, worded as the schedule words it. */
  what: string;
  /** The number, or null when there is none to give. */
  amount: number | null;
  basis: ChargeBasis;
  /** What the amount is charged against — "/HOUR", "/CONTAINER". */
  per: string;
};

export type TermBlock = {
  key: "LCL" | "FCL";
  /** What the block is called on screen, in both languages. */
  heading: string;
  thai: string;
  charges: Charge[];
  /** Lines that qualify the block rather than charge for anything. */
  notes: string[];
};

const LCL: TermBlock = {
  key: "LCL",
  heading: "LCL — Less than container load",
  thai: "สินค้าไม่เต็มตู้",
  charges: [
    { what: "Trailer head free time 3 hours including lunch break, after free time", amount: 400, basis: "baht", per: "/HOUR" },
    { what: "Reefer trailer head free time 2 hours including lunch break, after free time", amount: 400, basis: "baht", per: "/HOUR" },
    { what: "Truck engine idling charge for reefer genset operation", amount: 250, basis: "baht", per: "/HOUR" },
    { what: "If inspection by X-RAY, moving charge", amount: 1000, basis: "baht", per: "/TRIP/FREE TIME 3 HRS." },
    { what: "Cancellation booking on loading date", amount: 100, basis: "percent", per: "OF TRUCK RATE" },
    { what: "Cancellation booking on before loading date, after 4 pm.", amount: 80, basis: "percent", per: "OF TRUCK RATE" },
    // Three lines rather than one with a vehicle column: the schedule is read
    // by people, and a table that makes them work out which row is theirs is
    // how the wrong overnight charge gets quoted.
    { what: "Truck head overnight charge, 4WH", amount: 500, basis: "baht", per: "/NIGHT/TRIP" },
    { what: "Truck head overnight charge, 6WH", amount: 1000, basis: "baht", per: "/NIGHT/TRIP" },
    { what: "Truck head overnight charge, 10WH", amount: 1500, basis: "baht", per: "/NIGHT/TRIP" },
    { what: "Change drop point charge // 1 - 20 km.", amount: 1000, basis: "baht", per: "Based on distance" },
  ],
  notes: [
    "Not labor to support",
  ],
};

const FCL: TermBlock = {
  key: "FCL",
  heading: "FCL — Full container load",
  thai: "สินค้าเต็มตู้",
  charges: [
    { what: "Trailer head free time 3 hours including lunch break, after free time", amount: 500, basis: "baht", per: "/HOUR" },
    { what: "If container inspection by X-RAY, moving container charge", amount: 1500, basis: "baht", per: "/CONTAINER/FREE TIME 3 HRS." },
    { what: "Yard switching fee (pick up empty container) will be additional", amount: 1500, basis: "baht", per: "/TRUCK/TRIP" },
    { what: "รับตู้เปิดตรวจ อย. / เกษตร / พบเจ้าหน้าที่", amount: 3500, basis: "baht", per: "/TRUCK/TRIP/FREE TIME 3 HRS." },
    { what: "Truck engine idling charge for reefer genset operation", amount: 250, basis: "baht", per: "/HOUR" },
    { what: "Cancellation booking on loading date", amount: 100, basis: "percent", per: "OF TRUCK RATE" },
    { what: "Cancellation booking on before loading date, after 4 pm.", amount: 80, basis: "percent", per: "OF TRUCK RATE" },
    { what: "Chassis overnight charge", amount: 1500, basis: "baht", per: "/NIGHT" },
    { what: "Cargo handling alongside vessel", amount: 2000, basis: "baht", per: "/TRUCK/TRIP" },
    { what: "Trailer head overnight charge", amount: 100, basis: "percent", per: "NIGHT / OF TRUCK RATE" },
    { what: "Pick up container in advance 2 day, will have dropping charge", amount: 2500, basis: "baht", per: "/day/cont." },
    { what: "If customer request to pick up laden container for return to port in the next day, will charge of transportation charges", amount: 80, basis: "percent", per: "OF TRUCK RATE" },
    { what: "Change drop point charge", amount: 1000, basis: "baht", per: "or based on distance" },
    { what: "น้ำหนักสินค้าเกิน 25 ตัน ต้องใช้หาง 3 เพลา คิดค่าขนส่งเพิ่มจากปกติ", amount: 1500, basis: "baht", per: "/TRUCK/TRIP" },
    // The three port-return lines. The schedule marks them with stars because
    // they only apply from the Bangkok base, and that condition is carried in
    // the wording rather than dropped — a charge whose condition is a star in
    // another column is a charge that gets applied to the wrong journey.
    { what: "BKK area (Bangna / Kingkaew / LKB): deliver or return container to BMT / Suksawad / Siam River — port charge", amount: 1000, basis: "baht", per: "/CONTAINER" },
    { what: "BKK area (Bangna / Kingkaew / LKB): deliver or return container to Unithai / SCT / Saha Thai — port charge", amount: 500, basis: "baht", per: "/CONTAINER" },
    { what: "BKK route, return empty container in LCB area", amount: 5000, basis: "baht", per: "/CONTAINER · ปรับตามราคาน้ำมัน" },
    { what: "รับตู้ล่วงหน้า 1 วัน ก่อนลานปิด", amount: null, basis: "free", per: "/CONTAINER" },
    { what: "รับตู้ล่วงหน้าก่อนวันโหลดสินค้า มากกว่า 1 วัน", amount: 2500, basis: "baht", per: "/CONTAINER · ค้างคืนเพิ่ม 500 /CONTAINER" },
  ],
  notes: [
    "Not labor to support",
    "JTC TRANSPORT BKK route — 150 baht increase/decrease in the rates for every 1 baht increase/decrease in diesel",
    "JTC TRANSPORT LCB route — 300 baht increase/decrease in the rates for every 1 baht increase/decrease in diesel",
  ],
};

export const QUOTE_TERMS: TermBlock[] = [LCL, FCL];

/**
 * The amount as it is written on the schedule.
 *
 * Here rather than in the screen so the export and the screen cannot spell a
 * charge two ways — which, in a document that goes to a customer, is the
 * difference between 80% of the truck rate and eighty baht.
 */
export function chargeText(charge: Charge): string {
  if (charge.basis === "free") return "FREE SERVICE";
  if (charge.basis === "percent") return `${charge.amount}%`;
  return `THB ${(charge.amount ?? 0).toLocaleString("en-US")}`;
}
