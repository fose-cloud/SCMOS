"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import type { PlanPrefill } from "../actionPlanRequest";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { CELL, EMPTY, HEAD, INPUT, LABEL, Notice, OUTLINE, PANEL, SAVE, SMALL, TITLE } from "./ActionPlanParts";

type Skill = { id: number; category: string; name: string; position: number; active: boolean };
type Person = { id: string; name: string; role: string };
type SkillCell = { employeeId: string; skillId: number; level: number; targetLevel: number | null; note: string; assessedBy: string; assessedAt: string };
type Matrix = { skills: Skill[]; people: Person[]; cells: SkillCell[]; canAssess: boolean; canConfigure: boolean };
type History = { id: number; skill: string; category: string; level: number; targetLevel: number | null; note: string; assessedBy: string; assessedAt: string };

/**
 * Skill levels 1 Basic … 5 Expert as an ordinal ramp: one hue, light → dark, from the dataviz reference palette's
 * blue (steps 250–650), validated with --ordinal against white. The number is printed in every cell, so the
 * colour is never the only way to read a level.
 */
export const LEVEL_FILL = ["", "#86b6ef", "#5598e7", "#2a78d6", "#1c5cab", "#104281"];
const LEVEL_INK = ["", "#0B2A4A", "#0B2A4A", "#fff", "#fff", "#fff"];
export const LEVEL_NAME = ["", "Basic", "Beginner", "Competent", "Advanced", "Expert"];

/**
 * The department's Skill Matrix (1 Oct 2026, Action Plan round two): one row per person, one column per skill
 * of the chosen area, each cell the latest level and the level wanted. Reviewers see and assess everybody;
 * anybody else sees their own row. A gap opens a people development plan with it already filled in.
 */
export function SkillMatrix({ canPlan, onToast, onPlan }: {
  canPlan: boolean; onToast: (message: string) => void; onPlan: (prefill: PlanPrefill) => void;
}) {
  const [matrix, setMatrix] = useState<Matrix | null>(null);
  const [failure, setFailure] = useState("");
  const [area, setArea] = useState("");
  const [editing, setEditing] = useState<{ person: Person; skill: Skill; level: string; target: string; note: string } | null>(null);
  const [history, setHistory] = useState<{ person: Person; rows: History[] } | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch("/api/action-plans/skills", { headers: { accept: "application/json" } });
    const body = await response.json().catch(() => null) as Matrix & { error?: string } | null;
    if (!response.ok || !body) { setFailure(body?.error ?? `เปิด Skill Matrix ไม่ได้ (${response.status})`); return; }
    setMatrix(body); setFailure("");
  }, []);
  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const areas = useMemo(() => [...new Set((matrix?.skills ?? []).map((skill) => skill.category))], [matrix]);
  const shownArea = area || areas[0] || "";
  const skills = (matrix?.skills ?? []).filter((skill) => skill.category === shownArea);
  const cellOf = (person: string, skill: number) => matrix?.cells.find((cell) => cell.employeeId === person && cell.skillId === skill);

  /** A person's largest gap in the area shown — what a plan from this row is about. */
  function biggestGap(person: Person) {
    return skills.map((skill) => ({ skill, cell: cellOf(person.id, skill.id) }))
      .filter((one) => one.cell?.targetLevel && one.cell.targetLevel > one.cell.level)
      .sort((a, b) => (b.cell!.targetLevel! - b.cell!.level) - (a.cell!.targetLevel! - a.cell!.level))[0];
  }

  async function assess() {
    if (!editing || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/action-plans/skills/assess", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({ employeeId: editing.person.id, skillId: editing.skill.id, level: Number(editing.level),
          targetLevel: editing.target ? Number(editing.target) : null, note: editing.note }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (response.ok) { setEditing(null); await load(); }
    } finally { setBusy(false); }
  }

  async function openHistory(person: Person) {
    const response = await apiFetch(`/api/action-plans/skills/history?employeeId=${encodeURIComponent(person.id)}`, { headers: { accept: "application/json" } });
    setHistory({ person, rows: response.ok ? await response.json() as History[] : [] });
  }

  if (failure) return <Notice tone="#B45309">{failure}</Notice>;
  if (!matrix) return <Notice tone="#7B8CA0">กำลังโหลด Skill Matrix…</Notice>;

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css(PANEL + "display:flex;gap:10px;flex-wrap:wrap;align-items:center")}>
        <div style={css("display:flex;gap:6px;flex-wrap:wrap")}>
          {areas.map((one) => (
            <button key={one} onClick={() => { setArea(one); setEditing(null); }} style={css("height:28px;padding:0 11px;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer;border:1px solid "
              + (one === shownArea ? "#0A2240;background:#0A2240;color:#fff" : "#D3DBE3;background:#fff;color:#3F5265"))}>{one}</button>
          ))}
        </div>
        <span style={css("margin-left:auto;display:flex;gap:4px;align-items:center;font-size:11px;color:#5A6B7D")}>
          {[1, 2, 3, 4, 5].map((level) => (
            <span key={level} style={css("display:flex;align-items:center;gap:3px")}>
              <span style={css(`width:18px;height:16px;border-radius:3px;background:${LEVEL_FILL[level]};color:${LEVEL_INK[level]};font-size:10px;font-weight:700;text-align:center;line-height:16px`)}>{level}</span>
              {LEVEL_NAME[level]}
            </span>
          ))}
        </span>
      </div>

      {editing && (
        <div style={css(PANEL + "display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>
          <span style={css(TITLE + "align-self:center")}>{editing.person.name} · {editing.skill.name}</span>
          <Field label="ระดับปัจจุบัน">
            <select aria-label="ระดับปัจจุบัน" value={editing.level} onChange={(e) => setEditing({ ...editing, level: e.target.value })} style={css(INPUT)}>
              <option value="">—</option>{[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level} = {LEVEL_NAME[level]}</option>)}
            </select>
          </Field>
          <Field label="ระดับเป้าหมาย">
            <select aria-label="ระดับเป้าหมาย" value={editing.target} onChange={(e) => setEditing({ ...editing, target: e.target.value })} style={css(INPUT)}>
              <option value="">—</option>{[1, 2, 3, 4, 5].map((level) => <option key={level} value={level}>{level} = {LEVEL_NAME[level]}</option>)}
            </select>
          </Field>
          <Field label="หมายเหตุ"><input aria-label="หมายเหตุการประเมิน" value={editing.note} onChange={(e) => setEditing({ ...editing, note: e.target.value })} style={css(INPUT + "width:240px")} /></Field>
          <button disabled={busy || !editing.level} onClick={() => void assess()} style={css(SAVE + "opacity:" + (busy || !editing.level ? ".55" : "1"))}>บันทึกการประเมิน</button>
          <button onClick={() => setEditing(null)} style={css(OUTLINE)}>ยกเลิก</button>
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox capped={false}>
          <table style={css("border-collapse:collapse;font-size:12px;min-width:100%")}>
            <thead><tr>
              <th style={css(HEAD + "min-width:160px")}>พนักงาน</th>
              {skills.map((skill) => <th key={skill.id} title={skill.name} style={css(HEAD + "text-align:center;white-space:normal;min-width:76px;max-width:96px;line-height:1.25")}>{skill.name}</th>)}
              <th style={css(HEAD)} />
            </tr></thead>
            <tbody>
              {matrix.people.length === 0 && <tr><td colSpan={skills.length + 2} style={css(EMPTY)}>ไม่มีรายชื่อที่ดูได้</td></tr>}
              {matrix.people.map((person) => {
                const gap = biggestGap(person);
                return (
                  <tr key={person.id}>
                    <td style={css(CELL + "font-weight:600;color:#0A2240")}>
                      <button onClick={() => void openHistory(person)} style={css("border:0;background:none;padding:0;color:#0A2240;font-weight:600;cursor:pointer;text-align:left")}>{person.name}</button>
                      <div style={css("font-size:10.5px;color:#7B8CA0;font-weight:400")}>{person.role}</div>
                    </td>
                    {skills.map((skill) => {
                      const cell = cellOf(person.id, skill.id);
                      const short = cell?.targetLevel != null && cell.targetLevel > cell.level;
                      const tip = cell ? `${skill.name}: ${cell.level} ${LEVEL_NAME[cell.level]}${cell.targetLevel ? ` → ${cell.targetLevel}` : ""} · ${cell.assessedBy}${cell.note ? ` · ${cell.note}` : ""}` : `${skill.name}: ยังไม่ประเมิน`;
                      return (
                        <td key={skill.id} title={tip} style={css(CELL + "text-align:center;padding:4px")}>
                          <button disabled={!matrix.canAssess} aria-label={`${person.name} ${skill.name}`}
                            onClick={() => setEditing({ person, skill, level: cell ? String(cell.level) : "", target: cell?.targetLevel ? String(cell.targetLevel) : "", note: cell?.note ?? "" })}
                            style={css(`min-width:44px;height:26px;border-radius:4px;font-size:11.5px;font-weight:700;cursor:${matrix.canAssess ? "pointer" : "default"};`
                              + (cell ? `background:${LEVEL_FILL[cell.level]};color:${LEVEL_INK[cell.level]};border:${short ? "2px solid #B45309" : "1px solid transparent"}`
                                : "background:#fff;color:#94A3B8;border:1px dashed #D3DBE3"))}>
                            {cell ? `${cell.level}${short ? `→${cell.targetLevel}` : ""}` : "—"}
                          </button>
                        </td>
                      );
                    })}
                    <td style={css(CELL + "white-space:nowrap")}>
                      {canPlan && gap && (
                        <button onClick={() => onPlan({
                          developmentType: "people", targetType: "employee", employeeId: person.id, category: "Skill Development",
                          developmentArea: gap.skill.name, currentLevel: `${gap.cell!.level} = ${LEVEL_NAME[gap.cell!.level]}`,
                          targetLevel: `${gap.cell!.targetLevel} = ${LEVEL_NAME[gap.cell!.targetLevel!]}`,
                          title: `Develop ${gap.skill.name} — ${person.name}`,
                          references: [{ kind: "skill", refId: String(gap.skill.id), label: `${gap.skill.category} · ${gap.skill.name} ${gap.cell!.level} → ${gap.cell!.targetLevel}` }],
                        })} style={css(SMALL)}>+ Action Plan จาก gap</button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </ZoomBox>
      </div>

      {history && (
        <div style={css(PANEL)}>
          <div style={css("display:flex;justify-content:space-between;align-items:center;margin-bottom:6px")}>
            <span style={css(TITLE)}>ประวัติการประเมิน · {history.person.name}</span>
            <button onClick={() => setHistory(null)} style={css(SMALL)}>ปิด</button>
          </div>
          {history.rows.length === 0 && <div style={css("font-size:12px;color:#94A3B8")}>ยังไม่มีประวัติ</div>}
          {history.rows.map((row) => (
            <div key={row.id} style={css("font-size:12px;color:#334155;padding:4px 0;border-bottom:1px solid #F1F5F9")}>
              <span style={css("font-family:ui-monospace,monospace;color:#7B8CA0")}>{new Date(row.assessedAt).toLocaleDateString("en-GB")}</span>
              {" · "}{row.category} · <b>{row.skill}</b> {row.level}{row.targetLevel ? ` → ${row.targetLevel}` : ""} · {row.assessedBy}{row.note ? ` · ${row.note}` : ""}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}><span style={css(LABEL)}>{label}</span>{children}</label>;
}
