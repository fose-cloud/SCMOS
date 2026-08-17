"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import type { Period } from "../period";

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

type Measure = {
  id: string; english: string; thai: string; kind: string;
  available: boolean; value: number | null; base: number; unit: string; note: string;
  breakdown: Counted[];
};

type SupplierScore = {
  carrier: string; jobs: number;
  onTime: number | null; onTimeBase: number;
  confirmation: number | null; confirmationBase: number;
  delayFree: number | null; delayCount: number;
  score: number | null;
};

type EngineReport = { jobs: number; measures: Measure[]; suppliers: SupplierScore[] };

export function Kpi({ period, onDrill }: { period: Period; onDrill: (screen: string) => void }) {
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
        const [operational, measures] = await Promise.all([
          apiFetch(`/api/kpi?${query}`, { headers: { accept: "application/json" } }),
          apiFetch(`/api/kpi/measures?${query}`, { headers: { accept: "application/json" } }),
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

  if (error) {
    return (
      <div style={css("background:#fff;border:1px solid #F3C9C4;border-left:3px solid #B42318;border-radius:5px;padding:20px 22px")}>
        <div style={css("font-size:13.5px;font-weight:650;color:#B42318;margin-bottom:5px")}>คำนวณ KPI ไม่สำเร็จ</div>
        <div style={css("font-size:12.5px;color:#5A6B7D")}>{error}</div>
      </div>
    );
  }

  if (!report) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>
        กำลังคำนวณ KPI…
      </div>
    );
  }

  const done = report.byStatus.filter((s) => /complet|delivered|gate-in/i.test(s.label))
    .reduce((sum, s) => sum + s.value, 0);

  return (
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
        <Tile label="ต้องดำเนินการ" value={report.actionRequired.toLocaleString()} note="ข้อมูลผิดหรือยังขาด" colour="#B45309" onClick={() => onDrill("workspace")} />
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
                <tr>{["ตัวชี้วัด", "ค่า", "ฐานที่วัด", "รายละเอียด"].map((h, i) => (
                  <th key={h} style={css(
                    "padding:8px 14px;text-align:" + (i === 1 || i === 2 ? "right" : "left") +
                    ";font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;background:#F8FAFC;border-bottom:1px solid #E9EFF5;white-space:nowrap",
                  )}>{h}</th>
                ))}</tr>
              </thead>
              <tbody>
                {engine.measures.map((measure) => (
                  <tr key={measure.id} style={css("border-bottom:1px solid #F1F5F9")}>
                    <td style={css("padding:9px 14px")}>
                      <div style={css("font-weight:600;color:#0A2240")}>{measure.thai}</div>
                      <div style={css("font-size:11px;color:#94A3B8")}>{measure.english}</div>
                    </td>
                    <td style={css("padding:9px 14px;text-align:right;font-family:ui-monospace,monospace;font-weight:600;white-space:nowrap;color:" +
                      (measure.available ? "#16232F" : "#B45309"))}>
                      {measure.available && measure.value !== null
                        ? measure.kind === "Rate" ? `${measure.value}${measure.unit}` : `${measure.value.toLocaleString()} ${measure.unit}`
                        : "ยังวัดไม่ได้"}
                    </td>
                    <td style={css("padding:9px 14px;text-align:right;font-family:ui-monospace,monospace;color:#7B8CA0")}>
                      {measure.base.toLocaleString()}
                    </td>
                    <td style={css("padding:9px 14px;color:#5A6B7D;font-size:11.5px")}>
                      {measure.note}
                      {measure.breakdown.length > 0 && (
                        <div style={css("color:#94A3B8;margin-top:2px")}>
                          {measure.breakdown.filter((b) => b.value > 0).slice(0, 5)
                            .map((b) => `${b.label} ${b.value.toLocaleString()}`).join(" · ") || "—"}
                        </div>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {engine && engine.suppliers.some((s) => s.score !== null) && (
        <Panel
          title="คะแนนผู้ขนส่ง"
          note={`ให้คะแนนได้ ${engine.suppliers.filter((s) => s.score !== null).length} เจ้า · อีก ${engine.suppliers.filter((s) => s.score === null).length} เจ้ายังมีข้อมูลไม่พอ`}
        >
          <Table
            head={["ผู้ขนส่ง", "งาน", "ตรงเวลา %", "ฐาน", "คะแนน"]}
            rows={engine.suppliers.filter((s) => s.score !== null).map((s) => [
              s.carrier,
              s.jobs.toLocaleString(),
              s.onTime === null ? "—" : String(s.onTime),
              s.onTimeBase.toLocaleString(),
              String(s.score),
            ])}
          />
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

        <Panel title="ตรงเวลาแยกตามผู้ขนส่ง" note="แสดงเฉพาะเจ้าที่มีงานวัดผลได้">
          <Table
            head={["ผู้ขนส่ง", "งาน", "วัดได้", "ตรงเวลา", "%"]}
            rows={report.carriers
              .filter((c) => c.measured > 0)
              .slice(0, 14)
              .map((c) => [
                c.carrier,
                c.total.toLocaleString(),
                c.measured.toLocaleString(),
                c.onTime.toLocaleString(),
                c.percent + "%",
              ])}
          />
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
