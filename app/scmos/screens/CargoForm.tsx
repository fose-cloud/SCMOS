"use client";

import { useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";

/**
 * The cargo receipt, ISO-FRM-TH-ADM-26-06, reproduced from the account's file.
 *
 * Not the CCL-04-01 form this tab carried before. That one has an item table
 * whose columns each customer had changed to suit themselves; this one has no
 * item table at all — a package count, an approximate weight, the times the
 * truck came and went, and a remarks column. Different document, and this is
 * the one asked for.
 *
 * The sheet holds the receipt twice, rows 1-23 and 25-47, cell for cell
 * identical. That is not a mistake to tidy up: it is one copy for the customer
 * and one for the driver, printed on the same page and signed together. So the
 * screen shows one to fill in and both come out of the printer and the export.
 *
 * There is no signature block anywhere on it. Row 20 is sixty-two points of
 * nothing, which is where people sign. Adding labels would be inventing a
 * document the customer has never seen.
 */

/** Fixed on every copy: the letterhead the form prints. */
const COMPANY = "Leschaco(Thailand) Ltd.";
const ADDRESS = "3354/36-39 Manorom Building, 11th Floor, Rama IV Road, Klongtoey, Bangkok 10110";
const CONTACT = "Tel : (66) 0 2686 1000 Fax : (66) 0 2671 6717";
const FORM_NO = "ISO-FRM-TH-ADM-26-06";

/**
 * Who signs, in the order the form puts them.
 *
 * The template this was first built from had sixty-two points of empty row
 * here and no labels, so none were invented. A filled-in copy of the receipt
 * settled it: three signatures, the Leschaco officer on the left, the driver
 * taking the load in the middle, the customer receiving it on the right.
 *
 * The lines stay blank. The copy that named them was one officer signing one
 * delivery in June 2022; printing that name onto every blank form afterwards
 * would put somebody's signature under work they never saw.
 */
/**
 * Rows in one copy of the form, and therefore the offset of the second.
 *
 * Written down rather than left as the 24 it used to be: the signature block
 * made the copy a row taller, and a hard-coded offset would have dropped the
 * second copy's merges a row into the first one's note.
 */
const COPY_ROWS = 25;

const SIGNATURES: [string, string][] = [
  ["ลายมือชื่อพนักงานเลสชาโก้", "LESCHACO OFFICER"],
  ["ลายมือชื่อผู้รับบรรทุก", ""],
  ["ลายมือชื่อลูกค้า", ""],
];

/** The conditions of carriage, exactly as the cell in A19 holds them. */
const TERMS = "1. ได้รับสินค้าไปในสภาพที่เรียบร้อยตามรายการ (HAVE RECEIVED IN GOOD ORDER AND CONDITION FOR ABOVE MENTIONED GOODS.)\n"
  + "2. ถ้าสินค้ามีการสูญหายหรือชำรุด โปรดระบุลงในใบรับสินค้านี้ภายใน 24 ชั่วโมง ส่งจดหมายเคลมถึงเลสชาโก้ภายใน 7 วัน นับจากวันได้รับสินค้า มิเช่นนั้นแล้วทางบริษัทจะไม่รับผิดชอบต่อการสูญเสียหรือชำรุดรวมถึงการรับเคลมด้วย\n"
  + "(WE  CANNOT  BE  HELD  RESPONSIBLE  FOR  LOSS  OR  DAMAGE  OF  GOODS,UNLESS  STATED  ON  THIS  CARGO  RECEIPT  WITHIN  24  HOURS  AND  CLAIM  LETTER  RECEIVED  NO  LATER  THAN  AFTER  7  DAYS  FROM  DELIVERY, LESCHACO  WILL  NOT  TAKE RESPONSIBILITY  FOR  ANY  CLAIM  AFTER  THE  TIME  FRAME  GIVEN.)";

/** The standing note about how carriage is performed, exactly as A22 holds it. */
const NOTE = "หมายเหตุ :\n"
  + "คู่มือแนวทางปฏิบัติโดยทั่วไปของรถขนส่งที่ปฏิบัติการดำเนินการและดูแลรับผิดชอบการให้บริการโลจิสติกส์ในนามของบริษัท เลสชาโก้ (ประเทศไทย) จำกัด ถือเป็นส่วนหนึ่งของธุรกิจการขนส่งของเลสชาโก้ ซึ่งการให้บริการขนส่งจะต้องดำเนินการภายใต้กฎหมายการขนส่งทางถนนภายในประเทศและกฎระเบียบข้อบังคับอื่นๆที่เกี่ยวข้อง พนักงานขับรถที่ได้รับมอบหมายจะต้องยินยอมปฏิบัติตามกฎระเบียบข้อบังคับของโรงงานอาคารสถานที่ที่เกี่ยวข้องกับการปฏิบัติการขนส่งอย่างเคร่งครัด! รถขนส่งจะต้องมีอุปกรณ์เครื่องมือครบถ้วนตามข้อตกลงและเงื่อนไขทั่วไปของเลสชาโก้และกรณีเป็นการขนส่งสินค้าอันตรายจะต้องปฏิบัติตามข้อกำหนดว่าด้วยการขนส่งสินค้าอันตรายทางถนนของประเทศไทย ฉบับที่ 2 (ADR : Thai Provision Volume II) ข้อมูลสินค้าอันตรายจะต้องถ่ายทอดสื่อสารลงบนเอกสารประกอบการขนส่งอย่างครบถ้วนและห้ามแก้ไขเปลี่ยนแปลง ทั้งนี้การขนส่งตามข้อกำหนดว่าด้วยการขนส่งสินค้าอันตราย ข้อ 1.1.4.2.1.: คือเราไม่ใช่ผู้ขนส่งสินค้าตามข้อกำหนด ADR  และเอกสารชุดนี้ไม่สามารถนำไปใช้เป็นเอกสารการขนส่งตามข้อกำหนดของ ADR";

/** How many lines the body has, counted off rows 14 to 18 of the sheet. */
const BODY_ROWS = 5;

/**
 * One customer's form file: who it is for.
 *
 * The customer comes from the file name and nothing else. The customer cell
 * inside these workbooks is unreliable — the copy named for AAT says THE
 * CHEMOURS in it, and so do the ones for ISUZU and Iwatani, because somebody
 * opened the nearest file and typed over it. The name on the file is what the
 * operators actually use to find the right form.
 */
export type FormTemplate = { customer: string; file: string; columns: string[] };

/**
 * The customer a form file is for, read off its name.
 *
 * The form number and the words "Cargo Receipt" are the same on all of them;
 * whatever is left is the customer. What is left is sometimes not a customer at
 * all — "Copy" and "Simple" are working copies of the blank form — and those
 * are not offered, because a receipt addressed to "Copy" is a document nobody
 * can send.
 */
export function customerFromFile(name: string): string {
  let text = name.replace(/\.xlsx?$/i, "");
  text = text.replace(/^ISO-FRM-TH-[A-Z]+-?[\d-]*_?/i, "");
  text = text.replace(/^\s*Cargo\s*Receipt\s*/i, "");
  text = text.replace(/^[\s_-]+/, "").replace(/[\s_-]+$/, "");
  text = text.replace(/\s*-\s*Copy$/i, "").trim();
  return /^(copy|simple)$/i.test(text) ? "" : text;
}

/**
 * A form file read for its customer.
 *
 * The workbook is not opened at all. It used to be, to lift the item-table
 * headings out of it, and that reader turned away any file without such a table
 * — which is every copy of this form, including the one this screen is now
 * built from. The name is what is wanted and the name is on the file.
 */
export function readTemplate(file: File): FormTemplate | null {
  const customer = customerFromFile(file.name);
  return customer ? { customer, file: file.name, columns: [] } : null;
}

type Form = {
  customer: string;
  deliveryTo: string;
  invoiceNo: string;
  vessel: string;
  truckNo: string;
  date: string;
  blNo: string;
  eta: string;
};

type Line = { packages: string; weight: string; truckIn: string; truckOut: string; remark: string };

const BLANK: Form = {
  customer: "", deliveryTo: "", invoiceNo: "", vessel: "",
  truckNo: "", date: "", blNo: "", eta: "",
};

const BLANK_LINES: Line[] = Array.from({ length: BODY_ROWS }, () => ({
  packages: "", weight: "", truckIn: "", truckOut: "", remark: "",
}));

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
const FIELD = "flex:1;min-width:0;border:none;border-bottom:1px solid #CBD5E1;background:transparent;font-size:11.5px;font-family:inherit;padding:1px 3px;outline:none";
const BOX = "border:1px solid #333;padding:0";
const BOX_INPUT = "width:100%;border:none;background:transparent;font-size:11px;font-family:inherit;padding:3px 5px;outline:none";
const HEAD_CELL = "border:1px solid #333;padding:3px 5px;font-size:10px;text-align:center;line-height:1.25";

/** The printed sheet, and the frame the letterhead mark is placed against. */
const SHEET_FRAME = "position:relative";

export function CargoForm({ stored, onStore, onToast }: {
  /**
   * The customers already on file. null while they are still being fetched,
   * which is not the same as an empty list and must not read as "none exist".
   */
  stored: FormTemplate[] | null;
  /** Writes the whole set back, and answers with how many were kept. */
  onStore: (rows: FormTemplate[]) => Promise<number>;
  onToast: (message: string) => void;
}) {
  const [form, setForm] = useState<Form>(BLANK);
  const [lines, setLines] = useState<Line[]>(BLANK_LINES);

  /**
   * A folder read this session, if there has been one.
   *
   * Derived rather than copied: what is on file arrives as a prop, and mirroring
   * it into state means two versions of one list that drift the moment either is
   * written to. So the state here is only what the prop cannot know — a folder
   * somebody opened a minute ago — and the screen shows that, falling back to
   * what is stored. It gives "not saved yet" for free.
   */
  const [read, setRead] = useState<FormTemplate[] | null>(null);
  const customers = read ?? stored ?? [];
  const unsaved = read !== null;
  const [storing, setStoring] = useState(false);

  const set = (field: keyof Form, value: string) => setForm((held) => ({ ...held, [field]: value }));
  const setLine = (row: number, field: keyof Line, value: string) =>
    setLines((held) => held.map((line, i) => (i === row ? { ...line, [field]: value } : line)));

  function loadFolder(files: FileList) {
    const found: FormTemplate[] = [];
    let skipped = 0;
    for (const file of Array.from(files)) {
      const template = readTemplate(file);
      if (template) found.push(template); else skipped++;
    }
    if (!found.length) { onToast("ไม่พบชื่อลูกค้าในไฟล์ที่เลือก"); return; }

    // Two files for one customer is a working copy beside the real one. The
    // file without "Copy" in its name is the one to keep.
    const byCustomer = new Map<string, FormTemplate>();
    for (const template of found) {
      const held = byCustomer.get(template.customer);
      if (!held || /copy/i.test(held.file)) byCustomer.set(template.customer, template);
    }
    const kept = [...byCustomer.values()].sort((a, b) => a.customer.localeCompare(b.customer));
    setRead(kept);
    onToast(`อ่านชื่อลูกค้าแล้ว ${kept.length} ราย${skipped ? ` · ข้าม ${skipped} ไฟล์` : ""} — กดบันทึกเข้าระบบเพื่อไม่ต้องเลือกไฟล์อีก`);
  }

  async function store() {
    if (!customers.length) { onToast("ยังไม่มีรายชื่อให้บันทึก"); return; }
    setStoring(true);
    try {
      const kept = await onStore(customers);
      setRead(null);
      onToast(`บันทึกแล้ว ${kept} ลูกค้า — ครั้งต่อไปเลือกได้เลย`);
    } catch (error) {
      onToast(error instanceof Error ? error.message : String(error));
    } finally {
      setStoring(false);
    }
  }

  function exportSheet() {
    if (!form.customer.trim()) { onToast("เลือกชื่อลูกค้าก่อน"); return; }

    // Cell for cell as the workbook has it, twice, because that is what the
    // sheet is: one receipt for the customer and one for the driver.
    const copy = (): (string | number)[][] => {
      const rows: (string | number)[][] = Array.from({ length: COPY_ROWS - 1 }, () => Array(5).fill(""));
      const put = (r: number, c: number, v: string) => { rows[r - 1][c] = v; };

      put(1, 0, "CARGO RECEIPT");
      put(2, 0, "ใบรับ-ส่งสินค้า");
      put(3, 0, "Company :"); put(3, 1, COMPANY);
      put(4, 0, "Address :"); put(4, 1, ADDRESS);
      put(5, 0, CONTACT);

      put(7, 0, `CUSTOMER'S NAME : ${form.customer}`);
      put(7, 4, `TRUCK NO. : ${form.truckNo}`);
      put(8, 0, `Delivery to :   ${form.deliveryTo}`);
      put(9, 4, `DATE :   ${form.date}`);
      put(10, 0, `INVOICE NO. : ${form.invoiceNo}`);
      put(10, 4, `B/L NO./AWB NO. : ${form.blNo}`);
      put(11, 0, `VESSEL/FLIGHT : ${form.vessel}`);
      put(11, 4, `ETA. : ${form.eta}`);

      put(12, 0, "จำนวนหีบห่อ"); put(12, 1, "น้ำหนักโดยประมาณ"); put(12, 2, "Time");
      put(12, 4, "หมายเหตุ\nRemarks");
      put(13, 0, "No. of  P'kg (s)"); put(13, 1, "Approx Weight (kgs)");
      put(13, 2, "Truck in"); put(13, 3, "Truck out");

      lines.forEach((line, i) => {
        put(14 + i, 0, line.packages);
        put(14 + i, 1, line.weight);
        put(14 + i, 2, line.truckIn);
        put(14 + i, 3, line.truckOut);
        put(14 + i, 4, line.remark);
      });

      put(19, 0, TERMS);
      // The signature row, so a printed sheet carries the same three lines the
      // screen does rather than an empty band somebody has to label by hand.
      put(21, 0, "(                    )");
      put(21, 2, "(                    )");
      put(21, 4, "(                    )");
      put(22, 0, SIGNATURES[0][1] + "\n" + SIGNATURES[0][0]);
      put(22, 2, SIGNATURES[1][0]);
      put(22, 4, SIGNATURES[2][0]);
      put(23, 0, NOTE);
      put(24, 4, FORM_NO);
      return rows;
    };

    // A blank row between the two, as on the original. One row longer than it
    // was now that the signatures are written out, so the second copy shifts
    // with it rather than landing on top of the first one's note.
    const aoa = [...copy(), Array(5).fill(""), ...copy()];
    const sheet = XLSX.utils.aoa_to_sheet(aoa);

    // The same merges, offset by 24 for the second copy.
    const spans: [number, number, number, number][] = [
      [1, 0, 1, 4], [2, 0, 2, 4], [5, 0, 5, 4],
      [7, 0, 7, 2], [8, 0, 8, 2], [9, 0, 9, 2], [10, 0, 10, 2], [11, 0, 11, 2],
      [12, 2, 12, 3], [12, 4, 13, 4],
      [19, 0, 19, 4], [21, 0, 21, 1], [21, 2, 21, 3], [22, 0, 22, 1], [22, 2, 22, 3],
      [23, 0, 23, 4],
    ];
    sheet["!merges"] = spans.flatMap(([r1, c1, r2, c2]) => [0, COPY_ROWS].map((shift) => ({
      s: { r: r1 - 1 + shift, c: c1 }, e: { r: r2 - 1 + shift, c: c2 },
    })));
    sheet["!cols"] = [{ wch: 18 }, { wch: 22 }, { wch: 12 }, { wch: 12 }, { wch: 30 }];

    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, FORM_NO);
    const safe = form.customer.replace(/[\\/:*?"<>|]/g, "-").trim();
    XLSX.writeFile(book, `Cargo_Receipt_${safe}.xlsx`);
    onToast(`ส่งออกใบรับ-ส่งสินค้าของ ${form.customer} แล้ว`);
  }

  function print() {
    if (!form.customer.trim()) { onToast("เลือกชื่อลูกค้าก่อน"); return; }
    window.print();
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div className="no-print" style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;display:flex;gap:14px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>โฟลเดอร์ฟอร์มลูกค้า</span>
          <input
            type="file"
            accept=".xls,.xlsx"
            multiple
            onChange={(e) => { const chosen = e.target.files; if (chosen?.length) loadFolder(chosen); e.target.value = ""; }}
            style={css("font-size:12px;font-family:inherit;max-width:230px")}
          />
        </label>

        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ชื่อลูกค้า</span>
          <select
            value={form.customer}
            disabled={!customers.length}
            onChange={(e) => set("customer", e.target.value)}
            style={css(CONTROL + ";min-width:250px" + (customers.length ? "" : ";opacity:.5"))}
          >
            <option value="">
              {customers.length ? "เลือกลูกค้า"
                : stored === null ? "กำลังโหลดรายชื่อ…"
                : "เลือกโฟลเดอร์ฟอร์มก่อน"}
            </option>
            {customers.map((entry) => (
              <option key={entry.customer} value={entry.customer}>{entry.customer}</option>
            ))}
          </select>
        </label>

        <div style={css("display:flex;gap:8px;margin-left:auto")}>
          {unsaved && (
            <button
              onClick={store}
              disabled={storing}
              style={css("height:32px;padding:0 15px;border-radius:4px;font-size:12.5px;font-weight:600;font-family:inherit;"
                + (storing
                  ? "border:1px solid #E7ECF2;background:#FAFBFC;color:#B4C0CC;cursor:default"
                  : "border:1px solid #0A6E8A;background:#fff;color:#0A6E8A;cursor:pointer"))}
            >
              {storing ? "กำลังบันทึก…" : `บันทึกเข้าระบบ (${customers.length})`}
            </button>
          )}
          <button
            onClick={() => { setForm(BLANK); setLines(BLANK_LINES); }}
            className="ghost-btn"
            style={css("height:32px;padding:0 14px;border:1px solid #D3DBE3;background:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit;color:#465A6E")}
          >
            ล้างฟอร์ม
          </button>
          <button
            onClick={print}
            style={css("height:32px;padding:0 16px;border:1px solid #0A2240;background:#fff;color:#0A2240;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}
          >
            พิมพ์ / บันทึกเป็น PDF
          </button>
          <button
            onClick={exportSheet}
            style={css("height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit")}
          >
            Export Excel
          </button>
        </div>
      </div>

      <datalist id="cargo-customers">
        {customers.map((entry) => <option key={entry.customer} value={entry.customer} />)}
      </datalist>

      <div className="cargo-page" style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:20px 22px")}>
        <Receipt form={form} lines={lines} onField={set} onLine={setLine} />
        {/* The second copy. Hidden on screen — two identical forms to type into
            would be a question nobody should have to answer — and printed with
            the first, because the page is meant to carry both. */}
        <div className="print-only">
          <div style={css("height:14px")} />
          <Receipt form={form} lines={lines} onField={set} onLine={setLine} />
        </div>
      </div>
    </div>
  );
}

/**
 * One receipt.
 *
 * A top-level component rather than one built inside the form's render. Defined
 * in there it was a different component on every keystroke, so React threw the
 * subtree away and made a new one each time — and the box being typed into lost
 * focus after a single character.
 */
function Receipt({ form, lines, onField, onLine }: {
  form: Form;
  lines: Line[];
  onField: (field: keyof Form, value: string) => void;
  onLine: (row: number, field: keyof Line, value: string) => void;
}) {
  return (
  <div style={css("display:flex;flex-direction:column;gap:0;color:#111;" + SHEET_FRAME)}>
    <div style={css("text-align:center;font-size:15px;font-weight:700;line-height:1.3")}>CARGO RECEIPT</div>
    <div style={css("text-align:center;font-size:13px;font-weight:600;line-height:1.3")}>ใบรับ-ส่งสินค้า</div>

    {/* The mark, where the paper form carries it. Absolute so it sits beside
        the two title lines without pushing them off centre — they are centred
        on the sheet, not on the space left over next to a logo. */}
    <img src="/cargo-logo.png" alt="Leschaco"
      style={css("position:absolute;top:6px;right:10px;height:26px;width:auto")} />

    <div style={css("display:flex;gap:6px;font-size:10.5px;margin-top:2px")}>
      <span style={css("flex:0 0 62px;font-weight:600")}>Company :</span>
      <span>{COMPANY}</span>
    </div>
    <div style={css("display:flex;gap:6px;font-size:10.5px")}>
      <span style={css("flex:0 0 62px;font-weight:600")}>Address :</span>
      <span>{ADDRESS}</span>
    </div>
    <div style={css("font-size:10.5px;margin-bottom:7px")}>{CONTACT}</div>

    {/* The heading block: three of the five columns on the left, the fifth on
        the right, exactly as the merges in the sheet lay it out. */}
    <div style={css("display:grid;grid-template-columns:3fr 2fr;gap:2px 14px")}>
      <Field label="CUSTOMER'S NAME :" value={form.customer} onChange={(v) => onField("customer", v)} list="cargo-customers" />
      <Field label="TRUCK NO. :" value={form.truckNo} onChange={(v) => onField("truckNo", v)} />
      <Field label="Delivery to :" value={form.deliveryTo} onChange={(v) => onField("deliveryTo", v)} />
      <span />
      <span />
      <Field label="DATE :" value={form.date} onChange={(v) => onField("date", v)} />
      <Field label="INVOICE NO. :" value={form.invoiceNo} onChange={(v) => onField("invoiceNo", v)} />
      <Field label="B/L NO./AWB NO. :" value={form.blNo} onChange={(v) => onField("blNo", v)} />
      <Field label="VESSEL/FLIGHT :" value={form.vessel} onChange={(v) => onField("vessel", v)} />
      <Field label="ETA. :" value={form.eta} onChange={(v) => onField("eta", v)} />
    </div>

    <table style={css("width:100%;border-collapse:collapse;margin-top:8px")}>
      <thead>
        {/* Two header rows, because "Time" sits over both clock columns on the
            paper form. As one row it read as the heading of "Truck in" alone,
            with a blank cell over "Truck out". */}
        <tr>
          <th rowSpan={2} style={css(HEAD_CELL + ";width:17%")}>จำนวนหีบห่อ<br />No. of&nbsp; P&apos;kg (s)</th>
          <th rowSpan={2} style={css(HEAD_CELL + ";width:21%")}>น้ำหนักโดยประมาณ<br />Approx Weight (kgs)</th>
          <th colSpan={2} style={css(HEAD_CELL + ";width:26%")}>Time</th>
          <th rowSpan={2} style={css(HEAD_CELL)}>หมายเหตุ<br />Remarks</th>
        </tr>
        <tr>
          <th style={css(HEAD_CELL + ";width:13%")}>Truck in</th>
          <th style={css(HEAD_CELL + ";width:13%")}>Truck out</th>
        </tr>
      </thead>
      <tbody>
        {lines.map((line, i) => (
          <tr key={i}>
            {(["packages", "weight", "truckIn", "truckOut", "remark"] as (keyof Line)[]).map((field) => (
              <td key={field} style={css(BOX)}>
                <input
                  value={line[field]}
                  onChange={(e) => onLine(i, field, e.target.value)}
                  style={css(BOX_INPUT + (field === "packages" || field === "weight" ? ";text-align:right" : ""))}
                />
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </table>

    <div style={css("font-size:9px;line-height:1.5;white-space:pre-line;margin-top:7px")}>{TERMS}</div>

    {/* Where the form is signed. The blank row it used to be is now the three
        signatures the real receipt carries, left empty for whoever signs. */}
    <div style={css("display:flex;gap:18px;margin-top:34px;margin-bottom:6px")}>
      {SIGNATURES.map(([thai, english]) => (
        <div key={thai} style={css("flex:1;text-align:center")}>
          <div style={css("border-bottom:1px dotted #333;height:1px;margin-bottom:5px")} />
          <div style={css("font-size:10px;letter-spacing:.5px")}>(&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;)</div>
          <div style={css("font-size:9px;color:#333;margin-top:2px")}>{thai}</div>
          {english && <div style={css("font-size:9px;font-weight:600;margin-top:1px")}>{english}</div>}
        </div>
      ))}
    </div>

    <div style={css("font-size:8.5px;line-height:1.45;white-space:pre-line;color:#333;border-top:1px solid #999;padding-top:4px")}>{NOTE}</div>
    <div style={css("text-align:right;font-size:9px;color:#333;margin-top:3px")}>{FORM_NO}</div>
  </div>
  );
}

/** One labelled blank on the heading block. */
function Field({ label, value, onChange, list }: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  list?: string;
}) {
  return (
    <div style={css("display:flex;align-items:baseline;gap:5px;font-size:10.5px")}>
      <span style={css("white-space:nowrap;font-weight:600")}>{label}</span>
      <input list={list} value={value} onChange={(e) => onChange(e.target.value)} style={css(FIELD)} />
    </div>
  );
}
