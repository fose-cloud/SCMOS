"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { isCancelled, prep, wasMoved, type Job } from "../ops";
import { loadChangedJobs } from "../store";
import { badge, css } from "../theme";

/**
 * Jobs whose plan changed: moved to another day, or called off.
 *
 * These used to be a tab on the workspace, next to the ones that mean "still to
 * do". That put two different questions on one strip. Everything else on that
 * strip is work waiting for somebody — today's runs, the ones missing a
 * container, the delayed ones — and this is the opposite: work that is not
 * happening as booked, and which somebody has to explain to a customer rather
 * than dispatch.
 *
 * The rows come from an endpoint of its own, which filters them in SQL. The
 * first version asked the workspace's paging endpoint for the CANCEL / MOVED
 * tab, and that endpoint reads the whole register and counts all nine tabs
 * before answering — right for a grid that draws one tab and shows the numbers
 * on the others, wrong for a screen that wants a handful of rows and none of
 * the counts. It took over a minute in front of a real user.
 */

type Filter = "ALL" | "MOVED" | "CANCELLED";

export function Postpone({ me, onOpenJob }: {
  me: { opId: string; name: string };
  onOpenJob: (key: string) => void;
}) {
  const [jobs, setJobs] = useState<Job[] | null>(null);
  const [failure, setFailure] = useState("");
  const [filter, setFilter] = useState<Filter>("ALL");
  const [mineOnly, setMineOnly] = useState(false);

  const load = useCallback(async () => {
    setJobs(null);
    setFailure("");
    const rows = await loadChangedJobs();
    if (rows === null) {
      setFailure("อ่านข้อมูลจาก API ไม่ได้ — ลองใหม่อีกครั้ง");
      return;
    }
    // Through `prep` because the grid draws fields it derives — the priority,
    // the validation marks — and stored rows carry none of them.
    setJobs(prep({ jobs: rows }).jobs);
  }, []);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const shown = useMemo(() => {
    const all = jobs ?? [];
    return all.filter((job) => {
      if (mineOnly && job.opId !== me.opId) return false;
      if (filter === "MOVED") return wasMoved(job) && !isCancelled(job);
      if (filter === "CANCELLED") return isCancelled(job);
      return true;
    });
  }, [jobs, filter, mineOnly, me.opId]);

  const moved = (jobs ?? []).filter((job) => wasMoved(job) && !isCancelled(job));
  const cancelled = (jobs ?? []).filter(isCancelled);

  /** Who has been asking for the dates to move. The point of recording it. */
  const askedBy = useMemo(() => {
    const tally: Record<string, number> = {};
    moved.forEach((job) => {
      const who = (job.moveBy || "").trim() || "ไม่ระบุ";
      tally[who] = (tally[who] ?? 0) + 1;
    });
    return Object.entries(tally).sort((a, b) => b[1] - a[1]);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [jobs]);

  if (failure) {
    return (
      <div style={css("border:1px solid #F3C3BE;background:#FDF6F5;border-radius:5px;padding:14px 16px;display:flex;flex-direction:column;gap:9px")}>
        <span style={css("font-size:12.5px;color:#B42318")}>{failure}</span>
        <button onClick={() => { setFailure(""); void load(); }}
          style={css("align-self:flex-start;height:30px;padding:0 14px;border:1px solid #0A2240;background:#0A2240;color:#fff;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
          ลองใหม่
        </button>
      </div>
    );
  }

  if (!jobs) {
    return <Waiting onRetry={() => { void load(); }} />;
  }

  const chip = (key: Filter, label: string, count: number, colour: string) => {
    const on = filter === key;
    return (
      <button key={key} onClick={() => setFilter(key)}
        style={css("display:flex;align-items:center;gap:8px;height:34px;padding:0 14px;border:1px solid " +
          (on ? colour : "#E2E8F0") + ";background:" + (on ? colour : "#fff") +
          ";color:" + (on ? "#fff" : "#475569") +
          ";border-radius:4px;font-size:12.5px;cursor:pointer;font-family:inherit;font-weight:" +
          (on ? "600" : "400"))}>
        {label}
        <span style={css("font-family:'IBM Plex Mono',monospace;font-size:12px;opacity:.85")}>{count}</span>
      </button>
    );
  };

  return (
    <div style={css("display:flex;flex-direction:column;gap:13px")}>
      {/* ------------------------------------------------------- the numbers */}
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:10px")}>
        {([
          ["เลื่อนวัน", moved.length, "#B45309", "งานที่ถูกย้ายวันอย่างน้อยหนึ่งครั้ง"],
          ["ยกเลิก", cancelled.length, "#B42318", "งานที่ไม่เกิดขึ้น — ยังอยู่ในระบบ ไม่ถูกลบ"],
          ["รวม", jobs.length, "#0A2240", "ทั้งหมดที่แผนเปลี่ยนไปจากที่จองไว้"],
        ] as [string, number, string, string][]).map(([label, value, colour, note]) => (
          <div key={label} style={css("background:#fff;border:1px solid #E9EFF5;border-radius:5px;padding:12px 14px")}>
            <div style={css("font-size:11px;color:#64748B")}>{label}</div>
            <div style={css("font-size:26px;font-weight:600;font-family:'IBM Plex Mono',monospace;color:" + colour)}>{value}</div>
            <div style={css("font-size:10.5px;color:#94A3B8;line-height:1.45")}>{note}</div>
          </div>
        ))}
      </div>

      {askedBy.length > 0 && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 15px")}>
          <div style={css("font-size:11.5px;color:#64748B;margin-bottom:7px")}>
            ใครเป็นคนขอเลื่อน — ตัวเลขที่ฟิลด์นี้มีไว้ตอบ
          </div>
          <div style={css("display:flex;gap:8px;flex-wrap:wrap")}>
            {askedBy.map(([who, count]) => (
              <span key={who} style={css("display:flex;align-items:center;gap:7px;border:1px solid #F5E3C7;background:#FFFAEF;border-radius:4px;padding:5px 11px;font-size:12px;color:#B45309")}>
                {who}
                <b style={css("font-family:'IBM Plex Mono',monospace")}>{count}</b>
              </span>
            ))}
          </div>
        </div>
      )}

      {/* ------------------------------------------------------- the filters */}
      <div style={css("display:flex;gap:8px;flex-wrap:wrap;align-items:center")}>
        {chip("ALL", "ทั้งหมด", jobs.length, "#0A2240")}
        {chip("MOVED", "เลื่อนวัน", moved.length, "#B45309")}
        {chip("CANCELLED", "ยกเลิก", cancelled.length, "#B42318")}
        <label style={css("display:flex;align-items:center;gap:6px;font-size:12px;color:#0F2B46;cursor:pointer;margin-left:6px")}>
          <input type="checkbox" checked={mineOnly} onChange={(e) => setMineOnly(e.target.checked)} />
          เฉพาะงานของฉัน
        </label>
        <button onClick={() => void load()}
          style={css("margin-left:auto;height:32px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
          รีเฟรช
        </button>
      </div>

      {/* --------------------------------------------------------- the table */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        {shown.length === 0 ? (
          <div style={css("padding:30px 16px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            {jobs.length === 0
              ? "ยังไม่มีงานที่ถูกเลื่อนหรือยกเลิก — เปิดงานใน My Job แล้วกด “เลื่อนวัน” หรือ “ยกเลิกงาน”"
              : "ไม่มีงานที่ตรงกับตัวกรองนี้"}
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12px")}>
              <thead>
                <tr>
                  {["", "งาน / ABS", "ลูกค้า", "ผู้ขนส่ง", "วันเดิม", "วันปัจจุบัน", "ผู้ขอเลื่อน", "เหตุผล", "สถานะ", "เจ้าของงาน"].map((h, i) => (
                    <th key={i} style={css("background:#F4F7FA;padding:9px 12px;text-align:left;font-size:10.5px;color:#465A6E;border-bottom:1px solid #D8E0E8;white-space:nowrap")}>{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {shown.map((job) => {
                  const off = isCancelled(job);
                  return (
                    <tr key={job.key} className="row-hover"
                      onClick={() => onOpenJob(job.key)}
                      title={off ? "ยกเลิกแล้ว" : "เลื่อนจาก " + job.origDate}
                      style={css("cursor:pointer;background:" + (off ? "#F8FAFC" : "#FFFCF4") +
                        ";border-left:3px solid " + (off ? "#B42318" : "#D89614") +
                        (off ? ";opacity:.7" : ""))}>
                      <td style={css(CELL + ";white-space:nowrap")}>
                        <span style={css(badge(off ? "ยกเลิก" : "เลื่อน", off ? "red" : "amber"))}>
                          {off ? "ยกเลิก" : "เลื่อน"}
                        </span>
                      </td>
                      <td style={css(CELL + ";font-family:'IBM Plex Mono',monospace;white-space:nowrap" + (off ? ";text-decoration:line-through" : ""))}>
                        {job.jobCode || job.abs || job.jobNo || job.key}
                      </td>
                      <td style={css(CELL + (off ? ";text-decoration:line-through" : ""))}>{job.customer || "—"}</td>
                      <td style={css(CELL)}>{job.trucker || "—"}</td>
                      <td style={css(CELL + ";font-family:'IBM Plex Mono',monospace;white-space:nowrap;color:#B45309")}>
                        {job.origDate || "—"}
                      </td>
                      <td style={css(CELL + ";font-family:'IBM Plex Mono',monospace;white-space:nowrap;font-weight:600")}>
                        {job.date || "—"}
                      </td>
                      <td style={css(CELL + ";white-space:nowrap")}>{job.moveBy || "—"}</td>
                      <td style={css(CELL + ";max-width:280px")}>
                        {off ? (job.cancelReason || "—") : (job.moveReason || "—")}
                      </td>
                      <td style={css(CELL + ";white-space:nowrap")}>{job.status}</td>
                      <td style={css(CELL + ";white-space:nowrap")}>{job.op || "—"}</td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div style={css("font-size:11px;color:#94A3B8;line-height:1.6")}>
        คลิกแถวเพื่อเปิดรายละเอียดงาน · งานที่ยกเลิกจะไม่ปรากฏใน PENDING · TODAY · TOMORROW อีก
        แต่ไม่ถูกลบออกจากระบบ · งานที่เลื่อนยังเป็นงานที่ต้องทำ จึงยังอยู่ในรายการตามวันใหม่
      </div>
    </div>
  );
}

const CELL = "padding:9px 12px;border-bottom:1px solid #F1F5F9;vertical-align:middle";

/**
 * The wait, with something to do in it.
 *
 * A placeholder that only ever says "loading" is indistinguishable from one
 * that will never finish, and this screen sat on exactly that for over a
 * minute. After ten seconds it says the database may be starting up — the one
 * cause that genuinely takes minutes here — and offers the retry that a person
 * would otherwise get by reloading the whole page.
 */
function Waiting({ onRetry }: { onRetry: () => void }) {
  const [seconds, setSeconds] = useState(0);

  useEffect(() => {
    const timer = setInterval(() => setSeconds((value) => value + 1), 1000);
    return () => clearInterval(timer);
  }, []);

  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:30px;text-align:center;display:flex;flex-direction:column;align-items:center;gap:9px")}>
      <span style={css("font-size:12.5px;color:#94A3B8")}>
        กำลังโหลดงานที่เลื่อนและยกเลิก… {seconds > 3 ? `(${seconds} วินาที)` : ""}
      </span>
      {seconds >= 10 && (
        <>
          <span style={css("font-size:11.5px;color:#B45309;max-width:420px;line-height:1.6")}>
            นานกว่าปกติ — ฐานข้อมูลอาจกำลังเริ่มทำงานหลังพักตัว ซึ่งใช้เวลาราวสองนาที ไม่ต้องรีเฟรชหน้า
          </span>
          <button onClick={onRetry}
            style={css("height:30px;padding:0 14px;border:1px solid #D8E0E8;background:#fff;color:#475569;border-radius:4px;font-size:12px;cursor:pointer;font-family:inherit")}>
            ลองใหม่
          </button>
        </>
      )}
    </div>
  );
}
