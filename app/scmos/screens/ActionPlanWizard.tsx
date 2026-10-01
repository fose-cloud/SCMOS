"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { CARRIER_AREAS, DEVELOPMENT_TH, PEOPLE_AREAS, PERIOD_TH, PRIORITY, SKILL_LEVELS, TARGET_TH, type Meta } from "../actionPlan";
import { registerDate } from "../carrierPortal";
import { css } from "../theme";
import { INPUT, LABEL, OUTLINE, PANEL, PRIMARY, SAVE, TITLE } from "./ActionPlanParts";

type Step = { action: string; ownerId: string; targetDate: string; expectedResult: string };

type Draft = {
  developmentType: string; targetType: string; employeeId: string; position: string; team: string; supervisor: string;
  supplierId: string; targetName: string; category: string; developmentArea: string; currentLevel: string; targetLevel: string;
  gap: string; rootCause: string; method: string; coach: string; carrierContact: string; evaluationMethod: string;
  title: string; objective: string; expectedOutcome: string; description: string; metric: string; baseline: string; targetValue: string;
  items: Step[]; ownerId: string; period: string; year: string; quarter: string; month: string; startDate: string; targetDate: string;
  priority: string; reviewDate: string;
};

const STEPS = ["ประเภทแผน", "กลุ่มเป้าหมาย", "ด้านที่พัฒนา", "วัตถุประสงค์และเป้าหมาย", "Action Items", "เจ้าของและกำหนดเวลา", "ตรวจทาน", "สร้างแผน"];

/**
 * A new Action Plan, one question at a time (1 Oct 2026): the kind of plan, who or what it develops, the area,
 * the objective and its measure, the steps, who owns it and by when, a last look, and then the plan. Nothing is
 * sent until the last step; the API checks everything again.
 */
export function ActionPlanWizard({ meta, onToast, onCreated, onCancel }: {
  meta: Meta; onToast: (message: string) => void; onCreated: (id: number) => void; onCancel: () => void;
}) {
  const [step, setStep] = useState(0);
  const [busy, setBusy] = useState(false);
  const [carriers, setCarriers] = useState<{ id: number; name: string }[]>([]);
  const [draft, setDraft] = useState<Draft>({
    developmentType: "", targetType: "", employeeId: "", position: "", team: "", supervisor: "", supplierId: "", targetName: "",
    category: "", developmentArea: "", currentLevel: "", targetLevel: "", gap: "", rootCause: "", method: "", coach: "",
    carrierContact: "", evaluationMethod: "", title: "", objective: "", expectedOutcome: "", description: "", metric: "",
    baseline: "", targetValue: "", items: [{ action: "", ownerId: meta.me, targetDate: "", expectedResult: "" }], ownerId: meta.me,
    period: "annual", year: String(new Date().getFullYear()), quarter: "", month: "", startDate: "", targetDate: "", priority: "medium",
    reviewDate: "",
  });

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/suppliers?status=approved", { headers: { accept: "application/json" } });
      const list = response.ok ? await response.json() as { id: number; name: string }[] : [];
      if (!cancelled) setCarriers(list.map((one) => ({ id: one.id, name: one.name })).sort((a, b) => a.name.localeCompare(b.name)));
    })();
    return () => { cancelled = true; };
  }, []);

  const set = (key: keyof Draft) => (event: { target: { value: string } }) => setDraft({ ...draft, [key]: event.target.value });
  const people = draft.developmentType === "people";
  const steps = draft.items.filter((item) => item.action.trim());
  const nameOf = (id: string) => meta.people.find((person) => person.id === id)?.name ?? "";

  /** Why this step cannot be left yet, or "". */
  const problem = (() => {
    switch (step) {
      case 0: return draft.developmentType ? "" : "เลือกประเภทแผน";
      case 1:
        if (!draft.targetType) return "เลือกกลุ่มเป้าหมาย";
        if (draft.targetType === "employee" && !draft.employeeId) return "เลือกพนักงาน";
        if (!people && !draft.supplierId) return "เลือกผู้ขนส่ง";
        if (draft.targetType === "team" && !draft.targetName.trim()) return "ระบุชื่อทีม";
        return "";
      case 2: return draft.category ? "" : "เลือกประเภทแผน (Category)";
      case 3: return draft.title.trim() ? "" : "ระบุชื่อแผน";
      case 4: return steps.length ? "" : "ใส่อย่างน้อยหนึ่งขั้นตอน";
      case 5: return !draft.targetDate ? "ระบุวันครบกำหนด" : draft.startDate && draft.startDate > draft.targetDate ? "วันครบกำหนดต้องไม่ก่อนวันเริ่ม" : "";
      default: return "";
    }
  })();

  async function create() {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch("/api/action-plans", {
        method: "POST", headers: { "content-type": "application/json" },
        body: JSON.stringify({
          ...draft,
          year: Number(draft.year) || null, quarter: draft.quarter ? Number(draft.quarter) : null, month: draft.month ? Number(draft.month) : null,
          supplierId: draft.supplierId ? Number(draft.supplierId) : null,
          startDate: registerDate(draft.startDate), targetDate: registerDate(draft.targetDate), reviewDate: registerDate(draft.reviewDate),
          baseline: draft.baseline ? Number(draft.baseline) : null, targetValue: draft.targetValue ? Number(draft.targetValue) : null,
          items: steps.map((item) => ({ ...item, targetDate: registerDate(item.targetDate) })),
        }),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string; id?: number };
      onToast(reply.message ?? reply.error ?? (response.ok ? "สร้างแผนแล้ว" : `สร้างไม่สำเร็จ (${response.status})`));
      if (response.ok && reply.id) onCreated(reply.id);
    } finally { setBusy(false); }
  }

  return (
    <div style={css(PANEL + "display:flex;flex-direction:column;gap:14px")}>
      <ol style={css("display:flex;gap:6px;flex-wrap:wrap;list-style:none;margin:0;padding:0")}>
        {STEPS.map((label, index) => (
          <li key={label} style={css("font-size:11.5px;padding:4px 10px;border-radius:999px;border:1px solid "
            + (index === step ? "#0A2240;background:#0A2240;color:#fff" : index < step ? "#BFE0CD;background:#EDF7F1;color:#16794C" : "#E3E8EE;color:#7B8CA0"))}>
            {index + 1}. {label}
          </li>
        ))}
      </ol>

      {step === 0 && (
        <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px")}>
          {(["people", "subcontractor"] as const).map((kind) => (
            <button key={kind} onClick={() => setDraft({ ...draft, developmentType: kind, targetType: kind === "people" ? "employee" : "subcontractor", category: "" })}
              style={css("text-align:left;padding:16px;border-radius:6px;cursor:pointer;border:2px solid " + (draft.developmentType === kind ? "#0A2240;background:#F4F8FC" : "#E3E8EE;background:#fff"))}>
              <div style={css(TITLE + "font-size:14px")}>{DEVELOPMENT_TH[kind]}</div>
              <div style={css("font-size:12px;color:#5A6B7D;margin-top:4px")}>
                {kind === "people" ? "พนักงาน · ทีม · แผนก — ทักษะ การอบรม OJT Coaching" : "ผู้รับเหมาช่วง / ผู้ขนส่ง — OTD ความปลอดภัย คุณภาพ เอกสาร Billing"}
              </div>
            </button>
          ))}
        </div>
      )}

      {step === 1 && (
        <Row>
          <Field label="กลุ่มเป้าหมาย">
            <select aria-label="กลุ่มเป้าหมาย" value={draft.targetType} onChange={set("targetType")} style={css(INPUT)}>
              {(meta.targetTypes[draft.developmentType] ?? []).map((type) => <option key={type} value={type}>{TARGET_TH[type] ?? type}</option>)}
            </select>
          </Field>
          {draft.targetType === "employee" && <>
            <Field label="พนักงาน *">
              <select aria-label="พนักงาน" value={draft.employeeId} onChange={set("employeeId")} style={css(INPUT + "min-width:200px")}>
                <option value="">เลือก</option>
                {meta.people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
              </select>
            </Field>
            <Field label="ตำแหน่ง"><input aria-label="ตำแหน่ง" value={draft.position} onChange={set("position")} style={css(INPUT)} /></Field>
            <Field label="ทีม"><input aria-label="ทีม" value={draft.team} onChange={set("team")} style={css(INPUT)} /></Field>
            <Field label="หัวหน้า"><input aria-label="หัวหน้า" value={draft.supervisor} onChange={set("supervisor")} style={css(INPUT)} /></Field>
          </>}
          {draft.targetType === "team" && <Field label="ชื่อทีม *"><input aria-label="ชื่อทีม" value={draft.targetName} onChange={set("targetName")} style={css(INPUT)} /></Field>}
          {!people && (
            <Field label="ผู้ขนส่ง *">
              <select aria-label="ผู้ขนส่ง" value={draft.supplierId} onChange={set("supplierId")} style={css(INPUT + "min-width:220px")}>
                <option value="">เลือก</option>
                {carriers.map((one) => <option key={one.id} value={one.id}>{one.name}</option>)}
              </select>
            </Field>
          )}
        </Row>
      )}

      {step === 2 && (
        <Row>
          <Field label="Category *">
            <select aria-label="Category" value={draft.category} onChange={set("category")} style={css(INPUT + "min-width:220px")}>
              <option value="">เลือก</option>
              {meta.types.filter((type) => type.developmentType === draft.developmentType && type.active).map((type) => <option key={type.id} value={type.name}>{type.name}</option>)}
            </select>
          </Field>
          <Field label="ด้านที่พัฒนา">
            <input aria-label="ด้านที่พัฒนา" list="action-plan-areas" value={draft.developmentArea} onChange={set("developmentArea")} style={css(INPUT + "min-width:200px")} />
            <datalist id="action-plan-areas">{(people ? PEOPLE_AREAS : CARRIER_AREAS).map((area) => <option key={area} value={area} />)}</datalist>
          </Field>
          <Field label={people ? "ระดับทักษะปัจจุบัน" : "ผลงานปัจจุบัน"}>
            {people ? <Levels label="ระดับทักษะปัจจุบัน" value={draft.currentLevel} onChange={set("currentLevel")} />
              : <input aria-label="ผลงานปัจจุบัน" value={draft.currentLevel} onChange={set("currentLevel")} placeholder="เช่น OTD 80%" style={css(INPUT)} />}
          </Field>
          <Field label={people ? "ระดับทักษะเป้าหมาย" : "ผลงานเป้าหมาย"}>
            {people ? <Levels label="ระดับทักษะเป้าหมาย" value={draft.targetLevel} onChange={set("targetLevel")} />
              : <input aria-label="ผลงานเป้าหมาย" value={draft.targetLevel} onChange={set("targetLevel")} placeholder="เช่น OTD 95%" style={css(INPUT)} />}
          </Field>
          <Field label="Gap"><input aria-label="Gap" value={draft.gap} onChange={set("gap")} style={css(INPUT + "min-width:220px")} /></Field>
          {people ? <>
            <Field label="วิธีพัฒนา">
              <select aria-label="วิธีพัฒนา" value={draft.method} onChange={set("method")} style={css(INPUT)}>
                <option value="">—</option>{meta.methods.map((method) => <option key={method} value={method}>{method}</option>)}
              </select>
            </Field>
            <Field label="Coach / Mentor"><input aria-label="Coach / Mentor" list="action-plan-people" value={draft.coach} onChange={set("coach")} style={css(INPUT)} /></Field>
          </> : <>
            <Field label="Root Cause"><input aria-label="Root Cause" value={draft.rootCause} onChange={set("rootCause")} style={css(INPUT + "min-width:220px")} /></Field>
            <Field label="ผู้ติดต่อฝั่งผู้ขนส่ง"><input aria-label="ผู้ติดต่อฝั่งผู้ขนส่ง" value={draft.carrierContact} onChange={set("carrierContact")} style={css(INPUT)} /></Field>
          </>}
          <Field label="วิธีประเมินผล"><input aria-label="วิธีประเมินผล" value={draft.evaluationMethod} onChange={set("evaluationMethod")} style={css(INPUT)} /></Field>
          <datalist id="action-plan-people">{meta.people.map((person) => <option key={person.id} value={person.name} />)}</datalist>
        </Row>
      )}

      {step === 3 && (
        <div style={css("display:flex;flex-direction:column;gap:10px")}>
          <Field label="ชื่อแผน *"><input aria-label="ชื่อแผน" value={draft.title} onChange={set("title")} maxLength={200} style={css(INPUT + "width:100%;box-sizing:border-box")} /></Field>
          <Field label="วัตถุประสงค์"><Area label="วัตถุประสงค์" value={draft.objective} onChange={set("objective")} /></Field>
          <Field label="ผลที่คาดหวัง"><Area label="ผลที่คาดหวัง" value={draft.expectedOutcome} onChange={set("expectedOutcome")} /></Field>
          <Field label="รายละเอียด"><Area label="รายละเอียด" value={draft.description} onChange={set("description")} /></Field>
          <Row>
            <Field label="ตัวชี้วัด (Metric)"><input aria-label="ตัวชี้วัด" value={draft.metric} onChange={set("metric")} placeholder="เช่น OTD" style={css(INPUT)} /></Field>
            <Field label="Baseline"><input aria-label="Baseline" inputMode="decimal" value={draft.baseline} onChange={set("baseline")} style={css(INPUT + "width:100px")} /></Field>
            <Field label="Target"><input aria-label="Target" inputMode="decimal" value={draft.targetValue} onChange={set("targetValue")} style={css(INPUT + "width:100px")} /></Field>
          </Row>
        </div>
      )}

      {step === 4 && (
        <div style={css("display:flex;flex-direction:column;gap:8px")}>
          {draft.items.map((item, index) => (
            <Row key={index}>
              <span style={css("font-size:12px;color:#7B8CA0;align-self:center;width:18px")}>{index + 1}.</span>
              <Field label="สิ่งที่จะทำ *"><input aria-label={`สิ่งที่จะทำ ${index + 1}`} value={item.action} onChange={(e) => setItem(index, { action: e.target.value })} style={css(INPUT + "min-width:260px")} /></Field>
              <Field label="ผู้รับผิดชอบ">
                <select aria-label={`ผู้รับผิดชอบ ${index + 1}`} value={item.ownerId} onChange={(e) => setItem(index, { ownerId: e.target.value })} style={css(INPUT)}>
                  <option value="">—</option>{meta.people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
                </select>
              </Field>
              <Field label="ครบกำหนด"><input type="date" aria-label={`ครบกำหนด ${index + 1}`} value={item.targetDate} onChange={(e) => setItem(index, { targetDate: e.target.value })} style={css(INPUT)} /></Field>
              <Field label="ผลที่คาดหวัง"><input aria-label={`ผลที่คาดหวัง ${index + 1}`} value={item.expectedResult} onChange={(e) => setItem(index, { expectedResult: e.target.value })} style={css(INPUT + "min-width:200px")} /></Field>
              {draft.items.length > 1 && <button onClick={() => setDraft({ ...draft, items: draft.items.filter((_, at) => at !== index) })} style={css(OUTLINE + "align-self:flex-end")}>นำออก</button>}
            </Row>
          ))}
          <button onClick={() => setDraft({ ...draft, items: [...draft.items, { action: "", ownerId: draft.ownerId, targetDate: "", expectedResult: "" }] })}
            style={css(OUTLINE + "align-self:flex-start")}>+ เพิ่มขั้นตอน</button>
        </div>
      )}

      {step === 5 && (
        <Row>
          <Field label="เจ้าของแผน">
            <select aria-label="เจ้าของแผน" value={draft.ownerId} onChange={set("ownerId")} style={css(INPUT + "min-width:200px")}>
              {meta.people.map((person) => <option key={person.id} value={person.id}>{person.name}</option>)}
            </select>
          </Field>
          <Field label="รอบแผน">
            <select aria-label="รอบแผน" value={draft.period} onChange={set("period")} style={css(INPUT)}>
              {Object.entries(PERIOD_TH).map(([key, label]) => <option key={key} value={key}>{label}</option>)}
            </select>
          </Field>
          <Field label="ปี"><input aria-label="ปี" inputMode="numeric" value={draft.year} onChange={set("year")} style={css(INPUT + "width:80px")} /></Field>
          {draft.period === "quarterly" && <Field label="ไตรมาส">
            <select aria-label="ไตรมาส" value={draft.quarter} onChange={set("quarter")} style={css(INPUT)}>
              <option value="">ตามวันครบกำหนด</option>{[1, 2, 3, 4].map((q) => <option key={q} value={q}>Q{q}</option>)}
            </select></Field>}
          {draft.period === "monthly" && <Field label="เดือน">
            <select aria-label="เดือน" value={draft.month} onChange={set("month")} style={css(INPUT)}>
              <option value="">ตามวันครบกำหนด</option>{Array.from({ length: 12 }, (_, i) => i + 1).map((m) => <option key={m} value={m}>{String(m).padStart(2, "0")}</option>)}
            </select></Field>}
          <Field label="วันเริ่ม"><input type="date" aria-label="วันเริ่ม" value={draft.startDate} onChange={set("startDate")} style={css(INPUT)} /></Field>
          <Field label="วันครบกำหนด *"><input type="date" aria-label="วันครบกำหนด" value={draft.targetDate} onChange={set("targetDate")} style={css(INPUT)} /></Field>
          <Field label="วันทบทวน"><input type="date" aria-label="วันทบทวน" value={draft.reviewDate} onChange={set("reviewDate")} style={css(INPUT)} /></Field>
          <Field label="ความสำคัญ">
            <select aria-label="ความสำคัญ" value={draft.priority} onChange={set("priority")} style={css(INPUT)}>
              {Object.entries(PRIORITY).map(([key, value]) => <option key={key} value={key}>{value.label}</option>)}
            </select>
          </Field>
        </Row>
      )}

      {(step === 6 || step === 7) && (
        <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:8px 18px;font-size:12.5px;color:#334155")}>
          <Fact label="ประเภท">{DEVELOPMENT_TH[draft.developmentType]} · {draft.category}</Fact>
          <Fact label="เป้าหมาย">{TARGET_TH[draft.targetType]} · {draft.targetType === "employee" ? nameOf(draft.employeeId)
            : people ? draft.targetName || "Subcontract Management" : carriers.find((one) => String(one.id) === draft.supplierId)?.name}</Fact>
          <Fact label="ชื่อแผน">{draft.title}</Fact>
          <Fact label="ด้านที่พัฒนา">{draft.developmentArea || "—"} · {draft.currentLevel || "—"} → {draft.targetLevel || "—"}</Fact>
          <Fact label="ตัวชี้วัด">{draft.metric ? `${draft.metric}: ${draft.baseline || "—"} → ${draft.targetValue || "—"}` : "—"}</Fact>
          <Fact label="เจ้าของ / กำหนด">{nameOf(draft.ownerId)} · {PERIOD_TH[draft.period]} · {draft.startDate || "—"} → {draft.targetDate}</Fact>
          <Fact label="ความสำคัญ">{PRIORITY[draft.priority]?.label}</Fact>
          <Fact label="Action Items">{steps.map((item, index) => `${index + 1}. ${item.action}`).join(" · ")}</Fact>
        </div>
      )}

      <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
        <button onClick={step === 0 ? onCancel : () => setStep(step - 1)} style={css(OUTLINE)}>{step === 0 ? "ยกเลิก" : "ย้อนกลับ"}</button>
        {step < 7
          ? <button disabled={problem !== ""} onClick={() => setStep(step + 1)} style={css(PRIMARY + "opacity:" + (problem ? ".55" : "1"))}>ถัดไป</button>
          : <button disabled={busy} onClick={() => void create()} style={css(SAVE + "opacity:" + (busy ? ".55" : "1"))}>{busy ? "กำลังสร้าง…" : "สร้าง Action Plan"}</button>}
        {problem && <span style={css("font-size:11.5px;color:#B45309")}>{problem}</span>}
      </div>
    </div>
  );

  function setItem(index: number, change: Partial<Step>) {
    setDraft({ ...draft, items: draft.items.map((item, at) => (at === index ? { ...item, ...change } : item)) });
  }
}

function Levels({ label, value, onChange }: { label: string; value: string; onChange: (event: { target: { value: string } }) => void }) {
  return <select aria-label={label} value={value} onChange={onChange} style={css(INPUT)}>
    <option value="">—</option>{SKILL_LEVELS.map((level) => <option key={level} value={level}>{level}</option>)}
  </select>;
}

function Area({ label, value, onChange }: { label: string; value: string; onChange: (event: { target: { value: string } }) => void }) {
  return <textarea aria-label={label} rows={2} value={value} onChange={onChange} maxLength={2000}
    style={css("width:100%;box-sizing:border-box;border:1px solid #C9D6E2;border-radius:4px;padding:6px 8px;font-size:12.5px;font-family:inherit")} />;
}

function Row({ children }: { children: React.ReactNode }) {
  return <div style={css("display:flex;gap:10px;flex-wrap:wrap;align-items:flex-end")}>{children}</div>;
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return <label style={css("display:flex;flex-direction:column;gap:3px")}><span style={css(LABEL)}>{label}</span>{children}</label>;
}

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div><div style={css(LABEL)}>{label}</div><div style={css("font-weight:600;color:#0A2240")}>{children}</div></div>;
}
