"use client";

import { useCallback, useEffect, useState } from "react";
import {
  Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ReferenceLine,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import { apiFetch } from "../api";
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "../ui/Card";
import { cn } from "../ui/cn";

/**
 * The Report Centre — a customer's month, as a document rather than a table.
 *
 * The thing this replaces is an Excel export with two thousand rows in it,
 * which is not a report: it is the raw material for one, handed to whoever
 * asked in the hope that they will assemble it themselves. What is here instead
 * is the finished shape — the headline figures, what each vendor did, and how
 * far any of it can be trusted.
 *
 * <b>Nothing on this screen is calculated in the browser.</b> Every count and
 * every percentage arrives from /api/reports/monthly, which reads the same
 * JobRules the KPI screen and the carrier scorecard read. A report that worked
 * its own figures out would eventually disagree with the screen it was
 * generated from, and it would do it in a PDF somebody had already emailed.
 *
 * This is the first screen in the app built with shadcn/ui and Tailwind. The
 * utilities are generated without preflight and the reset is scoped to `.sc-ui`
 * — see globals.css — so the forty screens laid out with inline styles are
 * untouched by its arrival.
 */

/** One row of the report, with the base every rate was measured over. */
type Line = {
  name: string;
  trips: number;
  measurable: number;
  onTime: number;
  late: number;
  /** Null when it could not be measured, or the sample was too small to carry a rate. */
  otd: number | null;
  coverage: number;
};

type Report = {
  customer: string;
  month: string;
  summary: Line;
  target: number;
  vendors: Line[];
  delayReasons: { label: string; value: number }[];
  trend: { month: string; otd: number | null; measurable: number }[];
  meetsTarget: boolean | null;
  confidence: string;
  minimumSample: number;
  generatedBy: string;
  generatedAt: string;
};

type Choices = {
  customers: { customer: string; trips: number; measurable: number; coverage: number }[];
  months: string[];
};

/** Which blocks the reader wants. Off is a section left out, not hidden with CSS. */
type Include = { summary: boolean; vendors: boolean; trend: boolean; delays: boolean; commentary: boolean };

const ALL_IN: Include = { summary: true, vendors: true, trend: true, delays: true, commentary: true };

/*
 * One hue for the bars, because there is one measure.
 *
 * A colour per vendor would be encoding identity that the axis already carries,
 * and it would mean a vendor changing colour whenever the filter changed the
 * count — the thing a categorical palette exists to prevent. The comparison the
 * reader is making is against the target, and a reference line says that better
 * than a colour scale.
 */
const BAR = "#2E7DD1";
const BAR_UNDER = "#93B7DE";

/*
 * Status ink for the table.
 *
 * The palette validator puts this red and green at ΔE 6.7 for deuteranopia —
 * inside the floor band, and legal only with a second encoding. So the cell
 * never carries colour alone: the word is always there beside the figure.
 */
const MET = "#16794C";
const MISSED = "#B42318";

const nf = (n: number) => n.toLocaleString("en-US");

/** A rate, or an em dash. Never "0%" for something nobody measured. */
const rate = (value: number | null) => (value === null ? "—" : `${value}%`);

export function ReportCentre({ onToast }: { onToast: (message: string) => void }) {
  const [choices, setChoices] = useState<Choices | null>(null);
  const [customer, setCustomer] = useState("");
  const [month, setMonth] = useState("");
  const [include, setInclude] = useState<Include>(ALL_IN);
  const [report, setReport] = useState<Report | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void apiFetch("/api/reports/choices", { headers: { accept: "application/json" } })
      .then(async (response) => {
        if (!response.ok || cancelled) return;
        const body = await response.json() as Choices;
        if (cancelled) return;
        setChoices(body);
        // Lead with the customer who has the most to report on rather than the
        // alphabetical first, which is nobody's starting point.
        const best = [...body.customers].sort((a, b) => b.measurable - a.measurable)[0];
        if (best) setCustomer(best.customer);
        if (body.months[0]) setMonth(body.months[0]);
      })
      .catch(() => { /* The pickers stay empty; the screen says so below. */ });
    return () => { cancelled = true; };
  }, []);

  const generate = useCallback(async () => {
    if (!customer || !month || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(
        `/api/reports/monthly?customer=${encodeURIComponent(customer)}&month=${encodeURIComponent(month)}`,
        { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => ({})) as Report & { error?: string };
      if (!response.ok) { onToast(body.error ?? `สร้างรายงานไม่สำเร็จ (${response.status})`); return; }
      setReport(body);
    } finally { setBusy(false); }
  }, [customer, month, busy, onToast]);

  const chosen = choices?.customers.find((one) => one.customer === customer);

  return (
    <div className="sc-ui flex flex-col gap-4">
      {/* ------------------------------------------------ what to report on */}
      <Card className="no-print">
        <CardHeader>
          <CardTitle>Report Centre · ศูนย์รายงาน</CardTitle>
          <CardDescription>
            เลือกลูกค้าและเดือน แล้วกดสร้างรายงาน · ดูตัวอย่างก่อนแล้วค่อยบันทึกเป็น PDF หรือ Excel
          </CardDescription>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-wrap items-end gap-3">
            <Field label="ประเภทรายงาน">
              <select disabled className="h-[34px] w-[220px] cursor-not-allowed rounded-md border border-[var(--input)] bg-[var(--muted)] px-2 text-[12.5px] text-[var(--muted-foreground)]">
                <option>Monthly Trucking Performance</option>
              </select>
            </Field>

            <Field label="ลูกค้า">
              <select value={customer} onChange={(event) => setCustomer(event.target.value)}
                className="h-[34px] w-[260px] rounded-md border border-[var(--input)] bg-white px-2 text-[12.5px] outline-none focus:border-[var(--ring)]">
                {(choices?.customers ?? []).map((one) => (
                  <option key={one.customer} value={one.customer}>
                    {one.customer} · {one.trips} เที่ยว · วัดได้ {one.coverage}%
                  </option>
                ))}
              </select>
            </Field>

            <Field label="เดือน">
              <select value={month} onChange={(event) => setMonth(event.target.value)}
                className="h-[34px] w-[130px] rounded-md border border-[var(--input)] bg-white px-2 text-[12.5px] outline-none focus:border-[var(--ring)]">
                {(choices?.months ?? []).map((one) => <option key={one} value={one}>{one}</option>)}
              </select>
            </Field>

            <button type="button" onClick={() => void generate()} disabled={busy || !customer || !month}
              className={cn(
                "h-[34px] rounded-md px-4 text-[12.5px] font-semibold text-white",
                busy || !customer || !month ? "cursor-default bg-[#8FA3B8]" : "bg-[#0A2240]")}>
              {busy ? "กำลังสร้าง…" : "สร้างรายงาน"}
            </button>
          </div>

          <div className="flex flex-wrap items-center gap-4 border-t border-[var(--border)] pt-3">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-[var(--muted-foreground)]">
              หัวข้อที่จะใส่
            </span>
            {([
              ["summary", "สรุปผู้บริหาร"],
              ["vendors", "ผลงานผู้ขนส่ง"],
              ["trend", "แนวโน้ม OTD"],
              ["delays", "สาเหตุความล่าช้า"],
              ["commentary", "บทสรุปผู้บริหาร"],
            ] as [keyof Include, string][]).map(([key, label]) => (
              <label key={key} className="flex cursor-pointer items-center gap-2 text-[12.5px]">
                <input type="checkbox" checked={include[key]} className="size-[15px] cursor-pointer accent-[#2E7DD1]"
                  onChange={(event) => setInclude((was) => ({ ...was, [key]: event.target.checked }))} />
                {label}
              </label>
            ))}
          </div>

          {/*
           * The warning before the report, not after it.
           *
           * A customer whose trips carry no arrival times cannot have an OTD,
           * and the moment to learn that is while choosing them — not from a
           * finished PDF with a dash where the headline should be.
           */}
          {chosen && chosen.coverage < 60 && (
            <p className="rounded-md bg-[#FFF8F5] px-3 py-2 text-[12px] leading-relaxed text-[#9A3412]">
              {chosen.customer} มี {nf(chosen.trips)} เที่ยว แต่บันทึกเวลาถึงไว้เพียง {nf(chosen.measurable)} เที่ยว
              ({chosen.coverage}%) — รายงาน OTD ของลูกค้ารายนี้ยังไม่มีข้อมูลพอจะสรุป
            </p>
          )}
        </CardContent>
      </Card>

      {report && <ReportPage key={`${report.customer}|${report.month}`}
        report={report} include={include} onToast={onToast} />}
    </div>
  );
}

/* ------------------------------------------------------------ the document */

function ReportPage({ report, include, onToast }: {
  report: Report; include: Include; onToast: (m: string) => void;
}) {
  const { summary, target } = report;
  // Two points is the fewest that can show a direction. One is a dot, and a
  // chart of one dot invites a reader to see a trend that was never measured.
  const trend = report.trend.filter((one) => one.otd !== null);
  const showTrend = include.trend && trend.length >= 2;
  const vendors = report.vendors.filter((one) => one.otd !== null);

  return (
    <>
      <div className="report-page flex flex-col gap-4 rounded-lg border border-[var(--border)] bg-white p-7">
        {/* ---- masthead ---- */}
        <header className="report-block border-b-2 border-[var(--primary)] pb-4">
          <div className="flex flex-wrap items-baseline justify-between gap-3">
            <div>
              <h1 className="text-[22px] font-bold tracking-tight text-[var(--primary)]">
                {report.customer}
              </h1>
              <p className="mt-1 text-[13px] text-[var(--muted-foreground)]">
                Monthly Trucking Performance Report · {report.month}
              </p>
            </div>
            <div className="text-right text-[10.5px] leading-relaxed text-[var(--muted-foreground)]">
              <div className="font-semibold tracking-wider text-[var(--primary)]">SCMOS</div>
              <div>ออกโดย {report.generatedBy}</div>
              <div>{report.generatedAt}</div>
            </div>
          </div>
        </header>

        {include.summary && (
          <section className="report-block flex flex-col gap-3">
            <SectionTitle en="Executive Summary" th="สรุปผู้บริหาร" />
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Tile label="TRIPS" th="เที่ยวทั้งหมด" value={nf(summary.trips)} />
              <Tile label="ON TIME" th="ตรงเวลา" value={nf(summary.onTime)} />
              <Tile label="DELAYED" th="ล่าช้า" value={nf(summary.late)} />
              <Tile
                label="OTD"
                th={`วัดจาก ${nf(summary.measurable)} เที่ยว`}
                value={rate(summary.otd)}
                tone={report.meetsTarget === null ? undefined : report.meetsTarget ? MET : MISSED}
              />
            </div>

            {/*
             * The target beside the score, and the verdict in words.
             *
             * Never a colour on its own: the validator puts this green and red
             * at ΔE 6.7 for deuteranopia, which is legal only with a second
             * encoding. The sentence is that encoding.
             */}
            <p className="text-[12.5px] leading-relaxed">
              <span className="text-[var(--muted-foreground)]">เป้าหมาย {target}% · </span>
              {report.meetsTarget === null ? (
                <span className="font-semibold text-[var(--muted-foreground)]">ยังสรุปไม่ได้</span>
              ) : report.meetsTarget ? (
                <span className="font-semibold" style={{ color: MET }}>▲ ถึงเป้าหมาย</span>
              ) : (
                <span className="font-semibold" style={{ color: MISSED }}>▼ ต่ำกว่าเป้าหมาย</span>
              )}
            </p>

            <p className={cn(
              "rounded-md px-3 py-2 text-[11.5px] leading-relaxed",
              summary.coverage >= 90
                ? "bg-[#F1FAF5] text-[#16794C]"
                : "bg-[#FFF8F5] text-[#9A3412]")}>
              ความครบถ้วนของข้อมูล · {report.confidence}
              {summary.measurable < summary.trips && (
                <> · {nf(summary.trips - summary.measurable)} เที่ยวไม่ได้บันทึกเวลาถึง จึงไม่ถูกนับใน OTD</>
              )}
            </p>
          </section>
        )}

        {include.vendors && report.vendors.length > 0 && (
          <section className="report-block flex flex-col gap-3">
            <SectionTitle en="Vendor Performance" th="ผลงานผู้ขนส่ง" />

            {vendors.length > 0 && (
              <div className="h-[260px] w-full">
                <ResponsiveContainer width="100%" height="100%">
                  <BarChart data={vendors} layout="vertical" margin={{ top: 4, right: 44, bottom: 4, left: 4 }}>
                    <CartesianGrid horizontal={false} stroke="#EEF3F8" />
                    <XAxis type="number" domain={[0, 100]} unit="%" tick={{ fontSize: 11, fill: "#7B8CA0" }}
                      axisLine={false} tickLine={false} />
                    <YAxis type="category" dataKey="name" width={92} tick={{ fontSize: 11, fill: "#31465C" }}
                      axisLine={false} tickLine={false} />
                    <Tooltip
                      cursor={{ fill: "#F4F7FA" }}
                      contentStyle={{ fontSize: 12, borderRadius: 6, border: "1px solid #D8E0E8" }}
                      formatter={(value, _n, item) => {
                        const row = item.payload as Line;
                        return [`${value}% · ตรงเวลา ${row.onTime} จาก ${row.measurable} เที่ยว`, "OTD"];
                      }} />
                    {/* The comparison the reader is actually making. */}
                    <ReferenceLine x={target} stroke="#B42318" strokeDasharray="4 3"
                      label={{ value: `เป้า ${target}%`, position: "top", fontSize: 10, fill: "#B42318" }} />
                    <Bar dataKey="otd" radius={[0, 4, 4, 0]} barSize={16} isAnimationActive={false}>
                      {vendors.map((one) => (
                        // Recessive rather than alarming for the ones below the
                        // line: the line already says which those are, and a
                        // wall of red would make the chart an accusation.
                        <Cell key={one.name} fill={(one.otd ?? 0) >= target ? BAR : BAR_UNDER} />
                      ))}
                    </Bar>
                  </BarChart>
                </ResponsiveContainer>
              </div>
            )}

            {/* The table is the chart's accessible twin, and carries the rows
                the chart cannot draw — the ones with no rate to plot.

                Its own overflow rather than a ZoomBox: this is a page that gets
                printed, and a zoom control would be printed with it. Seven
                columns fit an A4 and a laptop; the scroll is for a phone. */}
            <div className="-mx-1 overflow-x-auto px-1">
            <table className="w-full min-w-[560px] border-collapse text-[12px]">
              <thead>
                <tr className="border-b border-[var(--border)] text-left text-[10.5px] uppercase tracking-wider text-[var(--muted-foreground)]">
                  <th className="py-2 pr-3 font-semibold">Vendor</th>
                  <th className="py-2 pr-3 text-right font-semibold">Trips</th>
                  <th className="py-2 pr-3 text-right font-semibold">วัดได้</th>
                  <th className="py-2 pr-3 text-right font-semibold">ตรงเวลา</th>
                  <th className="py-2 pr-3 text-right font-semibold">ล่าช้า</th>
                  <th className="py-2 pr-3 text-right font-semibold">OTD</th>
                  <th className="py-2 font-semibold">เทียบเป้า</th>
                </tr>
              </thead>
              <tbody>
                {report.vendors.map((one) => (
                  <tr key={one.name} className="border-b border-[#F4F7FA]">
                    <td className="py-[7px] pr-3 font-semibold text-[var(--primary)]">{one.name}</td>
                    <td className="py-[7px] pr-3 text-right tabular-nums">{nf(one.trips)}</td>
                    <td className="py-[7px] pr-3 text-right tabular-nums text-[var(--muted-foreground)]">{nf(one.measurable)}</td>
                    <td className="py-[7px] pr-3 text-right tabular-nums">{nf(one.onTime)}</td>
                    <td className="py-[7px] pr-3 text-right tabular-nums">{nf(one.late)}</td>
                    <td className="py-[7px] pr-3 text-right font-semibold tabular-nums">{rate(one.otd)}</td>
                    <td className="py-[7px] text-[11px]">
                      {one.otd === null ? (
                        <span className="text-[var(--muted-foreground)]">
                          วัดได้ {one.measurable} เที่ยว · น้อยกว่า {report.minimumSample} ยังไม่คิดเป็น %
                        </span>
                      ) : one.otd >= target ? (
                        <span style={{ color: MET }}>▲ ถึงเป้า</span>
                      ) : (
                        <span style={{ color: MISSED }}>▼ ต่ำกว่าเป้า</span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
          </section>
        )}

        {showTrend && (
          <section className="report-block flex flex-col gap-3">
            <SectionTitle en="OTD Trend" th="แนวโน้ม OTD" />
            <div className="h-[220px] w-full">
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={trend} margin={{ top: 8, right: 16, bottom: 4, left: 4 }}>
                  <CartesianGrid vertical={false} stroke="#EEF3F8" />
                  <XAxis dataKey="month" tick={{ fontSize: 11, fill: "#7B8CA0" }} axisLine={false} tickLine={false} />
                  <YAxis domain={[0, 100]} unit="%" tick={{ fontSize: 11, fill: "#7B8CA0" }}
                    axisLine={false} tickLine={false} />
                  <Tooltip
                    contentStyle={{ fontSize: 12, borderRadius: 6, border: "1px solid #D8E0E8" }}
                    formatter={(value, _n, item) => {
                      const row = item.payload as { measurable: number };
                      return [`${value}% · วัดจาก ${row.measurable} เที่ยว`, "OTD"];
                    }} />
                  <ReferenceLine y={target} stroke="#B42318" strokeDasharray="4 3"
                    label={{ value: `เป้า ${target}%`, position: "right", fontSize: 10, fill: "#B42318" }} />
                  <Line type="monotone" dataKey="otd" stroke={BAR} strokeWidth={2}
                    dot={{ r: 4, fill: BAR, stroke: "#fff", strokeWidth: 2 }} isAnimationActive={false} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </section>
        )}

        {include.trend && trend.length < 2 && (
          <p className="report-block rounded-md bg-[var(--muted)] px-3 py-2 text-[11.5px] text-[var(--muted-foreground)]">
            แนวโน้ม OTD ยังวาดไม่ได้ — ทะเบียนมีข้อมูลของลูกค้ารายนี้ที่วัดได้เพียง {trend.length} เดือน
            ต้องมีอย่างน้อย 2 เดือนจึงจะเห็นทิศทาง
          </p>
        )}

        {include.delays && (
          <section className="report-block flex flex-col gap-3">
            <SectionTitle en="Delay Reasons" th="สาเหตุความล่าช้า" />
            {report.delayReasons.length > 0 ? (
              <ol className="flex flex-col gap-2">
                {report.delayReasons.map((one, at) => (
                  <li key={one.label} className="flex items-baseline gap-3 text-[12.5px]">
                    <span className="w-5 text-right tabular-nums text-[var(--muted-foreground)]">{at + 1}.</span>
                    <span className="flex-1">{one.label}</span>
                    <span className="font-semibold tabular-nums">{nf(one.value)} เที่ยว</span>
                  </li>
                ))}
              </ol>
            ) : (
              // Said rather than left blank. An empty heading reads as "no
              // delays", and what it means is "nobody wrote down why".
              <p className="rounded-md bg-[var(--muted)] px-3 py-2 text-[11.5px] text-[var(--muted-foreground)]">
                เดือนนี้มี {nf(summary.late)} เที่ยวที่ถึงช้ากว่าแผน แต่ยังไม่มีใครบันทึกสาเหตุไว้ในระบบ —
                รายงานจึงบอกไม่ได้ว่าเพราะอะไร
              </p>
            )}
          </section>
        )}

        {include.commentary && (
          <Commentary customer={report.customer} month={report.month} onToast={onToast} />
        )}

        <footer className="report-block border-t border-[var(--border)] pt-3 text-[10.5px] leading-relaxed text-[var(--muted-foreground)]">
          ทุกอัตราในรายงานนี้คิดจากเที่ยวที่บันทึกเวลาถึงไว้เท่านั้น และแสดงจำนวนฐานกำกับไว้ทุกจุด ·
          เที่ยวที่ไม่มีเวลาถึงจะไม่ถูกนับเป็นทั้งตรงเวลาและล่าช้า · ออกจาก SCMOS เมื่อ {report.generatedAt}
        </footer>
      </div>

      <div className="no-print flex flex-wrap gap-2">
        <button type="button" onClick={() => window.print()}
          className="h-[34px] rounded-md px-4 text-[12.5px] font-semibold text-white"
          style={{ backgroundColor: "#0A2240" }}>
          พิมพ์ / บันทึกเป็น PDF
        </button>
        <button type="button" onClick={() => downloadCsv(report, onToast)}
          className="h-[34px] rounded-md border border-[var(--input)] bg-white px-4 text-[12.5px] font-semibold text-[var(--primary)]">
          ดาวน์โหลด Excel
        </button>
      </div>
    </>
  );
}

/**
 * The management summary — typed by a person, or drafted by the assistant and
 * then edited by a person.
 *
 * A textarea, always. The assistant fills it in when asked and never otherwise:
 * pressing that button sends this customer's trip counts and carrier names to
 * OpenAI, which is data leaving the building, and it should happen because
 * somebody decided to rather than because a report was opened.
 *
 * What comes back has already been through ReportCommentary.Judge on the API,
 * which throws away any draft citing a figure the report does not contain. So
 * the text in this box is either something a person wrote or something whose
 * every number was checked against the page it sits on.
 */
function Commentary({ customer, month, onToast }: {
  customer: string; month: string; onToast: (m: string) => void;
}) {
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [drafted, setDrafted] = useState(false);

  async function draft() {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(
        `/api/reports/commentary?customer=${encodeURIComponent(customer)}&month=${encodeURIComponent(month)}`,
        { method: "POST", headers: { accept: "application/json" } });
      const body = await response.json().catch(() => ({})) as { text?: string; error?: string };
      if (!response.ok || !body.text) {
        onToast(body.error ?? `ขอบทสรุปไม่สำเร็จ (${response.status})`);
        return;
      }
      setText(body.text);
      setDrafted(true);
    } finally { setBusy(false); }
  }

  return (
    <section className="report-block flex flex-col gap-3">
      <SectionTitle en="Management Summary" th="บทสรุปผู้บริหาร" />

      <div className="no-print flex flex-wrap items-center gap-3">
        <button type="button" onClick={() => void draft()} disabled={busy}
          className={cn("h-[30px] rounded-md border border-[var(--input)] bg-white px-3 text-[12px] font-semibold",
            busy ? "text-[var(--muted-foreground)]" : "text-[var(--primary)]")}>
          {busy ? "กำลังร่าง…" : "ให้ผู้ช่วยร่างให้"}
        </button>
        <span className="text-[11px] leading-relaxed text-[var(--muted-foreground)]">
          กดแล้วระบบจะส่ง <b>จำนวนเที่ยว ตรงเวลา ล่าช้า และชื่อผู้ขนส่ง</b> ของรายงานนี้ไปให้ OpenAI ·
          ไม่ส่งราคา ไม่ส่งชื่อคนขับหรือเบอร์โทร · ตัวเลขทุกตัวที่ AI เขียนถูกตรวจกับรายงานก่อนแสดง
        </span>
      </div>

      <textarea
        value={text}
        onChange={(event) => { setText(event.target.value); setDrafted(false); }}
        rows={5}
        placeholder="เขียนบทสรุปสำหรับผู้บริหาร หรือกดปุ่มด้านบนให้ผู้ช่วยร่างให้แล้วแก้ต่อ"
        className="w-full resize-y rounded-md border border-[var(--input)] bg-white p-3 text-[12.5px] leading-relaxed outline-none focus:border-[var(--ring)] print:border-0 print:p-0" />

      {/* Marked on the page itself, not only in the editor. Whoever reads the
          PDF should know which paragraph a person wrote and which one they
          only approved. */}
      {drafted && text.trim().length > 0 && (
        <p className="text-[10.5px] text-[var(--muted-foreground)]">
          ร่างโดยผู้ช่วย AI จากตัวเลขในรายงานนี้ · ตรวจและแก้ไขก่อนส่งให้ลูกค้า
        </p>
      )}
    </section>
  );
}

/* ---------------------------------------------------------------- the parts */

function SectionTitle({ en, th }: { en: string; th: string }) {
  return (
    <h2 className="flex items-baseline gap-2 border-b border-[var(--border)] pb-1.5">
      <span className="text-[13px] font-bold uppercase tracking-wider text-[var(--primary)]">{en}</span>
      <span className="text-[11.5px] text-[var(--muted-foreground)]">· {th}</span>
    </h2>
  );
}

/** A headline figure. Tabular so a column of them lines up. */
function Tile({ label, th, value, tone }: { label: string; th: string; value: string; tone?: string }) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--muted)] px-3 py-3">
      <div className="text-[22px] font-bold tabular-nums leading-none" style={{ color: tone ?? "#0A2240" }}>
        {value}
      </div>
      <div className="mt-1.5 text-[10px] font-semibold uppercase tracking-wider text-[var(--muted-foreground)]">
        {label}
      </div>
      <div className="text-[10.5px] text-[var(--muted-foreground)]">{th}</div>
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="flex flex-col gap-1">
      <span className="text-[11px] font-semibold text-[var(--muted-foreground)]">{label}</span>
      {children}
    </div>
  );
}

/**
 * The report as a spreadsheet.
 *
 * The summary and the vendor table, with every base beside every rate — not the
 * two thousand rows the old export handed over. A CSV rather than a workbook
 * because that is all this shape needs, and it opens in Excel either way; the
 * BOM is what stops Excel reading the Thai as mojibake.
 */
function downloadCsv(report: Report, onToast: (m: string) => void) {
  const q = (value: string | number | null) =>
    value === null ? "" : `"${String(value).replace(/"/g, '""')}"`;

  const rows: (string | number | null)[][] = [
    ["SCMOS · Monthly Trucking Performance"],
    ["ลูกค้า", report.customer],
    ["เดือน", report.month],
    ["ออกโดย", report.generatedBy],
    ["เมื่อ", report.generatedAt],
    ["ความครบถ้วนของข้อมูล", report.confidence],
    [],
    ["สรุป", "เที่ยวทั้งหมด", "วัดได้", "ตรงเวลา", "ล่าช้า", "OTD %", "เป้าหมาย %"],
    [report.customer, report.summary.trips, report.summary.measurable,
      report.summary.onTime, report.summary.late, report.summary.otd, report.target],
    [],
    ["ผู้ขนส่ง", "เที่ยวทั้งหมด", "วัดได้", "ตรงเวลา", "ล่าช้า", "OTD %"],
    ...report.vendors.map((one) =>
      [one.name, one.trips, one.measurable, one.onTime, one.late, one.otd]),
  ];

  if (report.delayReasons.length > 0) {
    rows.push([], ["สาเหตุความล่าช้า", "จำนวนเที่ยว"]);
    rows.push(...report.delayReasons.map((one) => [one.label, one.value]));
  }

  const csv = "﻿" + rows.map((row) => row.map(q).join(",")).join("\r\n");
  const url = URL.createObjectURL(new Blob([csv], { type: "text/csv;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = `SCMOS_${report.customer.replace(/[^\w]+/g, "_")}_${report.month.replace("/", "-")}.csv`;
  link.click();
  URL.revokeObjectURL(url);
  onToast("ดาวน์โหลดไฟล์แล้ว");
}
