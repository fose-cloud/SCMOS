"use client";

import { useState } from "react";
import { exportQuoteTerms } from "../excel";
import { QUOTE_TERMS, chargeText, type Charge, type TermBlock } from "../quoteTerms";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

/**
 * The conditions that go with a quotation, on screen.
 *
 * A reading screen, not a working one. The rate sheet next door is where
 * numbers are typed; this is the page somebody opens while they are on the
 * phone being asked what happens if the truck waits four hours — which today
 * means finding the workbook, finding the Remarks sheet, and reading a block
 * with no headings.
 *
 * Both halves are shown at once rather than behind a picker. LCL and FCL differ
 * in ways that are easy to get the wrong way round — free time is three hours
 * on both but the hour after costs 400 on one and 500 on the other — and a
 * screen that shows one at a time is a screen where you answer from the block
 * you happen to have open. Side by side, the two are checkable against each
 * other; the search box narrows both at once for the same reason.
 */

export function QuoteTerms({ onToast }: { onToast: (message: string) => void }) {
  const [find, setFind] = useState("");
  const wanted = find.trim().toLowerCase();

  const matches = (charge: Charge) =>
    !wanted
    || charge.what.toLowerCase().includes(wanted)
    || charge.per.toLowerCase().includes(wanted)
    || chargeText(charge).toLowerCase().includes(wanted);

  const blocks = QUOTE_TERMS.map((block) => ({
    block,
    charges: block.charges.filter(matches),
  }));
  const found = blocks.reduce((n, one) => n + one.charges.length, 0);
  const total = QUOTE_TERMS.reduce((n, one) => n + one.charges.length, 0);

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 15px;display:flex;gap:10px;align-items:center;flex-wrap:wrap")}>
        <input
          value={find}
          onChange={(event) => setFind(event.target.value)}
          onKeyDown={(event) => { if (event.key === "Escape") setFind(""); }}
          placeholder="ค้นหาเงื่อนไข — X-ray, overnight, cancellation, ตรวจ อย., ค่าเสียเวลา…"
          style={css("height:31px;border:1px solid #C9D6E2;border-radius:4px;padding:0 11px;"
            + "font-size:12.5px;font-family:inherit;flex:1;min-width:260px")} />
        {wanted && (
          <button onClick={() => setFind("")}
            style={css("height:31px;padding:0 12px;border:1px solid #BBD5EE;background:#F4F8FC;color:#0A2240;"
              + "border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
            ล้าง
          </button>
        )}
        <span style={css("font-size:11.5px;color:" + (wanted && !found ? "#B42318" : "#94A3B8"))}>
          {wanted ? `พบ ${found} จาก ${total} เงื่อนไข` : `${total} เงื่อนไข · LCL และ FCL`}
        </span>
        {/*
          Always the whole schedule, never what the search left. The file this
          feeds is the one being corrected, and a Remarks sheet replaced with
          the five rows somebody had filtered to would be a worse file than the
          out-of-date one it replaced.
        */}
        <button onClick={() => onToast("บันทึกไฟล์ " + exportQuoteTerms() + " แล้ว")}
          title="ดาวน์โหลดทั้ง 29 เงื่อนไขในรูปแบบชีท Remarks เพื่อนำไปวางทับในไฟล์ Rate Inquiry.xlsx"
          style={css("height:31px;padding:0 13px;border:1px solid #0A2240;background:#0A2240;color:#fff;"
            + "border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit;white-space:nowrap")}>
          Export Excel
        </button>
      </div>

      {blocks.map(({ block, charges }) => (
        <Block key={block.key} block={block} charges={charges} narrowed={!!wanted} />
      ))}

      {/*
        Which copy is right, said on the screen and not only in the code.
        Somebody quoting from this page will also have the workbook open, and
        the two do not agree — so the page has to say which of them to believe
        rather than leaving them to guess from which one looks older.
      */}
      <div style={css("background:#F2F8F4;border:1px solid #C6E0CF;border-left:3px solid #16794C;border-radius:5px;"
        + "padding:11px 14px;font-size:11.5px;color:#2F4A3A;line-height:1.75")}>
        <b>หน้านี้คือฉบับจริง</b> — ยึดตามชุดที่ทีมยืนยันเมื่อ 3 ก.ย. 2026 ·
        ชีท <b>Remarks</b> ในไฟล์ Rate Inquiry.xlsx <b>เก่ากว่า และต้องแก้ให้ตรงกับหน้านี้</b>
        <div style={css("margin-top:6px;color:#5A6B60")}>
          ไฟล์ต่างอยู่ 5 จุด — น้ำหนักที่ต้องใช้หาง 3 เพลาในไฟล์เป็น 23 ตัน (ที่ถูกคือ 25 ตัน) ·
          รายชื่อท่าคืนตู้ในไฟล์ไม่มี Siam River · ค่าค้างคืนหัวลากในไฟล์เขียนเป็น 1 /NIGHT/TRIP
          (ที่ถูกคือ 100% ของค่าเที่ยว) · ไฟล์ไม่มี Cargo handling alongside vessel และ
          ค่าเดินเครื่องทำความเย็นฝั่ง FCL · ไฟล์ไม่มีบล็อก LCL เลยทั้งบล็อก
        </div>
        <div style={css("margin-top:6px")}>
          กด <b>Export Excel</b> ด้านบนเพื่อดาวน์โหลดทั้ง 29 เงื่อนไขในรูปแบบชีท Remarks
          แล้วนำไปวางทับในไฟล์ได้เลย — ไม่ต้องพิมพ์ใหม่
        </div>
      </div>
    </div>
  );
}

function Block({ block, charges, narrowed }: {
  block: TermBlock;
  charges: Charge[];
  narrowed: boolean;
}) {
  // A block the search emptied is left out entirely rather than shown as a
  // heading over nothing, which reads as "there are no FCL conditions".
  if (narrowed && charges.length === 0) return null;

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:12px;font-weight:700;color:#fff;background:#0A2240;border-radius:3px;padding:2px 9px;letter-spacing:.05em")}>
          {block.key}
        </span>
        <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{block.thai}</span>
        <span style={css("font-size:11.5px;color:#94A3B8")}>{block.heading}</span>
        <span style={css("margin-left:auto;font-size:11.5px;color:#94A3B8")}>
          {charges.length}{narrowed ? ` จาก ${block.charges.length}` : ""} รายการ
        </span>
      </div>

      {/*
        Not held to the fold. Two blocks stacked means the second measures the
        room below where it starts, which on this page is a few rows — a
        nineteen-line list boxed into five with a scrollbar of its own, inside a
        page that already scrolls. Both lists are short enough to read whole.
      */}
      <ZoomBox capped={false}>
        <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
          <thead>
            <tr>
              {[["เงื่อนไข", "left"], ["อัตรา", "right"], ["ต่อหน่วย", "left"]].map(([label, align]) => (
                <th key={label} style={css("background:#F8FAFC;padding:8px 14px;text-align:" + align
                  + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;"
                  + "font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>
                  {label}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {charges.map((charge) => (
              <tr key={charge.what} style={css("border-bottom:1px solid #F1F5F9")}>
                <td style={css("padding:8px 14px;color:#31465C;line-height:1.55")}>{charge.what}</td>
                {/*
                  The amount and what it is charged against, kept in separate
                  columns and never joined into one string. "80% of truck rate"
                  and "80 baht" differ by a word, and a column of right-aligned
                  figures is the arrangement where that difference is visible.
                */}
                <td style={css("padding:8px 14px;text-align:right;white-space:nowrap;font-weight:600;"
                  + "font-family:'IBM Plex Mono',ui-monospace,monospace;color:"
                  + (charge.basis === "free" ? "#16794C" : charge.basis === "percent" ? "#B45309" : "#0A2240"))}>
                  {chargeText(charge)}
                </td>
                <td style={css("padding:8px 14px;font-size:11.5px;color:#7B8CA0;white-space:nowrap")}>
                  {charge.per}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </ZoomBox>

      {block.notes.length > 0 && !narrowed && (
        <div style={css("padding:10px 16px;border-top:1px solid #E9EFF5;background:#FBFCFD;"
          + "font-size:11.5px;color:#64748B;line-height:1.8")}>
          {block.notes.map((note) => (
            <div key={note}>
              <span style={css("color:#B45309;font-weight:700")}>***</span> {note}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
