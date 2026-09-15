"use client";

import { useMemo, useState } from "react";
import { Badge, PAGE_SIZES, Pager, Pick } from "../BoardBits";
import {
  carrierTallies, checkTrips, monthsOf, placeOf, trucksOf, verdictField, verdictText,
  type CarrierLane, type CheckedTrip, type Verdict,
} from "../chemoursCheck";
import type { DieselChange } from "../dieselMonth";
import { exportChemoursCheck, exportHaulierReconciliation, parseWorkbook } from "../excel";
import { reconcile, verdictWords, type Reconciled, type Reconciliation } from "../haulierReconcile";
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

export function ChemoursCheck({ jobs, card, changes, onOpenJob, onToast }: {
  jobs: Job[];
  /** The cost card — every haulier's lanes, as loaded on the ค่าขนส่ง tab. */
  card: RateCard | null;
  /** The published diesel changes, the same rows the Oil Rate tab keys. */
  changes: DieselChange[];
  onOpenJob?: (key: string) => void;
  onToast?: (message: string) => void;
}) {
  const [month, setMonth] = useState("");
  /** The haulier's file, read into rows shaped like ours, and its name. */
  const [file, setFile] = useState<{ name: string; rows: Job[] } | null>(null);
  const [reading, setReading] = useState(false);
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

  /**
   * The haulier's file against the month's runs — see haulierReconcile.ts.
   * Their rows are read by the same workbook reader the Domestic import
   * uses, so a file shaped like the grid reads the way the grid would.
   */
  const reconciled = useMemo<Reconciliation | null>(() => {
    if (!file) return null;
    const monthJobs = domestic.filter((job) => !chosenMonth || job.date.slice(3) === chosenMonth);
    return reconcile(file.rows, monthJobs, trips);
  }, [file, domestic, chosenMonth, trips]);

  async function readHaulierFile(picked: File) {
    setReading(true);
    try {
      const preview = await parseWorkbook(picked, "", []);
      const rows = preview.jobs;
      if (rows.length === 0) { onToast?.("อ่านไฟล์ไม่ได้ — ไม่พบแถวงานที่มีวันที่หรือลูกค้า"); return; }
      setFile({ name: picked.name, rows });
      onToast?.(`อ่านไฟล์ ${picked.name} แล้ว ${rows.length} แถว`
        + (preview.unmappedHeaders.length ? ` · ไม่รู้จักคอลัมน์ ${preview.unmappedHeaders.slice(0, 3).join(", ")}` : ""));
    } catch (error) {
      onToast?.("อ่านไฟล์ไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally { setReading(false); }
  }

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

      {reconciled && file && (
        <ReconciliationBoard file={file.name} done={reconciled} month={chosenMonth}
          onClear={() => setFile(null)} onOpenJob={onOpenJob} />
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
          {/* The haulier's own list, read against the runs above. */}
          <label style={css("height:30px;padding:0 12px;border:1px solid #0A2240;border-radius:6px;background:#fff;color:#0A2240;"
            + "font-size:12px;font-weight:600;font-family:inherit;display:inline-flex;align-items:center;gap:6px;cursor:" + (reading ? "default" : "pointer"))}>
            <StatGlyph icon="upload" size={14} />
            {reading ? "กำลังอ่าน…" : "อัปโหลดไฟล์ผู้ขนส่ง"}
            <input type="file" accept=".xlsx,.xls,.csv" disabled={reading} style={css("display:none")}
              onChange={(e) => { const picked = e.target.files?.[0]; e.target.value = ""; if (picked) void readHaulierFile(picked); }} />
          </label>
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

/** The tone a line's verdict is drawn in. */
const LINE_TONE: Record<Reconciled["verdict"], string> = { match: "#16794C", differs: "#B42318", unmatched: "#B45309", unpriced: "#1668AB" };
const RECON_HEADS = ["แถวในไฟล์", "งานในตาราง Domestic", "ผู้ขนส่งแจ้ง", "ตามตารางต้นทุน", "ผลการตรวจ"];

/**
 * The haulier's file, row by row, against the month's runs. Every line says
 * which run it was taken to be, what they charge, what our card says, and
 * where the two disagree — column by column, both ways round. Below it,
 * the runs of ours the file does not carry.
 */
function ReconciliationBoard({ file, done, month, onClear, onOpenJob }: {
  file: string; done: Reconciliation; month: string; onClear: () => void; onOpenJob?: (key: string) => void;
}) {
  const [only, setOnly] = useState<"" | Reconciled["verdict"]>("");
  const lines = only ? done.lines.filter((line) => line.verdict === only) : done.lines;
  const gap = done.theirTotal - done.ourTotal;
  const describe = (row: { date?: string; jobCode?: string; dCode?: string; customer?: string; zip?: string; destination?: string; wh?: string }) => (
    <>
      <div style={css(`${MONO};font-size:12px;color:#0A2240;font-weight:600`)}>{row.jobCode || row.dCode || "—"}{row.jobCode && row.dCode ? <span style={css("color:#7B8CA0;font-weight:400")}> · {row.dCode}</span> : null}</div>
      <div style={css("font-size:11.5px;color:#16232F")}>{row.date || "—"} · {row.customer || "—"}</div>
      <div style={css("font-size:11px;color:#7B8CA0")}>{[row.wh, row.destination || "", row.zip].filter(Boolean).join(" · ") || "—"}</div>
    </>
  );
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:10px 14px;border-bottom:1px solid #E9EFF5;display:flex;align-items:center;gap:6px;flex-wrap:wrap")}>
        <span style={css("display:inline-flex;align-items:center;gap:6px;font-size:12.5px;font-weight:700;color:#0A2240")}>
          <StatGlyph icon="sheet" size={15} />{file}
        </span>
        <span style={css("font-size:11.5px;color:#7B8CA0")}>เทียบกับงาน Domestic เดือน {month || "ทั้งหมด"}</span>
        <Pick on={only === ""} tone="#0A2240" count={done.counts.rows} onClick={() => setOnly("")}>ทุกแถว</Pick>
        <Pick on={only === "match"} tone={LINE_TONE.match} count={done.counts.match} onClick={() => setOnly("match")}>ตรงกัน</Pick>
        <Pick on={only === "differs"} tone={LINE_TONE.differs} count={done.counts.differs} onClick={() => setOnly("differs")}>ต่างกัน</Pick>
        <Pick on={only === "unmatched"} tone={LINE_TONE.unmatched} count={done.counts.unmatched} onClick={() => setOnly("unmatched")}>ไม่พบในตาราง</Pick>
        {done.counts.unpriced > 0 && <Pick on={only === "unpriced"} tone={LINE_TONE.unpriced} count={done.counts.unpriced} onClick={() => setOnly("unpriced")}>คิดราคาไม่ได้</Pick>}
        <span style={css(`margin-left:auto;font-size:12px;color:#0A2240;${MONO}`)}>
          ผู้ขนส่งแจ้ง {baht(done.theirTotal)} · ตามตาราง {baht(done.ourTotal)}
          {done.ourTotal > 0 && <span style={css("color:" + (gap === 0 ? "#16794C" : gap > 0 ? "#B42318" : "#B45309"))}> · {gap === 0 ? "เท่ากัน" : `${gap > 0 ? "+" : "−"}${baht(Math.abs(gap))}`}</span>}
        </span>
        <button type="button" onClick={() => exportHaulierReconciliation(done, file, month || "all")}
          style={css("height:30px;padding:0 12px;border:1px solid #0A2240;border-radius:6px;background:#0A2240;color:#fff;font-size:12px;font-weight:600;font-family:inherit;cursor:pointer;display:inline-flex;align-items:center;gap:6px")}>
          <StatGlyph icon="download" size={14} />ส่งออกผลตรวจ
        </button>
        <button type="button" onClick={onClear}
          style={css("height:30px;padding:0 10px;border:1px solid #C9D6E2;border-radius:6px;background:#fff;color:#5A6B7D;font-size:12px;font-family:inherit;cursor:pointer")}>
          ปิดไฟล์
        </button>
      </div>

      <ZoomBox capped={false}>
        <table style={css("width:100%;border-collapse:collapse")}>
          <thead><tr>{RECON_HEADS.map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
          <tbody>
            {lines.map((line, index) => (
              <tr key={line.row.key || index} style={css("background:" + (line.verdict === "differs" ? "#FFF5F5" : line.verdict === "unmatched" ? "#FFFBF5" : "#fff"))}>
                <td style={css(CELL + ";min-width:200px")}>{describe(line.row)}</td>
                <td style={css(CELL + ";min-width:200px")}>
                  {line.job ? (
                    <>
                      {describe(line.job)}
                      {onOpenJob && (
                        <button type="button" onClick={() => onOpenJob(line.job!.key)}
                          style={css("border:none;background:none;padding:0;font-size:11px;color:#0A5FA8;cursor:pointer;text-decoration:underline;font-family:inherit")}>
                          เปิดงาน{line.matchedBy === "date+customer+zip" ? " · จับคู่จากวันที่+ลูกค้า+ZIP" : ""}
                        </button>
                      )}
                    </>
                  ) : <span style={css("color:#94A3B8")}>—</span>}
                </td>
                <td style={css(CELL + ";white-space:nowrap")}>
                  <div>{trucksLine(line.row)}</div>
                  <div style={css(`${MONO};font-weight:600`)}>{baht(line.theirCost)}</div>
                </td>
                <td style={css(CELL + ";white-space:nowrap")}>
                  {line.job ? (
                    <>
                      <div>{trucksLine(line.job)}</div>
                      <div style={css(`${MONO};font-weight:600`)}>{baht(line.check?.total ?? null)}</div>
                      {line.check && line.check.total === null && <div style={css("font-size:10.5px;color:#7B8CA0;white-space:normal;max-width:220px")}>{verdictText(line.check)}</div>}
                    </>
                  ) : <span style={css("color:#94A3B8")}>—</span>}
                </td>
                <td style={css(CELL + ";min-width:240px")}>
                  <Badge tone={LINE_TONE[line.verdict]} icon={line.verdict === "match" ? "check" : line.verdict === "unmatched" ? "search" : "warning"}>
                    {line.verdict === "match" ? "ตรงกัน" : line.verdict === "differs" ? "ต่างกัน" : line.verdict === "unmatched" ? "ไม่พบในตาราง" : "คิดราคาไม่ได้"}
                  </Badge>
                  {line.differences.length > 0 && (
                    <ul style={css("margin:5px 0 0;padding-left:16px;font-size:11.5px;color:#16232F")}>
                      {line.differences.map((one) => (
                        <li key={one.field}>
                          <b>{one.label}</b>: ไฟล์ <span style={css(`${MONO}`)}>{one.theirs}</span> · ตาราง <span style={css(`${MONO}`)}>{one.ours}</span>
                          {one.gap !== undefined && one.gap !== 0 && <span style={css("color:" + (one.gap > 0 ? "#B42318" : "#B45309"))}> ({one.gap > 0 ? "+" : "−"}฿{Math.abs(one.gap).toLocaleString("en-US")})</span>}
                        </li>
                      ))}
                    </ul>
                  )}
                  {line.verdict !== "differs" && line.verdict !== "match" && <div style={css("margin-top:4px;font-size:11px;color:#5A6B7D")}>{verdictWords(line)}</div>}
                </td>
              </tr>
            ))}
            {lines.length === 0 && (
              <tr><td colSpan={RECON_HEADS.length} style={css("padding:22px;text-align:center;font-size:12.5px;color:#94A3B8")}>ไม่มีแถวตามเงื่อนไขนี้</td></tr>
            )}
          </tbody>
        </table>
      </ZoomBox>

      {done.unbilled.length > 0 && (
        <div style={css("padding:10px 14px;border-top:1px solid #E9EFF5;font-size:12px;color:#16232F")}>
          <b style={css("color:#B45309")}>งานของเราที่ไม่อยู่ในไฟล์ {done.unbilled.length} เที่ยว</b>
          <span style={css("color:#7B8CA0")}> — ผู้ขนส่งยังไม่ได้แจ้ง หรือแจ้งในรอบอื่น</span>
          <div style={css("margin-top:6px;display:flex;flex-wrap:wrap;gap:6px")}>
            {done.unbilled.map((job) => (
              <button key={job.key} type="button" onClick={() => onOpenJob?.(job.key)}
                style={css(`${MONO};font-size:11.5px;padding:3px 8px;border:1px solid #F1D48A;border-radius:4px;background:#FFF8E6;color:#8A5A12;cursor:pointer;font-family:inherit`)}>
                {job.jobCode || job.dCode || job.key} · {job.date} · {job.customer}
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

/** The trucks a row says it sent, or a dash. */
function trucksLine(row: { v4?: string; v6?: string; v10?: string; vtl?: string }): string {
  return trucksOf({ key: "", date: "", trucker: "", customer: "", ...row }) || "—";
}

export type { Verdict };
