"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { parseCarrierKpi, percentText, type CarrierKpi as Kpi } from "../carrierPortal";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

const MONTHS = ["ม.ค.", "ก.พ.", "มี.ค.", "เม.ย.", "พ.ค.", "มิ.ย.", "ก.ค.", "ส.ค.", "ก.ย.", "ต.ค.", "พ.ย.", "ธ.ค."];

/**
 * The carrier's own KPI (29 Sep 2026): on-time delivery over its own jobs and
 * its line of the contract scorecard — the figures Leschaco judges it on, and
 * only its own. The API measures over this company's jobs alone and sends no
 * other carrier's score, no ranking and no company-wide figure.
 */
export function CarrierKpi() {
  const now = new Date();
  const [year, setYear] = useState(String(now.getFullYear()));
  const [month, setMonth] = useState(String(now.getMonth() + 1).padStart(2, "0"));
  const [kpi, setKpi] = useState<Kpi | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let alive = true;
    void (async () => {
      await Promise.resolve();
      if (!alive) return;
      setLoading(true); setError("");
      try {
        const response = await apiFetch(`/api/carrier/kpi?year=${encodeURIComponent(year)}&month=${encodeURIComponent(month)}`,
          { headers: { accept: "application/json" } });
        const body: unknown = await response.json().catch(() => null);
        if (!response.ok) throw new Error(typeof body === "object" && body && "error" in body ? String(body.error) : `เปิด KPI ไม่ได้ (${response.status})`);
        if (alive) setKpi(parseCarrierKpi(body));
      } catch (problem) {
        if (alive) setError(problem instanceof Error && problem.message !== "invalid_response" ? problem.message : "ข้อมูลตอบกลับไม่ตรงรูปแบบ");
      } finally { if (alive) setLoading(false); }
    })();
    return () => { alive = false; };
  }, [year, month]);

  const years = Array.from({ length: 4 }, (_, i) => String(now.getFullYear() - i));
  const score = kpi?.score ?? null;
  const onTime = kpi?.onTime ?? null;

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:14px 17px;display:flex;gap:12px;flex-wrap:wrap;align-items:end;justify-content:space-between")}>
        <div>
          <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>ผลการประเมินของบริษัท</div>
          <div style={css("font-size:16px;font-weight:700;color:#0F2B46;margin-top:2px")}>{kpi?.supplierName ?? "—"}</div>
        </div>
        <div style={css("display:flex;gap:8px")}>
          <select aria-label="เดือน" value={month} onChange={(event) => setMonth(event.target.value)} style={css(PICK)}>
            <option value="">ทั้งปี</option>
            {MONTHS.map((name, i) => <option key={name} value={String(i + 1).padStart(2, "0")}>{name}</option>)}
          </select>
          <select aria-label="ปี" value={year} onChange={(event) => setYear(event.target.value)} style={css(PICK)}>
            {years.map(one => <option key={one} value={one}>{one}</option>)}
          </select>
        </div>
      </div>

      {error ? <Notice tone="#B45309">{error}</Notice>
        : loading && !kpi ? <div style={css("padding:30px;text-align:center;color:#7B8CA0;font-size:12.5px")}>กำลังโหลด…</div>
        : kpi && <>
          <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:8px;opacity:" + (loading ? ".6" : "1"))}>
            <Card label="คะแนนรวม" value={score?.weighted == null ? "—" : score.weighted.toFixed(1)}
              note={score ? `จากน้ำหนักที่วัดได้ ${score.weightAvailable}%` : "ไม่มีงานในช่วงนี้"} tone="#0A5C97" />
            <Card label="ส่งตรงเวลา" value={percentText(onTime?.available ? onTime.value : null)}
              note={onTime ? `${onTime.base} งานที่วัดได้` : "—"} tone="#16794C" />
            <Card label="งานในช่วงนี้" value={score?.shipments ?? 0} note="งานที่นับในคะแนน" tone="#5A6B7D" />
            <Card label="อุบัติเหตุ" value={(score?.tally.transportAccidentMajor ?? 0) + (score?.tally.transportAccidentMinor ?? 0) + (score?.tally.loadingAccident ?? 0)}
              note={`ร้ายแรง ${score?.tally.transportAccidentMajor ?? 0} · เล็กน้อย ${score?.tally.transportAccidentMinor ?? 0} · ขนถ่าย ${score?.tally.loadingAccident ?? 0}`} tone="#B42318" />
            <Card label="ข้อร้องเรียน" value={score?.tally.complaints ?? 0} note={`รถเสียไม่มีข้อร้องเรียน ${score?.tally.breakdownNoComplaint ?? 0}`} tone="#B45309" />
          </div>

          {score && <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px")}><ZoomBox capped={false} zoomable={false}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12px;min-width:640px")}>
              <thead><tr>{["เกณฑ์", "น้ำหนัก", "คะแนน", "ที่เกิด / ฐาน", "เป้า", "หมายเหตุ"].map(head => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
              <tbody>{score.lines.map(line => <tr key={line.id}>
                <td style={css(CELL)}><strong>{line.thai}</strong><div style={css("color:#7B8CA0;font-size:11px")}>{line.english}</div></td>
                <td style={css(CELL + NUMBER)}>{line.weight}%</td>
                <td style={css(CELL + NUMBER + "color:" + (line.percent == null ? "#7B8CA0" : line.percent >= line.target ? "#16794C" : "#B42318"))}>{percentText(line.percent)}</td>
                <td style={css(CELL + NUMBER)}>{line.count} / {line.base}</td>
                <td style={css(CELL + NUMBER)}>{line.target}%</td>
                <td style={css(CELL + "color:#5A6B7D")}>{line.note}</td>
              </tr>)}</tbody>
            </table>
          </ZoomBox></div>}

          {onTime?.trend && onTime.trend.length > 1 && <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:12px 14px")}>
            <div style={css("font-size:12px;font-weight:650;color:#0F2B46;margin-bottom:8px")}>ส่งตรงเวลา รายเดือน</div>
            <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
              {onTime.trend.map(point => <div key={point.period} style={css("border:1px solid #E3E8EE;border-radius:4px;padding:6px 9px;min-width:74px")}>
                <div style={css("font-size:10.5px;color:#7B8CA0")}>{point.period}</div>
                <div style={css("font-size:13px;font-weight:700;color:#0F2B46;font-family:'IBM Plex Mono',monospace")}>{percentText(point.value)}</div>
                <div style={css("font-size:10px;color:#94A3B8")}>{point.base} งาน</div>
              </div>)}
            </div>
          </div>}
        </>}
    </div>
  );
}

const PICK = "height:31px;padding:0 8px;border:1px solid #D3DBE3;border-radius:4px;background:#fff;font-size:12px;font-family:inherit";
const HEAD = "text-align:left;padding:9px 10px;background:#EEF3F8;color:#52657A;font-weight:650;white-space:nowrap;";
const CELL = "padding:8px 10px;border-top:1px solid #E6ECF3;vertical-align:top;color:#0F2B46;";
const NUMBER = "text-align:right;font-family:'IBM Plex Mono',monospace;white-space:nowrap;";

function Card({ label, value, note, tone }: { label: string; value: number | string; note: string; tone: string }) {
  return <div style={css(`background:#fff;border:1px solid #E3E8EE;border-top:3px solid ${tone};border-radius:5px;padding:9px 11px`)}>
    <div style={css("font-size:10px;color:#7B8CA0;font-weight:650")}>{label}</div>
    <div style={css("font-size:20px;color:#0F2B46;font-weight:700;font-family:'IBM Plex Mono',monospace;margin-top:3px")}>{value}</div>
    <div style={css("font-size:9.5px;color:#94A3B8;margin-top:2px")}>{note}</div>
  </div>;
}

function Notice({ tone, children }: { tone: string; children: React.ReactNode }) {
  return <div style={css("background:#fff;border:1px solid #E3E8EE;border-left:3px solid " + tone + ";border-radius:6px;padding:18px 20px;font-size:12.5px;color:#5A6B7D")}>{children}</div>;
}
