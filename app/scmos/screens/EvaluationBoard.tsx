"use client";

import { useState } from "react";
import { ELIGIBILITY, decisionLabel, scoreState, shown, type CampaignSummary, type CampaignView, type ResultRow } from "../annualEvaluation";
import { StatCard } from "../StatCard";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { CELL, EMPTY, HEAD, INPUT, LABEL, MONO, PANEL } from "./ActionPlanParts";

type Answered = "all" | "complete" | "pending" | "none";

/**
 * A campaign's board (2 Oct 2026, Annual Evaluation Phase 8): who was asked and who answered, each carrier's score with
 * whether it still matches its evidence, and the decision on it. Every figure is the API's.
 */
export function EvaluationBoard({ view, results, summary, onPick }: {
  view: CampaignView; results: ResultRow[]; summary: CampaignSummary | null; onPick: (evaluationCarrierId: number) => void;
}) {
  const [text, setText] = useState("");
  const [department, setDepartment] = useState("");
  const [state, setState] = useState("");
  const [band, setBand] = useState("");
  const [answered, setAnswered] = useState<Answered>("all");

  const departments = view.departments.filter((one) => one.enabled);
  const chosen = department ? Number(department) : null;
  const bandLabel = (code: string) => view.scoreBands.find((one) => one.code === code)?.label ?? code;
  /** The links a row is judged on: the chosen department's, or all of them. */
  const linksOf = (row: ResultRow) => {
    const line = chosen === null ? null : (row.departments ?? []).find((one) => one.departmentId === chosen);
    return chosen === null ? { responses: row.responses ?? 0, invited: row.invited ?? 0 } : { responses: line?.responses ?? 0, invited: line?.invited ?? 0 };
  };
  const rows = results.filter((row) => {
    if (text && !`${row.carrier} ${row.code}`.toLowerCase().includes(text.toLowerCase())) return false;
    if (state && scoreState(row) !== state) return false;
    if (band && row.band !== band) return false;
    const links = linksOf(row);
    if (answered === "complete" && !(links.invited > 0 && links.responses === links.invited)) return false;
    if (answered === "pending" && !(links.responses < links.invited)) return false;
    if (answered === "none" && links.invited > 0) return false;
    return true;
  });
  const scored = results.filter((row) => row.finalScore !== null);
  const average = scored.length ? scored.reduce((sum, row) => sum + (row.finalScore ?? 0), 0) / scored.length : null;
  const departmentName = departments.find((one) => one.departmentId === chosen)?.name ?? "Department";

  return (
    <>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px")}>
        <StatCard label="ปีประเมิน" value={String(view.campaign.year)} tone="#0A2240" />
        <StatCard label="ผู้ขนส่งที่ประเมิน" value={String(view.included)} tone="#0A2240" />
        <StatCard label="ผู้ประเมิน" value={String(summary?.evaluators ?? 0)} tone="#0A2240" />
        <StatCard label="ตอบแล้ว" value={summary ? `${summary.responses} / ${summary.invited}` : "—"} tone="#16794C" />
        <StatCard label="ความครบถ้วน" value={summary?.completion == null ? "—" : `${shown(summary.completion)}%`} tone="#1D5FA8" />
        <StatCard label="ต้องคำนวณใหม่" value={String(summary?.stale ?? 0)} tone="#B45309" />
        <StatCard label="ตัดสินแล้ว" value={summary ? `${summary.decided} / ${summary.carriers}` : "—"} tone="#6D28D9" />
        <StatCard label="คะแนนรวมเฉลี่ย" value={shown(average)} tone="#1D5FA8" />
      </div>

      <div style={css(PANEL + "display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap")}>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ผู้ขนส่ง</span>
          <input aria-label="ค้นหาผู้ขนส่ง" value={text} onChange={(e) => setText(e.target.value)} style={css(INPUT + "width:180px")} />
        </label>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>แผนก</span>
          <select aria-label="แผนก" value={department} onChange={(e) => setDepartment(e.target.value)} style={css(INPUT + "min-width:150px")}>
            <option value="">ทุกแผนก</option>
            {departments.map((one) => <option key={one.departmentId} value={one.departmentId}>{one.name}</option>)}
          </select>
        </label>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>สถานะคะแนน</span>
          <select aria-label="สถานะคะแนน" value={state} onChange={(e) => setState(e.target.value)} style={css(INPUT + "min-width:140px")}>
            <option value="">ทั้งหมด</option>
            <option value="calculated">คำนวณแล้ว</option>
            <option value="stale">ต้องคำนวณใหม่</option>
            <option value="insufficient">ข้อมูลไม่พอ</option>
            <option value="not-calculated">ยังไม่คำนวณ</option>
          </select>
        </label>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>ช่วงคะแนน</span>
          <select aria-label="ช่วงคะแนน" value={band} onChange={(e) => setBand(e.target.value)} style={css(INPUT + "min-width:140px")}>
            <option value="">ทั้งหมด</option>
            {view.scoreBands.map((one) => <option key={one.code} value={one.code}>{one.label}</option>)}
          </select>
        </label>
        <label style={css("display:flex;flex-direction:column;gap:3px")}>
          <span style={css(LABEL)}>การตอบ</span>
          <select aria-label="การตอบ" value={answered} onChange={(e) => setAnswered(e.target.value as Answered)} style={css(INPUT + "min-width:130px")}>
            <option value="all">ทั้งหมด</option>
            <option value="complete">ตอบครบ</option>
            <option value="pending">รอตอบ</option>
            <option value="none">ยังไม่ส่งลิงก์</option>
          </select>
        </label>
        <span style={css(LABEL + "align-self:center")}>{rows.length} / {results.length}</span>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["ผู้ขนส่ง", "งาน", "เกณฑ์", "System", chosen === null ? "Department" : departmentName, "คะแนนรวม", "ตอบแล้ว", "ช่วงคะแนน", "การตัดสิน", "สถานะ"]
              .map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {rows.map((row) => {
                const links = linksOf(row);
                const line = chosen === null ? null : (row.departments ?? []).find((one) => one.departmentId === chosen);
                const now = scoreState(row);
                return (
                  <tr key={row.evaluationCarrierId} onClick={() => onPick(row.evaluationCarrierId)} style={css("cursor:pointer")}>
                    <td style={css(CELL)}><div style={css("font-weight:600")}>{row.carrier}</div><div style={css(LABEL + MONO)}>{row.code}</div></td>
                    <td style={css(CELL + MONO)}>{row.totalJobs ?? "—"}</td>
                    <td style={css(CELL)}>{ELIGIBILITY[row.eligibility] ?? row.eligibility ?? "—"}</td>
                    <td style={css(CELL + MONO)}>{shown(row.systemScore)}</td>
                    <td style={css(CELL + MONO)}>{shown(chosen === null ? row.humanScore : line?.score)}</td>
                    <td style={css(CELL + MONO + "font-weight:700;font-size:13px")}>{shown(row.finalScore)}</td>
                    <td style={css(CELL + MONO)}>{links.invited ? `${links.responses} / ${links.invited}` : "—"}</td>
                    <td style={css(CELL)}>{row.band ? bandLabel(row.band) : "—"}</td>
                    <td style={css(CELL)}>{decisionLabel(view.decisions ?? [], row.decision)}</td>
                    <td style={css(CELL + "font-size:11.5px")} title={(row.stale ?? []).join(" · ") || undefined}>
                      {now === "not-calculated" ? <span style={css("color:#94A3B8")}>ยังไม่คำนวณ</span>
                        : now === "stale" ? <span style={css("color:#B45309;font-weight:600")}>ต้องคำนวณใหม่</span>
                          : now === "insufficient" ? <span style={css("color:#B45309")}>ข้อมูลไม่พอ</span>
                            : <span style={css("color:#16794C")}>คำนวณแล้ว v{row.version}</span>}
                    </td>
                  </tr>
                );
              })}
              {rows.length === 0 && <tr><td colSpan={10} style={css(EMPTY)}>{results.length === 0 ? "ยังไม่ได้เลือกผู้ขนส่ง" : "ไม่มีผู้ขนส่งตามตัวกรอง"}</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </>
  );
}
