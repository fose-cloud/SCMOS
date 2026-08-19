"use client";

import type { Job } from "./ops";
import { STATUS_TH, css } from "./theme";

/**
 * One job, one card — the phone view of the register.
 *
 * The grid carries twenty-odd columns because that is what keying a plan needs.
 * On a phone it is a wall to be dragged sideways, and the six things somebody
 * actually wants when they open the app on the road — whose customer, what
 * container, when, which carrier, which truck, where it has got to — are spread
 * across the full width with the rest in between.
 *
 * This shows those six and nothing else. It is deliberately read-only: editing
 * happens by tapping through to the job, where there is room to be careful. A
 * text field on a moving truck is how a plate ends up wrong.
 *
 * Rendered alongside the grid rather than instead of it, with the stylesheet
 * choosing which one is visible. Picking in JavaScript would mean asking how
 * wide the screen is, and that question has already been answered wrongly once
 * in this file's history — the layout must not depend on an event arriving.
 */

export function JobCards({ jobs, mine, onOpen }: {
  jobs: Job[];
  /** Whether this job belongs to the person looking at it. */
  mine: (job: Job) => boolean;
  onOpen: (key: string) => void;
}) {
  if (jobs.length === 0) {
    return (
      <div className="cards-only" style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:26px;text-align:center;color:#7B8CA0;font-size:12.5px")}>
        ไม่มีงานในช่วงที่เลือก
      </div>
    );
  }

  // No `display` in the inline style on purpose: the stylesheet owns whether
  // these are shown at all, and an inline `display:flex` outranks it — which is
  // exactly how the cards ended up drawn underneath the grid on a desktop.
  return (
    <div className="cards-only" style={css("flex-direction:column;gap:9px")}>
      {jobs.map((job) => {
        const own = mine(job);
        return (
          <button
            key={job.key}
            onClick={() => onOpen(job.key)}
            style={css("width:100%;text-align:left;font-family:inherit;cursor:pointer;" +
              "background:" + (own ? "#F4F8FC" : "#fff") + ";border:1px solid #E3E8EE;" +
              "border-left:3px solid " + (own ? "#2E7DD1" : "#E3E8EE") +
              ";border-radius:6px;padding:12px 14px")}
          >
            <div style={css("display:flex;justify-content:space-between;gap:10px;align-items:flex-start")}>
              <div style={css("min-width:0;flex:1")}>
                <div style={css("font-size:13.5px;font-weight:650;color:#0F2B46;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>
                  {job.customer || "—"}
                </div>
                <div style={css("font-size:11.5px;color:#7B8CA0;font-family:'IBM Plex Mono',monospace;margin-top:2px")}>
                  {job.jobCode || job.key}
                </div>
              </div>
              {own && (
                <span style={css("flex:none;font-size:9.5px;font-weight:700;letter-spacing:.05em;color:#1D4E80;background:#E7F0FA;border:1px solid #BBD5EE;border-radius:3px;padding:2px 6px")}>
                  งานของฉัน
                </span>
              )}
            </div>

            <div style={css("margin-top:9px;display:grid;grid-template-columns:1fr 1fr;gap:7px 12px")}>
              <Cell label="วันที่" value={job.date} />
              <Cell label="ตู้" value={job.container} mono />
              <Cell label="ผู้ขนส่ง" value={job.trucker} />
              <Cell label="ทะเบียน" value={job.licence} mono />
            </div>

            {/* The status carries the colour because it is the one field that
                says whether this job needs somebody today. */}
            <div style={css("margin-top:10px;display:flex;align-items:center;gap:8px;flex-wrap:wrap")}>
              <span style={css("font-size:11px;font-weight:600;letter-spacing:.03em;border-radius:3px;padding:3px 8px;" +
                tone(job.status))}>
                {STATUS_TH[job.status] ?? job.status ?? "—"}
              </span>
              {job.driver && (
                <span style={css("font-size:11.5px;color:#5A6B7D;overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>
                  {job.driver}
                </span>
              )}
            </div>
          </button>
        );
      })}
    </div>
  );
}

function Cell({ label, value, mono }: { label: string; value?: string; mono?: boolean }) {
  const shown = (value || "").trim();
  return (
    <div style={css("min-width:0")}>
      <div style={css("font-size:9.5px;letter-spacing:.05em;text-transform:uppercase;color:#9AAABB;font-weight:600")}>
        {label}
      </div>
      <div style={css("font-size:12.5px;color:" + (shown ? "#243B53" : "#C3CFDB") +
        ";overflow:hidden;text-overflow:ellipsis;white-space:nowrap" +
        (mono ? ";font-family:'IBM Plex Mono',monospace" : ""))}>
        {shown || "—"}
      </div>
    </div>
  );
}

/**
 * Colour by what the status means, not by matching every name in the ladder.
 * A status this does not recognise gets the neutral treatment rather than a
 * confident wrong colour.
 */
function tone(status: string): string {
  const s = (status || "").toUpperCase();
  if (/DELAY|HOLD|CANCEL|FAIL|REJECT/.test(s)) return "background:#FEF0EE;color:#B42318;border:1px solid #F3C9C4";
  if (/COMPLET|DELIVER|CLOSED|POD/.test(s)) return "background:#EDF7F1;color:#16794C;border:1px solid #BFE0CD";
  if (/WAIT|PENDING|DRAFT|RECEIV/.test(s)) return "background:#FFF8F0;color:#B45309;border:1px solid #F0D8B8";
  return "background:#F1F5F9;color:#475569;border:1px solid #E2E8F0";
}
