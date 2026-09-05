"use client";

import { Fragment, useMemo, useState } from "react";
import * as XLSX from "xlsx";
import { isCancelled, type Job } from "../ops";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";
import { dnum, lateMinutes } from "../util";
import { THIN, byVendor, otdLabel, type Counts } from "../vendorReport";

/**
 * Which carrier ran what, for whom, and how much of it arrived on time.
 *
 * Asked for as: each haulier — which customers do they run for, how many trips,
 * how many on time, how many late, and what percentage. The scorecard grades a
 * haulier as one number and the delay report cuts one customer by month;
 * neither crosses the two, which is where "SANGJA is fine except on HENKEL"
 * lives.
 *
 * The counting is in vendorReport, which has no imports and is tested on its
 * own. The lateness rule is `lateMinutes` from util — the same reading the
 * delay report and the dashboard use — handed in rather than written again.
 * This is the page around them, and it does the filtering so that the table,
 * the tiles and the workbook cannot disagree about what was in scope.
 */

/** Grace periods a customer's service level might allow, in minutes. */
const GRACE = [0, 15, 30, 60];

/** How many carrier rows open before the page is scrolling rather than reading. */
const TOP = 40;

export function VendorReport({ jobs, onToast, onBack }: {
  jobs: Job[];
  onToast: (message: string) => void;
  onBack: () => void;
}) {
  const [customer, setCustomer] = useState("");
  const [trucker, setTrucker] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  // Thirty, the same default the delay analysis opens on and the same figure
  // the carrier scorecard scores against. A report that opened on a different
  // tolerance would disagree with both on its first screen.
  const [grace, setGrace] = useState(30);
  const [openVendor, setOpenVendor] = useState("");

  /**
   * Every trip the filters select.
   *
   * Cancelled work is out: a trip that did not happen is not a trip this
   * carrier ran, and counting it would put a haulier's cancellations into their
   * own volume. The rule comes from ops rather than being written again here.
   */
  const inScope = useMemo(() => {
    const start = from ? dnum(from) : 0;
    const end = to ? dnum(to) : 0;
    return jobs
      .filter((job) => !isCancelled(job))
      .filter((job) => !customer || (job.customer ?? "").trim() === customer)
      .filter((job) => !trucker || (job.trucker ?? "").trim() === trucker)
      .filter((job) => {
        const day = dnum(job.date);
        if (start && (!day || day < start)) return false;
        if (end && (!day || day > end)) return false;
        return true;
      });
  }, [jobs, customer, trucker, from, to]);

  const report = useMemo(
    () => byVendor(inScope, { grace, lateOf: lateMinutes }),
    [inScope, grace]);

  /*
   * What the two pickers offer.
   *
   * Counted over the whole register rather than over what the other picker has
   * already narrowed to, so choosing a carrier does not silently empty the
   * customer list — and so the counts beside each name mean the same thing
   * whichever order somebody sets the two filters in.
   */
  const customers = useMemo(() => tally(jobs, (job) => job.customer), [jobs]);
  const truckers = useMemo(() => tally(jobs, (job) => job.trucker), [jobs]);

  const { total } = report;

  function exportSheet() {
    if (!inScope.length) { onToast("ไม่มีเที่ยวในขอบเขตที่เลือก"); return; }

    const book = XLSX.utils.book_new();
    const range = `${from || "ตั้งแต่แรก"} – ${to || "ถึงล่าสุด"}`;

    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["Vendor Performance · ผลงานผู้ขนส่งรายลูกค้า"],
      ["ช่วงเวลา", range],
      ["ลูกค้า", customer || "ทุกลูกค้า"],
      ["ผู้ขนส่ง", trucker || "ทุกราย"],
      ["ผ่อนผัน", grace === 0 ? "ไม่ผ่อนผัน (KPI)" : `เกิน ${grace} นาที จึงนับว่าสาย`],
      [],
      ["ผู้ขนส่ง", "เที่ยวทั้งหมด", "วัดได้", "ตรงเวลา", "สาย", "% ตรงเวลา", "วัดไม่ได้"],
      ...report.vendors.map((one) => [
        one.vendor, one.trips, one.measured, one.onTime, one.late,
        one.otd === null ? "วัดไม่ได้" : one.otd, one.notAssessable,
      ]),
      ["รวม", total.trips, total.measured, total.onTime, total.late,
        total.otd === null ? "วัดไม่ได้" : total.otd, total.notAssessable],
      ...(report.unnamed ? [[], ["ไม่ระบุผู้ขนส่ง", report.unnamed,
        "เที่ยวเหล่านี้อยู่ในยอดรวมแต่ไม่มีแถวผู้ขนส่ง"]] : []),
    ]), "By Vendor");

    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["ผู้ขนส่ง", "ลูกค้า", "เที่ยวทั้งหมด", "วัดได้", "ตรงเวลา", "สาย", "% ตรงเวลา", "วัดไม่ได้"],
      ...report.vendors.flatMap((one) => one.customers.map((line) => [
        one.vendor, line.customer, line.trips, line.measured, line.onTime, line.late,
        line.otd === null ? "วัดไม่ได้" : line.otd, line.notAssessable,
      ])),
    ]), "Vendor x Customer");

    // The method travels with the figures. Without it, "96.7%" is a number
    // somebody can read as covering every trip, which it never does.
    XLSX.utils.book_append_sheet(book, XLSX.utils.aoa_to_sheet([
      ["วิธีคิด"],
      [],
      ["ตรงเวลา / สาย", "เทียบวันที่-เวลาตามแผน กับวันที่-เวลาที่รถถึงจริง"],
      ["ผ่อนผัน", `สายเกิน ${grace} นาที จึงนับว่าสาย · เร็วกว่าแผนนับว่าตรงเวลา`],
      ["% ตรงเวลา", "คิดจากเที่ยวที่วัดได้เท่านั้น ไม่ได้คิดจากเที่ยวทั้งหมด"],
      ["วัดไม่ได้", "ไม่มีเวลาตามแผนหรือไม่มีเวลาถึง — ไม่นับทั้งฝั่งตรงเวลาและฝั่งสาย"],
      ["", `ในขอบเขตนี้มี ${total.notAssessable} จาก ${total.trips} เที่ยว`],
      ["ผู้ขนส่งที่ไม่มีเที่ยววัดได้เลย", "ไม่มีเปอร์เซ็นต์ ไม่ใช่ 0% และไม่ใช่ 100%"],
      [`ฐานน้อยกว่า ${THIN} เที่ยว`, "เปอร์เซ็นต์ยังไม่พอจะอ่านเป็นผลงาน — ตัวเลขแสดงไว้พร้อมฐานเสมอ"],
      ["งานที่ยกเลิก", "ไม่นับ"],
    ]), "Method");

    XLSX.writeFile(book, `vendor-performance-${(from || "all").replace(/\//g, "")}.xlsx`);
    onToast(`ส่งออก ${report.vendors.length} ผู้ขนส่ง · ${total.trips} เที่ยว`);
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("display:flex;align-items:center;gap:10px")}>
        <button onClick={onBack} style={BTN_SECONDARY}>← กลับไปรายการรายงาน</button>
        <span style={css("font-size:13px;font-weight:600;color:#0A2240")}>
          Vendor Performance · ผลงานผู้ขนส่งรายลูกค้า
        </span>
      </div>

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:13px 16px;"
        + "display:flex;gap:12px;align-items:flex-end;flex-wrap:wrap")}>
        <Field label="CUSTOMER · ลูกค้า" width="240px">
          <select value={customer} onChange={(e) => setCustomer(e.target.value)} style={SELECT}>
            <option value="">ทุกลูกค้า</option>
            {customers.map(([name, held]) => (
              <option key={name} value={name}>{name} · {held} เที่ยว</option>
            ))}
          </select>
        </Field>
        <Field label="TRUCK · ผู้ขนส่ง" width="240px">
          <select value={trucker} onChange={(e) => setTrucker(e.target.value)} style={SELECT}>
            <option value="">ทุกราย</option>
            {truckers.map(([name, held]) => (
              <option key={name} value={name}>{name} · {held} เที่ยว</option>
            ))}
          </select>
        </Field>
        <Field label="ตั้งแต่ (วว/ดด/ปปปป)" width="150px">
          <input value={from} onChange={(e) => setFrom(e.target.value)} placeholder="01/08/2026" style={INPUT} />
        </Field>
        <Field label="ถึง" width="150px">
          <input value={to} onChange={(e) => setTo(e.target.value)} placeholder="31/08/2026" style={INPUT} />
        </Field>
        <Field label="ผ่อนผัน" width="150px">
          <select value={grace} onChange={(e) => setGrace(Number(e.target.value))} style={SELECT}>
            {GRACE.map((minutes) => (
              <option key={minutes} value={minutes}>
                {minutes === 0 ? "ไม่ผ่อนผัน (KPI)" : `เกิน ${minutes} นาที`}
              </option>
            ))}
          </select>
        </Field>
        <button onClick={exportSheet} style={css(BTN_PRIMARY_CSS + ";margin-left:auto")}>Export Excel</button>
      </div>

      <div style={css("display:flex;gap:11px;flex-wrap:wrap")}>
        <Tile label="ผู้ขนส่ง" value={String(report.vendors.length)} note="ที่มีงานในขอบเขตนี้" />
        <Tile label="เที่ยวทั้งหมด" value={total.trips.toLocaleString()} note="ไม่รวมงานที่ยกเลิก" />
        <Tile label="ตรงเวลา" value={String(total.onTime)} tone="#16794C"
          note={`จาก ${total.measured} เที่ยวที่วัดได้`} />
        <Tile label={`สายเกิน ${grace} นาที`} value={String(total.late)} tone="#B3261E"
          note={`จาก ${total.measured} เที่ยวที่วัดได้`} />
        <Tile label="% ตรงเวลา" value={otdLabel(total)}
          note={total.measured ? `คิดจาก ${total.measured} เที่ยว ไม่ใช่ ${total.trips}` : "ยังไม่มีเที่ยวที่วัดได้"} />
        {/* Beside the rest, never folded into them. On the register this was
            built against it is most of the work, and a percentage that quietly
            counted these as successes is the one thing this report must not
            hand to somebody walking into a carrier meeting. */}
        <Tile label="วัดไม่ได้" value={total.notAssessable.toLocaleString()}
          note={total.notAssessable ? "ไม่มีเวลาแผนหรือเวลาถึง — ไม่นับทั้งสองฝั่ง" : "บันทึกครบ"} />
      </div>

      {report.unnamed > 0 && (
        <div style={css("background:#FFF8EC;border:1px solid #F0DCB4;border-radius:6px;padding:9px 14px;"
          + "font-size:11.5px;color:#8A6D1F")}>
          {report.unnamed.toLocaleString()} เที่ยวยังไม่ระบุผู้ขนส่ง — อยู่ในยอดรวมด้านบน
          แต่ไม่มีแถวของตัวเองด้านล่าง
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5")}>
          <div style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>
            ผู้ขนส่งแต่ละราย · กดที่แถวเพื่อดูว่าวิ่งให้ลูกค้าอะไรบ้าง
          </div>
          <div style={css("font-size:11px;color:#7B8CA0;margin-top:3px")}>
            เรียงตามจำนวนเที่ยว · % ตรงเวลาคิดจากเที่ยวที่วัดได้เท่านั้น
            {` · ฐานน้อยกว่า ${THIN} เที่ยวแสดงเป็นสีจาง เพราะยังน้อยเกินกว่าจะอ่านเป็นผลงาน`}
          </div>
        </div>
        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
            <thead>
              <tr>
                {["ผู้ขนส่ง / ลูกค้า", "เที่ยว", "วัดได้", "ตรงเวลา", "สาย", "% ตรงเวลา", "วัดไม่ได้"]
                  .map((head, at) => (
                    <th key={head} style={css(TH + (at === 0 ? "" : ";text-align:right"))}>{head}</th>
                  ))}
              </tr>
            </thead>
            <tbody>
              {report.vendors.slice(0, TOP).map((one) => (
                <Fragment key={one.vendor}>
                  <tr className="row-hover" style={css("cursor:pointer")}
                    onClick={() => setOpenVendor(openVendor === one.vendor ? "" : one.vendor)}>
                    <td style={css(TD + ";font-weight:600;color:#0A2240")}>
                      <span style={css("color:#94A3B8;margin-right:6px")}>
                        {openVendor === one.vendor ? "▾" : "▸"}
                      </span>
                      {one.vendor}
                      <span style={css("color:#94A3B8;font-weight:400;margin-left:7px;font-size:11px")}>
                        {one.customers.length} ลูกค้า
                      </span>
                    </td>
                    <Figures counts={one} bold />
                  </tr>
                  {openVendor === one.vendor && one.customers.map((line) => (
                    <tr key={line.customer} style={css("background:#FBFCFE")}>
                      <td style={css(TD + ";padding-left:30px;color:#31465C")}>{line.customer}</td>
                      <Figures counts={line} />
                    </tr>
                  ))}
                </Fragment>
              ))}
              {report.vendors.length === 0 && (
                <tr><td colSpan={7} style={css("padding:26px;text-align:center;color:#94A3B8")}>
                  ไม่มีเที่ยวในขอบเขตที่เลือก
                </td></tr>
              )}
            </tbody>
            {report.vendors.length > 0 && (
              <tfoot>
                <tr>
                  <td style={css(TD + ";font-weight:700;color:#0A2240;background:#F4F7FA")}>รวมทุกราย</td>
                  <Figures counts={total} bold background="#F4F7FA" />
                </tr>
              </tfoot>
            )}
          </table>
        </ZoomBox>
        {report.vendors.length > TOP && (
          <div style={css("padding:9px 14px;font-size:11.5px;color:#94A3B8")}>
            แสดง {TOP} รายแรกจาก {report.vendors.length} — เรียงตามจำนวนเที่ยวแล้ว
            {" (ยอดรวมด้านล่างนับครบทุกราย)"}
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * The six figures, written the same way on a carrier row, a customer row and
 * the total — so a reader comparing them is comparing like with like.
 *
 * The percentage is muted below THIN measured trips. Nothing is hidden: two
 * trips are two trips, and dropping the row would leave the total not adding
 * up. It is marked because "100%" scanned quickly does not say "of one".
 */
function Figures({ counts, bold, background }: {
  counts: Counts; bold?: boolean; background?: string;
}) {
  const cell = TD + ";text-align:right;font-family:'IBM Plex Mono',monospace"
    + (bold ? ";font-weight:600" : "") + (background ? ";background:" + background : "");
  const thin = counts.measured > 0 && counts.measured < THIN;

  return (
    <>
      <td style={css(cell)}>{counts.trips.toLocaleString()}</td>
      <td style={css(cell + ";color:#7B8CA0")}>{counts.measured.toLocaleString()}</td>
      <td style={css(cell + ";color:#16794C")}>{counts.onTime.toLocaleString()}</td>
      <td style={css(cell + (counts.late > 0 ? ";color:#B3261E" : ";color:#94A3B8"))}>
        {counts.late.toLocaleString()}
      </td>
      <td style={css(cell + ";color:" + (counts.otd === null || thin ? "#94A3B8" : tone(counts.otd)))}
        title={counts.otd === null
          ? "ไม่มีเที่ยวที่วัดได้เลย จึงยังบอกไม่ได้"
          : `ตรงเวลา ${counts.onTime} จาก ${counts.measured} เที่ยวที่วัดได้`}>
        {otdLabel(counts)}
        {thin && <span style={css("font-size:10px;color:#B45309")}> ฐาน {counts.measured}</span>}
      </td>
      <td style={css(cell + ";color:" + (counts.notAssessable > 0 ? "#B45309" : "#94A3B8"))}>
        {counts.notAssessable.toLocaleString()}
      </td>
    </>
  );
}

/** Green, amber or red — the bands the account team already reviews against. */
const tone = (otd: number): string => (otd >= 90 ? "#16794C" : otd >= 75 ? "#B45309" : "#B3261E");

/** Names with how many trips carry them, most-used first. */
function tally(jobs: Job[], read: (job: Job) => string | undefined): [string, number][] {
  const held = new Map<string, number>();
  jobs.forEach((job) => {
    const name = (read(job) ?? "").trim();
    if (name) held.set(name, (held.get(name) ?? 0) + 1);
  });
  return [...held.entries()].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
}

/*
 * The report chrome below is the fourth copy of it — Delay Analysis, Volume
 * Report and Supplier Performance each carry their own. Styling rather than
 * rules, so a drift here is a cosmetic difference and not a wrong number, but
 * it is worth lifting out the next time one of these screens is opened.
 */
const LABEL = css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600");
const INPUT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;font-family:inherit;width:100%");
const SELECT = css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;font-family:inherit;background:#fff;width:100%");
const TH = css("background:#F4F7FA;padding:7px 10px;text-align:left;font-size:10px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap");
const TD = "padding:7px 10px;border-bottom:1px solid #F1F5F9;vertical-align:top";
const BTN_PRIMARY_CSS = "height:32px;padding:0 16px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;font-family:inherit";
const BTN_SECONDARY = css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#31465C;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;font-family:inherit");

function Tile({ label, value, tone: colour, note }: {
  label: string; value: string; tone?: string; note?: string;
}) {
  return (
    <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 16px;"
      + "min-width:150px;display:flex;flex-direction:column;gap:3px")}>
      <span style={LABEL}>{label}</span>
      <span style={css(`font-size:19px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:${colour ?? "#0A2240"}`)}>
        {value}
      </span>
      {note && <span style={css("font-size:10.5px;color:#94A3B8")}>{note}</span>}
    </div>
  );
}

function Field({ label, width, children }: { label: string; width: string; children: React.ReactNode }) {
  return (
    <label style={css(`display:flex;flex-direction:column;gap:3px;min-width:${width}`)}>
      <span style={LABEL}>{label}</span>
      {children}
    </label>
  );
}
