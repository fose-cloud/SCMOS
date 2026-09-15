"use client";

import { useMemo, useState } from "react";
import { Badge, PAGE_SIZES, Pager, Pick } from "../BoardBits";
import {
  carrierTallies, checkTrips, monthsOf, placeOf, verdictField, verdictText,
  type CarrierLane, type CheckedTrip, type Verdict,
} from "../chemoursCheck";
import type { DieselChange } from "../dieselMonth";
import { exportChemoursCheck } from "../excel";
import type { Job } from "../ops";
import { sheetToday } from "../rateSheetDrafts";
import { StatCard, StatGlyph } from "../StatCard";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import type { RateCard } from "./ChemoursRates";

/**
 * ตรวจสอบค่าขนส่ง — the Domestic runs held against the cost card.
 *
 * One row per trip in the month: who ran it, where to, in what, at which
 * diesel band, and what the haulier's card says that costs. A trip the table
 * cannot price says which of those four it is missing, in the column it is
 * missing from, so the person checking the invoice knows whether to fix the
 * job or query the haulier. The per-haulier line above the table is the
 * figure the invoice total is held against.
 */

/** The colour a verdict is drawn in, by what it is about. */
const TONE: Record<ReturnType<typeof verdictField>, string> = {
  "": "#16794C", carrier: "#B42318", destination: "#B45309", vehicle: "#B45309", diesel: "#1668AB",
};

const CELL = "padding:7px 10px;border-bottom:1px solid #EDF1F5;font-size:12px;vertical-align:top";
const HEAD = "padding:8px 10px;text-align:left;font-size:10.5px;font-weight:700;color:#465A6E;"
  + "letter-spacing:.05em;text-transform:uppercase;background:#F4F7FA;border-bottom:1px solid #D8E0E8;white-space:nowrap";
const MONO = "font-family:'IBM Plex Mono',ui-monospace,monospace";

const baht = (n: number | null) => (n === null ? "—" : "฿" + n.toLocaleString("en-US"));

export function ChemoursCheck({ jobs, card, changes, onOpenJob }: {
  jobs: Job[];
  /** The cost card — every haulier's lanes, as loaded on the ค่าขนส่ง tab. */
  card: RateCard | null;
  /** The published diesel changes, the same rows the Oil Rate tab keys. */
  changes: DieselChange[];
  onOpenJob?: (key: string) => void;
}) {
  const [month, setMonth] = useState("");
  const [carrier, setCarrier] = useState("");
  const [onlyFlagged, setOnlyFlagged] = useState(false);
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [per, setPer] = useState(PAGE_SIZES[0]);

  const domestic = useMemo(() => jobs.filter((job) => job.cat === "DELIVERY"), [jobs]);
  const months = useMemo(() => monthsOf(domestic), [domestic]);
  const chosenMonth = month && months.includes(month) ? month : months[0] ?? "";

  const lanes = useMemo<CarrierLane[]>(() => (card?.lanes ?? []).map((lane) => ({
    carrier: lane.carrier, from: lane.from, to: lane.to, county: lane.county, prices: lane.prices,
  })), [card]);

  const trips = useMemo(() => checkTrips(
    domestic.filter((job) => !chosenMonth || job.date.slice(3) === chosenMonth),
    lanes, card?.bands ?? [], changes, sheetToday(),
  ), [domestic, chosenMonth, lanes, card, changes]);

  const tallies = useMemo(() => carrierTallies(trips), [trips]);
  const flagged = trips.filter((trip) => trip.verdict !== "");

  const needle = query.trim().toLowerCase();
  const shown = trips.filter((trip) =>
    (!carrier || (trip.carrier || trip.job.trucker) === carrier)
    && (!onlyFlagged || trip.verdict !== "")
    && (!needle || [trip.job.jobCode, trip.job.dCode, trip.job.customer, trip.job.trucker, placeOf(trip.job), trip.job.wh]
      .join(" ").toLowerCase().includes(needle)));
  const pageCount = Math.max(1, Math.ceil(shown.length / per));
  const at = Math.min(page, pageCount);
  const paged = shown.slice((at - 1) * per, at * per);

  const costTotal = trips.reduce((sum, trip) => sum + (trip.total ?? 0), 0);

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <StatCard label="งานที่ตรวจ" value={trips.length.toLocaleString()} tone="#0A2240" icon="truck"
          note={chosenMonth ? `เดือน ${chosenMonth}` : "ยังไม่มีงาน Domestic"} />
        <StatCard label="ตรงตามตาราง" value={(trips.length - flagged.length).toLocaleString()} tone="#16794C" icon="check" />
        <StatCard label="ต้องตรวจสอบ" value={flagged.length.toLocaleString()} tone="#B45309" icon="warning" />
        <StatCard label="ต้นทุนตามตาราง" value={baht(trips.length ? costTotal : null)} tone="#1668AB" icon="money"
          note={flagged.length ? `ไม่รวม ${flagged.length} งานที่ยังคิดราคาไม่ได้` : ""} />
      </div>

      {!card && (
        <div style={css("background:#FFF8E6;border:1px solid #F1D48A;border-radius:5px;padding:10px 14px;font-size:12px;color:#8A5A12")}>
          ยังไม่มีการ์ดต้นทุนในระบบ — โหลดไฟล์ราคาผู้ขนส่งในแท็บ ค่าขนส่ง แล้วบันทึกก่อน
        </div>
      )}

      {/* One line per haulier: the trips, how many the table priced, and what
          they come to. The number an invoice total is held against. */}
      {tallies.length > 0 && (
        <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
          {tallies.map((tally) => (
            <button key={tally.carrier} type="button"
              onClick={() => { setCarrier(carrier === tally.carrier ? "" : tally.carrier); setPage(1); }}
              style={css("text-align:left;background:#fff;border-radius:6px;padding:8px 12px;min-width:200px;font-family:inherit;cursor:pointer;border:1px solid "
                + (carrier === tally.carrier ? "#0A2240;box-shadow:0 0 0 1px rgba(46,125,209,.35)" : "#D8E0E8"))}>
              <div style={css("display:flex;align-items:center;gap:6px;font-size:12px;font-weight:700;color:#0A2240")}>
                <StatGlyph icon="truck" size={14} />{tally.carrier}
              </div>
              <div style={css("display:flex;gap:12px;margin-top:4px;font-size:11px;color:#5A6B7D")}>
                <span>{tally.trips} เที่ยว</span>
                <span style={css("color:#16794C")}>คิดราคาได้ {tally.priced}</span>
                {tally.flagged > 0 && <span style={css("color:#B45309")}>ต้องตรวจ {tally.flagged}</span>}
              </div>
              <div style={css(`margin-top:3px;font-size:13px;font-weight:700;color:#0A2240;${MONO}`)}>{baht(tally.priced ? tally.cost : null)}</div>
            </button>
          ))}
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:10px 14px;border-bottom:1px solid #E9EFF5;display:flex;align-items:center;gap:6px;flex-wrap:wrap")}>
          <label style={css("display:inline-flex;align-items:center;gap:6px;font-size:11.5px;color:#5A6B7D")}>
            <StatGlyph icon="calendar" size={14} />
            <select value={chosenMonth} onChange={(e) => { setMonth(e.target.value); setPage(1); }}
              style={css("height:30px;border:1px solid #C9D6E2;border-radius:6px;padding:0 8px;font-size:12px;font-family:inherit;background:#fff;color:#0A2240")}>
              {months.length === 0 && <option value="">—</option>}
              {months.map((one) => <option key={one} value={one}>{one}</option>)}
            </select>
          </label>
          <Pick on={!onlyFlagged} tone="#0A2240" count={trips.length} onClick={() => { setOnlyFlagged(false); setPage(1); }}>ทุกเที่ยว</Pick>
          <Pick on={onlyFlagged} tone="#B45309" count={flagged.length} onClick={() => { setOnlyFlagged(true); setPage(1); }}>ต้องตรวจสอบ</Pick>
          <span style={css("margin-left:auto;display:inline-flex;align-items:center;gap:6px;height:30px;border:1px solid #C9D6E2;border-radius:6px;padding:0 9px;background:#fff;min-width:220px")}>
            <span style={css("display:flex;color:#7B8CA0")}><StatGlyph icon="search" size={14} /></span>
            <input value={query} onChange={(e) => { setQuery(e.target.value); setPage(1); }}
              placeholder="ค้นหา Job / ลูกค้า / ผู้ขนส่ง / ปลายทาง"
              style={css("flex:1;border:0;outline:none;font-size:12px;font-family:inherit;background:transparent;color:#16232F")} />
          </span>
          <button type="button" disabled={shown.length === 0} onClick={() => exportChemoursCheck(shown, chosenMonth || "all")}
            style={css("height:30px;padding:0 12px;border:1px solid #0A2240;border-radius:6px;background:#0A2240;color:#fff;"
              + "font-size:12px;font-weight:600;font-family:inherit;cursor:pointer;display:inline-flex;align-items:center;gap:6px"
              + (shown.length === 0 ? ";opacity:.5;cursor:not-allowed" : ""))}>
            <StatGlyph icon="download" size={14} />ส่งออก Excel
          </button>
        </div>

        <ZoomBox capped={false}>
          <table style={css("width:100%;border-collapse:collapse")}>
            <thead><tr>{HEADS.map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {paged.map((trip) => <Row key={trip.job.key} trip={trip} onOpenJob={onOpenJob} />)}
              {paged.length === 0 && (
                <tr><td colSpan={HEADS.length} style={css("padding:26px;text-align:center;font-size:12.5px;color:#94A3B8")}>
                  {trips.length === 0 ? "ไม่มีงาน Domestic ในเดือนนี้" : "ไม่มีเที่ยวตามเงื่อนไขนี้"}
                </td></tr>
              )}
            </tbody>
          </table>
        </ZoomBox>
        {shown.length > 0 && (
          <Pager total={shown.length} page={at} pageCount={pageCount} per={per}
            onPage={setPage} onPer={(n) => { setPer(n); setPage(1); }} />
        )}
      </div>
    </div>
  );
}

const HEADS = ["วันที่", "Job", "ผู้ขนส่ง", "W/H", "ลูกค้า", "ปลายทาง", "รถ", "เรทน้ำมัน", "ต้นทุนตามตาราง", "รับกลับ", "รวม", "ผลการตรวจ"];

/** One trip. The column the verdict is about is drawn in the verdict's colour. */
function Row({ trip, onOpenJob }: { trip: CheckedTrip; onOpenJob?: (key: string) => void }) {
  const field = verdictField(trip.verdict);
  const tone = TONE[field];
  const mark = (which: typeof field) => (field === which ? `color:${tone};font-weight:700` : "");
  const job = trip.job;
  const parts = trip.cost.parts.map((part) => `${part.trucks}×${part.vehicle} @ ฿${part.each.toLocaleString("en-US")}`).join(" + ");
  const returnLeg = trip.total !== null && trip.cost.total !== null ? trip.total - trip.cost.total : null;

  return (
    <tr style={css("background:" + (trip.verdict ? "#FFFBF5" : "#fff"))}>
      <td style={css(CELL + `;${MONO};white-space:nowrap`)}>{job.date}</td>
      <td style={css(CELL + `;${MONO};white-space:nowrap`)}>
        {onOpenJob ? (
          <button type="button" onClick={() => onOpenJob(job.key)}
            style={css(`border:none;background:none;padding:0;${MONO};font-size:12px;color:#0A5FA8;cursor:pointer;text-decoration:underline`)}>
            {job.jobCode || job.dCode || job.key}
          </button>
        ) : (job.jobCode || job.dCode || job.key)}
        {job.jobCode && job.dCode && <div style={css("color:#7B8CA0;font-size:11px")}>{job.dCode}</div>}
      </td>
      <td style={css(CELL + ";white-space:nowrap;" + mark("carrier"))}>{trip.carrier || job.trucker || "—"}</td>
      <td style={css(CELL + ";white-space:nowrap")}>{job.wh || "—"}</td>
      <td style={css(CELL + ";min-width:140px")}>{job.customer}</td>
      <td style={css(CELL + ";min-width:150px;" + mark("destination"))}>
        {placeOf(job) || "—"}
        {trip.lane && <div style={css("color:#7B8CA0;font-size:11px")}>{trip.lane.from} → {trip.lane.to}</div>}
        {trip.originNote && <div style={css("color:#B45309;font-size:11px")}>{trip.originNote}</div>}
      </td>
      <td style={css(CELL + ";white-space:nowrap;" + mark("vehicle"))}>{trip.trucks || "—"}</td>
      <td style={css(CELL + `;${MONO};white-space:nowrap;` + mark("diesel"))}>
        {trip.diesel === null ? "—" : trip.diesel.toFixed(2)}
        {trip.bandLabel && <div style={css("color:#7B8CA0;font-size:11px")}>{trip.bandLabel}{trip.dieselFrom === "month" ? " · เฉลี่ยเดือน" : ""}</div>}
      </td>
      <td style={css(CELL + `;${MONO};text-align:right;white-space:nowrap`)} title={parts}>
        {baht(trip.cost.total)}
        {parts && <div style={css("color:#7B8CA0;font-size:10.5px")}>{parts}</div>}
      </td>
      <td style={css(CELL + `;${MONO};text-align:right;white-space:nowrap;color:#5A6B7D`)}>
        {trip.returnKind === "none" ? "" : returnLeg === null ? "—" : baht(returnLeg)}
        {trip.returnKind !== "none" && <div style={css("font-size:10.5px")}>{trip.returnKind === "finished" ? "finished goods" : "รับกลับ"}</div>}
      </td>
      <td style={css(CELL + `;${MONO};text-align:right;white-space:nowrap;font-weight:700`)}>{baht(trip.total)}</td>
      <td style={css(CELL + ";min-width:220px")}>
        <Badge tone={tone} icon={trip.verdict ? "warning" : "check"}>{trip.verdict ? "ต้องตรวจสอบ" : "ตรงตามตาราง"}</Badge>
        {trip.verdict && <div style={css("margin-top:3px;font-size:11px;color:#5A6B7D")}>{verdictText(trip)}</div>}
      </td>
    </tr>
  );
}

export type { Verdict };
