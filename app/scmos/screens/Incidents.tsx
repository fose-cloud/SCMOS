"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { createPortal } from "react-dom";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import type { Job } from "../ops";
import { stamp } from "./WorkflowPanel";
import { css } from "../theme";
import { StatCard, StatGlyph } from "../StatCard";
import { Badge, PAGE_SIZES, Pager, Pick } from "../BoardBits";
import { STAGES, STAGE_TH } from "../incidentStages";

/**
 * Incident and CAR/PAR — the status monitor.
 *
 * The eight-disciplines form is filled on paper and signed there; what SCMOS
 * holds is which cases exist, what each is about, which job it was raised
 * on, the evidence, and where each case stands. So this screen is a board of
 * cases by stage, and a supervisor moves a case to a stage or removes it
 * outright. There is nothing to type into a case here beyond opening it —
 * the department asked for the entry form to go, on 15 September 2026 — and
 * the details a case does carry (from the Excel register, or from the job it
 * was raised against) are shown as they are.
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

/** The colour a stage is drawn in: amber while it is new, green once it is signed off. */
const STAGE_TONE: Record<string, string> = {
  open: "#B45309", analysis: "#1668AB", action: "#1668AB",
  "follow-up": "#6D4FB3", monitoring: "#6D4FB3", approval: "#0A2240", closed: "#16794C",
};
const toneOf = (stage: string) => STAGE_TONE[stage] ?? "#7B8CA0";

/**
 * What a case holds, read out as it is. Only the lines that have something
 * in them are drawn: an Excel-imported case carries the register's columns
 * in its team note, a case raised from a job carries the job's answers to
 * where, when and who, and a case opened by hand carries its heading.
 */
const CASE_FACTS: [keyof Case, string][] = [
  ["company", "บริษัท"], ["grade", "ระดับ"], ["source", "ที่มา"], ["ncClause", "NC Clause"],
  ["requestedBy", "ผู้ร้องขอ"], ["requestedOn", "วันที่ร้องขอ"],
  ["responsiblePerson", "ผู้รับผิดชอบ"], ["dueDate", "กำหนดเสร็จ"],
  ["what", "What"], ["where", "Where"], ["when", "When"], ["who", "Who"],
  ["rootCause", "สาเหตุ"], ["correctiveAction", "การแก้ไข"], ["preventiveAction", "การป้องกัน"],
  ["followUpBy", "ผู้ติดตาม"], ["reviewedBy", "ผู้ทบทวน"], ["approvalOutcome", "ผลการอนุมัติ"],
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

/** What a sheet of cases would become. The record the import endpoint answers with. */
type SheetImport = {
  read: number; skipped: number; created: number;
  /** field: heading, for the columns it recognised. */
  matched: string[];
  /** Fields no column was found for. */
  missing: string[];
  sample: string[];
  applied: boolean;
  duplicates: string[];
  statuses: Record<string, number>;
};

/** The status the Excel register gave a case, kept in the note the importer writes. */
const EXCEL_STATUS = "SCMOS Excel import\nExcel status: ";
const excelStatus = (c: Case) =>
  c.teamNote?.startsWith(EXCEL_STATUS) ? c.teamNote.split("\n")[1].slice("Excel status: ".length) : "";

/** The shipment a case is about, as a person would name it, or the key when the register does not hold it. */
function jobName(c: Case, byKey: Map<string, Job>): string {
  if (!c.jobKey) return "";
  const job = byKey.get(c.jobKey);
  return job ? [job.jobCode || job.abs || job.container, job.customer].filter(Boolean).join(" · ") : c.jobKey;
}

/** The reason a supervisor gives, asked for in a prompt; null when they thought better of it. */
function askReason(question: string, onToast: (m: string) => void): string | null {
  const answer = window.prompt(question);
  if (answer === null) return null;
  if (answer.trim().length < 4) { onToast("ใส่เหตุผลอย่างน้อย 4 ตัวอักษร"); return null; }
  return answer.trim();
}

export function Incidents({ prefill, jobs, canImport = false, canManage = false, onPrefillTaken, onOpenJob, onToast }: {
  canImport?: boolean;
  /** Supervisor and above: may move a case to any stage, and may remove one. */
  canManage?: boolean;
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
  /* The sheet being imported: the file, and what the API says it would do. */
  const [sheetFile, setSheetFile] = useState<File | null>(null);
  const [sheet, setSheet] = useState<SheetImport | null>(null);
  /** The job the next case will be raised against, when one was sent over. */
  const [jobKey, setJobKey] = useState("");
  /** What went wrong, brought over from the issue. Only it can answer this. */
  const [what, setWhat] = useState("");
  const [fromIssue, setFromIssue] = useState("");
  const [kind, setKind] = useState("CAR");
  const [category, setCategory] = useState("accident");
  /* The board: which stage is picked out, what is typed in the search, which page. */
  const [stageFilter, setStageFilter] = useState("");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [per, setPer] = useState(PAGE_SIZES[0]);

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

  /**
   * Send the sheet and say what it would do, or do it.
   *
   * The same call either way, with `apply` deciding — so what is confirmed is
   * exactly what was previewed, read by the same code from the same file,
   * rather than a preview from one path and a write from another.
   */
  async function importSheet(file: File, apply: boolean) {
    if (busy) return;
    setBusy(true);
    try {
      const form = new FormData();
      form.append("file", file);
      if (apply) form.append("apply", "1");

      const response = await apiFetch("/api/incidents/import", { method: "POST", body: form });
      const reply = await response.json().catch(() => null) as (SheetImport & { error?: string }) | null;

      if (!response.ok) { onToast(reply?.error ?? `นำเข้าไม่สำเร็จ (${response.status})`); return; }

      setSheet(reply);
      if (apply) {
        onToast(`นำเข้าแล้ว ${reply?.created ?? 0} รายการ โดยคงสถานะต้นฉบับ`);
        setSheetFile(null);
        await load();
      }
    } catch (error) {
      onToast("นำเข้าไม่สำเร็จ: " + (error instanceof Error ? error.message : String(error)));
    } finally { setBusy(false); }
  }

  /**
   * One call to the case API. What it says comes back as the toast — or, when
   * it went through, what this screen would rather say, since the API speaks
   * in stage keys and the person reads Thai. The board is re-read either way.
   */
  async function call(path: string, method: "POST" | "DELETE", body?: unknown, said?: string): Promise<boolean> {
    if (busy) return false;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/incidents${path}`, {
        method,
        headers: body === undefined ? undefined : { "content-type": "application/json" },
        body: body === undefined ? undefined : JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast((response.ok && said) || reply.message || reply.error || "ทำรายการไม่สำเร็จ");
      await load();
      return response.ok;
    } finally { setBusy(false); }
  }

  /**
   * Moves a case to a stage. Closing is a signature, so closing asks why;
   * every other move is recorded with who and when, which is what the audit
   * needs to say about it.
   */
  function setStage(c: Case, stage: string) {
    if (stage === c.stage) return;
    let reason = "";
    if (stage === "closed") {
      const given = askReason(`ปิดเคส ${c.reference} — เหตุผลในการปิด`, onToast);
      if (given === null) return;
      reason = given;
    }
    void call(`/${c.id}/stage`, "POST", { stage, reason }, `${c.reference} → ${STAGE_TH[stage] ?? stage}`);
  }

  /** Removes a case for good. The reason is the only thing the case leaves behind. */
  function remove(c: Case) {
    const reason = askReason(`ลบเคส ${c.reference} · ${c.title}\nเหตุผลในการลบ (ลบแล้วกู้คืนไม่ได้)`, onToast);
    if (reason === null) return;
    void (async () => {
      const done = await call(`/${c.id}?reason=${encodeURIComponent(reason)}`, "DELETE");
      if (done && picked === c.id) setPicked(null);
    })();
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
  const atStage = (stage: string) => cases.filter((c) => c.stage === stage).length;

  // The rows on the board: the picked stage, then the search, then the page.
  const needle = query.trim().toLowerCase();
  const shown = cases.filter((c) => (!stageFilter || c.stage === stageFilter) && (!needle
    || [c.reference, c.title, c.kind, CATEGORY_TH[c.category] ?? c.category, c.responsiblePerson,
      jobName(c, byKey), excelStatus(c)].join(" ").toLowerCase().includes(needle)));
  const pageCount = Math.max(1, Math.ceil(shown.length / per));
  const at = Math.min(page, pageCount);
  const paged = shown.slice((at - 1) * per, at * per);
  const pick = (stage: string) => { setStageFilter(stage); setPage(1); };

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="เปิดอยู่" value={open.length} colour="#B45309" icon="flag" />
        <Tile label="เกินกำหนด" value={cases.filter((c) => c.overdue).length} colour="#B42318" icon="clock" />
        <Tile label="รออนุมัติ" value={atStage("approval")} colour="#0A2240" icon="shield" />
        <Tile label="ปิดแล้ว" value={cases.length - open.length} colour="#16794C" icon="check" />
      </div>

      {/*
        The job the case is about, shown before it is opened rather than after.
        The key travelled over from the workspace and then sat in a variable
        nobody could see: the screen filled in a heading and gave no sign which
        shipment it belonged to.
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

      {/*
        What a sheet of cases would become, before it becomes it.

        The same shape as the supplier import: read, show, then write only when
        told a second time. Cases are what corrective action is tracked on, and
        a file read wrongly makes work rather than recording it.
      */}
      {sheet && (
        <div style={css("background:#F8FAFC;border:1px solid #D8E0E8;border-radius:5px;padding:13px 16px")}>
          <p>คงสถานะต้นฉบับ: {Object.entries(sheet.statuses).map(([status, count]) => `${status} ${count}`).join(" · ")}</p>
          <p>พร้อมนำเข้า {sheet.created} รายการ · ข้ามเลขที่ซ้ำ {sheet.duplicates.length} รายการ</p>
          {sheet.duplicates.length > 0 && <p>ไม่เขียนทับ: {sheet.duplicates.join(", ")}</p>}
          <p>Closed → ปิดแล้ว · Request Evidence → ติดตาม · Waiting Response → เปิดเคส โดยแสดงสถานะต้นฉบับแยกไว้</p>
          <p>เก็บข้อมูลทุกคอลัมน์ไว้ในบันทึกถึงทีม เลขงานต้นฉบับยังไม่ผูกกับ My Job</p>
          <div style={css("display:flex;gap:18px;flex-wrap:wrap;align-items:center;margin-bottom:9px")}>
            <div>
              <div style={css("font-size:16px;font-weight:700;font-family:ui-monospace,monospace;color:#0A2240")}>{sheet.read}</div>
              <div style={css("font-size:10.5px;color:#7B8CA0")}>อ่านได้</div>
            </div>
            {sheet.skipped > 0 && (
              <div>
                <div style={css("font-size:16px;font-weight:700;font-family:ui-monospace,monospace;color:#B45309")}>{sheet.skipped}</div>
                <div style={css("font-size:10.5px;color:#7B8CA0")}>ข้ามเพราะไม่มีหัวข้อ</div>
              </div>
            )}
          </div>

          {/* Which column became which field. The importer was written without
              the file, so this is the part worth checking before writing. */}
          {sheet.matched.length > 0 && (
            <div style={css("font-size:11.5px;color:#5A6B7D;line-height:1.7;margin-bottom:6px")}>
              อ่านคอลัมน์ได้: <b>{sheet.matched.join(" · ")}</b>
            </div>
          )}
          {sheet.missing.length > 0 && (
            <div style={css("font-size:11.5px;color:#8A5A12;line-height:1.7;margin-bottom:6px")}>
              ไม่พบชื่อคอลัมน์มาตรฐาน: {sheet.missing.join(" · ")} — ข้อมูลต้นฉบับทุกคอลัมน์ยังเก็บไว้ในบันทึกถึงทีม
            </div>
          )}
          {sheet.sample.length > 0 && (
            <div style={css("font-size:11px;color:#7B8CA0;font-family:ui-monospace,monospace;line-height:1.7;margin-bottom:9px")}>
              {sheet.sample.map((line, i) => <div key={i}>{line}</div>)}
            </div>
          )}

          <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
            {sheet.applied ? (
              <span style={css("font-size:12px;color:#16794C;font-weight:600")}>
                นำเข้าแล้ว {sheet.created} รายการ โดยคงสถานะต้นฉบับ
              </span>
            ) : (
              <>
                <button disabled={busy || !sheetFile}
                  onClick={() => sheetFile && void importSheet(sheetFile, true)}
                  style={css("height:30px;padding:0 14px;border-radius:4px;font-size:12px;font-weight:600;font-family:inherit;"
                    + (busy ? "border:1px solid #DDE4EC;background:#E6EBF1;color:#94A3B8;cursor:not-allowed"
                            : "border:1px solid #16794C;background:#16794C;color:#fff;cursor:pointer"))}>
                  {busy ? "กำลังนำเข้า…" : `ยืนยันนำเข้า ${sheet.created} รายการ (คงสถานะ)`}
                </button>
                <span style={css("font-size:11.5px;color:#7B8CA0")}>ยังไม่ได้บันทึกอะไร</span>
              </>
            )}
            <button onClick={() => { setSheet(null); setSheetFile(null); }}
              style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;background:#fff;color:#5A6B7D;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
              ปิด
            </button>
          </div>
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
        {/* Reading a sheet the team already keeps, rather than retyping it.
            The importer matches on column headings in either language, because
            it was written without the file it will be given. */}
        {canImport && <label style={css("height:30px;padding:0 12px;border:1px solid #0A2240;background:"
          + (busy ? "#C3CFDB" : "#fff") + ";color:" + (busy ? "#fff" : "#0A2240")
          + ";border-radius:4px;font-size:12px;font-weight:600;font-family:inherit;"
          + "display:inline-flex;align-items:center;gap:6px;cursor:" + (busy ? "default" : "pointer"))}>
          <StatGlyph icon="upload" size={14} />
          {busy ? "กำลังอ่าน…" : "นำเข้าจาก Excel"}
          <input type="file" accept=".xlsx" disabled={busy} style={css("display:none")}
            onChange={(e) => {
              const chosen = e.target.files?.[0];
              // Cleared so choosing the same file twice fires again, which
              // somebody will do after fixing a heading in it.
              e.target.value = "";
              if (!chosen) return;
              setSheetFile(chosen);
              setSheet(null);
              void importSheet(chosen, false);
            }} />
        </label>}

        <button onClick={() => {
          // The job's own answers travel with the case rather than being typed
          // in again off the screen next door.
          const seed = raisingAgainst ? seedFromJob(raisingAgainst) : {};
          void call("", "POST", { kind, category, title, jobKey, what, ...seed });
          setTitle(""); setJobKey(""); setWhat(""); setFromIssue("");
        }}
          disabled={busy || !title.trim()}
          style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:" + (busy || !title.trim() ? "#C3CFDB" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:6px")}
        ><StatGlyph icon="plus" size={14} />เปิดเคส</button>
      </div>

      <div style={css("display:grid;grid-template-columns:" + (chosen ? "1fr 1fr" : "1fr") + ";gap:14px;align-items:start")}>
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          {/* The board's own bar: a chip per stage with how many sit there,
              the search, and a way to re-read. Above the scroll box, so a
              filter that is set stays in view. */}
          <div style={css("padding:10px 14px;border-bottom:1px solid #E9EFF5;display:flex;align-items:center;gap:6px;flex-wrap:wrap")}>
            <Pick on={stageFilter === ""} tone="#0A2240" count={cases.length} onClick={() => pick("")}>ทั้งหมด</Pick>
            {STAGES.map((stage) => (
              <Pick key={stage} on={stageFilter === stage} tone={toneOf(stage)} count={atStage(stage)}
                onClick={() => pick(stageFilter === stage ? "" : stage)}>
                {STAGE_TH[stage]}
              </Pick>
            ))}
            <span style={css("margin-left:auto;display:inline-flex;align-items:center;gap:6px;height:30px;border:1px solid #C9D6E2;border-radius:6px;padding:0 9px;background:#fff;min-width:220px")}>
              <span style={css("display:flex;color:#7B8CA0")}><StatGlyph icon="search" size={14} /></span>
              <input value={query} onChange={(e) => { setQuery(e.target.value); setPage(1); }}
                placeholder="ค้นหา เลขที่ / หัวข้อ / งาน / ผู้รับผิดชอบ"
                style={css("flex:1;border:0;outline:none;font-size:12px;font-family:inherit;background:transparent;color:#16232F")} />
            </span>
            <button type="button" onClick={() => void load()} disabled={busy}
              style={css("height:30px;padding:0 12px;border:1px solid #C9D6E2;border-radius:6px;background:#fff;"
                + "font-size:12px;font-weight:600;font-family:inherit;cursor:pointer;color:#0A2240;display:inline-flex;align-items:center;gap:6px")}>
              <StatGlyph icon="refresh" size={14} />
              รีเฟรช
            </button>
          </div>

          <div style={{ overflowX: "auto" }}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{[...HEADS, ...(canManage ? ["จัดการ"] : [])].map((h) => (
                <th key={h} style={css("background:#F4F7FA;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#465A6E;font-weight:700;border-bottom:1px solid #D8E0E8;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {paged.map((c) => (
                  <tr key={c.id} onClick={() => setPicked(c.id === picked ? null : c.id)}
                    style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (c.id === picked ? "#F2F7FC" : c.overdue ? "#FEF6F5" : "#fff"))}>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;font-weight:600;white-space:nowrap")}>{c.reference}</td>
                    <td style={css(CELL + ";white-space:nowrap")}>
                      <Badge tone={c.kind === "PAR" ? "#1668AB" : "#B42318"} icon={c.kind === "PAR" ? "shield" : "warning"}>{c.kind}</Badge>
                    </td>
                    <td style={css(CELL + ";color:#0A2240;min-width:180px")}>{c.title}</td>
                    {/* Which shipment, not which key. A case with no job is a
                        case about the operation rather than about a load, and
                        says so instead of showing a blank. */}
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{jobName(c, byKey) || "ไม่ผูกกับงาน"}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D;white-space:nowrap")}>{CATEGORY_TH[c.category] ?? c.category}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:#16232F;white-space:nowrap")}>{c.responsiblePerson || "—"}</td>
                    <td style={css(CELL + ";white-space:nowrap")} onClick={(e) => e.stopPropagation()}>
                      {/* A supervisor moves the case from here; everyone else
                          reads where it is. The Excel register's own words
                          stay under it for the cases that came from there. */}
                      {canManage ? (
                        <select value={c.stage} disabled={busy} onChange={(e) => setStage(c, e.target.value)}
                          style={css(`height:28px;border:1px solid ${toneOf(c.stage)}66;border-radius:5px;padding:0 6px;font-size:11.5px;font-weight:700;`
                            + `font-family:inherit;color:${toneOf(c.stage)};background:${toneOf(c.stage)}14;cursor:pointer`)}>
                          {STAGES.map((stage) => <option key={stage} value={stage}>{STAGE_TH[stage]}</option>)}
                          {!STAGES.includes(c.stage) && <option value={c.stage}>{c.stage}</option>}
                        </select>
                      ) : (
                        <Badge tone={toneOf(c.stage)} icon={c.stage === "closed" ? "check" : "flag"}>{STAGE_TH[c.stage] ?? c.stage}</Badge>
                      )}
                      {excelStatus(c) && <div style={css("font-size:10.5px;color:#7B8CA0;margin-top:3px")}>ต้นฉบับ: {excelStatus(c)}</div>}
                    </td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;white-space:nowrap;color:" + (c.overdue ? "#B42318" : "#7B8CA0"))}>{c.dueDate || "—"}</td>
                    <td style={css(CELL + ";font-size:11px;color:#7B8CA0;white-space:nowrap")}>{stamp(c.raisedAt)}<div>{c.raisedBy}</div></td>
                    {canManage && (
                      <td style={css(CELL + ";white-space:nowrap")} onClick={(e) => e.stopPropagation()}>
                        <button type="button" disabled={busy} onClick={() => remove(c)} title="ลบเคส"
                          style={css("height:28px;padding:0 10px;border:1px solid #B4231866;border-radius:5px;background:#fff;color:#B42318;"
                            + "font-size:11.5px;font-weight:600;font-family:inherit;cursor:pointer;display:inline-flex;align-items:center;gap:5px")}>
                          <StatGlyph icon="cancel" size={12} />ลบ
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
                {!paged.length && (
                  <tr><td colSpan={HEADS.length + (canManage ? 1 : 0)} style={css("padding:28px;text-align:center;color:#94A3B8")}>
                    {cases.length ? "ไม่มีเคสตามเงื่อนไขนี้" : "ยังไม่มีเคส"}
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>
          {shown.length > 0 && (
            <Pager total={shown.length} page={at} pageCount={pageCount} per={per}
              onPage={setPage} onPer={(n) => { setPer(n); setPage(1); }} />
          )}
        </div>

        {chosen && (
          <Detail case_={chosen} busy={busy} canManage={canManage} job={byKey.get(chosen.jobKey) ?? null} onOpenJob={onOpenJob}
            onRename={(reference) => void call(`/${chosen.id}`, "POST", { reference })}
            onStage={(stage) => setStage(chosen, stage)}
            onRemove={() => remove(chosen)}
            onUpload={(files, kind) => void upload(chosen.id, files, kind)}
            onClose={() => setPicked(null)} />
        )}
      </div>
    </div>
  );
}

const HEADS = ["เลขที่", "ประเภท", "หัวข้อ", "งาน", "หมวด", "ผู้รับผิดชอบ", "ขั้นตอน", "กำหนด", "เปิดเมื่อ"];
const CELL = "padding:8px 12px;vertical-align:top";

const EVIDENCE_KINDS: [string, string][] = [
  ["photo", "รูปถ่าย"], ["driver-statement", "คำให้การคนขับ"],
  ["supplier-report", "รายงานจากผู้ขนส่ง"], ["customer-information", "ข้อมูลจากลูกค้า"],
];

function Detail({ case_, busy, canManage, job, onOpenJob, onRename, onStage, onRemove, onUpload, onClose }: {
  case_: Case; busy: boolean; canManage: boolean;
  /** The job this case is about, when the register holds it. */
  job: Job | null;
  onOpenJob?: (jobKey: string) => void;
  onRename: (reference: string) => void;
  onStage: (stage: string) => void;
  onRemove: () => void;
  onUpload: (files: File[], kind: string) => void;
  onClose: () => void;
}) {
  const [kind, setKind] = useState("photo");
  /** Which evidence file is open, as a position in the case's own list. */
  const [viewing, setViewing] = useState<number | null>(null);
  /** The case number while it is being retyped; null when it is not. */
  const [renaming, setRenaming] = useState<string | null>(null);
  const position = STAGES.indexOf(case_.stage);
  const onView = (index: number) => setViewing(index);
  const facts = CASE_FACTS.filter(([key]) => String(case_[key] ?? "").trim().length > 0);
  const note = excelStatus(case_) ? case_.teamNote.split("\n").slice(2).join("\n").trim() : (case_.teamNote ?? "").trim();

  function rename() {
    const wanted = (renaming ?? "").trim().toUpperCase();
    setRenaming(null);
    if (wanted.length > 0 && wanted !== case_.reference) onRename(wanted);
  }

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;position:sticky;top:12px;max-height:calc(100vh - 110px);overflow-y:auto")}>
      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;gap:10px")}>
        <div>
          {/*
            The number is a supervisor's to change because the one that counts
            is on the paper form somebody is holding. A case entered after that
            form was written has to be able to take its number. Closed cases
            keep theirs.
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
            ) : canManage && case_.stage !== "closed" ? (
              <button type="button" onClick={() => setRenaming(case_.reference)} title="แก้ไขเลขที่"
                style={css("border:none;background:none;padding:0;font-family:ui-monospace,monospace;font-size:13.5px;font-weight:650;color:#0A2240;cursor:pointer;border-bottom:1px dashed #94A3B8")}>
                {case_.reference}
              </button>
            ) : (
              <span style={css("font-family:ui-monospace,monospace")}>{case_.reference}</span>
            )}
            <span>· {case_.title}</span>
          </div>
          <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px;display:flex;gap:6px;align-items:center;flex-wrap:wrap")}>
            <Badge tone={case_.kind === "PAR" ? "#1668AB" : "#B42318"} icon={case_.kind === "PAR" ? "shield" : "warning"}>{case_.kind}</Badge>
            <span>{CATEGORY_TH[case_.category] ?? case_.category}</span>
            <span>· เปิดโดย {case_.raisedBy} · {stamp(case_.raisedAt)}</span>
            {case_.approvedBy && <span>· ปิดโดย {case_.approvedBy}{case_.approvedAt ? ` · ${stamp(case_.approvedAt)}` : ""}</span>}
          </div>
        </div>
        <button onClick={onClose} style={css("border:none;background:none;font-size:17px;color:#94A3B8;cursor:pointer;line-height:1;padding:0")}>×</button>
      </div>

      {/* Where the case stands. A supervisor presses the stage it should be at;
          everyone else sees the ones passed, the one it is at, and the rest. */}
      <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;gap:4px;flex-wrap:wrap;align-items:center")}>
        {STAGES.map((stage, i) => {
          const look = "font-size:10.5px;font-weight:600;padding:4px 9px;border-radius:4px;font-family:inherit;border:1px solid transparent;"
            + (i < position ? "background:#E3F4EB;color:#16794C"
              : i === position ? "background:#0A2240;color:#fff"
                : "background:#F1F5F9;color:#94A3B8");
          return canManage ? (
            <button key={stage} type="button" disabled={busy || i === position} onClick={() => onStage(stage)}
              title={i === position ? "ขั้นตอนปัจจุบัน" : `ย้ายไป ${STAGE_TH[stage]}`}
              style={css(look + (i === position ? ";cursor:default" : ";cursor:pointer"))}>
              {STAGE_TH[stage]}
            </button>
          ) : (
            <span key={stage} style={css(look)}>{STAGE_TH[stage]}</span>
          );
        })}
        {!STAGES.includes(case_.stage) && (
          <span style={css("font-size:10.5px;font-weight:600;padding:4px 9px;border-radius:4px;background:#0A2240;color:#fff")}>{case_.stage}</span>
        )}
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

      {(facts.length > 0 || note) && (
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5")}>
          <div style={css("font-size:11px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:7px")}>รายละเอียดเคส</div>
          <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(200px,1fr));gap:6px 16px")}>
            {facts.map(([key, label]) => (
              <div key={key} style={css("display:flex;gap:6px;font-size:11.5px;min-width:0")}>
                <span style={css("flex:0 0 78px;color:#7B8CA0")}>{label}</span>
                <span style={css("color:#16232F;font-weight:600;overflow-wrap:anywhere;white-space:pre-wrap")}>{String(case_[key])}</span>
              </div>
            ))}
          </div>
          {note && (
            <div style={css("margin-top:8px;font-size:11.5px;color:#16232F;white-space:pre-wrap;line-height:1.6;background:#F8FAFC;border:1px solid #E9EFF5;border-radius:4px;padding:8px 10px")}>{note}</div>
          )}
        </div>
      )}

      <div style={css("padding:12px 16px")}>
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
            <label style={css("height:28px;padding:0 12px;border:1px solid #0A2240;background:#fff;color:#0A2240;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;gap:5px")}>
              <StatGlyph icon="upload" size={13} />
              แนบไฟล์
              <input type="file" multiple disabled={busy} style={css("display:none")}
                onChange={(e) => {
                  const files = [...e.target.files ?? []];
                  e.target.value = "";
                  if (files.length) onUpload(files, kind);
                }} />
            </label>
          </div>
        )}
      </div>

      {canManage && (
        <div style={css("padding:10px 16px;border-top:1px solid #E9EFF5;display:flex;justify-content:flex-end")}>
          <button type="button" disabled={busy} onClick={onRemove}
            style={css("height:30px;padding:0 12px;border:1px solid #B4231866;border-radius:5px;background:#fff;color:#B42318;"
              + "font-size:12px;font-weight:600;font-family:inherit;cursor:pointer;display:inline-flex;align-items:center;gap:6px")}>
            <StatGlyph icon="cancel" size={13} />ลบเคสนี้
          </button>
        </div>
      )}

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

function Tile({ label, value, colour, icon }: { label: string; value: number; colour: string; icon: "flag" | "clock" | "shield" | "check" }) {
  return <StatCard label={label} value={value.toLocaleString()} tone={colour} icon={icon} />;
}
