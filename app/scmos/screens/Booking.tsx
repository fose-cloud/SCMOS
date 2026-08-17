"use client";

import { useMemo, useState } from "react";
import {
  STAGES, bookingStats, byLoadingDate, candidatesFor, carrierOf, filled, missing,
  vehicleForType, type Stage,
} from "../booking";
import { requestSupplier } from "../flow";
import { WorkflowPanel } from "./WorkflowPanel";
import type { RateBook } from "../rates";
import type { Job } from "../ops";
import { css } from "../theme";

/**
 * Truck Booking.
 *
 * The queue is the plan itself, read for what each job is still missing, so it
 * cannot drift from the workspace: a plate keyed on the grid removes the job
 * from here without anything being told.
 *
 * The carrier list beside a job is the reason the rate cards were loaded. An
 * operator picking a carrier can see what each one quoted for that lane before
 * committing, which is the decision the screen exists to support.
 */

type Props = {
  jobs: Job[];
  book: RateBook | null;
  diesel: number;
  canEdit: (job: Job) => boolean;
  onAssign: (job: Job, patch: Partial<Job>) => void;
  onOpen: (job: Job) => void;
  onToast: (message: string) => void;
};

const PER_PAGE = 25;

export function Booking({ jobs, book, diesel, canEdit, onAssign, onOpen, onToast }: Props) {
  const [stage, setStage] = useState<Stage>("no-plate");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [picked, setPicked] = useState<string | null>(null);
  // Bumped whenever the workflow writes, so the carrier list and the process
  // panel are looking at the same job state.
  const [revision, setRevision] = useState(0);

  const stats = useMemo(() => bookingStats(jobs), [jobs]);

  const queue = useMemo(() => {
    const wanted = query.trim().toLowerCase();
    return stats[stage]
      .filter((job) => !wanted || [job.customer, job.trucker, job.jobCode, job.abs, job.container, job.destination]
        .some((field) => (field ?? "").toLowerCase().includes(wanted)))
      .slice()
      .sort(byLoadingDate);
  }, [stats, stage, query]);

  const job = useMemo(() => jobs.find((j) => j.key === picked) ?? null, [jobs, picked]);

  const pages = Math.max(1, Math.ceil(queue.length / PER_PAGE));
  const safePage = Math.min(page, pages);
  const slice = queue.slice((safePage - 1) * PER_PAGE, safePage * PER_PAGE);

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <Pipeline stats={stats} active={stage} onPick={(id) => { setStage(id); setPage(1); setPicked(null); }} />

      <div style={css("display:grid;grid-template-columns:" + (job ? "1.55fr 1fr" : "1fr") + ";gap:14px;align-items:start")}>
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
          <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;gap:10px;flex-wrap:wrap")}>
            <span style={css("font-size:12.5px;color:#465A6E")}>
              <b style={css("color:#0A2240")}>{queue.length.toLocaleString()}</b> งาน ·{" "}
              {STAGES.find((s) => s.id === stage)?.th}
            </span>
            <input
              value={query}
              onChange={(e) => { setQuery(e.target.value); setPage(1); }}
              placeholder="ค้นหา ลูกค้า / ผู้ขนส่ง / Job / ตู้"
              style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px;min-width:210px")}
            />
          </div>

          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead>
                <tr>
                  {["วันที่โหลด", "ลูกค้า", "ปลายทาง", "ประเภท", "ผู้ขนส่ง", "ยังขาด", ""].map((h) => (
                    <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {slice.map((row) => {
                  const gaps = missing(row);
                  const selected = row.key === picked;
                  return (
                    <tr
                      key={row.key}
                      onClick={() => setPicked(selected ? null : row.key)}
                      style={css("cursor:pointer;border-bottom:1px solid #F1F5F9;background:" + (selected ? "#F2F7FC" : "#fff"))}
                    >
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;white-space:nowrap")}>{row.date || "—"}</td>
                      <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{row.customer || "—"}</td>
                      <td style={css(CELL + ";color:#5A6B7D;max-width:220px")}>{row.destination || row.plant || "—"}</td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px")}>
                        {row.type || "—"}
                        {vehicleForType(row.type) && (
                          <span style={css("color:#94A3B8")}> → {vehicleForType(row.type)}</span>
                        )}
                      </td>
                      <td style={CELL_S}>{filled(row.trucker) ? row.trucker : <span style={css("color:#B42318")}>—</span>}</td>
                      <td style={CELL_S}>
                        {gaps.map((gap) => (
                          <span key={gap} style={css("display:inline-block;font-size:10.5px;font-weight:600;padding:2px 6px;border-radius:3px;background:#FDF2DF;color:#B45309;margin:0 4px 3px 0")}>{gap}</span>
                        ))}
                        {!gaps.length && <span style={css("color:#16794C;font-size:11.5px")}>ครบแล้ว</span>}
                      </td>
                      <td style={css(CELL + ";text-align:right;white-space:nowrap")}>
                        <button
                          onClick={(e) => { e.stopPropagation(); onOpen(row); }}
                          style={css("height:26px;padding:0 10px;border:1px solid #D8E0E8;border-radius:4px;background:#fff;font-size:11.5px;color:#0A2240;cursor:pointer")}
                        >เปิดงาน</button>
                      </td>
                    </tr>
                  );
                })}
                {!slice.length && (
                  <tr><td colSpan={7} style={css("padding:30px;text-align:center;color:#94A3B8;font-size:12.5px")}>
                    ไม่มีงานในขั้นตอนนี้
                  </td></tr>
                )}
              </tbody>
            </table>
          </div>

          {pages > 1 && (
            <div style={css("padding:10px 16px;border-top:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:center;background:#FBFCFD")}>
              <span style={css("font-size:12px;color:#7B8CA0")}>หน้า {safePage} / {pages}</span>
              <span style={css("display:flex;gap:6px")}>
                <Pager label="‹ ก่อนหน้า" disabled={safePage <= 1} onClick={() => setPage(safePage - 1)} />
                <Pager label="ถัดไป ›" disabled={safePage >= pages} onClick={() => setPage(safePage + 1)} />
              </span>
            </div>
          )}
        </div>

        {job && (
          <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;position:sticky;top:12px;max-height:calc(100vh - 110px);overflow-y:auto")}>
            <CarrierPanel
              job={job}
              book={book}
              diesel={diesel}
              canEdit={canEdit(job)}
              onAssign={onAssign}
              onClose={() => setPicked(null)}
              onToast={onToast}
            />
            <WorkflowPanel
              key={job.key + revision}
              jobKey={job.key}
              canEdit={canEdit(job)}
              onToast={onToast}
              onChanged={() => setRevision((n) => n + 1)}
            />
          </div>
        )}
      </div>
    </div>
  );
}

/* ------------------------------------------------------------------ pieces */

const CELL = "padding:8px 12px;vertical-align:top";
const CELL_S = css(CELL);

function Pager({ label, disabled, onClick }: { label: string; disabled: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={disabled} style={css(
      "height:28px;padding:0 12px;border:1px solid #D8E0E8;border-radius:4px;font-size:12px;background:#fff;color:" +
      (disabled ? "#C3CFDB" : "#0A2240") + ";cursor:" + (disabled ? "default" : "pointer"),
    )}>{label}</button>
  );
}

function Pipeline({ stats, active, onPick }: {
  stats: Record<Stage, Job[]>; active: Stage; onPick: (id: Stage) => void;
}) {
  return (
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(165px,1fr));gap:10px")}>
      {STAGES.map((entry) => {
        const on = entry.id === active;
        return (
          <button
            key={entry.id}
            onClick={() => onPick(entry.id)}
            // Each side is named rather than using the `border` shorthand with a
            // `border-top` after it: React warns about mixing the two when the
            // value changes on rerender, and this one changes on every click.
            style={css(
              "text-align:left;background:#fff;border-top:3px solid " + entry.tone +
              ";border-right:1px solid " + (on ? entry.tone : "#D8E0E8") +
              ";border-bottom:1px solid " + (on ? entry.tone : "#D8E0E8") +
              ";border-left:1px solid " + (on ? entry.tone : "#D8E0E8") +
              ";border-radius:4px;padding:11px 14px 13px;cursor:pointer;" +
              (on ? "box-shadow:0 0 0 2px " + entry.tone + "22;" : ""),
            )}
          >
            <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{entry.en}</div>
            <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${entry.tone}`)}>
              {stats[entry.id].length.toLocaleString()}
            </div>
            <div style={css("font-size:12px;color:#7B8CA0")}>{entry.th}</div>
          </button>
        );
      })}
    </div>
  );
}

/**
 * The carriers that could take this job.
 *
 * Priced suggestions first, cheapest at the top, each showing the lane it was
 * matched against. A carrier with a rate card but no lane for this journey is
 * still listed, unpriced — they are approved and can be asked, and hiding them
 * would make the list look like the whole market when it is not.
 */
function CarrierPanel({ job, book, diesel, canEdit, onAssign, onClose, onToast }: {
  job: Job; book: RateBook | null; diesel: number; canEdit: boolean;
  onAssign: (job: Job, patch: Partial<Job>) => void;
  onClose: () => void; onToast: (message: string) => void;
}) {
  /**
   * Asking a carrier goes through the workflow, never straight onto the job.
   *
   * The approved process is sequential — one carrier at a time, in order, and
   * a truck is only assigned to somebody who confirmed. Writing the carrier
   * onto the job from here, which is what this button used to do, produced
   * bookings nobody had agreed to take. The backend refuses that now, and this
   * asks it properly so the refusal never has to happen.
   */
  async function onRequest(target: Job, carrier: string, price: number | null) {
    const reply = await requestSupplier(target.key, carrier, price);
    onToast(reply.message);
  }

  const [licence, setLicence] = useState("");
  const [driver, setDriver] = useState("");
  const [contact, setContact] = useState("");

  const candidates = useMemo(() => candidatesFor(job, book, diesel), [job, book, diesel]);
  const priced = candidates.filter((c) => c.price !== null);
  const current = carrierOf(job);
  const vehicle = vehicleForType(job.type);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;position:sticky;top:12px;max-height:calc(100vh - 120px);overflow-y:auto")}>
      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5;display:flex;justify-content:space-between;align-items:flex-start;gap:10px")}>
        <div style={css("min-width:0")}>
          <div style={css("font-size:13.5px;font-weight:650;color:#0A2240")}>{job.customer || "—"}</div>
          <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>
            {job.jobCode || job.abs || job.jobNo || "—"} · {job.date || "ไม่มีวันที่"} {job.planTime}
          </div>
          <div style={css("font-size:11.5px;color:#5A6B7D;margin-top:3px")}>
            → {job.destination || job.plant || "—"} · <span style={css("font-family:ui-monospace,monospace")}>{job.type || "—"}</span>
            {vehicle && <span style={css("color:#94A3B8")}> ({vehicle})</span>}
          </div>
        </div>
        <button onClick={onClose} style={css("border:none;background:none;font-size:17px;color:#94A3B8;cursor:pointer;line-height:1;padding:0")}>×</button>
      </div>

      {!canEdit && (
        <div style={css("padding:10px 16px;background:#FDF2DF;border-bottom:1px solid #F3E2C4;font-size:12px;color:#B45309")}>
          งานนี้เป็นของ {job.op} — ดูได้อย่างเดียว
        </div>
      )}

      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:8px")}>
          ผู้ขนส่งที่เสนอราคาเส้นทางนี้ ({priced.length}) · ที่ดีเซล {diesel.toFixed(2)}
        </div>

        {!vehicle && (
          <div style={css("font-size:12px;color:#B45309;padding:8px 0")}>
            ประเภทรถ “{job.type || "ว่าง"}” ยังแมปเข้าตารางราคาไม่ได้ — เทียบราคาไม่ได้
          </div>
        )}

        {vehicle && !priced.length && (
          <div style={css("font-size:12px;color:#7B8CA0;padding:8px 0")}>
            ไม่มีใครเสนอราคาเส้นทางนี้ไว้ — ต้องขอราคาใหม่
          </div>
        )}

        <div style={css("display:flex;flex-direction:column;gap:7px")}>
          {priced.slice(0, 6).map((candidate, index) => {
            const isCurrent = candidate.carrier === current;
            return (
              <div key={candidate.carrier} style={css(
                "border:1px solid " + (isCurrent ? "#9FC6EC" : "#E9EFF5") + ";border-radius:4px;padding:9px 11px;background:" +
                (isCurrent ? "#F2F7FC" : "#FBFCFD"),
              )}>
                <div style={css("display:flex;align-items:center;gap:8px")}>
                  {index === 0 && <span style={css("font-size:10px;font-weight:700;color:#16794C")}>ถูกสุด</span>}
                  <span style={css("font-size:13px;font-weight:650;color:#0A2240;flex:1")}>{candidate.carrier}</span>
                  <span style={css("font-family:ui-monospace,monospace;font-size:13px;font-weight:600;color:" + (index === 0 ? "#16794C" : "#16232F"))}>
                    {candidate.price?.toLocaleString()}
                  </span>
                </div>
                {candidate.lane && (
                  <div style={css("font-size:11px;color:#94A3B8;margin-top:3px")}>
                    {candidate.lane.customer || "—"} · {candidate.lane.from || "—"} → {candidate.lane.to || "—"}
                  </div>
                )}
                {isCurrent && <div style={css("font-size:11px;color:#1D5FA8;margin-top:3px")}>ผู้ขนส่งปัจจุบันของงานนี้</div>}
                {canEdit && !isCurrent && (
                  <button
                    onClick={() => void onRequest(job, candidate.carrier, candidate.price)}
                    style={css("margin-top:7px;height:27px;padding:0 11px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer")}
                  >ขอกำลังรถจากเจ้านี้</button>
                )}
              </div>
            );
          })}
        </div>
      </div>

      {canEdit && (
        <div style={css("padding:13px 16px")}>
          <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:8px")}>
            บันทึกรถและคนขับ
          </div>
          <div style={css("display:flex;flex-direction:column;gap:8px")}>
            <Field label="ทะเบียนรถ" value={licence} placeholder={job.licence || "71-6904 ฉช"} onChange={setLicence} />
            <Field label="ชื่อคนขับ" value={driver} placeholder={job.driver || "นายสมชาย"} onChange={setDriver} />
            <Field label="เบอร์คนขับ" value={contact} placeholder={job.contact || "081-2345678"} onChange={setContact} />
            <button
              onClick={() => {
                const patch: Partial<Job> = {};
                if (licence.trim()) patch.licence = licence.trim();
                if (driver.trim()) patch.driver = driver.trim();
                if (contact.trim()) patch.contact = contact.trim();
                if (!Object.keys(patch).length) { onToast("ยังไม่ได้กรอกอะไร"); return; }
                onAssign(job, patch);
                setLicence(""); setDriver(""); setContact("");
                onToast("บันทึกรถและคนขับแล้ว");
              }}
              style={css("height:31px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer")}
            >บันทึก</button>
          </div>
        </div>
      )}
    </div>
  );
}

function Field({ label, value, placeholder, onChange }: {
  label: string; value: string; placeholder: string; onChange: (v: string) => void;
}) {
  return (
    <label style={css("display:flex;flex-direction:column;gap:3px")}>
      <span style={css("font-size:11px;color:#7B8CA0")}>{label}</span>
      <input
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 9px;font-size:12.5px")}
      />
    </label>
  );
}
