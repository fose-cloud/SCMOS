"use client";

import { useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";
import {
  receiptChoices, receiptHead, receiptLabel, receiptItem, type ReceiptJob,
} from "../cargoReceipt";
import {
  ADDRESS, COMPANY, CONTACT, FORM_NO, ITEM_ROWS, NOTE, SIGNATURES, TERMS, itemColumns,
} from "../cargoReceiptForm";

/**
 * The cargo receipt, ISO-FRM-TH-CCL-04-01, as the operators actually issue it.
 *
 * Rebuilt from seven signed copies of the real document. The screen had been
 * drawing ISO-FRM-TH-ADM-26-06 — a simpler form with no item table — chosen
 * because the item table's columns differed per customer and could not be
 * pinned down. The seven copies settle it: the columns do differ, everything
 * around them is identical, and the answer is to store the columns per
 * customer rather than to issue a different document.
 *
 * The form's fixed text and its column shapes live in cargoReceiptForm.ts, so
 * the wording of a controlled document is testable without a browser.
 *
 * One copy, not two. The ADM form was printed twice on a sheet — one for the
 * customer, one for the driver — and every one of the seven real copies is a
 * single receipt on a single page.
 */

/**
 * One customer's form file: who it is for.
 *
 * The customer comes from the file name and nothing else. The customer cell
 * inside these workbooks is unreliable — the copy named for AAT says THE
 * CHEMOURS in it, and so do the ones for ISUZU and Iwatani, because somebody
 * opened the nearest file and typed over it. The name on the file is what the
 * operators actually use to find the right form.
 *
 * `columns` is the item table's headings for that customer, which is the one
 * part of this document that differs between them.
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
 * The workbook is not opened. The name is what is wanted and the name is on
 * the file; the item columns are set on the screen, because the operators know
 * their own customers better than a heading row read out of a spreadsheet
 * somebody last edited in 2022.
 */
export function readTemplate(file: File): FormTemplate | null {
  const customer = customerFromFile(file.name);
  return customer ? { customer, file: file.name, columns: [] } : null;
}

/**
 * The heading block, in the order the form lays it out.
 *
 * Most of these stay empty on this account's work — there is no vessel, no
 * B/L, no container and no port of discharge on a lorry leaving Bangna. They
 * are on the form because the same document covers import work, and a receipt
 * missing rows the customer has signed for before is a different document.
 */
type Form = {
  // left column
  jobNo: string;
  createDate: string;
  invoiceNo: string;
  vessel: string;
  eta: string;
  portOfDischarge: string;
  deliveryDate: string;
  blNo: string;
  truckNo: string;
  packages: string;
  grossWeight: string;
  remark: string;
  // right column
  customer: string;
  receiverName: string;
  receiverAddress: string;
  agent: string;
  containerNo: string;
  truckIn: string;
  truckOut: string;
  // the vehicle block under the item table
  vehicle: string;
  plate: string;
  /** "" until somebody ticks one — the form offers both and neither by default. */
  crew: "" | "with" | "without";
};

/** One line of the item table: a cell per column, and the columns vary. */
type Item = { cells: string[] };

const BLANK: Form = {
  jobNo: "", createDate: "", invoiceNo: "", vessel: "", eta: "", portOfDischarge: "",
  deliveryDate: "", blNo: "", truckNo: "", packages: "", grossWeight: "", remark: "",
  customer: "", receiverName: "", receiverAddress: "", agent: "", containerNo: "",
  truckIn: "", truckOut: "", vehicle: "", plate: "", crew: "",
};

const blankItems = (width: number): Item[] =>
  Array.from({ length: ITEM_ROWS }, () => ({ cells: Array(width).fill("") }));

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
/*
 * One ink for every rule on the document.
 *
 * The form was drawn with four: #333 around the boxes, #CBD5E1 under the
 * fields, #999 above the closing note and #000 inside the item table. On a
 * black-and-white controlled form that reads as lines of different weights
 * wandering across the page, which is what was reported. There is one line
 * colour here now, and any rule that is not INK is a mistake.
 */
const INK = "#333";

/*
 * The document's type, taken off the seven signed copies rather than chosen.
 *
 * Every one of them embeds Tahoma and Tahoma-Bold, which is the font the Thai
 * office set the workbook in, and every one of them sets the body at 8pt. The
 * page sizes differ — 737, 805 and 887 points wide — but each is US Letter
 * multiplied by exactly the same factor in both directions (1.205, 1.316,
 * 1.449), so those are one Letter sheet printed at three "fit to page"
 * settings, not three designs.
 *
 * So: Letter, Tahoma, 8pt body, 16pt title, 10pt for the ITEM heading. Sizes
 * are in points rather than pixels because this is a document that gets
 * printed and signed, and a point is the same size on paper as the workbook's.
 */
const FACE = "Tahoma, 'Leelawadee UI', 'Segoe UI', sans-serif";
const BODY_PT = "8pt";
const TITLE_PT = "16pt";
const ITEM_PT = "10pt";

/** A written-on line. The same ink as the box it sits in, not a paler one. */
const FIELD = `flex:1;min-width:0;border:none;border-bottom:1px solid ${INK};`
  + `background:transparent;font-size:${BODY_PT};font-family:inherit;padding:1px 3px;outline:none`;
const BOX = `border:1px solid ${INK};padding:0`;
const BOX_INPUT = `width:100%;border:none;background:transparent;font-size:${BODY_PT};font-family:inherit;padding:1px 4px;outline:none`;
const HEAD_CELL = `border:1px solid ${INK};padding:1px 4px;font-size:${BODY_PT};text-align:center;line-height:1.2`;

/**
 * The heading block, as the paper form rules it.
 *
 * Two columns of labelled lines, and on paper every one of them is a cell of a
 * ruled table — so the left and right columns line up row for row and the rules
 * run unbroken across the block. It had been a grid with a one-pixel gap and
 * each field carrying its own pale underline, which left the two columns ruled
 * to different lengths at slightly different heights.
 *
 * Written as a list of pairs rather than as markup so the ruling is worked out
 * rather than placed by hand: every row is drawn the same way, and a row with
 * nothing on its right is still a ruled cell rather than a gap.
 */
/** Every field of the form that holds text — which is all of them but the tick. */
type HeadingKey = Exclude<keyof Form, "crew">;

const HEADING_ROWS: [HeadingKey, HeadingKey | null][] = [
  ["jobNo", "customer"],
  ["createDate", null],
  ["invoiceNo", "receiverName"],
  ["vessel", "receiverAddress"],
  ["eta", null],
  ["portOfDischarge", null],
  ["deliveryDate", null],
  ["blNo", "agent"],
  ["truckNo", "containerNo"],
  ["packages", "truckIn"],
  ["grossWeight", "truckOut"],
];

/** The label each of those keys is printed with, spelled as the form spells it. */
const HEADING_LABELS: Partial<Record<HeadingKey, string>> = {
  jobNo: "JOB NO. :", customer: "CUSTOMER'S NAME :",
  createDate: "CREATE DATE :", invoiceNo: "INVOICE NO. :",
  receiverName: "RECEIVER'S NAME :", vessel: "VESSEL/FLIGHT :",
  receiverAddress: "RECEIVER'S ADDRESS :", eta: "ETA :",
  portOfDischarge: "PORT OF DISCHARGE :", deliveryDate: "DELIVERY DATE :",
  blNo: "B/L NO./AWB NO. :", agent: "AGENT :",
  truckNo: "TRUCK NO. :", containerNo: "CONTAINER NO. :",
  packages: "NO. OF PACKAGE", truckIn: "TIME OF TRUCK IN :",
  grossWeight: "GROSS WEIGHT (KGM)", truckOut: "TIME OF TRUCK OUT :",
  remark: "REMARK :",
};

/** The printed sheet, and the frame the letterhead mark is placed against. */
const SHEET_FRAME = "position:relative";

export function CargoForm({ jobs, stored, onStore, onToast }: {
  /**
   * The Domestic work, so a receipt can be filled from the job it is for.
   *
   * The whole register is passed and the Domestic rows picked out here rather
   * than upstream, because which jobs this form may draw on is a fact about the
   * form — it has no room for a vessel — and not about the screen that hosts it.
   */
  jobs: readonly ReceiptJob[];
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
  /**
   * The item table's headings for the customer on the form, and the rows under
   * them.
   *
   * Held together because they change together: picking a customer whose
   * columns differ has to redraw the rows, and a row array left over from the
   * last customer would put a D-code under a PO number heading.
   */
  const [columns, setColumns] = useState<string[]>(itemColumns(null));
  const [items, setItems] = useState<Item[]>(blankItems(itemColumns(null).length));

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

  /** Which job this receipt is for, if it was filled from one. */
  const [from, setFrom] = useState("");
  const choices = useMemo(() => receiptChoices(jobs), [jobs]);

  /**
   * Fills the form from a job, and says what it did not fill.
   *
   * Every field is written, including the empty ones. A picker that only filled
   * the blanks would leave the last job's truck number under this job's
   * consignee, and the receipt would be signed with it.
   */
  function fillFrom(key: string) {
    setFrom(key);
    if (!key) return;
    const job = choices.find((one) => String(one.key ?? "") === key);
    if (!job) return;

    const head = receiptHead(job);
    setForm(head);
    // The customer's own columns, and one row filled as far as the register
    // can. Read here rather than left to an effect so the headings and the row
    // under them are never a render out of step.
    const heads = columnsFor(head.customer);
    setColumns(heads);
    setItems([{ cells: heads.map((column) => receiptItem(job, column)) }, ...blankItems(heads.length).slice(1)]);

    const missing = [
      !head.customer && "ชื่อลูกค้า",
      !head.receiverAddress && "ปลายทาง",
      !head.plate && "ทะเบียนรถ",
      !head.invoiceNo && "เลขที่ใบส่งของ",
    ].filter(Boolean);
    onToast(`กรอกจากงาน ${receiptLabel(job)} แล้ว`
      + (missing.length ? ` — งานนี้ยังไม่มี ${missing.join(", ")} ต้องกรอกเอง` : "")
      + " · เวลารถเข้า-ออก และรายละเอียดสินค้า กรอกที่หน้างาน");
  }

  const set = (field: keyof Form, value: string) => setForm((held) => ({ ...held, [field]: value }));
  const setItem = (row: number, column: number, value: string) =>
    setItems((held) => held.map((item, i) => (i === row
      ? { cells: item.cells.map((cell, c) => (c === column ? value : cell)) }
      : item)));

  /**
   * The item columns for a customer, off what is stored for them.
   *
   * Falls back to the commonest of the four shapes rather than to an empty
   * table — see cargoReceiptForm.ts. The screen says which of the two it used,
   * because a receipt drawn with the wrong references is one the consignee's
   * gate will turn away.
   */
  function columnsFor(customer: string): string[] {
    const held = (stored ?? []).find((one) =>
      one.customer.trim().toLowerCase() === customer.trim().toLowerCase());
    return itemColumns(held?.columns);
  }

  /** Changing the customer by hand changes whose columns the table draws. */
  function pickCustomer(customer: string) {
    set("customer", customer);
    const heads = columnsFor(customer);
    setColumns(heads);
    setItems((held) => held.map((item) => ({
      // Keep what has been typed where a column survives the change, by name
      // rather than by position: two customers can both have PRODUCT NAME in
      // different places, and shifting the text sideways would be worse than
      // clearing it.
      cells: heads.map((column) => {
        const was = columns.indexOf(column);
        return was >= 0 ? (item.cells[was] ?? "") : "";
      }),
    })));
  }

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

    // One receipt, laid out as the paper form reads: a two-column heading
    // block, the customer's own item table, the vehicle line, then the terms,
    // the signatures and the note.
    const width = Math.max(7, columns.length + 1);
    const rows: (string | number)[][] = [];
    const put = (...cells: (string | number)[]) => {
      const row: (string | number)[] = Array(width).fill("");
      cells.forEach((value, i) => { row[i] = value; });
      rows.push(row);
    };
    /** A heading line: a label and value on the left, another pair on the right. */
    const pair = (leftLabel: string, left: string, rightLabel = "", right = "") => {
      const row: (string | number)[] = Array(width).fill("");
      row[0] = leftLabel; row[1] = left;
      if (rightLabel) { row[width - 3] = rightLabel; row[width - 2] = right; }
      rows.push(row);
    };

    put("CARGO RECEIPT");
    put("ใบรับ-ส่งสินค้า");
    put("Company :", COMPANY);
    put("Address :", ADDRESS);
    put(CONTACT);
    put();

    pair("JOB NO. :", form.jobNo, "CUSTOMER'S NAME :", form.customer);
    pair("CREATE DATE :", form.createDate);
    pair("INVOICE NO. :", form.invoiceNo, "RECEIVER'S NAME :", form.receiverName);
    pair("VESSEL/FLIGHT :", form.vessel, "RECEIVER'S ADDRESS :", form.receiverAddress);
    pair("ETA :", form.eta);
    pair("PORT OF DISCHARGE :", form.portOfDischarge);
    pair("DELIVERY DATE :", form.deliveryDate);
    pair("B/L NO./AWB NO. :", form.blNo, "AGENT :", form.agent);
    pair("TRUCK NO. :", form.truckNo, "CONTAINER NO. :", form.containerNo);
    pair("NO. OF PACKAGE", form.packages, "TIME OF TRUCK IN :", form.truckIn);
    pair("GROSS WEIGHT (KGM)", form.grossWeight, "TIME OF TRUCK OUT :", form.truckOut);
    pair("REMARK :", form.remark);
    put();

    put("ITEM :");
    put("NO", ...columns);
    items.forEach((item, i) => {
      // Only the rows somebody filled in. A blank numbered row on a signed
      // document invites a line to be added after it was signed for.
      if (item.cells.every((cell) => !cell.trim())) return;
      put(i + 1, ...item.cells);
    });
    put("TOTAL");
    put();

    put("หัวลาก");
    put("รถบรรทุก", form.vehicle, "ทะเบียนเลขที่ :", form.plate);
    put(form.crew === "with" ? "[X] มีพนักงานยกสินค้า" : "[ ] มีพนักงานยกสินค้า");
    put(form.crew === "without" ? "[X] ไม่มีพนักงานยกสินค้า" : "[ ] ไม่มีพนักงานยกสินค้า");
    put();

    TERMS.forEach((term, i) => put(`${i + 1}. ${term}`));
    put();
    put("(                    )", "", "(                    )", "", "(                    )");
    put(...SIGNATURES.flatMap(([thai]) => [thai, ""]));
    put(...SIGNATURES.flatMap(([, english]) => [english, ""]));
    put();
    put(NOTE);
    put(FORM_NO);

    const sheet = XLSX.utils.aoa_to_sheet(rows);
    sheet["!cols"] = Array.from({ length: width }, (_, i) => ({ wch: i === 0 ? 22 : 18 }));

    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, "Cargo Receipt");
    const safe = form.customer.replace(/[/:*?"<>|]/g, "-").trim();
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

        {/* Before the customer picker, because filling from a job sets the
            customer — and a control that overwrites the one above it should not
            sit below it. */}
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>กรอกจากงาน Domestic</span>
          <select
            value={from}
            disabled={!choices.length}
            onChange={(e) => fillFrom(e.target.value)}
            style={css(CONTROL + ";min-width:330px" + (choices.length ? "" : ";opacity:.5"))}
          >
            <option value="">
              {choices.length ? `เลือกงาน · ${choices.length} รายการ` : "ยังไม่มีงาน Domestic ในระบบ"}
            </option>
            {choices.map((job) => (
              <option key={String(job.key)} value={String(job.key ?? "")}>{receiptLabel(job)}</option>
            ))}
          </select>
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
            onClick={() => { setForm(BLANK); setItems(blankItems(columns.length)); setFrom(""); }}
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

      {/* One receipt. The form this screen used to draw was printed twice on a
          sheet, one copy for the customer and one for the driver; every one of
          the seven real copies of CCL-04-01 is a single receipt on a page. */}
      <div className="cargo-page" style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:20px 22px")}>
        <Receipt
          form={form} columns={columns} items={items}
          onField={set} onCustomer={pickCustomer} onItem={setItem}
        />
      </div>
    </div>
  );
}

/**
 * One receipt — ISO-FRM-TH-CCL-04-01.
 *
 * A top-level component rather than one built inside the form's render.
 * Defined in there it was a different component on every keystroke, so React
 * threw the subtree away and made a new one each time — and the box being
 * typed into lost focus after a single character.
 */
function Receipt({ form, columns, items, onField, onCustomer, onItem }: {
  form: Form;
  columns: string[];
  items: Item[];
  onField: (field: keyof Form, value: string) => void;
  onCustomer: (value: string) => void;
  onItem: (row: number, column: number, value: string) => void;
}) {
  return (
  <div style={css(`display:flex;flex-direction:column;gap:0;color:${INK};font-family:${FACE};` + SHEET_FRAME)}>
    <div style={css(`text-align:center;font-size:${TITLE_PT};font-weight:700;line-height:1.25`)}>CARGO RECEIPT</div>
    <div style={css(`text-align:center;font-size:${TITLE_PT};font-weight:600;line-height:1.25`)}>ใบรับ-ส่งสินค้า</div>

    {/* The mark, where the paper form carries it. Absolute so it sits beside
        the two title lines without pushing them off centre — they are centred
        on the sheet, not on the space left over next to a logo.

        Its own file, not the one in the app's header. That one is drawn white
        for the navy band it sits on, which on a white sheet prints as a navy
        block and costs a cartridge to say what a letterhead says with ink on
        the letters. This one is the mark itself, dark on nothing. */}
    <img src="/cargo-receipt-logo.png" alt="Leschaco"
      style={css("position:absolute;top:6px;right:10px;height:26px;width:auto")} />

    <div style={css(`display:flex;gap:6px;font-size:${BODY_PT};margin-top:2px`)}>
      <span style={css("flex:0 0 58px;font-weight:600")}>Company :</span>
      <span>{COMPANY}</span>
    </div>
    <div style={css(`display:flex;gap:6px;font-size:${BODY_PT}`)}>
      <span style={css("flex:0 0 58px;font-weight:600")}>Address :</span>
      <span>{ADDRESS}</span>
    </div>
    <div style={css(`font-size:${BODY_PT};margin-bottom:6px`)}>{CONTACT}</div>

    {/* The heading block, two columns, in the order the paper form reads.
        Most of the left column is blank on this account's work — no vessel,
        no B/L, no port of discharge — and the rows stay because the same
        document covers import work. */}
    {/* One ruled block, drawn row by row from HEADING_ROWS so both columns line
        up and the rules run unbroken. Most of the left column stays blank on
        this account's work — no vessel, no B/L, no port of discharge — and the
        rows remain because the same controlled form covers import work. */}
    <div style={css(`border:1px solid ${INK};border-bottom:none`)}>
      {HEADING_ROWS.map(([left, right]) => (
        <div key={left} style={css("display:grid;grid-template-columns:1fr 1fr")}>
          <div style={css(`border-bottom:1px solid ${INK};border-right:1px solid ${INK};padding:0 6px`)}>
            <Field label={HEADING_LABELS[left] ?? left} value={form[left]}
              onChange={(v) => onField(left, v)} />
          </div>
          <div style={css(`border-bottom:1px solid ${INK};padding:0 6px`)}>
            {right
              ? <Field label={HEADING_LABELS[right] ?? right} value={form[right]}
                  onChange={(v) => right === "customer" ? onCustomer(v) : onField(right, v)}
                  list={right === "customer" ? "cargo-customers" : undefined} />
              /* A row with nothing on its right is still a ruled cell. Left as a
                 gap it would break the rule running across the block. */
              : <span style={css("display:block;height:14px")} />}
          </div>
        </div>
      ))}
      <div style={css(`border-bottom:1px solid ${INK};padding:0 6px`)}>
        <Field label={HEADING_LABELS.remark ?? "REMARK :"} value={form.remark}
          onChange={(v) => onField("remark", v)} />
      </div>
    </div>

    <div style={css(`font-size:${ITEM_PT};font-weight:600;margin:6px 0 2px`)}>ITEM :</div>
    <table style={css("width:100%;border-collapse:collapse")}>
      <thead>
        <tr>
          <th style={css(HEAD_CELL + ";width:26px")}>NO</th>
          {columns.map((column) => <th key={column} style={css(HEAD_CELL)}>{column}</th>)}
        </tr>
      </thead>
      <tbody>
        {items.map((item, row) => (
          <tr key={row}>
            <td style={css(BOX + `;text-align:center;font-size:${BODY_PT};color:${INK}`)}>{row + 1}</td>
            {item.cells.map((value, column) => (
              <td key={column} style={css(BOX)}>
                <input
                  value={value}
                  onChange={(e) => onItem(row, column, e.target.value)}
                  style={css(BOX_INPUT + (/WEIGHT|QTY|P.?KG/i.test(columns[column] ?? "") ? ";text-align:right" : ""))}
                />
              </td>
            ))}
          </tr>
        ))}
        <tr>
          <td colSpan={columns.length + 1} style={css(BOX + `;padding:1px 4px;font-size:${BODY_PT};font-weight:600`)}>
            TOTAL
          </td>
        </tr>
      </tbody>
    </table>

    {/* The vehicle block. "หัวลาก" and "รถบรรทุก" are the two kinds of vehicle
        the form offers; the line beside them is composed from the job's own
        truck counts. */}
    <div style={css(`display:flex;gap:16px;align-items:baseline;margin-top:6px;font-size:${BODY_PT};flex-wrap:wrap`)}>
      <span style={css("font-weight:600")}>หัวลาก</span>
      <span style={css("font-weight:600")}>รถบรรทุก</span>
      <input value={form.vehicle} onChange={(e) => onField("vehicle", e.target.value)}
        style={css(FIELD + ";flex:0 1 190px")} />
      <span style={css("font-weight:600;white-space:nowrap")}>ทะเบียนเลขที่ :</span>
      <input value={form.plate} onChange={(e) => onField("plate", e.target.value)}
        style={css(FIELD + ";flex:1 1 150px")} />
    </div>
    {/* One or the other, never both — which a pair of radios says and a pair of
        tick boxes does not. */}
    <div style={css(`display:flex;gap:20px;margin-top:3px;font-size:${BODY_PT}`)}>
      {([["with", "มีพนักงานยกสินค้า"], ["without", "ไม่มีพนักงานยกสินค้า"]] as const).map(([value, label]) => (
        <label key={value} style={css("display:flex;gap:5px;align-items:center;cursor:pointer")}>
          <input type="radio" name="cargo-crew" checked={form.crew === value}
            onChange={() => onField("crew", value)} style={css("margin:0")} />
          <span>{label}</span>
        </label>
      ))}
    </div>

    <ol style={css(`font-size:${BODY_PT};line-height:1.45;margin:7px 0 0;padding-left:15px`)}>
      {TERMS.map((term) => <li key={term.slice(0, 24)} style={css("margin-bottom:1px")}>{term}</li>)}
    </ol>

    {/* Where the form is signed. Left empty for whoever signs — one of the real
        copies has the officer's name typed in, and printing it on every blank
        form would put their signature under work they never saw. */}
    <div style={css("display:flex;gap:18px;margin-top:16px;margin-bottom:4px")}>
      {SIGNATURES.map(([thai, english]) => (
        <div key={thai} style={css("flex:1;text-align:center")}>
          <div style={css(`border-bottom:1px dotted ${INK};height:1px;margin-bottom:5px`)} />
          <div style={css(`font-size:${BODY_PT};letter-spacing:.5px`)}>(&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;)</div>
          <div style={css(`font-size:${BODY_PT};color:${INK};margin-top:2px`)}>{thai}</div>
          {english && <div style={css(`font-size:${BODY_PT};font-weight:600;margin-top:1px`)}>{english}</div>}
        </div>
      ))}
    </div>

    <div style={css(`font-size:${BODY_PT};line-height:1.4;white-space:pre-line;color:${INK};border-top:1px solid ${INK};padding-top:4px`)}>{NOTE}</div>
    <div style={css(`text-align:right;font-size:${BODY_PT};color:${INK};margin-top:3px`)}>{FORM_NO}</div>
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
    <div style={css(`display:flex;align-items:baseline;gap:5px;font-size:${BODY_PT}`)}>
      <span style={css("white-space:nowrap;font-weight:600")}>{label}</span>
      <input list={list} value={value} onChange={(e) => onChange(e.target.value)} style={css(FIELD)} />
    </div>
  );
}
