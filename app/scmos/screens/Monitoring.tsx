"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import {
  classifyDelay, delayReasons, readTrack, recordDelay, saveMilestone, updateDelay,
  type DelayReasonOption, type MilestoneView, type ShipmentTrack, type Suggestion,
} from "../flow";
import { stamp } from "./WorkflowPanel";
import { byLoadingDate, filled } from "../booking";
import type { Job } from "../ops";
import { css } from "../theme";
import { MonitorBoard } from "./MonitorBoard";

/**
 * Shipment monitoring.
 *
 * The run itself: dispatch through to close, one row per milestone with what was
 * planned against what happened. A milestone with nothing recorded is shown as
 * pending rather than left out — a journey with no pickup row is a journey where
 * nobody has said what happened at pickup, and that gap is the thing worth
 * seeing.
 */

type Props = {
  jobs: Job[];
  canEdit: (job: Job) => boolean;
  onToast: (message: string) => void;
  /** Whether the supervisor's three views are offered at all. */
  isSupervisor: boolean;
  /** Opens a job from a row on the board. */
  onOpenJob: (key: string) => void;
  /**
   * A shipment to open on arrival, from an alert or the header search.
   *
   * Null when somebody opened the menu entry themselves, which is the case
   * where they want the list and picking one for them would be wrong.
   */
  focus: string | null;
};

const STATUS_TONE: Record<string, string> = {
  pending: "#94A3B8", done: "#16794C", delayed: "#B42318", skipped: "#7B8CA0",
};
const STATUS_THAI: Record<string, string> = {
  pending: "ยังไม่บันทึก", done: "เสร็จ", delayed: "ล่าช้า", skipped: "ข้าม",
};

export function Monitoring({ jobs, canEdit, onToast, isSupervisor, onOpenJob, focus }: Props) {
  // The team's view or one journey's. An operator has no team view to switch
  // to, so they are not shown a switch — a control that only ever refuses is
  // worse than no control.
  const [view, setView] = useState<"board" | "journey">(isSupervisor ? "board" : "journey");

  // Somebody who arrived here from an alert about one shipment is asking about
  // that shipment, so the board steps aside for it. Read during render on the
  // value changing rather than in an effect, which would show the board for a
  // frame and then replace it.
  const [came, setCame] = useState(focus);
  if (came !== focus) {
    setCame(focus);
    if (focus) setView("journey");
  }

  if (isSupervisor && view === "board") {
    return (
      <div style={css("display:flex;flex-direction:column;gap:12px")}>
        <ViewSwitch view={view} onView={setView} />
        <MonitorBoard onOpenJob={onOpenJob} />
      </div>
    );
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      {isSupervisor && <ViewSwitch view={view} onView={setView} />}
      <Journey jobs={jobs} canEdit={canEdit} onToast={onToast} focus={focus} />
    </div>
  );
}

function ViewSwitch({ view, onView }: { view: string; onView: (v: "board" | "journey") => void }) {
  const pill = (on: boolean) =>
    "height:30px;padding:0 14px;border-radius:4px;font-size:12px;font-weight:600;font-family:inherit;"
    + "cursor:pointer;border:1px solid " + (on ? "#0A2240;background:#0A2240;color:#fff" : "#C9D6E2;background:#fff;color:#0A2240");
  return (
    <div style={css("display:flex;gap:8px;align-items:center")}>
      <button onClick={() => onView("board")} style={css(pill(view === "board"))}>ภาพรวมทีม</button>
      <button onClick={() => onView("journey")} style={css(pill(view === "journey"))}>ติดตามรายเที่ยว</button>
    </div>
  );
}

function Journey({ jobs, canEdit, onToast, focus }: {
  jobs: Job[]; canEdit: (job: Job) => boolean; onToast: (m: string) => void; focus: string | null;
}) {
  const [query, setQuery] = useState("");
  const [picked, setPicked] = useState<string | null>(focus);

  // The same arrival, one level down. Kept as its own state rather than used
  // directly so that clicking another row still works: the row the alert named
  // is where the person starts, not where they are held.
  const [came, setCame] = useState(focus);
  if (came !== focus) {
    setCame(focus);
    if (focus) setPicked(focus);
  }
  const [track, setTrack] = useState<ShipmentTrack | null>(null);
  const [reasons, setReasons] = useState<DelayReasonOption[]>([]);

  useEffect(() => { void delayReasons().then((list) => setReasons(list ?? [])); }, []);

  /** Shipments that have a carrier and are not finished — the ones being run. */
  const running = useMemo(() => {
    const wanted = query.trim().toLowerCase();
    return jobs
      .filter((job) => filled(job.trucker))
      .filter((job) => !wanted || [job.customer, job.trucker, job.jobCode, job.abs, job.container, job.licence]
        .some((field) => (field ?? "").toLowerCase().includes(wanted)))
      .slice()
      .sort(byLoadingDate)
      .slice(0, 300);
  }, [jobs, query]);

  const job = useMemo(() => jobs.find((j) => j.key === picked) ?? null, [jobs, picked]);

  const load = useCallback(async () => {
    setTrack(picked ? await readTrack(picked) : null);
  }, [picked]);

  useEffect(() => {
    // Guarded so a slow answer for a job the user has already moved off does not
    // land on top of the one they are looking at now.
    let cancelled = false;
    (async () => {
      const fresh = picked ? await readTrack(picked) : null;
      if (!cancelled) setTrack(fresh);
    })();
    return () => { cancelled = true; };
  }, [picked]);

  return (
    <div style={css("display:grid;grid-template-columns:" + (job ? "1fr 1.35fr" : "1fr") + ";gap:14px;align-items:start")}>
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
          <span style={css("font-size:12.5px;color:#465A6E")}>
            <b style={css("color:#0A2240")}>{running.length}</b> งานที่มีผู้ขนส่งแล้ว
          </span>
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="ค้นหา ลูกค้า / ผู้ขนส่ง / ตู้ / ทะเบียน"
            style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;min-width:220px")}
          />
        </div>
        <div style={css("max-height:calc(100vh - 220px);overflow-y:auto")}>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead>
              <tr>{["วันที่", "ลูกค้า", "ผู้ขนส่ง", "ทะเบียน"].map((h) => (
                <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr>
            </thead>
            <tbody>
              {running.map((row) => (
                <tr
                  key={row.key}
                  onClick={() => setPicked(row.key === picked ? null : row.key)}
                  style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (row.key === picked ? "#F2F7FC" : "#fff"))}
                >
                  <td style={css("padding:7px 12px;font-family:ui-monospace,monospace;white-space:nowrap")}>{row.date || "—"}</td>
                  <td style={css("padding:7px 12px;font-weight:600;color:#0A2240")}>{row.customer || "—"}</td>
                  <td style={css("padding:7px 12px;color:#5A6B7D")}>{row.trucker}</td>
                  <td style={css("padding:7px 12px;font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0")}>{row.licence || "—"}</td>
                </tr>
              ))}
              {!running.length && (
                <tr><td colSpan={4} style={css("padding:28px;text-align:center;color:#94A3B8")}>ไม่พบงาน</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>

      {job && track && (
        <Track
          job={job} track={track} reasons={reasons} canEdit={canEdit(job)}
          onToast={onToast} onReload={load} onClose={() => setPicked(null)}
        />
      )}
    </div>
  );
}

/* ------------------------------------------------------------------- track */

function Track({ job, track, reasons, canEdit, onToast, onReload, onClose }: {
  job: Job; track: ShipmentTrack; reasons: DelayReasonOption[]; canEdit: boolean;
  onToast: (m: string) => void; onReload: () => Promise<void>; onClose: () => void;
}) {
  const [openStage, setOpenStage] = useState<string | null>(null);
  const [delayOpen, setDelayOpen] = useState(false);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;position:sticky;top:12px;max-height:calc(100vh - 110px);overflow-y:auto")}>
      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:flex-start;gap:10px")}>
        <div>
          <div style={css("font-size:13.5px;font-weight:650;color:#0A2240")}>{track.customer || "—"}</div>
          <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>
            {track.reference || "—"} · {job.trucker} · {job.licence || "ยังไม่มีทะเบียน"}
          </div>
        </div>
        <button onClick={onClose} style={css("border:none;background:none;font-size:17px;color:#94A3B8;cursor:pointer;line-height:1;padding:0")}>×</button>
      </div>

      {track.milestones.map((milestone) => (
        <Milestone
          key={milestone.stage}
          jobKey={track.jobKey}
          milestone={milestone}
          canEdit={canEdit}
          open={openStage === milestone.stage}
          onOpen={() => setOpenStage(openStage === milestone.stage ? null : milestone.stage)}
          onToast={onToast}
          onSaved={async () => { setOpenStage(null); await onReload(); }}
        />
      ))}

      <div style={css("border-top:1px solid #E9EFF5")}>
        <button
          onClick={() => setDelayOpen((v) => !v)}
          style={css("width:100%;padding:11px 16px;background:#FBFCFD;border:none;text-align:left;font-size:12.5px;font-weight:650;color:#B45309;cursor:pointer")}
        >
          {delayOpen ? "▾" : "▸"} ความล่าช้า ({track.delays.length})
        </button>
        {delayOpen && (
          <div style={css("padding:0 16px 14px")}>
            {track.delays.map((delay) => (
              <div key={delay.id} style={css("border:1px solid #E9EFF5;border-radius:4px;padding:9px 11px;margin-bottom:7px;background:#FBFCFD")}>
                <div style={css("display:flex;align-items:center;gap:7px;flex-wrap:wrap")}>
                  <span style={css("font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;background:#FDF2DF;color:#B45309")}>{delay.categoryThai}</span>
                  <span style={css("font-size:12px;color:#5A6B7D;flex:1")}>{delay.responsibleThai}</span>
                  {delay.againstCarrier && (
                    <span style={css("font-size:10.5px;font-weight:700;color:#B42318")}>นับกับผู้ขนส่ง</span>
                  )}
                </div>
                {delay.detail && <div style={css("font-size:12px;color:#16232F;margin-top:4px")}>{delay.detail}</div>}
                <div style={css("font-size:11px;color:#94A3B8;margin-top:3px")}>
                  {stamp(delay.detectedAt)}
                  {delay.impactMinutes !== null && ` · กระทบ ${delay.impactMinutes} นาที`}
                  {" · จัดหมวดโดย "}{delay.classifiedBy === "rule" ? "ระบบ" : delay.classifiedBy === "ai" ? "AI" : "คน"}
                  {delay.classifierBasis && ` (${delay.classifierBasis})`}
                </div>
                {delay.notifiedTeam && (
                  <div style={css("font-size:11.5px;color:#1D5FA8;margin-top:3px")}>แจ้ง: {delay.notifiedTeam}</div>
                )}
                {delay.recoveryAction && (
                  <div style={css("font-size:11.5px;color:#16794C;margin-top:2px")}>แก้ไข: {delay.recoveryAction}</div>
                )}
                {canEdit && !delay.resolvedAt && (
                  <Recovery id={delay.id} onToast={onToast} onSaved={onReload} />
                )}
                {delay.resolvedAt && (
                  <div style={css("font-size:11px;color:#16794C;margin-top:3px")}>ปิดเรื่องแล้ว {stamp(delay.resolvedAt)}</div>
                )}
              </div>
            ))}
            {canEdit && <NewDelay jobKey={track.jobKey} reasons={reasons} onToast={onToast} onSaved={onReload} />}
          </div>
        )}
      </div>
    </div>
  );
}

function Milestone({ jobKey, milestone, canEdit, open, onOpen, onToast, onSaved }: {
  jobKey: string; milestone: MilestoneView; canEdit: boolean; open: boolean;
  onOpen: () => void; onToast: (m: string) => void; onSaved: () => Promise<void>;
}) {
  const [status, setStatus] = useState(milestone.status === "pending" ? "done" : milestone.status);
  const [truckNo, setTruckNo] = useState("");
  const [driver, setDriver] = useState("");
  const [remark, setRemark] = useState("");
  const [delayReason, setDelayReason] = useState("");
  const [busy, setBusy] = useState(false);

  const tone = STATUS_TONE[milestone.status] ?? "#94A3B8";

  async function save() {
    if (busy) return;
    setBusy(true);
    try {
      const reply = await saveMilestone(jobKey, { stage: milestone.stage, status, truckNo, driver, remark, delayReason });
      onToast(reply.message);
      if (reply.ok) await onSaved();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={css("border-bottom:1px solid #F1F5F9")}>
      <button
        onClick={onOpen}
        style={css("width:100%;padding:10px 16px;background:#fff;border:none;text-align:left;cursor:pointer;display:flex;align-items:center;gap:10px")}
      >
        <span style={css(`width:8px;height:8px;border-radius:50%;background:${tone};flex:none`)} />
        <span style={css("font-size:12.5px;font-weight:600;color:#0A2240;flex:1")}>{milestone.thai}</span>
        <span style={css(`font-size:11px;color:${tone};font-weight:600`)}>{STATUS_THAI[milestone.status] ?? milestone.status}</span>
        <span style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:#7B8CA0;min-width:74px;text-align:right")}>
          {milestone.actualAt ? stamp(milestone.actualAt).slice(-5) : (milestone.plannedAt || "—")}
        </span>
      </button>

      {(milestone.remark || milestone.delayReason) && !open && (
        <div style={css("padding:0 16px 8px 34px;font-size:11.5px;color:#7B8CA0")}>
          {milestone.delayReason && <span style={css("color:#B42318")}>{milestone.delayReason} · </span>}
          {milestone.remark}
        </div>
      )}

      {open && (
        <div style={css("padding:4px 16px 13px 34px")}>
          <div style={css("font-size:11px;color:#94A3B8;margin-bottom:8px")}>
            แผน {milestone.plannedAt || "ไม่ระบุ"}
            {milestone.updatedBy && ` · แก้ล่าสุดโดย ${milestone.updatedBy} ${milestone.updatedAt ? stamp(milestone.updatedAt) : ""}`}
          </div>

          {!canEdit && <div style={css("font-size:12px;color:#B45309")}>ดูได้อย่างเดียว</div>}

          {canEdit && (
            <>
              <div style={css("display:flex;gap:5px;margin-bottom:7px;flex-wrap:wrap")}>
                {["done", "delayed", "skipped"].map((option) => (
                  <button
                    key={option}
                    onClick={() => setStatus(option)}
                    style={css(
                      "height:26px;padding:0 10px;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer;border:1px solid " +
                      (status === option ? STATUS_TONE[option] : "#C9D6E2") +
                      ";background:" + (status === option ? STATUS_TONE[option] : "#fff") +
                      ";color:" + (status === option ? "#fff" : "#5A6B7D"),
                    )}
                  >{STATUS_THAI[option]}</button>
                ))}
              </div>

              {status === "delayed" && (
                <input
                  value={delayReason}
                  onChange={(e) => setDelayReason(e.target.value)}
                  placeholder="สาเหตุความล่าช้า (จำเป็น)"
                  style={css("width:100%;height:28px;border:1px solid #F0C2BC;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:6px")}
                />
              )}

              <div style={css("display:flex;gap:6px;margin-bottom:6px")}>
                <input value={truckNo} onChange={(e) => setTruckNo(e.target.value)}
                  placeholder={milestone.truckNo || "ทะเบียนรถ"}
                  style={css("flex:1;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
                <input value={driver} onChange={(e) => setDriver(e.target.value)}
                  placeholder={milestone.driver || "คนขับ"}
                  style={css("flex:1;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
              </div>

              <input value={remark} onChange={(e) => setRemark(e.target.value)}
                placeholder={milestone.remark || "หมายเหตุ"}
                style={css("width:100%;height:28px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:7px")} />

              <button onClick={() => void save()} disabled={busy}
                style={css("height:29px;padding:0 13px;border:1px solid #0A2240;background:" + (busy ? "#C3CFDB" : "#0A2240") + ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
              >บันทึก</button>
            </>
          )}
        </div>
      )}
    </div>
  );
}

/** Records a delay, showing what the classifier makes of the text as it is typed. */
function NewDelay({ jobKey, reasons, onToast, onSaved }: {
  jobKey: string; reasons: DelayReasonOption[]; onToast: (m: string) => void; onSaved: () => Promise<void>;
}) {
  const [detail, setDetail] = useState("");
  const [category, setCategory] = useState("");
  const [impact, setImpact] = useState("");
  const [suggestion, setSuggestion] = useState<Suggestion | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    // Debounced: the operator is still typing, and asking the classifier on
    // every keystroke would show it changing its mind mid-sentence.
    let cancelled = false;
    const text = detail.trim();
    const timer = setTimeout(() => {
      (async () => {
        const answer = text.length < 4 ? null : await classifyDelay(text);
        if (!cancelled) setSuggestion(answer);
      })();
    }, 350);
    return () => { cancelled = true; clearTimeout(timer); };
  }, [detail]);

  async function save() {
    if (busy) return;
    setBusy(true);
    try {
      const reply = await recordDelay(jobKey, {
        stage: "", detail, category: category || null,
        impactMinutes: impact.trim() ? Number(impact) : null,
      });
      onToast(reply.message);
      if (reply.ok) { setDetail(""); setCategory(""); setImpact(""); setSuggestion(null); await onSaved(); }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={css("border:1px dashed #D8E0E8;border-radius:4px;padding:10px 11px;margin-top:8px")}>
      <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:7px")}>
        บันทึกความล่าช้าใหม่
      </div>
      <input value={detail} onChange={(e) => setDetail(e.target.value)}
        placeholder="เกิดอะไรขึ้น — พิมพ์เป็นภาษาพูดได้"
        style={css("width:100%;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px;margin-bottom:6px")} />

      {suggestion && !category && (
        <div style={css("font-size:11.5px;color:#1D5FA8;margin-bottom:6px")}>
          ระบบเสนอ: <b>{suggestion.categoryThai}</b> · {suggestion.responsibleThai}
          <span style={css("color:#94A3B8")}> ({suggestion.basis})</span>
        </div>
      )}

      <div style={css("display:flex;gap:6px;margin-bottom:7px")}>
        <select value={category} onChange={(e) => setCategory(e.target.value)}
          style={css("flex:1;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px;background:#fff")}>
          <option value="">ใช้หมวดที่ระบบเสนอ</option>
          {reasons.map((reason) => (
            <option key={reason.id} value={reason.id}>{reason.thai} · {reason.responsibleThai}</option>
          ))}
        </select>
        <input value={impact} onChange={(e) => setImpact(e.target.value.replace(/\D/g, ""))}
          placeholder="กระทบ (นาที)" inputMode="numeric"
          style={css("width:110px;height:29px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12px")} />
      </div>

      <button onClick={() => void save()} disabled={busy || !detail.trim()}
        style={css("height:29px;padding:0 13px;border:1px solid #B45309;background:" + (busy || !detail.trim() ? "#C3CFDB" : "#B45309") + ";color:#fff;border-radius:4px;font-size:12px;font-weight:600;cursor:pointer")}
      >บันทึกความล่าช้า</button>
    </div>
  );
}

function Recovery({ id, onToast, onSaved }: { id: number; onToast: (m: string) => void; onSaved: () => Promise<void> }) {
  const [team, setTeam] = useState("");
  const [action, setAction] = useState("");
  const [busy, setBusy] = useState(false);

  async function save(resolved: boolean) {
    if (busy) return;
    setBusy(true);
    try {
      const reply = await updateDelay({ id, notifiedTeam: team, recoveryAction: action, resolved });
      onToast(reply.message);
      if (reply.ok) { setTeam(""); setAction(""); await onSaved(); }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div style={css("margin-top:7px;padding-top:7px;border-top:1px dashed #E9EFF5;display:flex;flex-direction:column;gap:5px")}>
      <input value={team} onChange={(e) => setTeam(e.target.value)} placeholder="แจ้งทีมไหน"
        style={css("height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px")} />
      <input value={action} onChange={(e) => setAction(e.target.value)} placeholder="แก้ไขอย่างไร"
        style={css("height:27px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12px")} />
      <div style={css("display:flex;gap:6px")}>
        <button onClick={() => void save(false)} disabled={busy}
          style={css("height:26px;padding:0 10px;border:1px solid #1D5FA8;background:#fff;color:#1D5FA8;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}
        >บันทึก</button>
        <button onClick={() => void save(true)} disabled={busy}
          style={css("height:26px;padding:0 10px;border:1px solid #16794C;background:#16794C;color:#fff;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}
        >บันทึกและปิดเรื่อง</button>
      </div>
    </div>
  );
}
