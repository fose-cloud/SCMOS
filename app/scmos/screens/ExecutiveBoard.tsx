"use client";

import { useEffect, useState } from "react";
import {
  Bar, BarChart, CartesianGrid, Cell, Line, LineChart, ReferenceLine,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from "recharts";
import { apiFetch } from "../api";
import { Card, CardContent, CardHeader, CardTitle } from "../ui/Card";
import { cn } from "../ui/cn";

/**
 * The month, as the API measured it.
 *
 * The panels below this one are worked out in the browser from the jobs it
 * already holds, which is right for a screen that filters and drills. This band
 * is the other thing: the figures the business reports upward, computed once on
 * the server by the same engine the KPI screen and the carrier scorecard use.
 *
 * That split is deliberate rather than accidental. "How many jobs are showing"
 * is a question about this screen and belongs to it; "what was our on-time
 * delivery" is a question about the operation, and it must give the same answer
 * here, on the KPI page, and in a report emailed to a customer. Every rate below
 * carries the base it was measured over for the same reason — 55% of 631 and
 * 55% of 6 are not the same claim, and only one of them is worth acting on.
 */

type Trend = { period: string; value: number | null; base: number };

type Measure = {
  id: string;
  english: string;
  thai: string;
  kind: string;
  available: boolean;
  /** Percent for a rate, a count for a count. Null when it cannot be measured. */
  value: number | null;
  base: number;
  unit: string;
  note: string;
  target: number | null;
  meetsTarget: boolean | null;
  trend: Trend[] | null;
};

type Supplier = {
  carrier: string;
  jobs: number;
  onTime: number | null;
  onTimeBase: number;
  score: number | null;
};

type Report = { jobs: number; measures: Measure[]; suppliers: Supplier[]; computedAt: string };

/*
 * One hue for the marks, the target as a line.
 *
 * The same choice the Report Centre made and for the same reason: there is one
 * measure on each chart, so a colour scale would be encoding an identity the
 * axis already carries. Validated against the app's surface — see the note in
 * ReportCentre on the status pair.
 */
const BAR = "#2E7DD1";
const BAR_UNDER = "#93B7DE";
const MET = "#16794C";
const MISSED = "#B42318";

/**
 * Below this many measured jobs a percentage says more about the sample than
 * about the carrier. The same five the monthly report uses.
 */
const MINIMUM_SAMPLE = 5;

const nf = (n: number) => n.toLocaleString("en-US");

export function ExecutiveBoard({ onOpenKpi }: { onOpenKpi?: () => void }) {
  const [report, setReport] = useState<Report | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let cancelled = false;
    void apiFetch("/api/kpi/measures?trend=true", { headers: { accept: "application/json" } })
      .then(async (response) => {
        if (cancelled) return;
        if (!response.ok) { setFailed(true); return; }
        const body = await response.json() as Report;
        if (!cancelled) setReport(body);
      })
      .catch(() => { if (!cancelled) setFailed(true); });
    return () => { cancelled = true; };
  }, []);

  // Said rather than left blank. A band that silently disappears when the API
  // is unreachable looks exactly like a month with nothing in it.
  if (failed) {
    return (
      <div className="sc-ui">
        <Card>
          <CardContent className="pt-5 text-[12px] text-[#B45309]">
            อ่านตัวชี้วัดจากเซิร์ฟเวอร์ไม่สำเร็จ — ตัวเลขด้านล่างยังใช้ได้ตามปกติ
          </CardContent>
        </Card>
      </div>
    );
  }
  if (!report) return null;

  const otd = report.measures.find((one) => one.id === "OnTimeDelivery");
  const trend = (otd?.trend ?? []).filter((one) => one.value !== null);

  // Only the carriers a rate can honestly be quoted for. The rest are named in
  // the table on the KPI screen; a bar chart of one-trip carriers at 100% is a
  // chart that ranks noise.
  const carriers = report.suppliers
    .filter((one) => one.onTime !== null && one.onTimeBase >= MINIMUM_SAMPLE)
    .sort((a, b) => (b.onTime ?? 0) - (a.onTime ?? 0))
    .slice(0, 10);

  const thin = report.suppliers.length - carriers.length;
  const target = otd?.target ?? null;

  return (
    <div className="sc-ui flex flex-col gap-4">
      <Card>
        <CardHeader className="flex-row items-baseline justify-between gap-3 pb-2">
          <div className="flex items-baseline gap-2">
            <CardTitle>ตัวชี้วัดจากทะเบียนงานจริง</CardTitle>
            <span className="text-[11px] text-[var(--muted-foreground)]">
              คำนวณฝั่งเซิร์ฟเวอร์ · ชุดเดียวกับหน้า KPI และรายงาน
            </span>
          </div>
          {onOpenKpi && (
            <button type="button" onClick={onOpenKpi}
              className="text-[11.5px] font-semibold text-[#1D5FA8] hover:underline">
              ดูทั้งหมด →
            </button>
          )}
        </CardHeader>

        <CardContent className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-4">
          <Tile label="TOTAL TRIPS" th="เที่ยวทั้งหมดในทะเบียน" value={nf(report.jobs)} />
          {report.measures.map((one) => <MeasureTile key={one.id} measure={one} />)}
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        {/* ---- how on-time delivery has moved ---- */}
        <Card>
          <CardHeader className="pb-1">
            <CardTitle>แนวโน้มการส่งมอบตรงเวลา</CardTitle>
          </CardHeader>
          <CardContent>
            {trend.length >= 2 ? (
              <div className="h-[220px] w-full">
                <ResponsiveContainer width="100%" height="100%">
                  <LineChart data={trend} margin={{ top: 8, right: 14, bottom: 4, left: 0 }}>
                    <CartesianGrid vertical={false} stroke="#EEF3F8" />
                    <XAxis dataKey="period" tick={{ fontSize: 11, fill: "#7B8CA0" }}
                      axisLine={false} tickLine={false} />
                    <YAxis domain={[0, 100]} unit="%" tick={{ fontSize: 11, fill: "#7B8CA0" }}
                      axisLine={false} tickLine={false} width={38} />
                    <Tooltip
                      contentStyle={{ fontSize: 12, borderRadius: 6, border: "1px solid #D8E0E8" }}
                      formatter={(value, _n, item) => {
                        const row = item.payload as Trend;
                        return [`${value}% · วัดจาก ${nf(row.base)} เที่ยว`, "ตรงเวลา"];
                      }} />
                    {target !== null && (
                      <ReferenceLine y={target} stroke={MISSED} strokeDasharray="4 3"
                        label={{ value: `เป้า ${target}%`, position: "right", fontSize: 10, fill: MISSED }} />
                    )}
                    <Line type="monotone" dataKey="value" stroke={BAR} strokeWidth={2}
                      dot={{ r: 4, fill: BAR, stroke: "#fff", strokeWidth: 2 }} isAnimationActive={false} />
                  </LineChart>
                </ResponsiveContainer>
              </div>
            ) : (
              /*
               * One point is a dot, not a direction — and saying so is more use
               * than an axis with a single mark on it.
               *
               * Careful about what it claims. The engine sends no trend at all
               * when the register holds fewer than two months, so `trend.length`
               * is zero in that case and printing it would say "0 months" of a
               * register that plainly has one. What is true either way is that
               * there is not enough to draw a direction from.
               */
              <p className="rounded-md bg-[var(--muted)] px-3 py-3 text-[11.5px] leading-relaxed text-[var(--muted-foreground)]">
                ยังวาดแนวโน้มไม่ได้ — ต้องมีอย่างน้อย 2 เดือนที่วัดผลได้จึงจะเห็นทิศทาง
                {trend.length === 1 && " · ตอนนี้มีเดือนเดียว"}
              </p>
            )}
          </CardContent>
        </Card>

        {/* ---- who is carrying the month, and how well ---- */}
        <Card>
          <CardHeader className="pb-1">
            <CardTitle>ตรงเวลาแยกตามผู้ขนส่ง</CardTitle>
          </CardHeader>
          <CardContent>
            {carriers.length > 0 ? (
              <>
                <div className="h-[220px] w-full">
                  <ResponsiveContainer width="100%" height="100%">
                    <BarChart data={carriers} layout="vertical"
                      margin={{ top: 4, right: 40, bottom: 4, left: 0 }}>
                      <CartesianGrid horizontal={false} stroke="#EEF3F8" />
                      <XAxis type="number" domain={[0, 100]} unit="%"
                        tick={{ fontSize: 11, fill: "#7B8CA0" }} axisLine={false} tickLine={false} />
                      <YAxis type="category" dataKey="carrier" width={86}
                        tick={{ fontSize: 10.5, fill: "#31465C" }} axisLine={false} tickLine={false} />
                      <Tooltip
                        cursor={{ fill: "#F4F7FA" }}
                        contentStyle={{ fontSize: 12, borderRadius: 6, border: "1px solid #D8E0E8" }}
                        formatter={(value, _n, item) => {
                          const row = item.payload as Supplier;
                          return [`${value}% · วัดจาก ${nf(row.onTimeBase)} เที่ยว จากทั้งหมด ${nf(row.jobs)}`, "ตรงเวลา"];
                        }} />
                      {target !== null && (
                        <ReferenceLine x={target} stroke={MISSED} strokeDasharray="4 3" />
                      )}
                      <Bar dataKey="onTime" radius={[0, 4, 4, 0]} barSize={14} isAnimationActive={false}>
                        {carriers.map((one) => (
                          <Cell key={one.carrier}
                            fill={target !== null && (one.onTime ?? 0) < target ? BAR_UNDER : BAR} />
                        ))}
                      </Bar>
                    </BarChart>
                  </ResponsiveContainer>
                </div>
                {thin > 0 && (
                  // The chart is a selection, and a chart that does not say so
                  // reads as the whole list.
                  <p className="mt-2 text-[11px] text-[var(--muted-foreground)]">
                    แสดงเฉพาะผู้ขนส่งที่วัดผลได้ตั้งแต่ {MINIMUM_SAMPLE} เที่ยวขึ้นไป ·
                    อีก {nf(thin)} รายมีเที่ยวที่วัดผลได้น้อยเกินกว่าจะคิดเป็นเปอร์เซ็นต์
                  </p>
                )}
              </>
            ) : (
              <p className="rounded-md bg-[var(--muted)] px-3 py-3 text-[11.5px] leading-relaxed text-[var(--muted-foreground)]">
                ยังไม่มีผู้ขนส่งรายใดมีเที่ยวที่วัดเวลาถึงได้ถึง {MINIMUM_SAMPLE} เที่ยว
              </p>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}

/* ---------------------------------------------------------------- the tiles */

/**
 * One measure, with everything needed to judge it.
 *
 * The base is not decoration. A rate with no base is a number somebody will
 * quote in a meeting without knowing whether it rests on six hundred trips or
 * six, and this system has the base for free because the engine already
 * returns it.
 */
function MeasureTile({ measure }: { measure: Measure }) {
  const unmeasured = measure.value === null;
  const rate = measure.kind === "Rate";

  const tone = unmeasured ? undefined
    : measure.meetsTarget === true ? MET
    : measure.meetsTarget === false ? MISSED
    : undefined;

  return (
    <Tile
      label={measure.english.toUpperCase()}
      th={measure.thai}
      // An em dash, never a nought. "Nobody could measure this" and "this was
      // zero" are different findings and only one of them is good news.
      value={unmeasured ? "—" : rate ? `${measure.value}%` : nf(measure.value ?? 0)}
      tone={tone}
      foot={
        unmeasured
          ? (measure.note || "ยังวัดไม่ได้")
          : [
              rate && measure.base > 0 ? `วัดจาก ${nf(measure.base)}` : "",
              measure.target !== null ? `เป้า ${measure.target}${rate ? "%" : ""}` : "",
            ].filter(Boolean).join(" · ")
      }
      verdict={
        measure.meetsTarget === true ? "▲ ถึงเป้า"
        : measure.meetsTarget === false ? "▼ ต่ำกว่าเป้า"
        : ""
      }
    />
  );
}

function Tile({ label, th, value, tone, foot, verdict }: {
  label: string; th: string; value: string;
  tone?: string; foot?: string; verdict?: string;
}) {
  return (
    <div className="rounded-md border border-[var(--border)] bg-[var(--muted)] px-3 py-3">
      <div className={cn("text-[21px] font-bold leading-none tabular-nums")}
        style={{ color: tone ?? "#0A2240" }}>
        {value}
      </div>
      <div className="mt-1.5 text-[9.5px] font-semibold uppercase tracking-wider text-[var(--muted-foreground)]">
        {label}
      </div>
      <div className="text-[10.5px] leading-snug text-[var(--muted-foreground)]">{th}</div>
      {foot && <div className="mt-1 text-[10px] text-[var(--muted-foreground)]">{foot}</div>}
      {/* Never colour alone — the red and green here are ΔE 6.7 apart for a
          deuteranope, so the word carries the verdict and the colour agrees. */}
      {verdict && (
        <div className="mt-0.5 text-[10px] font-semibold" style={{ color: tone }}>{verdict}</div>
      )}
    </div>
  );
}
