"use client";

import { useState } from "react";
import { dayInput, type CampaignView, type Kpi, type Question, type ScoreBand } from "../annualEvaluation";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { CELL, HEAD, INPUT, LABEL, PANEL, SAVE, SMALL, TITLE } from "./ActionPlanParts";

/**
 * A campaign's rules (Annual Evaluation, Phase 5): the period and weights, the seven KPIs and their bands, the score
 * bands, the departments and the questions they are asked. Each part is saved on its own; what is wrong with the whole
 * is the API's to say, and the campaign screen lists it above. Read-only once the campaign has opened.
 */
export function AnnualEvaluationSetup({ view, editable, busy, onSave, onAddDepartment }: {
  view: CampaignView;
  editable: boolean;
  busy: boolean;
  /** "" for the campaign itself, else "/kpis", "/score-bands", "/departments", "/questions". */
  onSave: (path: string, body: unknown) => Promise<boolean>;
  onAddDepartment: (name: string) => Promise<void>;
}) {
  const campaign = view.campaign;
  const [header, setHeader] = useState({
    name: campaign.name, periodStart: dayInput(campaign.periodStart), periodEnd: dayInput(campaign.periodEnd),
    openOn: dayInput(campaign.openOn), dueOn: dayInput(campaign.dueOn),
    systemWeight: String(campaign.systemWeight), humanWeight: String(campaign.humanWeight), minimumJobs: String(campaign.minimumJobs),
    minimumSystemCoverage: String(campaign.minimumSystemCoverage), commentRequiredAtOrBelow: String(campaign.commentRequiredAtOrBelow),
  });
  const [kpis, setKpis] = useState<Kpi[]>(view.kpis.map((kpi) => ({ ...kpi, bands: kpi.bands.map((band) => ({ ...band })) })));
  const [bands, setBands] = useState<ScoreBand[]>(view.scoreBands.map((band) => ({ ...band })));
  const [departments, setDepartments] = useState(view.departments.map((row) => ({ ...row })));
  const [questions, setQuestions] = useState<Question[]>(view.questions.map((question) => ({ ...question, departments: question.departments.map((row) => ({ ...row })) })));
  const [newDepartment, setNewDepartment] = useState("");

  const number = (text: string) => Number(text.replace(",", "."));
  const kpiWeight = kpis.filter((kpi) => kpi.enabled).reduce((sum, kpi) => sum + Number(kpi.weight), 0);
  const questionWeight = questions.filter((question) => question.enabled).reduce((sum, question) => sum + Number(question.weight), 0);
  const disabled = !editable || busy;
  const setKpi = (index: number, change: Partial<Kpi>) => setKpis((rows) => rows.map((row, at) => (at === index ? { ...row, ...change } : row)));
  const setQuestion = (index: number, change: Partial<Question>) => setQuestions((rows) => rows.map((row, at) => (at === index ? { ...row, ...change } : row)));
  const asked = (question: Question, departmentId: number) => question.departments.some((row) => row.departmentId === departmentId && row.enabled);

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <section style={css(PANEL + "display:flex;flex-direction:column;gap:10px")}>
        <div style={css(TITLE)}>แคมเปญ</div>
        <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
          {([
            ["name", "ชื่อ", "text", "260px"], ["periodStart", "เริ่มช่วงประเมิน", "date", ""], ["periodEnd", "สิ้นสุดช่วงประเมิน", "date", ""],
            ["openOn", "เปิดรับประเมิน", "date", ""], ["dueOn", "ปิดรับประเมิน", "date", ""],
            ["systemWeight", "น้ำหนัก System", "number", "80px"], ["humanWeight", "น้ำหนัก Department", "number", "80px"],
            ["minimumJobs", "งานขั้นต่ำ", "number", "70px"], ["minimumSystemCoverage", "KPI ที่วัดได้ขั้นต่ำ %", "number", "80px"],
            ["commentRequiredAtOrBelow", "บังคับความเห็นเมื่อ ≤", "number", "70px"],
          ] as [keyof typeof header, string, string, string][]).map(([key, label, type, width]) => (
            <label key={key} style={css("display:flex;flex-direction:column;gap:3px")}>
              <span style={css(LABEL)}>{label}</span>
              <input aria-label={label} type={type} value={header[key]} disabled={disabled}
                onChange={(e) => setHeader({ ...header, [key]: e.target.value })} style={css(INPUT + (width ? `width:${width}` : ""))} />
            </label>
          ))}
          {editable && (
            <button type="button" disabled={busy} style={css(SAVE)} onClick={() => void onSave("", {
              name: header.name, periodStart: header.periodStart, periodEnd: header.periodEnd, openOn: header.openOn, dueOn: header.dueOn,
              systemWeight: number(header.systemWeight), humanWeight: number(header.humanWeight), minimumJobs: Math.round(number(header.minimumJobs)),
              minimumSystemCoverage: number(header.minimumSystemCoverage), commentRequiredAtOrBelow: Math.round(number(header.commentRequiredAtOrBelow)),
            })}>บันทึกแคมเปญ</button>
          )}
        </div>
      </section>

      <section style={css(PANEL + "display:flex;flex-direction:column;gap:8px")}>
        <div style={css("display:flex;align-items:baseline;gap:10px")}>
          <span style={css(TITLE)}>KPI ระบบ</span>
          <span style={css("font-size:11.5px;font-weight:600;color:" + (kpiWeight === Number(header.systemWeight) ? "#16794C" : "#B42318"))}>
            รวม {kpiWeight} / {header.systemWeight}
          </span>
        </div>
        <ZoomBox capped={false}><table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
          <thead><tr>{["KPI", "ใช้", "น้ำหนัก", "วิธี", "ทิศทาง", "ช่วงคะแนน (ค่า → คะแนน)", "นอกช่วง"].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
          <tbody>
            {kpis.map((kpi, index) => (
              <tr key={kpi.code} style={css(kpi.enabled ? "" : "opacity:.55")}>
                <td style={css(CELL)}><div style={css("font-weight:600")}>{kpi.name}</div><div style={css(LABEL)}>{kpi.measure}</div></td>
                <td style={css(CELL)}><input type="checkbox" aria-label={`ใช้ ${kpi.name}`} checked={kpi.enabled} disabled={disabled} onChange={(e) => setKpi(index, { enabled: e.target.checked })} /></td>
                <td style={css(CELL)}><input aria-label={`น้ำหนัก ${kpi.name}`} type="number" step="any" value={kpi.weight} disabled={disabled} onChange={(e) => setKpi(index, { weight: number(e.target.value) || 0 })} style={css(INPUT + "width:60px")} /></td>
                <td style={css(CELL)}>
                  <select aria-label={`วิธี ${kpi.name}`} value={kpi.method} disabled={disabled} onChange={(e) => setKpi(index, { method: e.target.value })} style={css(INPUT)}>
                    <option value="band">จากข้อมูล</option><option value="manual">ประเมินเอง</option>
                  </select>
                </td>
                <td style={css(CELL)}>
                  <select aria-label={`ทิศทาง ${kpi.name}`} value={kpi.direction} disabled={disabled || kpi.method === "manual"} onChange={(e) => setKpi(index, { direction: e.target.value })} style={css(INPUT)}>
                    <option value="higher">ยิ่งมากยิ่งดี</option><option value="lower">ยิ่งน้อยยิ่งดี</option>
                  </select>
                </td>
                <td style={css(CELL)}>
                  {kpi.method === "manual" ? <span style={css(LABEL)}>—</span> : (
                    <div style={css("display:flex;flex-direction:column;gap:4px")}>
                      {kpi.bands.map((band, at) => (
                        <div key={at} style={css("display:flex;gap:4px;align-items:center")}>
                          <span style={css(LABEL)}>{kpi.direction === "lower" ? "≤" : "≥"}</span>
                          <input aria-label={`เกณฑ์ ${kpi.name} ${at + 1}`} type="number" step="any" value={band.threshold} disabled={disabled} style={css(INPUT + "width:64px")}
                            onChange={(e) => setKpi(index, { bands: kpi.bands.map((one, i) => (i === at ? { ...one, threshold: number(e.target.value) || 0 } : one)) })} />
                          <span style={css(LABEL)}>→</span>
                          <input aria-label={`คะแนน ${kpi.name} ${at + 1}`} type="number" step="any" value={band.score} disabled={disabled} style={css(INPUT + "width:56px")}
                            onChange={(e) => setKpi(index, { bands: kpi.bands.map((one, i) => (i === at ? { ...one, score: number(e.target.value) || 0 } : one)) })} />
                          {editable && <button type="button" style={css(SMALL)} onClick={() => setKpi(index, { bands: kpi.bands.filter((_, i) => i !== at) })}>ลบ</button>}
                        </div>
                      ))}
                      {editable && <button type="button" style={css(SMALL + "align-self:flex-start")} onClick={() => setKpi(index, { bands: [...kpi.bands, { threshold: 0, score: 0 }] })}>+ ช่วง</button>}
                    </div>
                  )}
                </td>
                <td style={css(CELL)}><input aria-label={`คะแนนนอกช่วง ${kpi.name}`} type="number" step="any" value={kpi.fallbackScore} disabled={disabled || kpi.method === "manual"}
                  onChange={(e) => setKpi(index, { fallbackScore: number(e.target.value) || 0 })} style={css(INPUT + "width:56px")} /></td>
              </tr>
            ))}
          </tbody>
        </table></ZoomBox>
        {editable && (
          <button type="button" disabled={busy} style={css(SAVE + "align-self:flex-start")} onClick={() => void onSave("/kpis", kpis.map((kpi) => ({
            code: kpi.code, weight: Number(kpi.weight), enabled: kpi.enabled, method: kpi.method, direction: kpi.direction, measure: kpi.measure,
            fallbackScore: Number(kpi.fallbackScore), bands: kpi.method === "manual" ? [] : kpi.bands,
          })))}>บันทึก KPI</button>
        )}
      </section>

      <section style={css(PANEL + "display:flex;flex-direction:column;gap:8px")}>
        <span style={css(TITLE)}>ช่วงคะแนนรวม</span>
        {bands.map((band, index) => (
          <div key={index} style={css("display:flex;gap:6px;align-items:center")}>
            <input aria-label={`ชื่อช่วง ${index + 1}`} value={band.label} disabled={disabled} placeholder="ชื่อ" style={css(INPUT + "width:200px")}
              onChange={(e) => setBands(bands.map((one, at) => (at === index ? { ...one, label: e.target.value, code: one.code || "" } : one)))} />
            <span style={css(LABEL)}>ตั้งแต่</span>
            <input aria-label={`คะแนนเริ่ม ${index + 1}`} type="number" step="any" value={band.minScore} disabled={disabled} style={css(INPUT + "width:70px")}
              onChange={(e) => setBands(bands.map((one, at) => (at === index ? { ...one, minScore: number(e.target.value) || 0 } : one)))} />
            {editable && <button type="button" style={css(SMALL)} onClick={() => setBands(bands.filter((_, at) => at !== index))}>ลบ</button>}
          </div>
        ))}
        {editable && (
          <div style={css("display:flex;gap:8px")}>
            <button type="button" style={css(SMALL)} onClick={() => setBands([...bands, { code: "", label: "", minScore: 0 }])}>+ ช่วงคะแนน</button>
            <button type="button" disabled={busy} style={css(SAVE)} onClick={() => void onSave("/score-bands", bands)}>บันทึกช่วงคะแนน</button>
          </div>
        )}
      </section>

      <section style={css(PANEL + "display:flex;flex-direction:column;gap:8px")}>
        <span style={css(TITLE)}>แผนกที่ประเมิน</span>
        <div style={css("display:flex;gap:12px;flex-wrap:wrap")}>
          {departments.map((row, index) => (
            <div key={row.departmentId} style={css("display:flex;gap:6px;align-items:center;border:1px solid #E3E8EE;border-radius:5px;padding:5px 8px")}>
              <input type="checkbox" aria-label={`ใช้ ${row.name}`} checked={row.enabled} disabled={disabled}
                onChange={(e) => setDepartments(departments.map((one, at) => (at === index ? { ...one, enabled: e.target.checked } : one)))} />
              <span style={css("font-size:12px;font-weight:600")}>{row.name}</span>
              <input aria-label={`น้ำหนัก ${row.name}`} type="number" step="any" value={row.weight} disabled={disabled} style={css(INPUT + "width:50px")}
                onChange={(e) => setDepartments(departments.map((one, at) => (at === index ? { ...one, weight: number(e.target.value) || 0 } : one)))} />
            </div>
          ))}
        </div>
        {editable && (
          <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
            <button type="button" disabled={busy} style={css(SAVE)}
              onClick={() => void onSave("/departments", departments.map((row) => ({ departmentId: row.departmentId, weight: Number(row.weight), enabled: row.enabled })))}>
              บันทึกแผนก
            </button>
            <input aria-label="แผนกใหม่" value={newDepartment} onChange={(e) => setNewDepartment(e.target.value)} placeholder="แผนกใหม่" style={css(INPUT + "width:180px")} />
            <button type="button" disabled={busy || !newDepartment.trim()} style={css(SMALL)}
              onClick={() => { void onAddDepartment(newDepartment.trim()); setNewDepartment(""); }}>+ เพิ่มแผนก</button>
          </div>
        )}
      </section>

      <section style={css(PANEL + "display:flex;flex-direction:column;gap:8px;overflow-x:auto")}>
        <div style={css("display:flex;align-items:baseline;gap:10px")}>
          <span style={css(TITLE)}>คำถามสำหรับแผนก</span>
          <span style={css("font-size:11.5px;font-weight:600;color:" + (questionWeight === Number(header.humanWeight) ? "#16794C" : "#B42318"))}>
            รวม {questionWeight} / {header.humanWeight}
          </span>
        </div>
        <ZoomBox capped={false}><table style={css("border-collapse:collapse;font-size:12px;min-width:100%")}>
          <thead><tr>
            {["คำถาม", "ใช้", "น้ำหนัก", ...view.departments.map((row) => row.name), ""].map((head, at) => <th key={`${head}-${at}`} style={css(HEAD)}>{head}</th>)}
          </tr></thead>
          <tbody>
            {questions.map((question, index) => (
              <tr key={question.code || index} style={css(question.enabled ? "" : "opacity:.55")}>
                <td style={css(CELL + "min-width:220px")}>
                  <input aria-label={`คำถาม ${index + 1}`} value={question.text} disabled={disabled} style={css(INPUT + "width:100%")}
                    onChange={(e) => setQuestion(index, { text: e.target.value })} />
                  <input aria-label={`คำถามภาษาไทย ${index + 1}`} value={question.textTh} disabled={disabled} placeholder="ภาษาไทย" style={css(INPUT + "width:100%;margin-top:3px")}
                    onChange={(e) => setQuestion(index, { textTh: e.target.value })} />
                </td>
                <td style={css(CELL)}><input type="checkbox" aria-label={`ใช้คำถาม ${index + 1}`} checked={question.enabled} disabled={disabled} onChange={(e) => setQuestion(index, { enabled: e.target.checked })} /></td>
                <td style={css(CELL)}><input aria-label={`น้ำหนักคำถาม ${index + 1}`} type="number" step="any" value={question.weight} disabled={disabled} style={css(INPUT + "width:56px")}
                  onChange={(e) => setQuestion(index, { weight: number(e.target.value) || 0 })} /></td>
                {view.departments.map((row) => (
                  <td key={row.departmentId} style={css(CELL + "text-align:center")}>
                    <input type="checkbox" aria-label={`ถาม ${row.name} ข้อ ${index + 1}`} checked={asked(question, row.departmentId)} disabled={disabled}
                      onChange={(e) => setQuestion(index, {
                        departments: [...question.departments.filter((one) => one.departmentId !== row.departmentId),
                          { departmentId: row.departmentId, enabled: e.target.checked, required: true, commentRequiredAtOrBelow: null }],
                      })} />
                  </td>
                ))}
                <td style={css(CELL)}>{editable && <button type="button" style={css(SMALL)} onClick={() => setQuestions(questions.filter((_, at) => at !== index))}>ลบ</button>}</td>
              </tr>
            ))}
          </tbody>
        </table></ZoomBox>
        {editable && (
          <div style={css("display:flex;gap:8px")}>
            <button type="button" style={css(SMALL)} onClick={() => setQuestions([...questions, {
              id: 0, code: "", text: "", textTh: "", weight: 0, enabled: true, position: questions.length,
              departments: view.departments.map((row) => ({ departmentId: row.departmentId, enabled: true, required: true, commentRequiredAtOrBelow: null })),
            }])}>+ คำถาม</button>
            <button type="button" disabled={busy} style={css(SAVE)} onClick={() => void onSave("/questions", questions.map((question) => ({
              code: question.code, text: question.text, textTh: question.textTh, weight: Number(question.weight), enabled: question.enabled,
              departments: question.departments,
            })))}>บันทึกคำถาม</button>
          </div>
        )}
      </section>
    </div>
  );
}
