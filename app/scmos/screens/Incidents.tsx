"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import type { Job } from "../ops";
import { stamp } from "./WorkflowPanel";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";
import { STAGES, STAGE_TH } from "../incidentStages";

/**
 * Incident and CAR/PAR.
 *
 * The stages follow the quality process the team already runs on paper. The
 * backend refuses to skip the things that make a case worth having — a
 * corrective action without a root cause is a guess, and an action without an
 * owner and a date is a wish — so this screen shows the refusals rather than
 * disabling buttons and leaving people to wonder why.
 */

type Case = {
  id: number; reference: string; jobKey: string; kind: string; category: string;
  title: string; stage: string;
  what: string; where: string; when: string; who: string; why: string; how: string;
  aiSummary: string; rootCause: string; correctiveAction: string; preventiveAction: string;
  responsiblePerson: string; dueDate: string; followUpNote: string; effectivenessNote: string;
  approvedBy: string; approvedAt: string | null;
  raisedBy: string; raisedAt: string; overdue: boolean;
  evidence: Evidence[];
  /* The rest of ISO-FRM-TH-ISO-08-09. */
  company: string; grade: string; source: string; ncClause: string;
  team: string; requestedBy: string; requestedOn: string;
  immediateAction: string; immediateBy: string; immediateDue: string;
  documentsToRevise: string; followUpBy: string; reviewedBy: string;
  approvalOutcome: string; approvalNote: string; teamNote: string;
};

/** A file on the case. The path it went to is decided by the API, not here. */
type Evidence = {
  id: number; kind: string; fileName: string; note: string;
  folder: string; sizeBytes: number; objectKey: string;
  uploadedBy: string; uploadedAt: string;
  /**
   * Whether the API will show this in the browser rather than hand it over to
   * be saved. Decided there, by the same rule the content route serves by, so
   * this screen never offers to open something the API would refuse — see
   * Rules/InlineViewing.cs for why the answer is not simply "is it an image".
   */
  canShow: boolean;
};

/** Where the bytes come from: shown in the page, or saved to disk. */
const shownAt = (id: number) => `/api/documents/${id}/content?inline=1`;
const savedAt = (id: number) => `/api/documents/${id}/content`;


const CATEGORY_TH: Record<string, string> = {
  accident: "อุบัติเหตุ", damage: "ความเสียหาย", delay: "ความล่าช้า",
  safety: "ความปลอดภัย", quality: "คุณภาพ", other: "อื่นๆ",
};

/** What a field offers when the paper form offers a tick box rather than a line. */
const CHOICES: Record<string, string[]> = {
  company: ["LSTH", "LSSV", "LSCC", "TIH"],
  grade: ["Major", "Minor", "OBS"],
  source: ["Customer Complaint", "Internal Audit", "Management Review", "Other"],
  documentsToRevise: ["ไม่ต้องแก้เอกสาร", "SOP", "Form", "Others"],
  approvalOutcome: ["Closed CAR", "Not Accept"],
};

/**
 * ISO-FRM-TH-ISO-08-09, in the order the paper form asks for it.
 *
 * The record already held D2, D4, D5 and D7 — the form and the record were both
 * built on 8D, so they agreed about the middle of it. What was missing was
 * everything around the edges: which company it is raised under, how it was
 * graded, where it came from, who is on the team, what was done on the day
 * before anyone knew the cause, which documents the fix means rewriting, and
 * who followed it up as distinct from who reviewed it. Those were being written
 * on paper beside a case that had nowhere to keep them.
 *
 * Grouped by the form's own D-steps so somebody holding the printed sheet can
 * work down the screen without hunting.
 */
const SECTIONS: [string, [string, string][]][] = [
  ["D1 · จัดตั้งทีม (Establishing the Team)", [
    ["company", "บริษัท"],
    ["grade", "ระดับ — CAR: Major/Minor · PAR: OBS"],
    ["source", "ที่มา (Source)"],
    ["ncClause", "NC Clause (ถ้ามี)"],
    ["requestedBy", "ผู้ร้องขอ (Request By)"],
    ["requestedOn", "วันที่ร้องขอ (DD/MM/YYYY)"],
    ["team", "ทีมผู้ร่วมแก้ไข (คั่นด้วยจุลภาค)"],
    ["responsiblePerson", "ผู้รับผิดชอบตอบกลับ (Response By)"],
    ["dueDate", "กำหนดเสร็จ (DD/MM/YYYY)"],
  ]],
  ["D2 · รายละเอียดของปัญหา (Describe Problem)", [
    ["what", "What — เกิดอะไรขึ้น"], ["where", "Where — ที่ไหน"], ["when", "When — เมื่อไหร่"],
    ["who", "Who — ใครเกี่ยวข้อง"], ["why", "Why — ทำไม"], ["how", "How — อย่างไร"],
  ]],
  ["D3 · การแก้ไขเฉพาะหน้า (Immediate / Interim Action)", [
    ["immediateAction", "สิ่งที่ทำทันที"],
    ["immediateBy", "ผู้ดำเนินการ (Action By)"],
    ["immediateDue", "กำหนดเสร็จ (DD/MM/YYYY)"],
  ]],
  ["D4 · สาเหตุที่แท้จริง (Determine Root Cause)", [
    ["rootCause", "สาเหตุที่แท้จริง — Fishbone / 5 Why / Pareto"],
  ]],
  ["D5 · การแก้ไขไม่ให้เกิดซ้ำ (Corrective Action)", [
    ["correctiveAction", "การแก้ไขถาวร"],
  ]],
  ["D6 · การทวนสอบ (Validate Corrective Action)", [
    ["effectivenessNote", "วิธีการหรือหลักฐานที่ใช้พิสูจน์"],
  ]],
  ["D7 · การป้องกันไม่ให้เกิดซ้ำ (Preventive Action)", [
    ["preventiveAction", "การป้องกัน"],
    ["documentsToRevise", "เอกสารที่ต้องแก้ไข"],
  ]],
  ["ผลการตรวจติดตามและการอนุมัติ", [
    ["followUpNote", "ผลการตรวจติดตาม"],
    ["followUpBy", "ผู้ติดตาม (Follow Up By)"],
    ["reviewedBy", "ผู้ทบทวน (Review By)"],
    ["approvalOutcome", "ผลการอนุมัติ"],
    ["approvalNote", "เหตุผลเมื่อไม่รับ (Not Accept: Specify)"],
  ]],
  ["D8 · แสดงความยินดีกับทีม (Congratulate Your Team)", [
    ["teamNote", "บันทึกถึงทีม"],
  ]],
];

/**
 * What a case is about, read off the job it was raised against.
 *
 * A case stores only the job's key. That is right for storage — it is the one
 * thing that cannot go stale — and useless on screen, where "งาน 2607014" tells
 * nobody which shipment went wrong. The register is already loaded, so the
 * details are looked up rather than copied into the case, which also means a
 * container number corrected on the job is corrected here.
 */
const JOB_FACTS: [string, (job: Job) => string][] = [
  ["ลูกค้า", (j) => j.customer],
  ["ผู้ขนส่ง", (j) => j.trucker],
  ["Job / ABS", (j) => j.jobCode || j.abs || j.jobNo || ""],
  ["Booking", (j) => j.booking],
  ["ตู้ / ซีล", (j) => [j.container, j.seal].filter(Boolean).join(" · ")],
  ["ประเภท", (j) => j.type],
  ["ปลายทาง", (j) => j.destination || j.plant || ""],
  ["วันที่ / เวลา", (j) => [j.date, j.planTime].filter(Boolean).join(" ")],
  ["ทะเบียนรถ", (j) => j.licence],
  ["คนขับ", (j) => [j.driver, j.contact].filter(Boolean).join(" · ")],
  ["สถานะ", (j) => j.status],
  ["ผู้รับผิดชอบ", (j) => j.op],
];

/**
 * The three of the 5W1H a job can answer, and only those.
 *
 * Where the load was going, when it was due, who was driving. What went wrong,
 * why it went wrong and how are the case itself — a job knows none of them, and
 * a form that arrives with those filled in from a shipment record is a form
 * people stop reading. Blanks are the honest answer and they are left blank.
 *
 * Written as sentences rather than a run of values, because these boxes are
 * read by whoever picks the case up, and "ผู้ขนส่ง JTC · คนขับ สมชาย 08x" is a
 * sentence while "JTC, สมชาย, 08x" is a row somebody has to decode.
 */
function seedFromJob(job: Job): { where: string; when: string; who: string } {
  // A label is skipped when the value already opens with it. The pickup note
  // on an import job is a whole sentence — "รับตู้ 02.07.26 .08.00 น." — and
  // labelling that produced "รับตู้ รับตู้ 02.07.26", which reads like a
  // stutter and is the sort of thing that makes people distrust the rest.
  const parts = (entries: [string, string][]) =>
    entries
      .filter(([, value]) => value && value.trim())
      .map(([label, value]) => {
        const text = value.trim();
        return text.startsWith(label) ? text : `${label} ${text}`;
      })
      .join(" · ");

  return {
    where: parts([
      ["ปลายทาง", job.destination],
      ["โรงงาน/สถานที่โหลด", job.plant],
      ["ลานตู้", job.cyYard],
      ["คืนตู้", job.returnLoc],
    ]),
    when: parts([
      ["แผน", [job.date, job.planTime].filter(Boolean).join(" ")],
      ["รับตู้", [job.pickupPlan, job.pickupTime].filter(Boolean).join(" ")],
      ["ถึงจริง", [job.arrDate, job.arrTime].filter(Boolean).join(" ")],
      ["ปิดตู้", [job.closingDate, job.closingTime].filter(Boolean).join(" ")],
    ]),
    who: parts([
      ["ผู้ขนส่ง", job.trucker],
      ["คนขับ", [job.driver, job.contact].filter(Boolean).join(" ")],
      ["ทะเบียน", job.licence],
      ["ผู้รับผิดชอบงาน", job.op],
    ]),
  };
}

export function Incidents({ prefill, jobs, onPrefillTaken, onOpenJob, onToast }: {
  /**
   * An operational issue escalated into a case.
   *
   * A case is only ever opened this way. The issue carries what went wrong and
   * which job it happened on; the job key is what makes the evidence files land
   * in that job's own folder rather than under a loose case number.
   */
  prefill?: { jobKey: string; title: string; what: string; issueCode: string } | null;
  /** The register, so a case can show the job it is about and not just its key. */
  jobs: Job[];
  onPrefillTaken?: () => void;
  /** Opens the job itself, for when the case is not where the answer is. */
  onOpenJob?: (jobKey: string) => void;
  onToast: (m: string) => void;
}) {
  const [cases, setCases] = useRemembered<Case[]>("incidents");
  const [picked, setPicked] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("");
  /** The job the next case will be raised against, when one was sent over. */
  const [jobKey, setJobKey] = useState("");
  /** What went wrong, brought over from the issue. Only it can answer this. */
  const [what, setWhat] = useState("");
  const [fromIssue, setFromIssue] = useState("");
  const [kind, setKind] = useState("CAR");
  const [category, setCategory] = useState("accident");

  const byKey = useMemo(() => new Map(jobs.map((job) => [job.key, job])), [jobs]);
  /** The job the next case will be raised against, once one has been sent over. */
  const raisingAgainst = jobKey ? byKey.get(jobKey) ?? null : null;

  const load = useCallback(async () => {
    const response = await apiFetch("/api/incidents", { headers: { accept: "application/json" } });
    const body = response.ok ? await response.json() as Case[] : null;
    setCases((held) => body ?? held ?? []);
  }, [setCases]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/incidents", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Case[] : null;
      if (!cancelled) setCases((held) => body ?? held ?? []);
    })();
    return () => { cancelled = true; };
  }, [setCases]);

  /** A job arriving from the workspace, taken once and then let go of. */
  useEffect(() => {
    if (!prefill) return;
    // The prop arrives once and has to become editable state — the next thing
    // that happens to the heading is somebody rewriting it.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTitle(prefill.title);
    setJobKey(prefill.jobKey);
    setWhat(prefill.what);
    setFromIssue(prefill.issueCode);
    onPrefillTaken?.();
  }, [prefill, onPrefillTaken]);

  async function post(path: string, body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/incidents${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  /**
   * Attaches evidence.
   *
   * The screen sends the case id and the file; it does not name a path. Where
   * the file lands — the job's own year, customer and CARPAR folder — is the
   * API's to decide, so the storage structure holds however the file arrives.
   */
  async function upload(caseId: number, files: File[], kind: string) {
    if (busy || files.length === 0) return;
    setBusy(true);
    try {
      // One request each rather than one for all: the API decides a path per
      // file, and a batch that fails halfway would leave nobody able to say
      // which half. Each file's own refusal is kept and reported by name.
      const refused: string[] = [];
      let added = 0;
      for (const file of files) {
        const body = new FormData();
        body.append("caseId", String(caseId));
        body.append("kind", kind);
        body.append("file", file);
        const response = await apiFetch("/api/documents", { method: "POST", body });
        const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
        if (response.ok) added++;
        else refused.push(`${file.name}: ${reply.error ?? reply.message ?? response.status}`);
      }
      onToast(refused.length === 0
        ? `แนบไฟล์แล้ว ${added} ไฟล์`
        : `แนบได้ ${added} จาก ${files.length} — ${refused[0]}`);
      await load();
    } finally { setBusy(false); }
  }

  if (!cases) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const open = cases.filter((c) => c.stage !== "closed");
  const chosen = cases.find((c) => c.id === picked) ?? null;

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="เปิดอยู่" value={open.length} colour="#B45309" />
        <Tile label="เกินกำหนด" value={cases.filter((c) => c.overdue).length} colour="#B42318" />
        <Tile label="อุบัติเหตุ" value={cases.filter((c) => c.category === "accident").length} colour="#B42318" />
        <Tile label="ปิดแล้ว" value={cases.length - open.length} colour="#16794C" />
      </div>

      {/*
        The job the case is about, shown before it is opened rather than after.
        The key travelled over from the workspace and then sat in a variable
        nobody could see: the screen filled in a heading and gave no sign which
        shipment it belonged to, which is a poor thing to ask somebody to sign
        an 8D against.
      */}
      {(jobKey || fromIssue) && (
        <div style={css("background:#F7FAFD;border:1px solid #C9DCEC;border-radius:5px;padding:12px 16px")}>
          <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:10px;flex-wrap:wrap")}>
            <span style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>
              {fromIssue ? `เปิดเคสจากปัญหา ${fromIssue}` : "เปิดเคสจากงานนี้"}
            </span>
            <div style={css("display:flex;gap:10px;align-items:baseline")}>
              {onOpenJob && raisingAgainst && (
                <button onClick={() => onOpenJob(jobKey)}
                  style={css("border:none;background:none;padding:0;font-size:11.5px;color:#2E7DD1;cursor:pointer;font-family:inherit;text-decoration:underline")}>
                  เปิดงาน
                </button>
              )}
              <button onClick={() => setJobKey("")}
                style={css("border:none;background:none;padding:0;font-size:11.5px;color:#7B8CA0;cursor:pointer;font-family:inherit")}>
                ไม่ผูกกับงาน
              </button>
            </div>
          </div>

          {raisingAgainst ? (
            <>
            <div style={css("margin-top:8px;font-size:11px;color:#5A6B7D;line-height:1.6")}>
              เปิดเคสแล้วระบบจะเติมช่อง <b>Where · When · Who</b> จากงานนี้
              {what ? <> และ <b>What</b> จากรายละเอียดของปัญหา</> : null} ให้เอง —
              ส่วน <b>Why · How</b> เว้นว่างไว้ เพราะนั่นคือสิ่งที่การสอบสวนต้องหาคำตอบ
            </div>
            <div style={css("margin-top:9px;display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:7px 20px")}>
              {JOB_FACTS.map(([label, read]) => {
                const value = read(raisingAgainst).trim();
                if (!value) return null;
                return (
                  <div key={label} style={css("display:flex;gap:7px;font-size:11.5px;min-width:0")}>
                    <span style={css("flex:0 0 84px;color:#7B8CA0")}>{label}</span>
                    <span style={css("color:#16232F;font-weight:600;overflow-wrap:anywhere")}>{value}</span>
                  </div>
                );
              })}
            </div>
            </>
          ) : (
            // The key is kept even when the job is not in the register that was
            // loaded — a case still belongs to it, and quietly dropping the link
            // would put the evidence in the wrong folder.
            <div style={css("margin-top:7px;font-size:11.5px;color:#B45309")}>
              ผูกกับงาน {jobKey} — ยังไม่พบงานนี้ในทะเบียนที่โหลดไว้ เคสยังผูกกับงานถูกต้อง
            </div>
          )}
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px;display:flex;gap:8px;align-items:flex-end;flex-wrap:wrap")}>
        <input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="หัวข้อเคสใหม่"
          style={css("flex:1;min-width:220px;height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px")} />
        <select value={kind} onChange={(e) => setKind(e.target.value)}
          style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff")}>
          <option value="CAR">CAR (แก้ไข)</option><option value="PAR">PAR (ป้องกัน)</option>
        </select>
        <select value={category} onChange={(e) => setCategory(e.target.value)}
          style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff")}>
          {Object.entries(CATEGORY_TH).map(([id, th]) => <option key={id} value={id}>{th}</option>)}
        </select>
        <button onClick={() => {
          // The job's own answers travel with the case rather than being typed
          // in again off the screen next door.
          const seed = raisingAgainst ? seedFromJob(raisingAgainst) : {};
          void post("", { kind, category, title, jobKey, what, ...seed });
          setTitle(""); setJobKey(""); setWhat(""); setFromIssue("");
        }}
          disabled={busy || !title.trim()}
          style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:" + (busy || !title.trim() ? "#C3CFDB" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
        >เปิดเคส</button>
      </div>

      <div style={css("display:grid;grid-template-columns:" + (chosen ? "1fr 1.2fr" : "1fr") + ";gap:14px;align-items:start")}>
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <ZoomBox>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["เลขที่", "หัวข้อ", "งาน", "หมวด", "ขั้นตอน", "กำหนด"].map((h) => (
                <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {cases.map((c) => (
                  <tr key={c.id} onClick={() => setPicked(c.id === picked ? null : c.id)}
                    style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (c.id === picked ? "#F2F7FC" : c.overdue ? "#FEF6F5" : "#fff"))}>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;font-weight:600")}>{c.reference}</td>
                    <td style={css(CELL + ";color:#0A2240")}>{c.title}</td>
                    {/* Which shipment, not which key. A case with no job is a
                        case about the operation rather than about a load, and
                        says so instead of showing a blank. */}
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>
                      {c.jobKey
                        ? (() => {
                          const job = byKey.get(c.jobKey);
                          return job
                            ? [job.jobCode || job.abs || job.container, job.customer].filter(Boolean).join(" · ")
                            : c.jobKey;
                        })()
                        : "ไม่ผูกกับงาน"}
                    </td>
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{CATEGORY_TH[c.category] ?? c.category}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:" + (c.stage === "closed" ? "#16794C" : "#B45309"))}>{STAGE_TH[c.stage] ?? c.stage}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;color:" + (c.overdue ? "#B42318" : "#7B8CA0"))}>{c.dueDate || "—"}</td>
                  </tr>
                ))}
                {!cases.length && <tr><td colSpan={6} style={css("padding:28px;text-align:center;color:#94A3B8")}>ยังไม่มีเคส</td></tr>}
              </tbody>
            </table>
          </ZoomBox>
        </div>

        {chosen && (
          <Detail case_={chosen} busy={busy} job={byKey.get(chosen.jobKey) ?? null} onOpenJob={onOpenJob}
            onSave={(fields) => void post(`/${chosen.id}`, fields)}
            onAdvance={() => void post(`/${chosen.id}/advance`, {})}
            onUpload={(files, kind) => void upload(chosen.id, files, kind)}
            onClose={() => setPicked(null)} />
        )}
      </div>
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:top";

const EVIDENCE_KINDS: [string, string][] = [
  ["photo", "รูปถ่าย"], ["driver-statement", "คำให้การคนขับ"],
  ["supplier-report", "รายงานจากผู้ขนส่ง"], ["customer-information", "ข้อมูลจากลูกค้า"],
];

function Detail({ case_, busy, job, onOpenJob, onSave, onAdvance, onUpload, onClose }: {
  case_: Case; busy: boolean;
  /** The job this case is about, when the register holds it. */
  job: Job | null;
  onOpenJob?: (jobKey: string) => void;
  onSave: (fields: Record<string, string>) => void;
  onAdvance: () => void;
  onUpload: (files: File[], kind: string) => void;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [kind, setKind] = useState("photo");
  /** Which evidence file is open, as a position in the case's own list. */
  const [viewing, setViewing] = useState<number | null>(null);
  /** The case number while it is being retyped; null when it is not. */
  const [renaming, setRenaming] = useState<string | null>(null);
  const position = STAGES.indexOf(case_.stage);
  const onView = (index: number) => setViewing(index);

  function rename() {
    const wanted = (renaming ?? "").trim().toUpperCase();
    setRenaming(null);
    if (wanted.length > 0 && wanted !== case_.reference) onSave({ reference: wanted });
  }

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;position:sticky;top:12px;max-height:calc(100vh - 110px);overflow-y:auto")}>
      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;gap:10px")}>
        <div>
          {/*
            The number is editable because the one that counts is on the paper
            form somebody is holding. A case entered after that form was written
            has to be able to take its number. Closed cases refuse, as they
            refuse every other edit.
          */}
          <div style={css("font-size:13.5px;font-weight:650;color:#0A2240;display:flex;align-items:baseline;gap:6px;flex-wrap:wrap")}>
            {renaming !== null ? (
              <input
                // eslint-disable-next-line jsx-a11y/no-autofocus
                autoFocus
                value={renaming} disabled={busy}
                // Selected on opening: this is a short identifier somebody is
                // replacing, not prose they are amending, so the first keystroke
                // should overwrite it rather than land in the middle of it.
                onFocus={(e) => e.target.select()}
                onChange={(e) => setRenaming(e.target.value)}
                onBlur={rename}
                onKeyDown={(e) => {
                  if (e.key === "Enter") rename();
                  if (e.key === "Escape") setRenaming(null);
                }}
                style={css("width:150px;height:26px;border:1px solid #0A5FA8;border-radius:3px;padding:0 7px;font-family:ui-monospace,monospace;font-size:12.5px;font-weight:600;color:#0A2240")} />
            ) : case_.stage === "closed" ? (
              <span style={css("font-family:ui-monospace,monospace")}>{case_.reference}</span>
            ) : (
              <button type="button" onClick={() => setRenaming(case_.reference)} title="แก้ไขเลขที่"
                style={css("border:none;background:none;padding:0;font-family:ui-monospace,monospace;font-size:13.5px;font-weight:650;color:#0A2240;cursor:pointer;border-bottom:1px dashed #94A3B8")}>
                {case_.reference}
              </button>
            )}
            <span>· {case_.title}</span>
          </div>
          <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>
            เปิดโดย {case_.raisedBy} · {stamp(case_.raisedAt)}
            {case_.approvedBy && ` · ปิดโดย ${case_.approvedBy}`}
          </div>
        </div>
        <button onClick={onClose} style={css("border:none;background:none;font-size:17px;color:#94A3B8;cursor:pointer;line-height:1;padding:0")}>×</button>
      </div>

      {job && (
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;background:#F7FAFD")}>
          <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:8px")}>
            <span style={css("font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>งานที่เกี่ยวข้อง</span>
            {onOpenJob && (
              <button onClick={() => onOpenJob(case_.jobKey)}
                style={css("border:none;background:none;padding:0;font-size:11.5px;color:#2E7DD1;cursor:pointer;font-family:inherit;text-decoration:underline")}>
                เปิดงาน
              </button>
            )}
          </div>
          <div style={css("margin-top:7px;display:grid;grid-template-columns:repeat(auto-fit,minmax(165px,1fr));gap:6px 16px")}>
            {JOB_FACTS.map(([label, read]) => {
              const value = read(job).trim();
              if (!value) return null;
              return (
                <div key={label} style={css("display:flex;gap:6px;font-size:11.5px;min-width:0")}>
                  <span style={css("flex:0 0 78px;color:#7B8CA0")}>{label}</span>
                  <span style={css("color:#16232F;font-weight:600;overflow-wrap:anywhere")}>{value}</span>
                </div>
              );
            })}
          </div>
        </div>
      )}

      <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;gap:4px;flex-wrap:wrap")}>
        {STAGES.map((stage, i) => (
          <span key={stage} style={css(
            "font-size:10.5px;font-weight:600;padding:3px 8px;border-radius:3px;" +
            (i < position ? "background:#E3F4EB;color:#16794C"
              : i === position ? "background:#0A2240;color:#fff"
                : "background:#F1F5F9;color:#94A3B8"))}>
            {STAGE_TH[stage]}
          </span>
        ))}
      </div>

      <div style={css("padding:13px 16px;display:flex;flex-direction:column;gap:9px")}>
        {SECTIONS.map(([heading, fields]) => (
          <div key={heading} style={css("display:flex;flex-direction:column;gap:6px;padding-top:4px")}>
            <span style={css("font-size:11px;font-weight:700;color:#0A2240;letter-spacing:.02em;border-bottom:1px solid #E9EFF5;padding-bottom:4px")}>
              {heading}
            </span>
            {fields.map(([key, label]) => {
              const held = (case_ as unknown as Record<string, string>)[key] || "";
              const choices = CHOICES[key];
              return (
                <label key={key} style={css("display:flex;flex-direction:column;gap:3px")}>
                  <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
                  {choices ? (
                    <select
                      value={draft[key] ?? held}
                      onChange={(e) => setDraft({ ...draft, [key]: e.target.value })}
                      disabled={case_.stage === "closed"}
                      style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 7px;font-size:12px;background:#fff")}>
                      <option value="">—</option>
                      {choices.map((choice) => <option key={choice} value={choice}>{choice}</option>)}
                    </select>
                  ) : (
                    <input
                      value={draft[key] ?? ""}
                      placeholder={held || "—"}
                      onChange={(e) => setDraft({ ...draft, [key]: e.target.value })}
                      disabled={case_.stage === "closed"}
                      style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
                  )}
                </label>
              );
            })}
          </div>
        ))}

        {case_.stage !== "closed" && (
          <div style={css("display:flex;gap:7px;margin-top:4px")}>
            <button onClick={() => { onSave(draft); setDraft({}); }} disabled={busy}
              style={css("height:30px;padding:0 13px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
            >บันทึก</button>
            <button onClick={onAdvance} disabled={busy}
              style={css("height:30px;padding:0 13px;border:1px solid #16794C;background:#fff;color:#16794C;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
            >ขั้นตอนถัดไป →</button>
          </div>
        )}
      </div>

      <div style={css("padding:12px 16px;border-top:1px solid #E9EFF5")}>
        <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:7px")}>
          หลักฐาน · {case_.evidence.length} ไฟล์
        </div>

        {case_.evidence.map((file, index) => (
          <div key={file.id} style={css("display:flex;gap:9px;align-items:center;padding:5px 0;border-bottom:1px solid #F1F5F9")}>
            {/*
              A photograph is recognised by looking at it, not by reading
              "IMG_20260902.JPG". The thumbnail is the same route the viewer
              uses, so a picture that will not open does not appear here either.
            */}
            {file.canShow && IMAGE.test(file.fileName) ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img src={shownAt(file.id)} alt="" loading="lazy"
                style={css("width:34px;height:34px;object-fit:cover;border-radius:3px;border:1px solid #E3E8EE;flex:none;background:#F8FAFC")} />
            ) : (
              <span style={css("width:34px;height:34px;border-radius:3px;border:1px solid #E3E8EE;background:#F8FAFC;flex:none;display:flex;align-items:center;justify-content:center;font-size:9.5px;font-weight:700;color:#94A3B8")}>
                {extensionOf(file.fileName)}
              </span>
            )}

            {file.canShow ? (
              <button type="button" onClick={() => onView(index)}
                style={css("flex:1;text-align:left;border:none;background:none;padding:0;font-family:inherit;font-size:12px;color:#0A5FA8;cursor:pointer;word-break:break-all;text-decoration:underline")}>
                {file.fileName}
              </button>
            ) : (
              // Nothing a browser can render, so the only honest offer is to
              // save it. Dressing it as "open" would produce a download anyway.
              <a href={savedAt(file.id)} download
                style={css("flex:1;font-size:12px;color:#0A5FA8;text-decoration:none;word-break:break-all")}>
                {file.fileName} ↓
              </a>
            )}
            <span style={css("font-size:11px;color:#7B8CA0;white-space:nowrap")}>{file.kind}</span>
            <span style={css("font-family:ui-monospace,monospace;font-size:11px;color:#94A3B8;white-space:nowrap")}>{size(file.sizeBytes)}</span>
          </div>
        ))}
        {!case_.evidence.length && (
          <div style={css("font-size:11.5px;color:#94A3B8;padding-bottom:6px")}>ยังไม่มีหลักฐานแนบ</div>
        )}

        {case_.stage !== "closed" && (
          <div style={css("display:flex;gap:6px;align-items:center;margin-top:9px;flex-wrap:wrap")}>
            <select value={kind} onChange={(e) => setKind(e.target.value)}
              style={css("height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 7px;font-size:11.5px;background:#fff")}>
              {EVIDENCE_KINDS.map(([id, label]) => <option key={id} value={id}>{label}</option>)}
            </select>
            <label style={css("height:28px;padding:0 12px;border:1px solid #0A2240;background:#fff;color:#0A2240;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center")}>
              แนบไฟล์
              <input type="file" multiple disabled={busy} style={css("display:none")}
                onChange={(e) => {
                  const files = [...e.target.files ?? []];
                  e.target.value = "";
                  if (files.length) onUpload(files, kind);
                }} />
            </label>
            <span style={css("font-size:11px;color:#94A3B8")}>
              เลือกได้หลายไฟล์ · เก็บใน SCMOS/{case_.jobKey ? "ปี/ลูกค้า/งาน" : "ปี/CARPAR/เลขเคส"}/CARPAR
            </span>
          </div>
        )}
      </div>

      {viewing !== null && case_.evidence[viewing] && (
        <Viewer files={case_.evidence} at={viewing} onMove={setViewing} onClose={() => setViewing(null)} />
      )}
    </div>
  );
}

/** Names that hold a picture, for deciding whether to draw a thumbnail. */
const IMAGE = /\.(jpe?g|png|gif|webp|bmp|heic)$/i;

const extensionOf = (name: string) => {
  const dot = name.lastIndexOf(".");
  return dot < 0 ? "?" : name.slice(dot + 1, dot + 5).toUpperCase();
};

/**
 * Looking at the evidence, rather than only collecting it.
 *
 * A CAR/PAR is mostly photographs of the damage and a scan of the signed form,
 * and every one of them used to be a download — which meant nobody read a case
 * on the screen, they filed it. The bytes still come through the API, which
 * knows who is asking; the container stays private and no URL here works
 * without a session.
 *
 * What may be displayed at all is the API's decision, not this component's. It
 * only ever asks for files the list already marked as showable.
 */
function Viewer({ files, at, onMove, onClose }: {
  files: Evidence[]; at: number;
  onMove: (at: number) => void; onClose: () => void;
}) {
  const file = files[at];
  // Stepping moves between the files that can be shown, skipping any that would
  // only offer to download — an arrow key that lands on a blank frame is worse
  // than one that does nothing.
  const shown = files.map((f, i) => (f.canShow ? i : -1)).filter((i) => i >= 0);
  const place = shown.indexOf(at);

  useEffect(() => {
    function pressed(event: KeyboardEvent) {
      if (event.key === "Escape") onClose();
      if (event.key === "ArrowRight" && place >= 0 && place < shown.length - 1) onMove(shown[place + 1]);
      if (event.key === "ArrowLeft" && place > 0) onMove(shown[place - 1]);
    }
    window.addEventListener("keydown", pressed);
    return () => window.removeEventListener("keydown", pressed);
  });

  /*
   * Rendered into the body rather than where it sits in the tree.
   *
   * The drawer around it is position:sticky, which makes a stacking context,
   * and a fixed child of one cannot rise above anything outside it however high
   * its z-index goes. In place, the viewer covered the page but the header and
   * the rail were drawn over the top of it — the file was on screen and its own
   * close button was not.
   */
  return createPortal(
    <div style={css("position:fixed;inset:0;z-index:60;display:flex;flex-direction:column;background:rgba(4,16,30,.86)")}>
      <div style={css("display:flex;align-items:center;gap:12px;padding:10px 16px;color:#fff;flex:none")}>
        <span style={css("font-size:12.5px;font-weight:600;flex:1;word-break:break-all")}>{file.fileName}</span>
        <span style={css("font-size:11.5px;color:#B6C6D6;white-space:nowrap")}>
          {file.kind} · {size(file.sizeBytes)}
          {shown.length > 1 && ` · ${place + 1}/${shown.length}`}
        </span>
        <a href={savedAt(file.id)} download
          style={css("font-size:11.5px;color:#fff;border:1px solid #46617E;border-radius:4px;padding:4px 10px;text-decoration:none;white-space:nowrap")}>
          บันทึกไฟล์
        </a>
        <button type="button" onClick={onClose} aria-label="ปิด"
          style={css("border:none;background:none;color:#fff;font-size:22px;line-height:1;cursor:pointer;padding:0 4px")}>×</button>
      </div>

      <div style={css("flex:1;min-height:0;display:flex;align-items:center;gap:8px;padding:0 12px 14px")}>
        <Step to={place > 0 ? shown[place - 1] : null} onMove={onMove} back />
        <div style={css("flex:1;height:100%;min-width:0;display:flex;align-items:center;justify-content:center")}>
          {IMAGE.test(file.fileName) ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={shownAt(file.id)} alt={file.fileName}
              style={css("max-width:100%;max-height:100%;object-fit:contain;border-radius:4px;background:#fff")} />
          ) : (
            // A PDF or a text file: the browser's own viewer, pointed at the
            // API. It runs under the policy the API sends with the file.
            <iframe src={shownAt(file.id)} title={file.fileName}
              style={css("width:100%;height:100%;border:none;border-radius:4px;background:#fff")} />
          )}
        </div>
        <Step to={place >= 0 && place < shown.length - 1 ? shown[place + 1] : null} onMove={onMove} />
      </div>
    </div>,
    document.body,
  );
}

function Step({ to, onMove, back }: { to: number | null; onMove: (at: number) => void; back?: boolean }) {
  return (
    <button type="button" disabled={to === null} aria-label={back ? "ก่อนหน้า" : "ถัดไป"}
      onClick={() => to !== null && onMove(to)}
      style={css("flex:none;width:36px;height:56px;border:none;border-radius:4px;font-size:24px;line-height:1;font-family:inherit;"
        + (to === null ? "background:transparent;color:#3C5470;cursor:default" : "background:rgba(255,255,255,.14);color:#fff;cursor:pointer"))}>
      {back ? "‹" : "›"}
    </button>
  );
}

function size(bytes: number) {
  return bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + " MB" : Math.max(1, Math.round(bytes / 1024)) + " KB";
}

function Tile({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
    </div>
  );
}
