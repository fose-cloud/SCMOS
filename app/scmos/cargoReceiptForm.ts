/**
 * ISO-FRM-TH-CCL-04-01 — the cargo receipt as the operators actually issue it.
 *
 * Rebuilt from seven signed copies of the real thing (AAT, SRITHEPTHAI, UC,
 * MERIT ×2, Ampacet, UNIC, Aug–Sep 2026). The screen had been drawing
 * ISO-FRM-TH-ADM-26-06 instead — a simpler form with no item table — on the
 * grounds that the item table's columns differed per customer and could not be
 * pinned down. The seven copies confirm the columns do differ. They also show
 * that everything around the table is identical on all seven, so the answer is
 * to store the columns per customer rather than to issue a different document.
 *
 * What the copies also show is how much of this form stays empty on domestic
 * work: no vessel, no B/L, no container, no port of discharge. Those lines are
 * on the form because the same document covers import work. They are drawn and
 * left blank rather than removed — it is a controlled form, and a receipt
 * missing rows the customer has signed for before is a different document.
 *
 * No imports on purpose: this is the document's fixed text and the arithmetic
 * over one job, and both should be checkable without a browser.
 */

export const FORM_NO = "ISO-FRM-TH-CCL-04-01";

/** Fixed on every copy: the letterhead. */
export const COMPANY = "Leschaco(Thailand) Ltd.";
export const ADDRESS = "3354/36-39 Manorom Building, 11th Floor, Rama IV Road, Klongtoey, Bangkok 10110";
export const CONTACT = "Tel : (66) 0 2686 1000  Fax : (66) 0 2671 6717";

/**
 * The conditions of carriage, as CCL-04-01 words them.
 *
 * Not the wording the ADM form carried. That one gave 24 hours to note damage
 * and 7 days to send a claim letter; this one gives 24 hours and no claim
 * clause at all. Clause 2 says nearly the same thing twice, which is how the
 * document reads — it is a controlled form and tidying its prose would make
 * this a copy of something nobody has approved.
 */
export const TERMS: string[] = [
  "ได้รับสินค้าไปในสภาพที่เรียบร้อยตามรายการ (HAVE RECEIVED IN GOOD ORDER AND CONDITION FOR ABOVE MENTIONED GOODS)",
  "ถ้าสินค้ามีการสูญหายหรือชำรุด โปรดระบุลงในใบรับสินค้านี้ ไม่เช่นนั้นแล้วทางบริษัทจะไม่รับผิดชอบต่อการสูญเสียหรือว่าชำรุดของสินค้าใดๆทั้งสิ้น"
    + " ถ้าสินค้ามีการสูญหายหรือชำรุด โปรดระบุลงในใบรับสินค้านี้ ภายใน 24 ชั่วโมง"
    + " ไม่เช่นนั้นแล้วทางบริษัทจะไม่รับผิดชอบต่อการสูญเสียหรือว่าชำรุดของสินค้า"
    + " (WE CANNOT BE HELD RESPONSIBLE FOR LOSS OR DAMAGE OF GOODS, UNLESS STATED ON THIS CARGO RECEIPT.)",
];

/** The standing note about how carriage is performed. */
export const NOTE = "หมายเหตุ :\n"
  + "คู่มือแนวทางปฏิบัติโดยทั่วไปของรถขนส่งที่ปฏิบัติการดำเนินการและดูแลรับผิดชอบการให้บริการโลจิสติกส์ในนามของบริษัท เลสชาโก้ (ประเทศไทย) จำกัด "
  + "ถือเป็นส่วนหนึ่งของธุรกิจการขนส่งของเลสชาโก้ ซึ่งการให้บริการขนส่งจะต้องดำเนินการภายใต้กฎหมายการขนส่งทางถนนภายในประเทศและกฎระเบียบ"
  + "ข้อบังคับอื่นๆที่เกี่ยวข้อง พนักงานขับรถที่ได้รับมอบหมายจะต้องยินยอมปฏิบัติตามกฎระเบียบข้อบังคับของโรงงานอาคารสถานที่ที่เกี่ยวข้องกับการปฏิบัติการ"
  + "ขนส่งอย่างเคร่งครัด! รถขนส่งจะต้องมีอุปกรณ์เครื่องมือครบถ้วนตามข้อตกลงและเงื่อนไขทั่วไปของเลสชาโก้ และกรณีเป็นการขนส่งอันตรายจะต้องปฏิบัติ"
  + "ตามข้อกำหนดว่าด้วยการขนส่งสินค้าอันตรายทางถนนของประเทศไทย ฉบับที่ 2 (ADR: Thai Provision Volume II) ข้อมูลสินค้าอันตรายจะต้องถ่ายทอด"
  + "สื่อสารลงบนเอกสารประกอบการขนส่งอย่างครบถ้วน และห้ามแก้ไขเปลี่ยนแปลง ทั้งนี้การขนส่งตามข้อกำหนดว่าด้วยการขนส่งสินค้าอันตราย "
  + "ข้อ 1.1.4.2.1 : คือเราไม่ใช่ผู้ขนส่งสินค้าตามข้อกำหนด ADR และเอกสารชุดนี้ไม่สามารถนำไปใช้เป็นเอกสารการขนส่งตามข้อกำหนดของ ADR";

/**
 * Who signs, left to right.
 *
 * The lines stay blank. One of the seven copies has the officer's name typed
 * in — it was that officer signing that delivery — and printing a name onto
 * every blank form afterwards would put somebody's signature under work they
 * never saw.
 */
export const SIGNATURES: [string, string][] = [
  ["ลายมือชื่อของพนักงานเลสชาโก้", "LESCHACO OFFICER"],
  ["ลายมือชื่อผู้รับบรรทุก", "TRUCK DRIVER"],
  ["ลายมือชื่อลูกค้า", "CUSTOMER"],
];

/**
 * The item table's columns, which are the one thing that differs per customer.
 *
 * Four shapes across the seven copies. Every one begins with NO and carries a
 * product description, a quantity and a net weight; what changes is which of
 * the customer's own references sits beside them — a PO number, a D-code, a
 * delivery note number, or two of the three.
 *
 * `NO` is not in these lists. It is the row number, drawn by the table itself,
 * and a customer whose stored columns began with it would get two.
 */
export const ITEM_PRESETS: { id: string; label: string; seen: string[]; columns: string[] }[] = [
  {
    id: "po-packages",
    label: "PO NO. + จำนวนหีบห่อ",
    seen: ["AAT", "UNIC"],
    columns: ["PO NO", "No. of P'kg (s)", "PRODUCT NAME", "IM", "UN NUMBER CLASS", "NET WEIGHT (KGS)"],
  },
  {
    id: "dcode-qty",
    label: "D-Code + QTY (KG)",
    seen: ["SRITHEPTHAI", "UC"],
    columns: ["D-Code", "PRODUCT", "QTY (KG)", "DELIVERY NO.", "UN NUMBER CLASS", "NET WEIGHT (KGS)"],
  },
  {
    id: "po-dcode",
    label: "PO NO. + Dcode",
    seen: ["MERIT"],
    columns: ["PO NO", "Dcode", "PRODUCT NAME", "IM", "UN NUMBER CLASS", "NET WEIGHT (KGS)"],
  },
  {
    id: "po-delivery",
    label: "PO NO. + DELIVERY NO.",
    seen: ["Ampacet"],
    columns: ["PO NO", "DELIVERY NO.", "PRODUCT NAME", "IM", "UN NUMBER CLASS", "NET WEIGHT (KGS)"],
  },
];

/** The shape used when a customer has none stored. The commonest of the four. */
export const DEFAULT_ITEM_COLUMNS = ITEM_PRESETS[0].columns;

/**
 * Which columns to draw for a customer.
 *
 * Stored first, because the operators know their own customers. The default is
 * a starting point, not a guess to be trusted: a receipt drawn with the wrong
 * references is one the customer's gate will reject, so the screen says which
 * of the two it used.
 */
export function itemColumns(stored: string[] | undefined | null): string[] {
  const kept = (stored ?? []).map((one) => String(one ?? "").trim()).filter(Boolean);
  return kept.length ? kept : DEFAULT_ITEM_COLUMNS;
}

/** How many rows of the item table are drawn on a blank form. */
export const ITEM_ROWS = 6;
