"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css, STATUS_RE } from "../theme";
import type { Period } from "../period";
import type { Job } from "../ops";
import { PeriodBar } from "../PeriodBar";

/**
 * Operational KPI.
 *
 * This screen holds no rules. Every figure on it is computed by the API from the
 * register in Azure SQL, which is the architecture's decision: a number the team
 * reports upward should not depend on which build of the front end the viewer
 * happens to have loaded.
 *
 * What is left here is presentation — and one thing worth saying out loud, which
 * is the base each rate was measured over.
 */

type Counted = { label: string; value: number };
type Measured = { base: number; met: number; percent: number };
type OwnerLoad = { owner: string; ownerId: string; total: number; open: number; action: number; late: number };
type CarrierLoad = { carrier: string; total: number; measured: number; onTime: number; percent: number };

export type KpiReport = {
  total: number;
  byCategory: Counted[];
  byStatus: Counted[];
  onTime: Measured;
  actionRequired: number;
  formatErrors: number;
  gateInRisk: number;
  undated: number;
  team: OwnerLoad[];
  carriers: CarrierLoad[];
  byDay: Counted[];
  computedAt: string;
};

type TrendPoint = { period: string; value: number | null; base: number };

type Measure = {
  id: string; english: string; thai: string; kind: string;
  available: boolean; value: number | null; base: number; unit: string; note: string;
  breakdown: Counted[];
  target: number | null;
  meetsTarget: boolean | null;
  trend: TrendPoint[] | null;
};

type SupplierScore = {
  carrier: string; jobs: number;
  onTime: number | null; onTimeBase: number;
  confirmation: number | null; confirmationBase: number;
  delayFree: number | null; delayCount: number;
  score: number | null;
};

/** One criterion of the contract scorecard, for one carrier. */
type ScoreLine = {
  id: string; english: string; thai: string; weight: number;
  percent: number | null; count: number; base: number; target: number; note: string;
};

/** The tally the customer's own monthly report is laid out in. */
type CarrierTally = {
  transportAccidentMajor: number;
  transportAccidentMinor: number;
  loadingAccident: number;
  complaints: number;
  breakdownNoComplaint: number;
};

type CarrierScore = {
  carrier: string; shipments: number; lines: ScoreLine[];
  weighted: number | null; weightAvailable: number; ungradedAccidents: number;
  tally: CarrierTally;
};

/** The columns of that report, in its order and its words. */
const TALLY_COLUMNS: [string, (t: CarrierTally) => number][] = [
  ["Transport Accident (Major)", (t) => t.transportAccidentMajor],
  ["Transport Accident (Minor)", (t) => t.transportAccidentMinor],
  ["Loading Accident", (t) => t.loadingAccident],
  ["Complaint (Internal & external)", (t) => t.complaints],
  ["Truck break down / No customer complaint", (t) => t.breakdownNoComplaint],
];

type EngineReport = {
  jobs: number; measures: Measure[]; suppliers: SupplierScore[];
  scorecard?: CarrierScore[] | null;
  unattributedIssues?: number;
  issuesInPeriod?: number;
};

/**
 * Which workspace slice answers "why is this number what it is".
 *
 * Only the measures where the register can show you the jobs. Confirmation SLA
 * and Billing have no slice to open because the records behind them do not
 * exist yet, and a link to an empty list teaches people the links do not work.
 */
const DRILL: Record<string, { kpi?: string; status?: string }> = {
  OnTimeDelivery: { kpi: "Done" },
  Delay: { kpi: "Delay" },
};

export function Kpi({ period, onPeriod, allJobs, onDrill, onOpenJobs }: {
  period: Period;
  /**
   * The picker on this screen, and the same one the dashboard carries.
   *
   * Every figure here is already computed for a period — the request has always
   * sent year, month and day — but there was nothing on the screen to change it,
   * so it only ever showed whatever period had last been chosen somewhere else.
   */
  onPeriod: (period: Period) => void;
  /** The register, so the picker can only offer months that have work in them. */
  allJobs: Job[];
  onDrill: (screen: string) => void;
  /** Opens the workspace on the jobs behind a figure. */
  onOpenJobs: (filter: { kpi?: string; trucker?: string; status?: string }) => void;
}) {
  const [report, setReport] = useState<KpiReport | null>(null);
  const [engine, setEngine] = useState<EngineReport | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      // The dimming while a new period loads is set inside the async body, not
      // straight away in the effect: React flags a synchronous setState there,
      // and the request is what the flag is really about anyway.
      setLoading(true);
      try {
        const query = new URLSearchParams();
        if (period.year && period.year !== "ALL") query.set("year", period.year);
        if (period.month && period.month !== "ALL") query.set("month", period.month);
        if (period.day && period.day !== "ALL") query.set("day", period.day.slice(0, 2));

        // apiFetch, not fetch: it carries the signed-in identity the API refuses
        // to answer without.
        const withTrend = new URLSearchParams(query);
        withTrend.set("trend", "true");

        const [operational, measures] = await Promise.all([
          apiFetch(`/api/kpi?${query}`, { headers: { accept: "application/json" } }),
          apiFetch(`/api/kpi/measures?${withTrend}`, { headers: { accept: "application/json" } }),
        ]);
        if (!operational.ok) throw new Error("HTTP " + operational.status);
        const body = await operational.json() as KpiReport;
        const engineBody = measures.ok ? await measures.json() as EngineReport : null;
        if (!cancelled) { setReport(body); setEngine(engineBody); setError(""); }
      } catch (problem) {
        if (!cancelled) setError(problem instanceof Error ? problem.message : String(problem));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [period]);

  // Drawn above every state of this screen, including the two that return
  // early. A period you cannot change while the figures are loading, or after
  // they failed, is a period you are stuck with — and the usual reason a KPI
  // request fails is that it was asked for a month nobody has data for.
  const bar = (
    <PeriodBar
      allJobs={allJobs}
      shown={report?.total ?? 0}
      period={period}
      onPeriod={onPeriod}
    />
  );

  if (error) {
    return (
      <div style={css("display:flex;flex-direction:column;gap:14px")}>
        {bar}
        <div style={css("background:#fff;border:1px solid #F3C9C4;border-left:3px solid #B42318;border-radius:5px;padding:20px 22px")}>
          <div style={css("font-size:13.5px;font-weight:650;color:#B42318;margin-bottom:5px")}>คำนวณ KPI ไม่สำเร็จ</div>
          <div style={css("font-size:12.5px;color:#5A6B7D")}>{error}</div>
        </div>
      </div>
    );
  }

  if (!report) {
    return (
      <div style={css("display:flex;flex-direction:column;gap:14px")}>
        {bar}
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
          กำลังคำนวณ KPI…
        </div>
      </div>
    );
  }

  const done = report.byStatus.filter((s) => STATUS_RE.done.test(s.label))
    .reduce((sum, s) => sum + s.value, 0);

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      {bar}
      {/* The figures dim while a new period is being fetched; the picker does
          not, because it is what somebody is using at that moment. */}
      <div style={css("display:flex;flex-direction:column;gap:14px;opacity:" + (loading ? ".55" : "1"))}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:11px")}>
        <Tile label="งานทั้งหมด" value={report.total.toLocaleString()} note={`${report.byDay.length} วันปฏิบัติงาน`} colour="#0A2240" />
        <Tile
          label="ตรงเวลา"
          value={report.onTime.base ? report.onTime.percent + "%" : "—"}
          note={report.onTime.base
            ? `${report.onTime.met.toLocaleString()} จาก ${report.onTime.base.toLocaleString()} งานที่วัดได้`
            : "ไม่มีงานที่วัดได้"}
          colour={report.onTime.percent >= 80 ? "#16794C" : report.onTime.percent >= 60 ? "#B45309" : "#B42318"}
        />
        <Tile label="เสร็จสิ้น" value={done.toLocaleString()} note="ตามสถานะในทะเบียน" colour="#16794C" />
        <Tile label="ต้องดำเนินการ" value={report.actionRequired.toLocaleString()} note="ข้อมูลผิดหรือยังขาด" colour="#B45309" onClick={() => onDrill("myjob")} />
        <Tile label="รูปแบบข้อมูลผิด" value={report.formatErrors.toLocaleString()} note="ค่าที่อ่านไม่ได้ในฐานข้อมูล" colour="#B42318" />
        <Tile label="เสี่ยงตกเรือ (Export)" value={report.gateInRisk.toLocaleString()} note="เวลาปิดตู้มาก่อนรถถึง" colour="#B42318" />
      </div>

      {engine && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
            <div>
              <div style={css("font-size:13px;font-weight:650;color:#0A2240")}>ตัวชี้วัดหลัก 8 ตัว</div>
              <div style={css("font-size:11.5px;color:#94A3B8;margin-top:1px")}>
                ตัวไหนยังไม่มีข้อมูลจะบอกว่า “ยังวัดไม่ได้” — ไม่แสดงเป็น 0% หรือ 100%
              </div>
            </div>
            <button
              onClick={() => void download(period, setExporting)}
              disabled={exporting}
              style={css("height:31px;padding:0 14px;border:1px solid #0A2240;background:" + (exporting ? "#C3CFDB" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
            >{exporting ? "กำลังสร้าง…" : "ดาวน์โหลด Excel"}</button>
          </div>
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead>
                <tr>{["ตัวชี้วัด", "ค่า", "เป้า", "แนวโน้ม", "ฐานที่วัด", "รายละเอียด"].map((h, i) => (
                  <th key={h} style={css(
                    "padding:8px 14px;text-align:" + (i >= 1 && i <= 2 || i === 4 ? "right" : "left") +
                    ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;white-space:nowrap",
                  )}>{h}</th>
                ))}</tr>
              </thead>
              <tbody>
                {engine.measures.map((measure) => {
                  const drill = DRILL[measure.id];
                  return (
                    <tr key={measure.id} style={css("border-bottom:1px solid #F1F5F9;vertical-align:top")}>
                      <td style={css("padding:9px 14px")}>
                        {drill ? (
                          <button onClick={() => onOpenJobs(drill)}
                            style={css("border:none;background:none;padding:0;font-family:inherit;text-align:left;cursor:pointer")}>
                            <div style={css("font-weight:600;color:#0A5FA8")}>{measure.thai} →</div>
                            <div style={css("font-size:11px;color:#94A3B8")}>{measure.english}</div>
                          </button>
                        ) : (
                          <>
                            <div style={css("font-weight:600;color:#0A2240")}>{measure.thai}</div>
                            <div style={css("font-size:11px;color:#94A3B8")}>{measure.english}</div>
                          </>
                        )}
                      </td>
                      <td style={css("padding:9px 14px;text-align:right;font-family:ui-monospace,monospace;font-weight:600;white-space:nowrap;color:" +
                        (!measure.available ? "#B45309"
                          : measure.meetsTarget === false ? "#B42318"
                            : measure.meetsTarget === true ? "#16794C" : "#16232F"))}>
                        {measure.available && measure.value !== null
                          ? measure.kind === "Rate" ? `${measure.value}${measure.unit}` : `${measure.value.toLocaleString()} ${measure.unit}`
                          : "ยังวัดไม่ได้"}
                      </td>
                      <td style={css("padding:9px 14px;text-align:right;font-family:ui-monospace,monospace;white-space:nowrap;color:#7B8CA0")}>
                        {measure.target === null ? "—" : `${measure.target}%`}
                        {measure.meetsTarget !== null && (
                          <div style={css("font-size:10.5px;font-weight:700;color:" + (measure.meetsTarget ? "#16794C" : "#B42318"))}>
                            {measure.meetsTarget ? "ถึงเป้า" : "ต่ำกว่าเป้า"}
                          </div>
                        )}
                      </td>
                      <td style={css("padding:9px 14px")}>
                        <Trend points={measure.trend} unit={measure.unit} />
                      </td>
                      <td style={css("padding:9px 14px;text-align:right;font-family:ui-monospace,monospace;color:#7B8CA0")}>
                        {measure.base.toLocaleString()}
                      </td>
                      <td style={css("padding:9px 14px;color:#5A6B7D;font-size:11.5px;max-width:320px")}>
                        {measure.note}
                        {measure.breakdown.length > 0 && (
                          <div style={css("color:#94A3B8;margin-top:2px")}>
                            {measure.breakdown.filter((b) => b.value > 0).slice(0, 5)
                              .map((b) => `${b.label} ${b.value.toLocaleString()}`).join(" · ") || "—"}
                          </div>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {engine?.scorecard?.length ? (
        <Panel
          title="คะแนนตามสัญญา (Carrier Scorecard)"
          note={
            // Says how much it counted, not only how it counted. A card of
            // straight hundreds is either a clean month or a broken link, and
            // the reader is owed the difference.
            engine.issuesInPeriod === 0
              ? "ไม่มีรายการใน Operational Issues ในช่วงเวลานี้ — ทุกเกณฑ์จึงเป็น 100% เพราะไม่มีเหตุให้หัก ไม่ใช่เพราะระบบนับไม่เจอ"
              : `คิดจาก ${engine.issuesInPeriod} รายการใน Operational Issues ในช่วงนี้`
                + (engine.unattributedIssues
                  ? ` · ${engine.unattributedIssues} รายการเลขงานจับคู่ไม่ได้ จึงไม่เข้าคะแนนของใคร`
                  : " · ผูกกับงานได้ทั้งหมด")
          }
        >
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead>
                <tr>
                  {[["TRUCK", "left"], ["Total individual shipment", "right"],
                    ...TALLY_COLUMNS.map(([head]) => [head, "right"] as [string, string]),
                    ["คะแนนรวม", "right"]].map(([head, align]) => (
                    <th key={head} style={css("padding:8px 12px;text-align:" + align
                      + ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;vertical-align:bottom;max-width:130px")}>{head}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {engine.scorecard.map((row) => (
                  <tr key={row.carrier} style={css("border-bottom:1px solid #F1F5F9")}>
                    <td style={css("padding:8px 12px")}>
                      <button onClick={() => onOpenJobs({ trucker: row.carrier })}
                        style={css("border:none;background:none;padding:0;font-family:inherit;font-size:12.5px;font-weight:600;color:#0A5FA8;cursor:pointer")}>
                        {row.carrier} →
                      </button>
                      {row.ungradedAccidents > 0 && (
                        <div style={css("font-size:11px;color:#B45309;margin-top:2px")}>
                          อุบัติเหตุยังไม่ระบุชนิด {row.ungradedAccidents} เคส
                        </div>
                      )}
                    </td>
                    <td style={NUM_S}>{row.shipments.toLocaleString()}</td>

                    {/* Counts, as their report writes them. A zero is a real
                        answer here and is shown as one — it is the column
                        everybody wants to be zero. */}
                    {TALLY_COLUMNS.map(([head, read]) => {
                      const value = read(row.tally);
                      return (
                        <td key={head} style={css("padding:8px 12px;text-align:right;font-family:'IBM Plex Mono',monospace;color:"
                          + (value > 0 ? "#B42318;font-weight:700" : "#94A3B8"))}>
                          {value}
                        </td>
                      );
                    })}

                    <td style={css("padding:8px 12px;text-align:right;font-weight:700;font-family:'IBM Plex Mono',monospace;color:"
                      + (row.weighted == null ? "#B4C0CC" : row.weighted >= 95 ? "#16794C" : row.weighted >= 85 ? "#B45309" : "#B42318"))}>
                      {row.weighted == null ? "—" : row.weighted.toFixed(1)}
                      {row.weightAvailable < 100 && (
                        <div style={css("font-size:10.5px;color:#94A3B8;font-weight:400")}>
                          จากน้ำหนัก {row.weightAvailable}%
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          {/* What each column counts, said once under the table rather than in
              six tooltips nobody hovers over. */}
          <div style={css("padding:10px 14px;font-size:11px;color:#7B8CA0;line-height:1.8;border-top:1px solid #F1F5F9")}>
            ตัวเลขในตารางคือ<b>จำนวนครั้ง</b>ที่บันทึกไว้ใน Operational Issues ของงานที่ผู้ขนส่งรายนั้นวิ่ง ·
            อุบัติเหตุสามช่องแยกตามชนิดที่ระบุไว้ในรายการปัญหา (Transport Major / Transport Minor / Loading) ·
            ข้อร้องเรียนนับทั้งจากลูกค้า และจากภายใน (CS · Shipping · Billing · คลัง) ·
            รถเสียนับเฉพาะครั้งที่<b>ไม่มี</b>ข้อร้องเรียนจากลูกค้าในงานเดียวกัน จะได้ไม่ถูกนับซ้ำสองช่อง
            <div style={css("margin-top:5px")}>
              คะแนนรวมคิดตามน้ำหนักในสัญญา โดยเกณฑ์ที่ยังวัดไม่ได้จะไม่ถูกให้ 0
              แต่ถูกตัดออกจากน้ำหนักแล้วปรับฐานคะแนนตามน้ำหนักที่เหลือ ซึ่งแสดงกำกับไว้ใต้คะแนน
            </div>
          </div>
        </Panel>
      ) : null}

      {engine && engine.suppliers.some((s) => s.score !== null) && (
        <Panel
          title="คะแนนผู้ขนส่ง"
          note={`ให้คะแนนได้ ${engine.suppliers.filter((s) => s.score !== null).length} เจ้า · อีก ${engine.suppliers.filter((s) => s.score === null).length} เจ้ายังมีข้อมูลไม่พอ · คลิกชื่อเพื่อดูงานของเจ้านั้น`}
        >
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["ผู้ขนส่ง", "งาน", "ตรงเวลา", "ตอบยืนยัน", "ไม่มีความล่าช้า", "คะแนน"].map((h, i) => (
              <th key={h} style={css("padding:8px 14px;text-align:" + (i === 0 ? "left" : "right") +
                ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {engine.suppliers.filter((s) => s.score !== null).map((s) => (
                <tr key={s.carrier} style={css("border-bottom:1px solid #F1F5F9")}>
                  <td style={css("padding:8px 14px")}>
                    <button onClick={() => onOpenJobs({ trucker: s.carrier })}
                      style={css("border:none;background:none;padding:0;font-family:inherit;font-size:12.5px;font-weight:600;color:#0A5FA8;cursor:pointer")}>
                      {s.carrier} →
                    </button>
                  </td>
                  <td style={NUM_S}>{s.jobs.toLocaleString()}</td>
                  {/* Each component shows its own base. A carrier scored on
                      forty measured jobs and one scored on five are not the same
                      claim, and the score alone hides which is which. */}
                  <Component value={s.onTime} base={s.onTimeBase} />
                  <Component value={s.confirmation} base={s.confirmationBase} />
                  <Component value={s.delayFree} base={s.delayCount} baseLabel="ล่าช้า" />
                  <td style={css("padding:8px 14px;text-align:right;font-family:ui-monospace,monospace;font-weight:600;color:" +
                    (s.score! >= 85 ? "#16794C" : s.score! >= 70 ? "#B45309" : "#B42318"))}>{s.score}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-left:3px solid #1D5FA8;border-radius:5px;padding:12px 16px;font-size:12.5px;color:#465A6E")}>
        ทุกตัวเลขคำนวณฝั่ง .NET จากทะเบียนงานใน Azure SQL ด้วยกฎชุดเดียวกับหน้า Workspace ·
        อ่านค่าที่ <b>เก็บจริง</b> ไม่ใช่ค่าที่แก้รูปแบบให้อัตโนมัติตอนแสดงผล — ตัวเลข “รูปแบบข้อมูลผิด”
        จะลดลงเมื่อกด <b>ล้างข้อมูล</b> ใน Settings → Data เพื่อเขียนการแก้ลงฐานข้อมูล
      </div>

      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(320px,1fr));gap:14px;align-items:start")}>
        <Panel title="ภาระงานแต่ละคน" note="เปิดค้าง · ต้องดำเนินการ · ล่าช้า">
          <Table
            head={["ผู้รับผิดชอบ", "ทั้งหมด", "เปิดค้าง", "ต้องทำ", "ล่าช้า"]}
            rows={report.team.map((t) => [
              t.owner,
              t.total.toLocaleString(),
              t.open.toLocaleString(),
              t.action.toLocaleString(),
              t.late.toLocaleString(),
            ])}
          />
        </Panel>

        <Panel title="สถานะงาน" note="ตามชุดสถานะของแต่ละหมวด">
          <Table
            head={["สถานะ", "งาน", "สัดส่วน"]}
            rows={report.byStatus.map((s) => [
              s.label,
              s.value.toLocaleString(),
              report.total ? Math.round((s.value / report.total) * 100) + "%" : "—",
            ])}
          />
        </Panel>

        <Panel title="ตรงเวลาแยกตามผู้ขนส่ง" note="แสดงเฉพาะเจ้าที่มีงานวัดผลได้ · คลิกชื่อเพื่อดูงาน">
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["ผู้ขนส่ง", "งาน", "วัดได้", "ตรงเวลา", "%"].map((h, i) => (
              <th key={h} style={css("padding:8px 14px;text-align:" + (i === 0 ? "left" : "right") +
                ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {report.carriers.filter((c) => c.measured > 0).slice(0, 14).map((c) => (
                <tr key={c.carrier} style={css("border-bottom:1px solid #F1F5F9")}>
                  <td style={css("padding:8px 14px")}>
                    <button onClick={() => onOpenJobs({ trucker: c.carrier })}
                      style={css("border:none;background:none;padding:0;font-family:inherit;font-size:12.5px;font-weight:600;color:#0A5FA8;cursor:pointer")}>
                      {c.carrier} →
                    </button>
                  </td>
                  <td style={NUM_S}>{c.total.toLocaleString()}</td>
                  <td style={NUM_S}>{c.measured.toLocaleString()}</td>
                  <td style={NUM_S}>{c.onTime.toLocaleString()}</td>
                  <td style={css(NUM + ";font-weight:600;color:" +
                    (c.percent >= 95 ? "#16794C" : c.percent >= 80 ? "#B45309" : "#B42318"))}>{c.percent}%</td>
                </tr>
              ))}
              {!report.carriers.some((c) => c.measured > 0) && (
                <tr><td colSpan={5} style={css("padding:22px;text-align:center;color:#94A3B8")}>ไม่มีข้อมูลในช่วงนี้</td></tr>
              )}
            </tbody>
          </table>
        </Panel>

        <Panel title="ปริมาณงานตามหมวด" note={report.undated ? `${report.undated} งานไม่มีวันที่ใช้ได้` : "ทุกงานมีวันที่"}>
          <Table
            head={["หมวด", "งาน", "สัดส่วน"]}
            rows={report.byCategory.map((c) => [
              c.label,
              c.value.toLocaleString(),
              report.total ? Math.round((c.value / report.total) * 100) + "%" : "—",
            ])}
          />
        </Panel>
      </div>
      </div>
    </div>
  );
}

/**
 * Fetches the workbook and hands it to the browser.
 *
 * Not a plain link: the API needs the caller's identity, which a navigation
 * would not carry in development. Fetching it means the same code path works
 * signed in through the platform and signed in through the demo gate.
 */
async function download(period: Period, setBusy: (busy: boolean) => void) {
  setBusy(true);
  try {
    const query = new URLSearchParams();
    if (period.year && period.year !== "ALL") query.set("year", period.year);
    if (period.month && period.month !== "ALL") query.set("month", period.month);
    if (period.day && period.day !== "ALL") query.set("day", period.day.slice(0, 2));

    const response = await apiFetch(`/api/kpi/excel?${query}`);
    if (!response.ok) throw new Error("HTTP " + response.status);

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `SCMOS_KPI_${new Date().toISOString().slice(0, 10)}.xlsx`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  } finally {
    setBusy(false);
  }
}

/**
 * The measure over the preceding months.
 *
 * A bar per month with the latest value beside it, and the direction of travel
 * spelled out — 55% is a crisis if it was 80% last month and a recovery if it
 * was 40%. When the register only holds one month there is no trend to draw, and
 * the screen says that rather than leaving an empty cell somebody reads as a
 * loading failure.
 */
function Trend({ points, unit }: { points: TrendPoint[] | null; unit: string }) {
  const measured = (points ?? []).filter((p) => p.value !== null);
  if (measured.length < 2) {
    return (
      <div style={css("font-size:11px;color:#94A3B8;white-space:nowrap")}>
        {points === null || points.length < 2 ? "มีข้อมูลเดือนเดียว" : "วัดได้เดือนเดียว"}
      </div>
    );
  }

  const values = measured.map((p) => p.value!);
  const top = Math.max(...values, 1);
  const first = values[0];
  const last = values[values.length - 1];
  const change = Math.round((last - first) * 10) / 10;

  return (
    <div style={css("display:flex;gap:8px;align-items:flex-end;white-space:nowrap")}>
      <div style={css("display:flex;gap:2px;align-items:flex-end;height:26px")} title={
        measured.map((p) => `${p.period} ${p.value}${unit} (${p.base})`).join("\n")
      }>
        {measured.map((point) => (
          <div key={point.period}
            style={css(`width:7px;border-radius:1px;background:${point === measured[measured.length - 1] ? "#0A2240" : "#C9D6E2"};height:${Math.max(2, Math.round((point.value! / top) * 26))}px`)} />
        ))}
      </div>
      <div style={css("font-size:11px;font-family:ui-monospace,monospace;color:" +
        (change === 0 ? "#7B8CA0" : change > 0 ? "#16794C" : "#B42318"))}>
        {change > 0 ? "▲" : change < 0 ? "▼" : "="}{Math.abs(change)}
      </div>
    </div>
  );
}

function Tile({ label, value, note, colour, onClick }: {
  label: string; value: string; note: string; colour: string; onClick?: () => void;
}) {
  const body = (
    <>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:26px;font-weight:600;line-height:1.25;margin-top:3px;color:${colour}`)}>{value}</div>
      <div style={css("font-size:12px;color:#7B8CA0")}>{note}</div>
    </>
  );
  const skin = `background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:12px 15px 14px;text-align:left;width:100%`;
  return onClick
    ? <button onClick={onClick} style={css(skin + ";font-family:inherit;cursor:pointer")}>{body}</button>
    : <div style={css(skin)}>{body}</div>;
}

const NUM = "padding:8px 14px;text-align:right;font-family:ui-monospace,monospace;color:#16232F";
const NUM_S = css(NUM);

/** One component of a carrier's score, with the base it was measured over. */
function Component({ value, base, baseLabel }: { value: number | null; base: number; baseLabel?: string }) {
  return (
    <td style={css(NUM)}>
      {value === null
        ? <span style={css("color:#B45309;font-family:inherit;font-size:11.5px")}>ยังวัดไม่ได้</span>
        : <>
            <div>{value}%</div>
            <div style={css("font-size:10.5px;color:#94A3B8")}>{baseLabel ? `${baseLabel} ${base}` : base}</div>
          </>}
    </td>
  );
}

function Panel({ title, note, children }: { title: string; note: string; children: React.ReactNode }) {
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:13px;font-weight:650;color:#0A2240")}>{title}</div>
        <div style={css("font-size:11.5px;color:#94A3B8;margin-top:1px")}>{note}</div>
      </div>
      <div style={css("overflow-x:auto")}>{children}</div>
    </div>
  );
}

function Table({ head, rows }: { head: string[]; rows: string[][] }) {
  return (
    <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
      <thead>
        <tr>
          {head.map((h, i) => (
            <th key={h} style={css(
              "padding:8px 14px;text-align:" + (i === 0 ? "left" : "right") +
              ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;white-space:nowrap",
            )}>{h}</th>
          ))}
        </tr>
      </thead>
      <tbody>
        {rows.map((row, r) => (
          <tr key={r} style={css("border-bottom:1px solid #F1F5F9")}>
            {row.map((value, c) => (
              <td key={c} style={css(
                "padding:8px 14px;text-align:" + (c === 0 ? "left" : "right") +
                (c === 0 ? ";font-weight:600;color:#0A2240" : ";font-family:ui-monospace,monospace;color:#16232F"),
              )}>{value}</td>
            ))}
          </tr>
        ))}
        {!rows.length && (
          <tr><td colSpan={head.length} style={css("padding:22px;text-align:center;color:#94A3B8")}>ไม่มีข้อมูลในช่วงนี้</td></tr>
        )}
      </tbody>
    </table>
  );
}
