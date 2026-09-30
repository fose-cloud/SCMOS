"use client";

import { useMemo, useState } from "react";
import { css } from "../theme";
import {
  INVOICE_LINES, TRANSPORT_CHARGE, WITHHOLDING_PERCENT, addDays, invoiceTotals, lineAmount, longDate, money, rateReason,
} from "../invoiceLines";
import type { BillingCase, BillingIssuer } from "./BillingControl";

/** What the form saves: the draft's fields and every line, as PUT /api/carrier-billing/invoices/{id} takes them. */
export type InvoiceDraftBody = {
  invoiceNumber: string; invoiceDate: string; currency: string; subtotal: number; taxAmount: number;
  creditTermDays: number; poNumber: string; jobNo: string; paymentNote: string; preparedBy: string;
  lines: { code: string; quantity: number; unitPrice: number; description: string; detail: string }[];
};

type Line = { code: string; quantity: string; unitPrice: string; description: string; detail: string };

const today = () => {
  const now = new Date(Date.now() + 7 * 3600_000);
  return now.toISOString().slice(0, 10);
};
const numberOf = (text: string) => {
  const value = Number(String(text).replace(/,/g, "").trim());
  return Number.isFinite(value) && value >= 0 ? value : 0;
};

/**
 * The carrier's invoice for one job (30 Sep 2026), laid out as the department lead's sample ใบแจ้งหนี้: the
 * carrier's letterhead from the Supplier Register, Leschaco as the party billed, the job as its trucking
 * order, 1.1 the transportation charge filled from the carrier's Rate (and editable — a figure other than the
 * Rate is flagged to the department at review, not refused), 1.2–1.4 and the reimbursements, the totals, 1%
 * withheld on the transportation charge, and the net. Printed from the browser, it is the paper itself.
 * The server works every amount out again on save (Rules/InvoiceLines.cs); this only shows them as typed.
 */
export function CarrierInvoice({ item, issuer, editable, busy, onSave, onClose }: {
  item: BillingCase; issuer: BillingIssuer | null; editable: boolean; busy: boolean;
  onSave: (invoiceId: number, body: InvoiceDraftBody) => Promise<boolean>;
  onClose: () => void;
}) {
  const invoice = item.invoice!;
  const job = item.job ?? null;
  const rate = item.contractRate ?? null;
  const [number, setNumber] = useState(invoice.invoiceNumber || "");
  const [date, setDate] = useState(invoice.invoiceDate || today());
  const [term, setTerm] = useState(String(invoice.creditTermDays ?? issuer?.creditTermDays ?? 30));
  const [po, setPo] = useState(invoice.poNumber || job?.customerPo || "");
  const [jobNo, setJobNo] = useState(invoice.jobNo || job?.jobNo || "");
  const [note, setNote] = useState(invoice.paymentNote || "");
  const [preparedBy, setPreparedBy] = useState(invoice.preparedBy || "");
  const [lines, setLines] = useState<Line[]>(() => INVOICE_LINES.map((kind) => {
    const saved = invoice.lines?.find((line) => line.code === kind.code);
    if (saved) return { code: kind.code, quantity: String(saved.quantity), unitPrice: String(saved.unitPrice), description: saved.description, detail: saved.detail };
    const transport = kind.code === TRANSPORT_CHARGE;
    return {
      code: kind.code, quantity: "1",
      unitPrice: transport && rate?.amount != null ? String(rate.amount) : "0",
      description: transport ? job?.route ?? "" : "",
      detail: transport ? [job?.containerType, job?.container, job?.licence].filter(Boolean).join(" ") : "",
    };
  }));

  const typed = useMemo(() => lines.map((line) => ({ code: line.code, quantity: numberOf(line.quantity), unitPrice: numberOf(line.unitPrice) })), [lines]);
  const totals = invoiceTotals(typed);
  const due = addDays(date, numberOf(term));
  const transport = typed.find((line) => line.code === TRANSPORT_CHARGE);
  const offRate = rate?.amount != null && transport != null && transport.unitPrice !== rate.amount;

  const set = (code: string, field: keyof Line, value: string) =>
    setLines((was) => was.map((line) => (line.code === code ? { ...line, [field]: value } : line)));
  const line = (code: string) => lines.find((one) => one.code === code)!;

  async function save() {
    await onSave(invoice.id, {
      invoiceNumber: number.trim(), invoiceDate: date, currency: invoice.currency || "THB",
      subtotal: totals.total, taxAmount: invoice.taxAmount ?? 0,
      creditTermDays: Math.trunc(numberOf(term)), poNumber: po.trim(), jobNo: jobNo.trim(),
      paymentNote: note.trim(), preparedBy: preparedBy.trim(),
      lines: lines.map((one) => ({ code: one.code, quantity: numberOf(one.quantity), unitPrice: numberOf(one.unitPrice),
        description: one.description.trim(), detail: one.detail.trim() })),
    });
  }

  const field = (value: string, onChange: (value: string) => void, style = "", placeholder = "") => editable
    ? <input value={value} placeholder={placeholder} onChange={(event) => onChange(event.target.value)} style={css(INPUT + style)} />
    : <span style={css(style)}>{value}</span>;
  const amountRow = (code: string, label: string, indent = false) => {
    const one = line(code);
    const amount = lineAmount(numberOf(one.quantity), numberOf(one.unitPrice));
    return (
      <tr key={code}>
        <td style={css(CELL)} />
        <td style={css(CELL + "text-align:center")}>{editable ? field(one.quantity, (value) => set(code, "quantity", value), "width:46px;text-align:center") : amount > 0 ? one.quantity : ""}</td>
        <td style={css(CELL + (indent ? "padding-left:34px" : ""))}>{label}</td>
        <td style={css(CELL + "text-align:right")}>{editable ? field(one.unitPrice, (value) => set(code, "unitPrice", value), "width:96px;text-align:right") : amount > 0 ? money(numberOf(one.unitPrice)) : ""}</td>
        <td style={css(CELL + "text-align:right")}>{money(amount)}</td>
      </tr>
    );
  };
  const transportLine = line(TRANSPORT_CHARGE);

  return (
    <div style={css("position:fixed;inset:0;z-index:60;background:rgba(15,23,42,.55);overflow:auto;padding:24px 12px")} role="dialog" aria-label="ใบแจ้งหนี้">
      <style>{PRINT}</style>
      <div className="scmos-invoice" style={css("max-width:860px;margin:0 auto")}>
        <div className="no-print" style={css("display:flex;gap:8px;justify-content:flex-end;flex-wrap:wrap;margin-bottom:10px")}>
          {editable && <button type="button" disabled={busy} onClick={() => void save()} style={css(BUTTON + "background:#16794C;color:#fff;border-color:#16794C")}>บันทึก Draft</button>}
          <button type="button" onClick={() => window.print()} style={css(BUTTON + "background:#fff;color:#0A5C97;border-color:#0A5C97")}>พิมพ์ / PDF</button>
          <button type="button" onClick={onClose} style={css(BUTTON + "background:#fff;color:#475569;border-color:#CBD5E1")}>ปิด</button>
        </div>

        <div style={css("background:#fff;color:#111827;padding:28px 30px;font-size:12px;line-height:1.45;font-family:'IBM Plex Sans Thai','IBM Plex Sans',sans-serif")}>
          {/* ------------------------------------------------ the carrier's letterhead */}
          <div style={css("display:flex;justify-content:space-between;gap:16px;border-bottom:2px solid #111827;padding-bottom:8px")}>
            <div>
              <div style={css("font-size:14px;font-weight:700")}>{issuer?.name || item.supplier}</div>
              <div>{issuer?.address || ""}</div>
              <div>{[issuer?.telephone && `Tel. ${issuer.telephone}`, issuer?.fax && `Fax : ${issuer.fax}`, issuer?.email && `Email : ${issuer.email}`].filter(Boolean).join("   ")}</div>
            </div>
            <div style={css("text-align:right;white-space:nowrap")}>
              <div style={css("font-weight:700")}>TAX ID No. {issuer?.taxId || ""}</div>
              <div>ต้นฉบับ (ORIGINAL)</div>
            </div>
          </div>
          <div style={css("text-align:center;font-size:15px;font-weight:700;margin:10px 0 6px")}>ใบแจ้งหนี้ (INVOICE)</div>
          <div style={css("display:flex;justify-content:space-between;gap:12px;flex-wrap:wrap;margin-bottom:6px")}>
            <div>วันที่/ INVOICE DATE : {editable
              ? <input type="date" value={date} onChange={(event) => setDate(event.target.value)} style={css(INPUT + "width:150px")} />
              : longDate(date)}</div>
            <div>เลขที่ / INVOICE NO. {field(number, setNumber, "width:170px", "เช่น LES69-09-319")}</div>
          </div>

          {/* ------------------------------------------------ who is billed */}
          <table style={css(TABLE)}>
            <tbody>
              <tr>
                <td style={css(BOX + "width:70px")}>NAME :</td><td style={css(BOX)}>{issuer?.billTo.name}</td>
                <td style={css(BOX + "width:90px")}>JOB NO.</td><td style={css(BOX + "width:150px")}>{field(jobNo, setJobNo, "width:130px")}</td>
              </tr>
              <tr>
                <td style={css(BOX)}>ADDRESS :</td><td style={css(BOX)}>{issuer?.billTo.address}</td>
                <td style={css(BOX)}>CREDIT TERM</td><td style={css(BOX)}>{field(term, setTerm, "width:46px;text-align:center")} Days</td>
              </tr>
              <tr>
                <td style={css(BOX)}>TAX ID :</td><td style={css(BOX)}>{issuer?.billTo.taxId} <span style={css("margin-left:24px;font-size:10.5px")}>P/O NO.</span> {field(po, setPo, "width:150px")}</td>
                <td style={css(BOX)}>DUE DATE</td><td style={css(BOX)}>{longDate(due)}</td>
              </tr>
            </tbody>
          </table>

          {/* ------------------------------------------------ the lines */}
          <table style={css(TABLE + "margin-top:-1px")}>
            <thead>
              <tr>
                {[["ลำดับที่", "Item", "52px"], ["จำนวน", "Quantity", "64px"], ["รายการ", "Description", ""], ["ราคา/หน่วย", "Unit Price", "118px"], ["จำนวนเงิน", "Amount", "118px"]]
                  .map(([th, en, width]) => <th key={en} style={css(HEAD + (width ? `width:${width}` : ""))}>{th}<div style={css("font-weight:400;font-size:10px")}>{en}</div></th>)}
              </tr>
            </thead>
            <tbody>
              <tr>
                <td style={css(CELL + "text-align:center")}>1</td>
                <td style={css(CELL + "text-align:center")}>{editable ? field(transportLine.quantity, (value) => set(TRANSPORT_CHARGE, "quantity", value), "width:46px;text-align:center") : transportLine.quantity}</td>
                <td style={css(CELL)}>
                  <div><b>1.1</b>&nbsp; TRANSPORTATION CHARGE</div>
                  <div style={css("display:grid;grid-template-columns:130px 1fr;gap:2px 10px;margin:4px 0 4px 26px")}>
                    <span>JOB DATE</span><span>{longDate(job?.jobDate)}</span>
                    <span>JOB TYPE</span><span>{job?.jobType || item.category}</span>
                    <span>TRUCKING ORDER</span><span>{job?.truckingOrder || item.jobCode}</span>
                  </div>
                  <div style={css("margin-left:26px")}>{field(transportLine.description, (value) => set(TRANSPORT_CHARGE, "description", value), "width:100%", "เส้นทาง")}</div>
                  <div style={css("margin:6px 0 0 26px")}>CONTAINER NO.</div>
                  <div style={css("margin-left:26px")}>{field(transportLine.detail, (value) => set(TRANSPORT_CHARGE, "detail", value), "width:100%", "ขนาดตู้ เลขตู้ ทะเบียนรถ")}</div>
                  <div className="no-print" style={css("margin:6px 0 0 26px;font-size:11px;color:" + (rate?.amount == null ? "#B45309" : offRate ? "#B42318" : "#16794C"))}>
                    {rate?.amount != null
                      ? <>ราคาตาม Rate {money(rate.amount)} · {rate.vehicle} · {rate.lane} · น้ำมัน {rate.diesel} ({rate.dieselFrom === "job" ? "ตามงาน" : `เฉลี่ย ${rate.dieselMonth}${rate.dieselClosed ? "" : " ยังไม่สิ้นเดือน"}`}) · {rate.band}
                        {offRate && <> · ไม่ตรงกับ Rate {editable && <button type="button" onClick={() => set(TRANSPORT_CHARGE, "unitPrice", String(rate.amount))}
                          style={css("margin-left:6px;border:1px solid #0A5C97;background:#fff;color:#0A5C97;border-radius:3px;font:inherit;font-size:10.5px;padding:1px 6px;cursor:pointer")}>ใช้ราคา Rate</button>}</>}</>
                      : rateReason(rate) || "ยังไม่มีราคาจาก Rate"}
                  </div>
                </td>
                <td style={css(CELL + "text-align:right;vertical-align:top")}>{editable ? field(transportLine.unitPrice, (value) => set(TRANSPORT_CHARGE, "unitPrice", value), "width:96px;text-align:right") : money(numberOf(transportLine.unitPrice))}</td>
                <td style={css(CELL + "text-align:right;vertical-align:top")}>{money(lineAmount(numberOf(transportLine.quantity), numberOf(transportLine.unitPrice)))}</td>
              </tr>
              {INVOICE_LINES.filter((kind) => kind.section === "TRANSPORT" && kind.code !== TRANSPORT_CHARGE)
                .map((kind) => amountRow(kind.code, `${kind.number} ${kind.thai} (${kind.english})`))}
              <tr><td colSpan={4} style={css(TOTAL)}>TOTAL TRANSPORTATION CHARGE</td><td style={css(TOTAL + "text-align:right")}>{money(totals.transport)}</td></tr>
              <tr>
                <td style={css(CELL + "text-align:center")}>2</td><td style={css(CELL)} />
                <td style={css(CELL)} colSpan={3}>REIMBURSEMENT FROM THE RESERVE PAID</td>
              </tr>
              {INVOICE_LINES.filter((kind) => kind.section === "REIMBURSEMENT").map((kind) => amountRow(kind.code, kind.english, true))}
              <tr><td colSpan={4} style={css(TOTAL)}>TOTAL REIMBURSEMENT FROM THE RESERVE PAID</td><td style={css(TOTAL + "text-align:right")}>{money(totals.reimbursement)}</td></tr>
            </tbody>
          </table>

          {/* ------------------------------------------------ the sums */}
          <table style={css(TABLE + "margin-top:-1px")}>
            <tbody>
              <tr>
                <td rowSpan={3} style={css(BOX + "vertical-align:top;font-size:11px")}>
                  {editable
                    ? <textarea value={note} onChange={(event) => setNote(event.target.value)} rows={3} placeholder="การชำระเงิน เช่น โปรดสั่งจ่ายเช็คขีดคร่อม… หรือโอนเข้าบัญชี…"
                      style={css(INPUT + "width:100%;resize:vertical;font-size:11px")} />
                    : <span style={css("white-space:pre-wrap")}>{note}</span>}
                </td>
                <td style={css(BOX + "text-align:right;width:260px")}>รวมเงินทั้งหมด (TOTAL AMOUNT)</td>
                <td style={css(BOX + "text-align:right;width:118px;font-weight:700")}>{money(totals.total)}</td>
              </tr>
              <tr>
                <td style={css(BOX + "text-align:right")}>หักภาษี ณ ที่จ่าย (WITHHOLDING TAX) {WITHHOLDING_PERCENT} %</td>
                <td style={css(BOX + "text-align:right")}>{totals.withholding > 0 ? `-${money(totals.withholding)}` : money(0)}</td>
              </tr>
              <tr>
                <td style={css(BOX + "text-align:right;font-weight:700")}>รับเงินสุทธิ (NET AMOUNT)</td>
                <td style={css(BOX + "text-align:right;font-weight:700")}>{money(totals.net)}</td>
              </tr>
            </tbody>
          </table>

          {/* ------------------------------------------------ who signs */}
          <table style={css(TABLE + "margin-top:-1px")}>
            <tbody>
              <tr>
                <td style={css(SIGN)}><div style={css("font-weight:600")}>{issuer?.name || item.supplier}</div><div style={css("height:44px")} /><div>AUTHORIZED SIGNATURE</div></td>
                <td style={css(SIGN)}><div>PREPARED BY</div><div style={css("height:14px")} />{field(preparedBy, setPreparedBy, "width:180px;text-align:center", "ชื่อผู้จัดทำ")}<div>{longDate(date)}</div></td>
                <td style={css(SIGN)}><div>RECEIVED BY</div><div style={css("height:44px")} /><div>PAYEE CO.REP.</div><div style={css("font-size:10.5px;text-align:left")}>RECEIVED DATE :</div></td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

const INPUT = "border:1px solid #CBD5E1;border-radius:3px;padding:2px 5px;font:inherit;font-size:12px;background:#FFFDF5;";
const BUTTON = "height:32px;padding:0 14px;border:1px solid;border-radius:4px;font:inherit;font-size:12.5px;font-weight:650;cursor:pointer;";
const TABLE = "width:100%;border-collapse:collapse;";
const BOX = "border:1px solid #111827;padding:6px 8px;vertical-align:middle;";
const HEAD = "border:1px solid #111827;padding:5px 6px;font-size:11px;font-weight:600;text-align:center;background:#F3F4F6;";
const CELL = "border-left:1px solid #111827;border-right:1px solid #111827;padding:3px 8px;vertical-align:top;";
const TOTAL = "border:1px solid #111827;padding:5px 8px;font-weight:600;";
const SIGN = "border:1px solid #111827;padding:10px;text-align:center;vertical-align:top;width:33%;";

/** Printing shows the paper alone, its fields as plain text. */
const PRINT = `@media print {
  @page { size: A4; margin: 10mm; }
  body * { visibility: hidden !important; }
  .scmos-invoice, .scmos-invoice * { visibility: visible !important; }
  .scmos-invoice { position: absolute; left: 0; top: 0; width: 100%; max-width: none !important; }
  .scmos-invoice .no-print { display: none !important; }
  .scmos-invoice input, .scmos-invoice textarea { border: none !important; background: transparent !important; padding: 0 !important; }
  .scmos-invoice input::placeholder, .scmos-invoice textarea::placeholder { color: transparent !important; }
}`;
