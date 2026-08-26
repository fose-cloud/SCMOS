"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

/**
 * What each job still owes in paperwork.
 *
 * The workflow's document gate could always hold a job, but nobody could see
 * across the plan which jobs were short of what. This is that view.
 *
 * Nothing here judges a file's contents — the system cannot read a PDF and know
 * the B/L matches the booking. What it can say is which required folders are
 * empty, which container numbers will fail at the gate, and which files a person
 * has opened and could not read. All three are facts, and the screen does not
 * pretend to the fourth.
 */

type Doc = { id: number; fileName: string; note: string; uploadedBy: string };

type Item = {
  folder: string; english: string; thai: string; blocking: boolean; why: string;
  expectedNow: boolean; count: number; unclear: boolean; files: Doc[];
};

type Job = {
  jobKey: string; reference: string; customer: string; carrier: string; category: string;
  date: string; status: string; container: string; containerSuspect: boolean;
  missing: number; missingBlocking: number; unclearCount: number; checklist: Item[];
};

type Board = { total: number; clear: number; blocked: number; unclear: number; jobs: Job[] };

const SCOPES: [string, string][] = [
  ["outstanding", "ยังไม่ครบ"], ["blocked", "ติดที่เอกสารบังคับ"],
  ["unclear", "เอกสารอ่านไม่ชัด"], ["clear", "ครบแล้ว"],
];

export function Verification({ canUpload, onToast }: { canUpload: boolean; onToast: (m: string) => void }) {
  const [board, setBoard] = useRemembered<Board>("verification");
  const [scope, setScope] = useState("outstanding");
  const [open, setOpen] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async (which: string) => {
    const response = await apiFetch(`/api/verification?scope=${which}`, { headers: { accept: "application/json" } });
    return response.ok ? await response.json() as Board : null;
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const data = await load(scope);
      if (!cancelled) setBoard((held) => data ?? held);
    })();
    return () => { cancelled = true; };
  }, [load, scope, setBoard]);

  async function act(path: string, body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(path, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      setBoard(await load(scope));
    } finally { setBusy(false); }
  }

  async function upload(job: Job, folder: string, file: File) {
    if (busy) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append("jobKey", job.jobKey);
      body.append("folder", folder);
      body.append("file", file);
      const response = await apiFetch("/api/documents", { method: "POST", body });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "อัปโหลดไม่สำเร็จ");
      setBoard(await load(scope));
    } finally { setBusy(false); }
  }

  if (!board) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="งานทั้งหมด" value={board.total} colour="#0A2240" />
        <Tile label="เอกสารครบ" value={board.clear} colour="#16794C" />
        <Tile label="ติดเอกสารบังคับ" value={board.blocked} colour="#B42318" />
        <Tile label="มีไฟล์อ่านไม่ชัด" value={board.unclear} colour="#B45309" />
      </div>

      <div style={css("display:flex;gap:7px;flex-wrap:wrap")}>
        {SCOPES.map(([id, label]) => (
          <button key={id} onClick={() => { setScope(id); setOpen(null); }}
            style={css("height:30px;padding:0 13px;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer;border:1px solid " +
              (scope === id ? "#0A2240;background:#0A2240;color:#fff" : "#C9D6E2;background:#fff;color:#5A6B7D"))}>
            {label}
          </button>
        ))}
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        {board.jobs.length === 0 ? (
          <div style={css("padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ไม่มีงานในมุมมองนี้
          </div>
        ) : board.jobs.map((job) => (
          <div key={job.jobKey} style={css("border-bottom:1px solid #F1F5F9")}>
            <button onClick={() => setOpen(open === job.jobKey ? null : job.jobKey)}
              style={css("width:100%;text-align:left;font-family:inherit;background:" +
                (job.missingBlocking > 0 ? "#FEF6F5" : "#fff") +
                ";border:none;padding:10px 16px;cursor:pointer;display:flex;gap:12px;align-items:center;flex-wrap:wrap")}>
              <span style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0;min-width:96px")}>{job.date}</span>
              <span style={css("font-weight:600;color:#0A2240;font-size:12.5px;min-width:130px")}>{job.reference}</span>
              <span style={css("font-size:12px;color:#5A6B7D;flex:1;min-width:120px")}>{job.customer}</span>
              <span style={css("font-size:11.5px;color:#7B8CA0;min-width:110px")}>{job.carrier || "—"}</span>
              {job.containerSuspect && (
                <span style={css("font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:#B42318")}>
                  เลขตู้ผิดรูปแบบ
                </span>
              )}
              {job.unclearCount > 0 && (
                <span style={css("font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:#B45309")}>
                  อ่านไม่ชัด {job.unclearCount}
                </span>
              )}
              <span style={css("font-size:11.5px;color:" + (job.missing === 0 ? "#16794C" : job.missingBlocking > 0 ? "#B42318" : "#B45309"))}>
                {job.missing === 0 ? "ครบ" : `ขาด ${job.missing}`}
              </span>
            </button>

            {open === job.jobKey && (
              <div style={css("padding:2px 16px 14px 16px;background:#FBFCFD")}>
                {job.containerSuspect && (
                  <div style={css("font-size:11.5px;color:#B42318;padding:6px 0")}>
                    เลขตู้ “{job.container}” ไม่ตรงมาตรฐาน 4 ตัวอักษร + 7 ตัวเลข — ที่หน้าท่าจะไม่ตรงกับ E-Card
                  </div>
                )}
                {job.checklist.map((item) => (
                  <div key={item.folder} style={css("display:flex;gap:11px;align-items:flex-start;padding:7px 0;border-top:1px solid #F1F5F9")}>
                    <span style={css("font-size:15px;line-height:1.2;color:" +
                      (item.count > 0 ? (item.unclear ? "#B45309" : "#16794C")
                        : item.expectedNow ? (item.blocking ? "#B42318" : "#B45309") : "#C3CFDB"))}>
                      {item.count > 0 ? (item.unclear ? "!" : "✓") : item.expectedNow ? "✕" : "·"}
                    </span>
                    <div style={css("flex:1;min-width:0")}>
                      <div style={css("font-size:12.5px;color:#0A2240")}>
                        {item.thai}
                        {item.blocking && <span style={css("font-size:10px;color:#B42318;margin-left:6px")}>บังคับ</span>}
                        {!item.expectedNow && <span style={css("font-size:10.5px;color:#94A3B8;margin-left:6px")}>ยังไม่ถึงเวลา</span>}
                      </div>
                      {item.count === 0 && item.expectedNow && (
                        <div style={css("font-size:11px;color:#7B8CA0;margin-top:1px")}>{item.why}</div>
                      )}
                      {item.files.map((file) => (
                        <div key={file.id} style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:3px")}>
                          <a href={`/api/documents/${file.id}/content`} target="_blank" rel="noreferrer"
                            style={css("font-size:11.5px;color:#0A5FA8;text-decoration:none")}>{file.fileName}</a>
                          {file.note && <span style={css("font-size:11px;color:#B45309")}>{file.note}</span>}
                          {canUpload && (file.note
                            ? <Mini label="ได้ไฟล์ใหม่แล้ว" tone="#16794C" busy={busy}
                                onClick={() => void act(`/api/verification/documents/${file.id}/clear`, {})} />
                            : <Mini label="อ่านไม่ชัด" tone="#B45309" busy={busy} onClick={() => {
                                const detail = window.prompt(`${file.fileName}\n\nอ่านส่วนไหนไม่ออก`, "");
                                if (detail === null) return;
                                void act(`/api/verification/documents/${file.id}/unclear`, { detail });
                              }} />)}
                        </div>
                      ))}
                    </div>
                    {canUpload && (
                      <label style={css("height:26px;padding:0 10px;border:1px solid #C9D6E2;background:#fff;color:#5A6B7D;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;display:inline-flex;align-items:center;white-space:nowrap")}>
                        แนบ
                        <input type="file" disabled={busy} style={css("display:none")}
                          onChange={(e) => {
                            const file = e.target.files?.[0];
                            e.target.value = "";
                            if (file) void upload(job, item.folder, file);
                          }} />
                      </label>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        ))}
      </div>
    </div>
  );
}

function Mini({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={busy}
      style={css(`height:22px;padding:0 8px;border:1px solid ${tone};background:#fff;color:${tone};border-radius:3px;font-size:10.5px;font-weight:600;cursor:pointer`)}
    >{label}</button>
  );
}

function Tile({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
    </div>
  );
}
