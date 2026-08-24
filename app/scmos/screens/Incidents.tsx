"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "./WorkflowPanel";
import { css } from "../theme";

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
};

/** A file on the case. The path it went to is decided by the API, not here. */
type Evidence = {
  id: number; kind: string; fileName: string; note: string;
  folder: string; sizeBytes: number; objectKey: string;
  uploadedBy: string; uploadedAt: string;
};

const STAGES = ["open", "analysis", "action", "follow-up", "monitoring", "approval", "closed"];
const STAGE_TH: Record<string, string> = {
  open: "เปิดเคส", analysis: "วิเคราะห์", action: "กำหนดการแก้ไข",
  "follow-up": "ติดตาม", monitoring: "ติดตามประสิทธิผล", approval: "รออนุมัติ", closed: "ปิดแล้ว",
};
const CATEGORY_TH: Record<string, string> = {
  accident: "อุบัติเหตุ", damage: "ความเสียหาย", delay: "ความล่าช้า",
  safety: "ความปลอดภัย", quality: "คุณภาพ", other: "อื่นๆ",
};

const FIELDS: [string, string][] = [
  ["what", "What — เกิดอะไรขึ้น"], ["where", "Where — ที่ไหน"], ["when", "When — เมื่อไหร่"],
  ["who", "Who — ใครเกี่ยวข้อง"], ["why", "Why — ทำไม"], ["how", "How — อย่างไร"],
  ["rootCause", "สาเหตุที่แท้จริง (Root Cause)"],
  ["correctiveAction", "การแก้ไข (Corrective)"], ["preventiveAction", "การป้องกัน (Preventive)"],
  ["responsiblePerson", "ผู้รับผิดชอบ"], ["dueDate", "กำหนดเสร็จ (DD/MM/YYYY)"],
  ["followUpNote", "บันทึกการติดตาม"], ["effectivenessNote", "ผลการติดตามประสิทธิผล"],
];

export function Incidents({ prefill, onPrefillTaken, onToast }: {
  /**
   * A job sent over from the workspace drawer.
   *
   * The case is opened against its key, which is what makes the evidence files
   * land in the job's own folder rather than under a loose case number.
   */
  prefill?: { jobKey: string; title: string } | null;
  onPrefillTaken?: () => void;
  onToast: (m: string) => void;
}) {
  const [cases, setCases] = useState<Case[] | null>(null);
  const [picked, setPicked] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [title, setTitle] = useState("");
  /** The job the next case will be raised against, when one was sent over. */
  const [jobKey, setJobKey] = useState("");
  const [kind, setKind] = useState("CAR");
  const [category, setCategory] = useState("accident");

  const load = useCallback(async () => {
    const response = await apiFetch("/api/incidents", { headers: { accept: "application/json" } });
    setCases(response.ok ? await response.json() as Case[] : []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/incidents", { headers: { accept: "application/json" } });
      const body = response.ok ? await response.json() as Case[] : [];
      if (!cancelled) setCases(body);
    })();
    return () => { cancelled = true; };
  }, []);

  /** A job arriving from the workspace, taken once and then let go of. */
  useEffect(() => {
    if (!prefill) return;
    // The prop arrives once and has to become editable state — the next thing
    // that happens to the heading is somebody rewriting it.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTitle(prefill.title);
    setJobKey(prefill.jobKey);
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
  async function upload(caseId: number, file: File, kind: string) {
    if (busy) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append("caseId", String(caseId));
      body.append("kind", kind);
      body.append("file", file);
      const response = await apiFetch("/api/documents", { method: "POST", body });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "อัปโหลดไม่สำเร็จ");
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
        <button onClick={() => { void post("", { kind, category, title, jobKey }); setTitle(""); setJobKey(""); }}
          disabled={busy || !title.trim()}
          style={css("height:30px;padding:0 14px;border:1px solid #0A2240;background:" + (busy || !title.trim() ? "#C3CFDB" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
        >เปิดเคส</button>
      </div>

      <div style={css("display:grid;grid-template-columns:" + (chosen ? "1fr 1.2fr" : "1fr") + ";gap:14px;align-items:start")}>
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{["เลขที่", "หัวข้อ", "หมวด", "ขั้นตอน", "กำหนด"].map((h) => (
              <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
            ))}</tr></thead>
            <tbody>
              {cases.map((c) => (
                <tr key={c.id} onClick={() => setPicked(c.id === picked ? null : c.id)}
                  style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (c.id === picked ? "#F2F7FC" : c.overdue ? "#FEF6F5" : "#fff"))}>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;font-weight:600")}>{c.reference}</td>
                  <td style={css(CELL + ";color:#0A2240")}>{c.title}</td>
                  <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{CATEGORY_TH[c.category] ?? c.category}</td>
                  <td style={css(CELL + ";font-size:11.5px;color:" + (c.stage === "closed" ? "#16794C" : "#B45309"))}>{STAGE_TH[c.stage] ?? c.stage}</td>
                  <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;color:" + (c.overdue ? "#B42318" : "#7B8CA0"))}>{c.dueDate || "—"}</td>
                </tr>
              ))}
              {!cases.length && <tr><td colSpan={5} style={css("padding:28px;text-align:center;color:#94A3B8")}>ยังไม่มีเคส</td></tr>}
            </tbody>
          </table>
        </div>

        {chosen && (
          <Detail case_={chosen} busy={busy}
            onSave={(fields) => void post(`/${chosen.id}`, fields)}
            onAdvance={() => void post(`/${chosen.id}/advance`, {})}
            onUpload={(file, kind) => void upload(chosen.id, file, kind)}
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

function Detail({ case_, busy, onSave, onAdvance, onUpload, onClose }: {
  case_: Case; busy: boolean;
  onSave: (fields: Record<string, string>) => void;
  onAdvance: () => void;
  onUpload: (file: File, kind: string) => void;
  onClose: () => void;
}) {
  const [draft, setDraft] = useState<Record<string, string>>({});
  const [kind, setKind] = useState("photo");
  const position = STAGES.indexOf(case_.stage);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;position:sticky;top:12px;max-height:calc(100vh - 110px);overflow-y:auto")}>
      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;gap:10px")}>
        <div>
          <div style={css("font-size:13.5px;font-weight:650;color:#0A2240")}>{case_.reference} · {case_.title}</div>
          <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>
            เปิดโดย {case_.raisedBy} · {stamp(case_.raisedAt)}
            {case_.approvedBy && ` · ปิดโดย ${case_.approvedBy}`}
          </div>
        </div>
        <button onClick={onClose} style={css("border:none;background:none;font-size:17px;color:#94A3B8;cursor:pointer;line-height:1;padding:0")}>×</button>
      </div>

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
        {FIELDS.map(([key, label]) => (
          <label key={key} style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
            <input
              value={draft[key] ?? ""}
              placeholder={(case_ as unknown as Record<string, string>)[key] || "—"}
              onChange={(e) => setDraft({ ...draft, [key]: e.target.value })}
              disabled={case_.stage === "closed"}
              style={css("height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
          </label>
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

        {case_.evidence.map((file) => (
          <div key={file.id} style={css("display:flex;gap:9px;align-items:baseline;padding:5px 0;border-bottom:1px solid #F1F5F9")}>
            <a href={`/api/documents/${file.id}/content`} target="_blank" rel="noreferrer"
              style={css("font-size:12px;color:#0A5FA8;text-decoration:none;flex:1;word-break:break-all")}>
              {file.fileName}
            </a>
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
              <input type="file" disabled={busy} style={css("display:none")}
                onChange={(e) => {
                  const file = e.target.files?.[0];
                  e.target.value = "";
                  if (file) onUpload(file, kind);
                }} />
            </label>
            <span style={css("font-size:11px;color:#94A3B8")}>
              เก็บใน SCMOS/{case_.jobKey ? "ปี/ลูกค้า/งาน" : "ปี/CARPAR/เลขเคส"}/CARPAR
            </span>
          </div>
        )}
      </div>
    </div>
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
