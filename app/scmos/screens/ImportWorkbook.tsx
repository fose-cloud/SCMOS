"use client";

import { useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import { readWorkbook, type ImportRead } from "../rateInquiryImport";

/**
 * The historical workbook, brought in.
 *
 * The team has kept every quote request in one file since August 2025, a sheet
 * per month, and the register was modelled on it and stood empty. This reads
 * the file the person picks and sends it in batches.
 *
 * What it will not do is pretend. Columns it cannot place, rows it had to date
 * from the sheet's name rather than from the row, and prices two columns
 * disagreed about are all counted and shown before anything is sent — so the
 * decision to import is made knowing what is imperfect about it.
 *
 * It sat on the Rate Inquiry tab, which is gone — the form for raising a new
 * inquiry was not being used. This is: the sheet it fills is the screen the
 * team works in, and after the workbook is corrected it has to be read again.
 * So it moved to the sheet rather than going with the tab, folded away behind
 * a button because it is used on the day a file changes and not otherwise.
 */
export function ImportWorkbook({ onToast, onDone }: { onToast: (m: string) => void; onDone: () => void }) {
  const [read, setRead] = useState<ImportRead | null>(null);
  const [file, setFile] = useState("");
  const [sending, setSending] = useState(false);
  const [done, setDone] = useState(0);

  async function pick(chosen: File) {
    try {
      const XLSX = await import("xlsx");
      const book = XLSX.read(await chosen.arrayBuffer(), { type: "array" });
      // Only the month sheets. The file also holds Remarks, a trucker list and
      // two customers' own rate cards, none of which are inquiries.
      const sheets = book.SheetNames
        .filter((name) => /\d{4}\s*$/.test(name))
        .map((name) => ({
          name,
          rows: XLSX.utils.sheet_to_json<unknown[]>(book.Sheets[name],
            { header: 1, raw: true, defval: null }),
        }));
      setFile(chosen.name);
      setRead(readWorkbook(sheets));
    } catch (error) {
      onToast("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    }
  }

  async function send() {
    if (!read) return;
    setSending(true);
    setDone(0);
    let added = 0;
    // The endpoint lists at most twenty refusals a batch so the reply stays
    // small, and says separately how many there really were. Counting the list
    // reported 272 of 1,454 — an answer that quietly contradicted its own
    // "added 1290 of 2744".
    let refusedCount = 0;
    const examples: string[] = [];
    try {
      // Two hundred at a time: one body for three thousand inquiries is several
      // megabytes, and a link that drops takes the lot with it.
      for (let at = 0; at < read.inquiries.length; at += 200) {
        const batch = read.inquiries.slice(at, at + 200).map((one) => ({
          inquiredOn: one.inquiredOn,
          customer: one.customer,
          fuelBand: one.fuelBand,
          lanes: one.lanes.map((lane) => ({
            fromPlace: lane.fromPlace, toPlace: lane.toPlace, county: lane.county,
            carriers: lane.carriers, fcl: lane.fcl, lcl: lane.lcl, remark: lane.remark,
            prices: lane.prices,
          })),
        }));
        const response = await apiFetch("/api/rate-inquiries/import", {
          method: "POST",
          headers: { "content-type": "application/json", accept: "application/json" },
          body: JSON.stringify(batch),
        });
        const answer = await response.json().catch(() => null) as
          { added?: number; refused?: string[]; refusedTotal?: number; error?: string } | null;
        if (!response.ok) throw new Error(answer?.error ?? `นำเข้าไม่สำเร็จ (${response.status})`);
        added += answer?.added ?? 0;
        refusedCount += answer?.refusedTotal ?? answer?.refused?.length ?? 0;
        if (answer?.refused) examples.push(...answer.refused);
        setDone(Math.min(at + 200, read.inquiries.length));
      }
      onToast(`นำเข้าแล้ว ${added} จาก ${read.inquiries.length} ใบ`
        + (refusedCount ? ` · ปฏิเสธ ${refusedCount} — ${examples[0]}` : ""));
      setRead(null);
      setFile("");
      onDone();
    } catch (error) {
      onToast(error instanceof Error ? error.message : String(error));
    } finally {
      setSending(false);
    }
  }

  const lanes = read?.inquiries.reduce((n, one) => n + one.lanes.length, 0) ?? 0;
  const prices = read?.inquiries.reduce(
    (n, one) => n + one.lanes.reduce((m, lane) => m + Object.keys(lane.prices).length, 0), 0) ?? 0;
  const fromSheet = read?.inquiries.filter((one) => one.datedFromSheet).length ?? 0;
  // The register will not hold a quote with nobody to quote to. Naming these
  // here — sheet and number — lets the blank be filled in the file, which is
  // where it is wrong; inventing a customer to get them in would not.
  const nameless = read?.inquiries.filter((one) => !one.customer.trim()) ?? [];

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;flex-direction:column;gap:10px")}>
      <div style={css("display:flex;align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>นำเข้าจากไฟล์ Rate Inquiry</span>
        <span style={css("font-size:11px;color:#7B8CA0;flex:1;min-width:200px")}>
          อ่านเฉพาะชีทที่เป็นเดือน — ชีท Remarks, Trucker และการ์ดราคาลูกค้าไม่ถูกอ่าน
        </span>
        <input type="file" accept=".xlsx,.xls" disabled={sending}
          onChange={(event) => { const one = event.target.files?.[0]; if (one) void pick(one); }}
          style={css("font-size:11.5px")} />
      </div>

      {read && (
        <div style={css("border:1px solid #E3E8EE;background:#F8FAFC;border-radius:4px;padding:11px 13px;display:flex;flex-direction:column;gap:7px")}>
          <div style={css("font-size:12px;color:#0A2240")}>
            <b>{file}</b> — {read.inquiries.length.toLocaleString()} ใบ ·
            {" "}{lanes.toLocaleString()} เส้นทาง · {prices.toLocaleString()} ราคา
          </div>

          {/* Everything imperfect, before anything is sent. */}
          <div style={css("font-size:11.5px;color:#7B8CA0;line-height:1.7")}>
            {fromSheet > 0 && (
              <div style={css("color:#B45309")}>
                {fromSheet.toLocaleString()} ใบไม่มีวันที่ในไฟล์ — ใช้วันที่ 1 ของเดือนตามชื่อชีท
              </div>
            )}
            {read.unmapped.length > 0 && (
              <div style={css("color:#B42318")}>
                {read.unmapped.length} คอลัมน์ราคาที่ระบบยังไม่รู้จัก จะไม่ถูกนำเข้า: {read.unmapped.join(" · ")}
              </div>
            )}
            {read.conflicts.length > 0 && (
              <div style={css("color:#B45309")}>
                {read.conflicts.length} แถวที่สองคอลัมน์รวมเป็นรถคันเดียวแล้วราคาไม่ตรงกัน — เก็บค่าแรกไว้ ·
                {" "}เช่น {read.conflicts[0]}
              </div>
            )}
            {nameless.length > 0 && (
              <div style={css("color:#B42318")}>
                {nameless.length} ใบไม่มีชื่อลูกค้าในไฟล์ — ระบบจะปฏิเสธ ต้องเติมในไฟล์แล้วนำเข้าใหม่:
                {" "}{nameless.slice(0, 8).map((one) => `${one.sheet} no.${one.number}`).join(" · ")}
                {nameless.length > 8 && ` … อีก ${nameless.length - 8} ใบ`}
              </div>
            )}
            {read.skipped > 0 && <div>{read.skipped} แถวว่างหรือแถวรวม ไม่ถูกนำเข้า</div>}
            {read.unmapped.length === 0 && read.conflicts.length === 0 && fromSheet === 0
              && nameless.length === 0 && (
              <div style={css("color:#16794C")}>อ่านได้ครบทุกคอลัมน์ ไม่มีอะไรตกหล่น</div>
            )}
          </div>

          <div style={css("display:flex;gap:9px;align-items:center")}>
            <button onClick={() => void send()} disabled={sending || read.inquiries.length === 0}
              style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:"
                + (sending ? "#8FA3B8" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit")}>
              {sending ? `กำลังนำเข้า ${done.toLocaleString()}/${read.inquiries.length.toLocaleString()}…` : "นำเข้าเข้าระบบ"}
            </button>
            <button onClick={() => { setRead(null); setFile(""); }} disabled={sending}
              style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
              ยกเลิก
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
