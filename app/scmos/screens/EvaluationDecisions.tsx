"use client";

import { useState } from "react";
import {
  DECISION_NOTE_MINIMUM, bangkokTime, decisionLabel, needsDecisionNote, shown, type CampaignSummary, type CampaignView, type ResultRow,
} from "../annualEvaluation";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { CELL, EMPTY, HEAD, INPUT, LABEL, MONO, Notice, SAVE } from "./ActionPlanParts";

/**
 * Management review (2 Oct 2026, Annual Evaluation Phase 9): a decision and its reason per carrier, recorded while the
 * campaign is under review and confirmed by approving it. The calculated band sits beside the decision and never fills it
 * in. What approval still waits for, and the decisions the Supplier Register has to carry out, come from the API.
 */
export function EvaluationDecisions({ view, results, summary, editable, busy, onDecide }: {
  view: CampaignView; results: ResultRow[]; summary: CampaignSummary | null; editable: boolean; busy: boolean;
  onDecide: (evaluationCarrierId: number, decision: string, note: string) => Promise<boolean>;
}) {
  const [drafts, setDrafts] = useState<Record<number, { decision: string; note: string }>>({});
  const status = view.campaign.status;
  const decisions = view.decisions ?? [];
  const bandLabel = (code: string) => view.scoreBands.find((one) => one.code === code)?.label ?? code;
  const draftOf = (row: ResultRow) => drafts[row.evaluationCarrierId] ?? { decision: row.decision, note: row.decisionNote ?? "" };
  const setDraft = (row: ResultRow, change: Partial<{ decision: string; note: string }>) =>
    setDrafts((all) => ({ ...all, [row.evaluationCarrierId]: { ...draftOf(row), ...change } }));

  return (
    <div style={css("display:flex;flex-direction:column;gap:10px")}>
      {status === "finalized" || status === "archived" ? (
        <Notice tone="#16794C">สรุปผลแล้ว · ส่งผลเข้าทะเบียนผู้ขนส่ง {summary?.published ?? 0} ราย</Notice>
      ) : status === "approved" ? (
        <Notice tone="#16794C">อนุมัติแล้ว · รอสรุปผล</Notice>
      ) : status !== "under-review" ? (
        <Notice tone="#7B8CA0">บันทึกการตัดสินได้เมื่อสถานะ &quot;กำลังพิจารณา&quot;</Notice>
      ) : null}

      {(summary?.approvalProblems ?? []).length > 0 && (
        <Notice tone="#B45309">
          <div style={css("font-weight:650;color:#8A4B08;margin-bottom:4px")}>ยังอนุมัติไม่ได้ · {summary!.approvalProblems.length}</div>
          <ul style={css("margin:0;padding-left:18px;display:flex;flex-direction:column;gap:2px")}>
            {summary!.approvalProblems.map((problem) => <li key={problem}>{problem}</li>)}
          </ul>
        </Notice>
      )}

      {(summary?.followUps ?? []).length > 0 && (
        <Notice tone="#B42318">
          <div style={css("font-weight:650;color:#912018;margin-bottom:4px")}>ต้องดำเนินการในทะเบียนผู้ขนส่ง · {summary!.followUps.length}</div>
          <ul style={css("margin:0;padding-left:18px;display:flex;flex-direction:column;gap:2px")}>
            {summary!.followUps.map((one) => <li key={one.evaluationCarrierId}>{one.carrier} — {one.label}{one.note ? ` · ${one.note}` : ""}</li>)}
          </ul>
        </Notice>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["ผู้ขนส่ง", "คะแนนรวม", "ช่วงคะแนน", "การตัดสิน", "เหตุผล", "บันทึกโดย", ""].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {results.map((row) => {
                const draft = draftOf(row);
                const changed = draft.decision !== row.decision || draft.note !== (row.decisionNote ?? "");
                const needsNote = draft.decision !== "" && needsDecisionNote(draft.decision, row.decision);
                const short = needsNote && draft.note.trim().length < DECISION_NOTE_MINIMUM;
                const blocked = busy || !changed || !draft.decision || short;
                return (
                  <tr key={row.evaluationCarrierId}>
                    <td style={css(CELL)}><div style={css("font-weight:600")}>{row.carrier}</div><div style={css(LABEL + MONO)}>{row.code}</div></td>
                    <td style={css(CELL + MONO + "font-weight:700")}>{shown(row.finalScore)}</td>
                    <td style={css(CELL)}>{row.band ? bandLabel(row.band) : row.version ? "ข้อมูลไม่พอ" : "—"}</td>
                    <td style={css(CELL)}>
                      {editable ? (
                        <select aria-label={`การตัดสิน ${row.carrier}`} value={draft.decision} onChange={(e) => setDraft(row, { decision: e.target.value })}
                          style={css(INPUT + "min-width:200px")}>
                          <option value="">ยังไม่ตัดสิน</option>
                          {decisions.map((one) => <option key={one.code} value={one.code}>{one.label}</option>)}
                        </select>
                      ) : <span style={css(row.decision ? "font-weight:600" : "color:#94A3B8")}>{decisionLabel(decisions, row.decision)}</span>}
                    </td>
                    <td style={css(CELL + "min-width:220px")}>
                      {editable ? (
                        <input aria-label={`เหตุผล ${row.carrier}`} value={draft.note} maxLength={2000} onChange={(e) => setDraft(row, { note: e.target.value })}
                          placeholder={needsNote ? "จำเป็น" : ""}
                          style={css(INPUT + "width:100%" + (short ? ";border:1px solid #E5A33B" : ""))} />
                      ) : <span style={css("font-size:12px")}>{row.decisionNote || "—"}</span>}
                    </td>
                    <td style={css(CELL + "font-size:11.5px")}>
                      {row.decidedBy ? <><div>{row.decidedBy}</div><div style={css(LABEL + MONO)}>{bangkokTime(row.decidedAt)}</div></> : "—"}
                    </td>
                    <td style={css(CELL + "white-space:nowrap")}>
                      {editable && (
                        <button type="button" disabled={blocked} style={css(SAVE + (blocked ? "opacity:.45;cursor:default" : ""))}
                          onClick={async () => {
                            if (await onDecide(row.evaluationCarrierId, draft.decision, draft.note.trim()))
                              setDrafts((all) => { const next = { ...all }; delete next[row.evaluationCarrierId]; return next; });
                          }}>บันทึก</button>
                      )}
                    </td>
                  </tr>
                );
              })}
              {results.length === 0 && <tr><td colSpan={7} style={css(EMPTY)}>ยังไม่ได้เลือกผู้ขนส่ง</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}
