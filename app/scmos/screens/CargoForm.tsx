"use client";

import { useState } from "react";
import * as XLSX from "xlsx";
import { css } from "../theme";

/**
 * The cargo receipt, ISO-FRM-TH-CCL-04-01, as a form that can be filled in.
 *
 * Built from the fifty-five copies the operators keep, one per customer. The
 * heading block is the same in fifty-four of them — job number, invoice,
 * vessel, ETA, port, delivery date, B/L, truck, packages, weight, and the
 * receiver and agent down the right — so that part is the form itself.
 *
 * The item table is not. Its column headings are what each customer's copy was
 * changed to: thirty-seven ask for a PO number and a package count, fifteen ask
 * for a D-code and a quantity in kilos, and a handful are one-offs — Toshiba
 * want a part code, Penn Color a delivery number. So the headings are data
 * here, not markup: pick one of the two common sets, or type over any heading.
 * Hard-coding the majority set would have quietly handed nearly a third of
 * these customers a document with the wrong columns on it.
 *
 * Exports as the .xls it came from, and prints as an A4 page. There is no PDF
 * library in this app; printing to PDF is a button every operator already has,
 * and it renders the Thai with the page's own fonts.
 */

/**
 * One customer's copy of the form: who it is for, and what its item table asks.
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
 * are not offered as customers, because a receipt addressed to "Copy" is a
 * document nobody can send.
 */
export function customerFromFile(name: string): string {
  let text = name.replace(/\.xlsx?$/i, "");
  text = text.replace(/^ISO-FRM-TH-[A-Z]+-?[\d-]*_?/i, "");
  text = text.replace(/^\s*Cargo\s*Receipt\s*/i, "");
  text = text.replace(/^[\s_-]+/, "").replace(/[\s_-]+$/, "");
  text = text.replace(/\s*-\s*Copy$/i, "").trim();
  return /^(copy|simple)$/i.test(text) ? "" : text;
}

/** The item-table headings a form file asks for, read from its header row. */
export async function readTemplate(file: File): Promise<FormTemplate | null> {
  const customer = customerFromFile(file.name);
  if (!customer) return null;

  const book = XLSX.read(await file.arrayBuffer(), { cellDates: false });
  const sheet = book.Sheets[book.SheetNames[0]];
  if (!sheet) return null;
  const rows = XLSX.utils.sheet_to_json<unknown[]>(sheet, { header: 1, blankrows: false, defval: "" });

  // The headings sit on the row whose first cell is NO, under ITEM. Found by
  // what it says rather than by counting to eighteen.
  //
  // A file with no such row is not this form. One of the fifty-five is
  // ISO-FRM-TH-ADM-26-06 — a different document that happens to be filed with
  // them, with its terms at the top and no item table at all. It is skipped
  // rather than half-read, and the customer it names has a real CCL-04-01 copy
  // elsewhere in the folder, so nobody loses a form over it.
  const headRow = rows.findIndex((row) => /^no$/i.test(String(row?.[0] ?? "").trim()));
  if (headRow < 0) return null;

  const columns = (rows[headRow] as unknown[])
    .slice(1)
    .map((cell) => String(cell ?? "").replace(/\s+/g, " ").trim());
  while (columns.length && !columns[columns.length - 1]) columns.pop();
  if (!columns.length) return null;

  return { customer, file: file.name, columns };
}

/** The heading sets actually in use, counted across the fifty-five forms. */
const COLUMN_SETS: { name: string; columns: string[] }[] = [
  {
    name: "มาตรฐาน (PO NO)",
    columns: ["PO NO", "No. of P'kg (s)", "PRODUCT NAME", "IM", "UN NUMBER CLASS", "NET WEIGHT (KGS)"],
  },
  {
    name: "แบบ D-Code",
    columns: ["D-Code", "PRODUCT", "QTY (KG)", "DELIVERY NO.", "", ""],
  },
];

const TERMS = [
  "1. ได้รับสินค้าไปในสภาพที่เรียบร้อยตามรายการ (HAVE RECEIVED IN GOOD ORDER AND CONDITION FOR ABOVE MENTIONED GOODS)",
  "2. ถ้าสินค้ามีการสูญหายหรือชำรุด โปรดระบุลงในใบรับสินค้านี้ ภายใน 24 ชั่วโมง ไม่เช่นนั้นแล้วทางบริษัทจะไม่รับผิดชอบต่อการสูญเสียหรือว่าชำรุดของสินค้าใดๆทั้งสิ้น",
  "(WE CANNOT BE HELD RESPONSIBLE FOR LOSS OR DAMAGE OF GOODS, UNLESS STATED ON THIS CARGO RECEIPT.)",
];

/** Left column of the heading block: the label as printed, and its field. */
const LEFT: [string, keyof Form][] = [
  ["JOB NO. :", "jobNo"],
  ["CREATE DATE :", "createDate"],
  ["INVOICE NO. :", "invoiceNo"],
  ["VESSEL/FLIGHT :", "vessel"],
  ["ETA :", "eta"],
  ["PORT OF DISCHARGE :", "port"],
  ["DELIVERY DATE :", "deliveryDate"],
  ["B/L NO./AWB NO. :", "blNo"],
  ["TRUCK NO. :", "truckNo"],
  ["NO. OF PACKAGE", "packages"],
  ["GROSS WEIGHT (KGM)", "grossWeight"],
];

const RIGHT: [string, keyof Form][] = [
  ["CUSTOMER'S NAME :", "customer"],
  ["RECEIVER'S NAME :", "receiver"],
  ["RECEIVER'S ADDRESS :", "receiverAddress"],
  ["AGENT :", "agent"],
  ["CONTAINER NO. :", "containerNo"],
  ["TIME OF TRUCK IN :", "timeIn"],
  ["TIME OF TRUCK OUT :", "timeOut"],
];

type Form = {
  customer: string;
  jobNo: string; createDate: string; invoiceNo: string; vessel: string; eta: string;
  port: string; deliveryDate: string; blNo: string; truckNo: string;
  packages: string; grossWeight: string; remark: string;
  receiver: string; receiverAddress: string; agent: string; containerNo: string;
  timeIn: string; timeOut: string;
  /** Which of the two boxes is ticked, and what is written beside it. */
  haulage: "หัวลาก" | "รถบรรทุก";
  haulageNote: string;
  plate: string;
  labour: "มี" | "ไม่มี";
  officer: string;
};

const ITEM_ROWS = 7;

const BLANK: Form = {
  customer: "", jobNo: "", createDate: "", invoiceNo: "", vessel: "", eta: "",
  port: "", deliveryDate: "", blNo: "", truckNo: "", packages: "", grossWeight: "",
  remark: "", receiver: "", receiverAddress: "", agent: "", containerNo: "",
  timeIn: "", timeOut: "", haulage: "หัวลาก", haulageNote: "", plate: "",
  labour: "มี", officer: "",
};

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CONTROL = "height:30px;padding:0 9px;border:1px solid #D3DBE3;border-radius:4px;font-size:12.5px;font-family:inherit;background:#fff";
const FIELD = "width:100%;border:none;border-bottom:1px solid #CBD5E1;background:transparent;font-size:11.5px;font-family:inherit;padding:2px 3px;outline:none";
const CELL = "border:1px solid #94A3B8;padding:0";
const ITEM_INPUT = "width:100%;border:none;background:transparent;font-size:11px;font-family:inherit;padding:4px 5px;outline:none";

export function CargoForm({ onToast }: { onToast: (message: string) => void }) {
  const [form, setForm] = useState<Form>(BLANK);
  /**
   * The customers who have a form, and what each of their forms asks for.
   *
   * Read from the folder of form files rather than from the register. Those two
   * lists are not the same thing and should not be pretended to be: the
   * register holds every customer with a job, and this holds the ones a cargo
   * receipt has been drawn up for. Offering the first as if it were the second
   * put names in the picker that have no form behind them.
   */
  const [templates, setTemplates] = useState<FormTemplate[]>([]);
  const [reading, setReading] = useState(false);
  const [columns, setColumns] = useState<string[]>(COLUMN_SETS[0].columns);
  const [items, setItems] = useState<string[][]>(
    Array.from({ length: ITEM_ROWS }, () => COLUMN_SETS[0].columns.map(() => "")),
  );

  const set = (field: keyof Form, value: string) => setForm((held) => ({ ...held, [field]: value }));

  async function loadFolder(files: FileList) {
    setReading(true);
    const read: FormTemplate[] = [];
    let skipped = 0;
    for (const file of Array.from(files)) {
      try {
        const template = await readTemplate(file);
        if (template) read.push(template); else skipped++;
      } catch { skipped++; }
    }
    setReading(false);

    if (!read.length) { onToast("อ่านฟอร์มไม่ได้สักไฟล์ — เลือกไฟล์ .xls ในโฟลเดอร์ Cargo"); return; }

    // Two files for one customer is a working copy beside the real one. The
    // file without "Copy" in its name is the one to keep.
    const byCustomer = new Map<string, FormTemplate>();
    for (const template of read) {
      const held = byCustomer.get(template.customer);
      if (!held || /copy/i.test(held.file)) byCustomer.set(template.customer, template);
    }
    const kept = [...byCustomer.values()].sort((a, b) => a.customer.localeCompare(b.customer));
    setTemplates(kept);
    onToast(`อ่านฟอร์มลูกค้าแล้ว ${kept.length} ราย${skipped ? ` · ข้าม ${skipped} ไฟล์ที่ไม่ใช่ฟอร์มลูกค้า` : ""}`);
  }

  /** Picking a customer brings that customer's own item columns with it. */
  function chooseCustomer(customer: string) {
    set("customer", customer);
    const template = templates.find((entry) => entry.customer === customer);
    if (!template) return;
    setColumns(template.columns);
    setItems((held) => held.map((row) => template.columns.map((_, i) => row[i] ?? "")));
  }

  function applyColumnSet(name: string) {
    const chosen = COLUMN_SETS.find((entry) => entry.name === name);
    if (!chosen) return;
    setColumns(chosen.columns);
    // The rows keep what they hold, padded or trimmed to the new width, so
    // switching heading sets by accident does not empty a filled-in form.
    setItems((held) => held.map((row) => chosen.columns.map((_, i) => row[i] ?? "")));
  }

  function setColumn(index: number, value: string) {
    setColumns((held) => held.map((label, i) => (i === index ? value : label)));
  }

  function setItem(row: number, column: number, value: string) {
    setItems((held) => held.map((line, r) => (r === row ? line.map((v, c) => (c === column ? value : v)) : line)));
  }

  /**
   * Which columns get a total under them.
   *
   * The form itself totals two: the package count and the net weight, both
   * showing 0 in the blank copy. So this is not one column but every column
   * that counts something, found by its heading.
   *
   * Matching on "kg" alone does not work, and finding that out is why this is
   * written down: the standard heading is "No. of P'kg (s)", which contains kg
   * and is a count of packages, not a weight. A total under the wrong heading
   * on a document somebody signs is a claim about the shipment.
   */
  const COUNTABLE = /weight|\bqty\b|\bea\b|p'?kg|package/i;
  const totals = columns.map((label, index) => {
    if (!COUNTABLE.test(label)) return null;
    const sum = items.reduce((held, row) => {
      const value = Number((row[index] ?? "").replace(/,/g, ""));
      return held + (Number.isFinite(value) ? value : 0);
    }, 0);
    return sum;
  });

  function exportSheet() {
    if (!form.customer.trim()) { onToast("เลือกชื่อลูกค้าก่อน"); return; }

    // Laid out cell for cell like the workbook it came from: labels down B with
    // their values in C, the second block of labels in F with values in G, the
    // item table from row 18. Somebody comparing this against last month's file
    // should find the same things in the same places.
    const aoa: string[][] = Array.from({ length: 46 }, () => Array(8).fill(""));
    const put = (row: number, col: number, value: string) => { aoa[row - 1][col] = value; };

    put(1, 1, "CARGO RECEIPT  ใบรับ-ส่งสินค้า");
    LEFT.forEach(([label, field], i) => {
      put(2 + i, 1, label);
      put(2 + i, 2, String(form[field] ?? ""));
    });
    RIGHT.forEach(([label, field], i) => {
      put(2 + i, 5, label);
      put(2 + i, 6, String(form[field] ?? ""));
    });
    put(14, 1, "REMARK :");
    put(14, 2, form.remark);
    put(16, 0, "ITEM :");

    put(18, 0, "NO");
    columns.forEach((label, i) => put(18, 1 + i, label));
    items.forEach((row, r) => {
      if (!row.some((value) => value.trim())) return;
      put(19 + r, 0, String(r + 1));
      row.forEach((value, c) => put(19 + r, 1 + c, value));
    });
    put(26, 1, "TOTAL");
    totals.forEach((sum, index) => {
      if (sum === null) return;
      put(26, 1 + index, sum ? sum.toLocaleString("en-US") : "0");
    });

    put(31, 1, (form.haulage === "หัวลาก" ? "[X] " : "[  ] ") + "หัวลาก");
    put(31, 2, form.haulageNote);
    put(32, 1, (form.haulage === "รถบรรทุก" ? "[X] " : "[  ] ") + "รถบรรทุก");
    put(32, 5, "ทะเบียนเลขที่ : " + form.plate);
    put(33, 1, (form.labour === "มี" ? "[X] " : "[  ] ") + "มีพนักงานยกสินค้า");
    put(34, 1, (form.labour === "ไม่มี" ? "[X] " : "[  ] ") + "ไม่มีพนักงานยกสินค้า");

    TERMS.forEach((line, i) => put(36 + i, 0, line));
    put(43, 1, form.officer);
    put(44, 1, "ลายมือชื่อของพนักงานเลสชาโก้");
    put(44, 3, "ลายมือชื่อผู้รับบรรทุก");
    put(44, 6, "ลายมือชื่อลูกค้า");
    put(45, 1, "LESCHACO OFFICER");
    put(45, 3, "TRUCK DRIVER");
    put(45, 6, "CUSTOMER");

    const sheet = XLSX.utils.aoa_to_sheet(aoa);
    sheet["!cols"] = [{ wch: 6 }, { wch: 22 }, { wch: 20 }, { wch: 26 }, { wch: 10 }, { wch: 20 }, { wch: 18 }, { wch: 14 }];
    const book = XLSX.utils.book_new();
    XLSX.utils.book_append_sheet(book, sheet, "ISO-FRM-TH-CCL-04-01");
    const safe = form.customer.replace(/[\\/:*?"<>|]/g, "-").trim();
    XLSX.writeFile(book, `Cargo_Receipt_${safe}${form.jobNo ? "_" + form.jobNo.replace(/[\\/:*?"<>|]/g, "-") : ""}.xlsx`);
    onToast(`ส่งออกใบรับ-ส่งสินค้าของ ${form.customer} แล้ว`);
  }

  function print() {
    if (!form.customer.trim()) { onToast("เลือกชื่อลูกค้าก่อน"); return; }
    // The browser's own print dialog, where "Save as PDF" is the destination.
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
            onChange={(e) => { const chosen = e.target.files; if (chosen?.length) void loadFolder(chosen); e.target.value = ""; }}
            style={css("font-size:12px;font-family:inherit;max-width:230px")}
          />
        </label>

        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ชื่อลูกค้า</span>
          <select
            value={form.customer}
            disabled={!templates.length}
            onChange={(e) => chooseCustomer(e.target.value)}
            style={css(CONTROL + ";min-width:250px" + (templates.length ? "" : ";opacity:.5"))}
          >
            <option value="">
              {reading ? "กำลังอ่านฟอร์ม…" : templates.length ? "เลือกลูกค้า" : "เลือกโฟลเดอร์ฟอร์มก่อน"}
            </option>
            {templates.map((entry) => (
              <option key={entry.customer} value={entry.customer}>{entry.customer}</option>
            ))}
          </select>
        </label>

        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ชุดคอลัมน์รายการสินค้า</span>
          <select
            defaultValue={COLUMN_SETS[0].name}
            onChange={(e) => applyColumnSet(e.target.value)}
            style={css(CONTROL)}
          >
            {COLUMN_SETS.map((entry) => <option key={entry.name} value={entry.name}>{entry.name}</option>)}
          </select>
        </label>

        <div style={css("display:flex;gap:8px;margin-left:auto")}>
          <button
            onClick={() => { setForm(BLANK); setItems(Array.from({ length: ITEM_ROWS }, () => columns.map(() => ""))); }}
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

      <div className="cargo-page" style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:22px 24px;display:flex;flex-direction:column;gap:14px")}>
        {/*
          The letterhead, laid out as ISO-FRM-TH-CCL-04-01 lays it out: the mark
          on the left and the title across the middle, and nothing else.

          The company name, address and phone were here for a while, lifted from
          the block on ISO-FRM-TH-ADM-26-06. They are gone again: that is a
          different document, and this form does not carry them. A receipt that
          prints more letterhead than the paper one is not the same document,
          and this one gets signed.
        */}
        <div style={css("display:flex;align-items:center;gap:18px;padding-bottom:4px")}>
          {/* A plain img on purpose: next/image lazy-loads behind a wrapper,
              and a letterhead that has not loaded yet prints as a blank box.
              It is a 20 KB file served from this same origin. */}
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/cargo-logo.png"
            alt="LESCHACO"
            width={198}
            height={39}
            style={css("flex:none;object-fit:contain")}
          />
        </div>

        <div style={css("text-align:center;font-size:16px;font-weight:700;color:#0A2240;letter-spacing:.02em")}>
          CARGO RECEIPT &nbsp;ใบรับ-ส่งสินค้า
        </div>

        <div style={css("display:grid;grid-template-columns:1fr 1fr;gap:4px 26px")}>
          <div style={css("display:flex;flex-direction:column;gap:3px")}>
            {LEFT.map(([label, field]) => (
              <div key={label} style={css("display:flex;align-items:center;gap:8px")}>
                <span style={css("flex:0 0 140px;font-size:11px;color:#465A6E;font-weight:600")}>{label}</span>
                <input value={String(form[field] ?? "")} onChange={(e) => set(field, e.target.value)} style={css(FIELD)} />
              </div>
            ))}
          </div>
          <div style={css("display:flex;flex-direction:column;gap:3px")}>
            {RIGHT.map(([label, field]) => (
              <div key={label} style={css("display:flex;align-items:center;gap:8px")}>
                <span style={css("flex:0 0 148px;font-size:11px;color:#465A6E;font-weight:600")}>{label}</span>
                <input value={String(form[field] ?? "")} onChange={(e) => set(field, e.target.value)} style={css(FIELD)} />
              </div>
            ))}
          </div>
        </div>

        <div style={css("display:flex;align-items:flex-start;gap:8px")}>
          <span style={css("flex:0 0 140px;font-size:11px;color:#465A6E;font-weight:600;padding-top:3px")}>REMARK :</span>
          <textarea
            value={form.remark}
            onChange={(e) => set("remark", e.target.value)}
            rows={2}
            style={css(FIELD + ";border:1px solid #CBD5E1;border-radius:3px;line-height:1.5")}
          />
        </div>

        <div>
          <div style={css("font-size:11px;color:#465A6E;font-weight:700;margin-bottom:5px")}>ITEM :</div>
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse")}>
              <thead>
                <tr>
                  <th style={css(CELL + ";background:#F4F7FA;width:38px;font-size:10.5px;color:#465A6E;padding:4px")}>NO</th>
                  {columns.map((label, index) => (
                    <th key={index} style={css(CELL + ";background:#F4F7FA")}>
                      {/* The heading is a field. A third of these customers do
                          not use the standard set, and typing over it is how
                          the operators produced their own copies in the first
                          place. */}
                      <input
                        value={label}
                        onChange={(e) => setColumn(index, e.target.value)}
                        style={css(ITEM_INPUT + ";font-weight:700;color:#465A6E;text-align:center;font-size:10.5px")}
                      />
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {items.map((row, r) => (
                  <tr key={r}>
                    <td style={css(CELL + ";text-align:center;font-size:11px;color:#64748B;padding:4px")}>{r + 1}</td>
                    {row.map((value, c) => (
                      <td key={c} style={css(CELL)}>
                        <input value={value} onChange={(e) => setItem(r, c, e.target.value)} style={css(ITEM_INPUT)} />
                      </td>
                    ))}
                  </tr>
                ))}
                <tr>
                  <td style={css(CELL + ";padding:4px")} />
                  <td style={css(CELL + ";font-size:11px;font-weight:700;color:#465A6E;padding:4px 5px")}>TOTAL</td>
                  {columns.slice(1).map((_, index) => {
                    const sum = totals[index + 1];
                    return (
                      <td key={index} style={css(CELL + ";text-align:right;font-size:11px;font-weight:700;padding:4px 5px;font-family:'IBM Plex Mono',monospace")}>
                        {sum === null ? "" : sum ? sum.toLocaleString("en-US") : "0"}
                      </td>
                    );
                  })}
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <div style={css("display:flex;gap:30px;flex-wrap:wrap;font-size:11.5px;color:#16232F")}>
          <div style={css("display:flex;flex-direction:column;gap:5px")}>
            {(["หัวลาก", "รถบรรทุก"] as const).map((kind) => (
              <label key={kind} style={css("display:flex;align-items:center;gap:6px;cursor:pointer")}>
                <input type="radio" checked={form.haulage === kind} onChange={() => set("haulage", kind)} />
                {kind}
              </label>
            ))}
          </div>
          <label style={css("display:flex;align-items:center;gap:8px")}>
            <span style={css("font-size:11px;color:#465A6E;font-weight:600")}>ประเภทตู้ :</span>
            <input value={form.haulageNote} onChange={(e) => set("haulageNote", e.target.value)}
              placeholder="1X20 DC" style={css(FIELD + ";width:130px")} />
          </label>
          <label style={css("display:flex;align-items:center;gap:8px")}>
            <span style={css("font-size:11px;color:#465A6E;font-weight:600")}>ทะเบียนเลขที่ :</span>
            <input value={form.plate} onChange={(e) => set("plate", e.target.value)} style={css(FIELD + ";width:150px")} />
          </label>
          <div style={css("display:flex;flex-direction:column;gap:5px")}>
            {(["มี", "ไม่มี"] as const).map((has) => (
              <label key={has} style={css("display:flex;align-items:center;gap:6px;cursor:pointer")}>
                <input type="radio" checked={form.labour === has} onChange={() => set("labour", has)} />
                {has}พนักงานยกสินค้า
              </label>
            ))}
          </div>
        </div>

        <div style={css("font-size:10px;color:#64748B;line-height:1.75;border-top:1px solid #E3E8EE;padding-top:10px")}>
          {TERMS.map((line) => <div key={line}>{line}</div>)}
        </div>

        <div style={css("display:grid;grid-template-columns:repeat(3,1fr);gap:24px;padding-top:18px")}>
          {[
            ["ลายมือชื่อของพนักงานเลสชาโก้", "LESCHACO OFFICER", true],
            ["ลายมือชื่อผู้รับบรรทุก", "TRUCK DRIVER", false],
            ["ลายมือชื่อลูกค้า", "CUSTOMER", false],
          ].map(([thai, english, named]) => (
            <div key={String(english)} style={css("display:flex;flex-direction:column;gap:3px;align-items:center")}>
              {named ? (
                <input value={form.officer} onChange={(e) => set("officer", e.target.value)}
                  placeholder="ชื่อพนักงาน" style={css(FIELD + ";text-align:center")} />
              ) : (
                <div style={css("width:100%;border-bottom:1px solid #94A3B8;height:22px")} />
              )}
              <span style={css("font-size:10.5px;color:#465A6E")}>{thai}</span>
              <span style={css("font-size:10px;color:#7B8CA0;font-weight:600")}>{english}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
