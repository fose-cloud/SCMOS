"use client";

import { useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

/**
 * The supervisor's four questions.
 *
 * The shipment view next door answers "where is this journey", which is the
 * question of whoever is carrying it. These are the others: what has already
 * gone wrong, what is about to go wrong today, who is carrying too much, and
 * where the month's time went.
 *
 * The first is what a supervisor opens this screen to find out, so it is the
 * one on screen when they arrive. It is not the same list as the second: the
 * risk list is about pieces missing before a shipment goes and drops a job the
 * moment its lorry turns up, which is exactly when a four-hour delay becomes
 * visible.
 *
 * Everything shown is counted by the API over the whole register. Counting in
 * the browser would report one page of a team's work as the team's work, which
 * is the kind of wrong that looks right.
 */

type RiskRow = {
  key: string; why: string; daysAway: number; cat: string; date: string;
  customer: string; trucker: string; owner: string; status: string; jobCode: string;
};
type LoadRow = {
  ownerId: string; owner: string; carrying: number; flagged: number;
  oldestDaysWaiting: number; coveredBy: string;
};
type BlameRow = { party: string; thai: string; cases: number; minutes: number; unmeasured: number };
type ProblemRow = {
  key: string; problems: string[]; problemsThai: string[];
  minutesLate: number; measurable: boolean; note: string; noteFrom: string;
  date: string; customer: string; trucker: string; owner: string; status: string; jobCode: string;
  planned: string; arrived: string;
};
type Tally = {
  live: number; withProblem: number; unmeasurable: number; arrivedLate: number; lateMinutes: number;
};
type Board = {
  risks: RiskRow[]; loads: LoadRow[]; blames: BlameRow[]; today: string; live: number;
  problems: ProblemRow[]; tally: Tally;
};

/** What went wrong, and how loudly to say it. Ordered as the API sends them. */
const PROBLEM: Record<string, string> = {
  Incident: "#B42318",
  DelayOpen: "#B42318",
  StageDelayed: "#B45309",
  ArrivedLate: "#B45309",
  // The weakest of the five: the Reason column collects progress notes as well
  // as delays. Muted so a screen of them does not read as a screen of trouble.
  DelayNoted: "#8A6D1F",
};

/**
 * The order the API sorts by, so the filter buttons read in the same direction
 * the rows do.
 *
 * A copy of an order that lives in ProblemRules, which is a thing this codebase
 * has been bitten by before — so it is only ever a sort key for buttons. Every
 * label, count and row order comes from the API; a name added there and missing
 * here sorts to the front rather than disappearing.
 */
const ORDER = ["Incident", "DelayOpen", "StageDelayed", "ArrivedLate", "DelayNoted"];

/** What put a job on the list, and how loudly to say it. */
const RISK: Record<string, { th: string; tone: string }> = {
  Overdue: { th: "เลยกำหนดแล้ว", tone: "#B42318" },
  Unassigned: { th: "ไม่มีเจ้าของงาน", tone: "#B45309" },
  NoCarrier: { th: "ยังไม่มีผู้ขนส่ง", tone: "#B45309" },
  NoTruck: { th: "ยังไม่มีรถ/คนขับ", tone: "#8A6D1F" },
};

const CELL = "padding:8px 12px;border-bottom:1px solid #EDF1F5;font-size:12.5px;white-space:nowrap";
const HEAD = "padding:8px 12px;text-align:left;font-size:10.5px;font-weight:700;color:#465A6E;"
  + "letter-spacing:.05em;text-transform:uppercase;background:#F4F7FA;border-bottom:1px solid #D8E0E8";

export function MonitorBoard({ onOpenJob }: { onOpenJob: (key: string) => void }) {
  const [board, setBoard] = useRemembered<Board>("monitorBoard");
  // Problems first, because it is the question the screen was opened to answer.
  const [tab, setTab] = useState<"problem" | "risk" | "load" | "delay">("problem");
  const [busy, setBusy] = useState(false);
  const [denied, setDenied] = useState(false);
  // Bumped by the refresh button; the effect above watches it.
  const [reload, setReload] = useState(0);

  // Inline and guarded by `alive`, the shape ContractScores already uses for
  // the same job. A useCallback invoked from the effect would put its setState
  // on the render that scheduled it.
  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch("/api/monitor", { headers: { accept: "application/json" } });
        if (!alive) return;
        if (response.status === 403) { setDenied(true); return; }
        if (!response.ok) return;
        const body = await response.json() as Board;
        if (alive) { setBoard(body); setBusy(false); }
      } catch {
        /* Keep what is on screen. A board that empties itself when the network
           blinks reads as "nothing is wrong today", which is the one thing it
           must never say by accident. */
      }
    })();
    return () => { alive = false; };
  }, [setBoard, reload]);

  const refresh = () => { setBusy(true); setReload((turn) => turn + 1); };

  // The cache holds whatever the API last said, including from before this
  // screen learned to ask about problems — a board saved twenty minutes ago has
  // no `problems` and no `tally` at all, and reading straight through either
  // would blank the screen rather than draw the three lists it does have.
  const problems = useMemo(() => board?.problems ?? [], [board]);
  const tally = board?.tally;

  /*
   * Narrowing the problem list.
   *
   * Two hundred and ninety rows in worst-first order is a list somebody reads
   * the top of and abandons. A supervisor's actual morning is "show me the
   * incidents" — seven rows, which is a morning's work — or "everything on
   * SHPP", and neither was reachable.
   *
   * Both run over the whole list the API sent, not a page of it. That is the
   * one thing that makes counting in the browser honest here: `problems` is
   * every problem in the register, so a count taken from it is the register's.
   */
  const [kind, setKind] = useState("");
  const [query, setQuery] = useState("");

  // The Thai for each kind comes back beside the machine name on every row, so
  // the buttons are labelled in the API's words rather than a second copy of
  // them kept here that would drift the day a name is reworded.
  const kinds = useMemo(() => {
    const seen = new Map<string, { thai: string; count: number }>();
    for (const row of problems) {
      row.problems.forEach((name, at) => {
        const found = seen.get(name);
        if (found) found.count += 1;
        else seen.set(name, { thai: row.problemsThai[at] ?? name, count: 1 });
      });
    }
    // In the API's order of seriousness, not the order they happened to appear.
    return [...seen.entries()]
      .map(([name, one]) => ({ name, ...one }))
      .sort((a, b) => ORDER.indexOf(a.name) - ORDER.indexOf(b.name));
  }, [problems]);

  const shown = useMemo(() => {
    const wanted = query.trim().toLowerCase();
    return problems.filter((row) => {
      if (kind && !row.problems.includes(kind)) return false;
      if (!wanted) return true;
      // The note is searched too: "รถติดในท่า" is how somebody would look for
      // every job held up in the port, and it is the only place that says so.
      return [row.customer, row.trucker, row.owner, row.jobCode, row.note, row.status]
        .some((field) => (field ?? "").toLowerCase().includes(wanted));
    });
  }, [problems, kind, query]);

  if (denied) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:30px;text-align:center;font-size:12.5px;color:#7B8CA0")}>
        หน้านี้สำหรับระดับหัวหน้างานขึ้นไป
      </div>
    );
  }

  const tabs: [typeof tab, string, number | null][] = [
    ["problem", "ปัญหาที่เกิดขึ้น", board ? problems.length : null],
    ["risk", "ต้องจัดการวันนี้", board?.risks.length ?? null],
    ["load", "ภาระงานรายคน", board?.loads.length ?? null],
    ["delay", "สาเหตุความล่าช้า 30 วัน", board?.blames.length ?? null],
  ];

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("display:flex;align-items:center;gap:8px;flex-wrap:wrap")}>
        {tabs.map(([key, label, count]) => (
          <button key={key} onClick={() => setTab(key)}
            style={css("height:30px;padding:0 13px;border-radius:4px;font-size:12px;font-weight:600;"
              + "font-family:inherit;cursor:pointer;border:1px solid "
              + (tab === key ? "#0A2240;background:#0A2240;color:#fff" : "#C9D6E2;background:#fff;color:#0A2240"))}>
            {label}{count === null ? "" : ` ${count}`}
          </button>
        ))}
        <span style={css("margin-left:auto;display:flex;align-items:center;gap:10px;font-size:11.5px;color:#7B8CA0")}>
          {board && <>นับ ณ {board.today} · งานที่ยังไม่จบ {board.live.toLocaleString()}</>}
          <button onClick={refresh} disabled={busy}
            style={css("height:26px;padding:0 10px;border:1px solid #C9D6E2;border-radius:4px;background:#fff;"
              + "font-size:11.5px;font-family:inherit;cursor:pointer;color:#0A2240")}>
            {busy ? "กำลังอ่าน…" : "อ่านใหม่"}
          </button>
        </span>
      </div>

      {board && tally && <Headline board={board} tally={tally} />}

      {!board && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
          กำลังอ่านข้อมูลทั้งทะเบียน…
        </div>
      )}

      {board && tab === "problem" && (
        <Card title="ปัญหาที่เกิดขึ้นกับงานที่กำลังวิ่ง"
          note={"เรียงจากร้ายแรงที่สุด — เหตุผิดปกติ · ความล่าช้าที่ยังไม่ปิด · ขั้นตอนล่าช้า"
            + (tally ? ` · ถึงช้ากว่าแผนเกิน ${tally.lateMinutes} นาที` : "")
            + " · มีบันทึกความล่าช้า"}
          tools={
            <div style={css("display:flex;align-items:center;gap:6px;flex-wrap:wrap;margin-top:9px")}>
              <Pick on={kind === ""} tone="#0A2240" onClick={() => setKind("")}>
                ทั้งหมด {problems.length}
              </Pick>
              {/* Only kinds that have something behind them. A button that can
                  only ever come back empty is a button people stop trusting. */}
              {kinds.map((one) => (
                <Pick key={one.name} on={kind === one.name} tone={PROBLEM[one.name] ?? "#0A2240"}
                  onClick={() => setKind(kind === one.name ? "" : one.name)}>
                  {one.thai} {one.count}
                </Pick>
              ))}
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="ค้นหา ลูกค้า / ผู้ขนส่ง / เจ้าของงาน / ข้อความ"
                style={css("margin-left:auto;height:27px;border:1px solid #C9D6E2;border-radius:4px;"
                  + "padding:0 9px;font-size:12px;font-family:inherit;min-width:250px")}
              />
            </div>
          }>
          {problems.length === 0
            ? <Empty>ยังไม่พบปัญหากับงานที่กำลังวิ่ง</Empty>
            : shown.length === 0
            ? (
              <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#7B8CA0")}>
                ไม่มีงานที่ตรงกับตัวกรอง — จาก {problems.length} รายการ
              </div>
            )
            : (
              <Table heads={["ปัญหา", "ช้ากว่าแผน", "วันที่", "ลูกค้า", "ผู้ขนส่ง", "เจ้าของงาน", "สิ่งที่บันทึกไว้"]}>
                {shown.slice(0, 200).map((row) => (
                  <tr key={row.key} className="row-hover" style={css("cursor:pointer")}
                    onClick={() => onOpenJob(row.key)}>
                    <td style={css(CELL)}>
                      <span style={css("display:flex;gap:5px;flex-wrap:wrap")}>
                        {row.problems.map((name, at) => (
                          <span key={name} style={css("font-size:10.5px;font-weight:700;padding:2px 7px;"
                            + "border-radius:3px;white-space:nowrap;border:1px solid "
                            + (PROBLEM[name] ?? "#7B8CA0") + ";color:" + (PROBLEM[name] ?? "#7B8CA0"))}>
                            {row.problemsThai[at] ?? name}
                          </span>
                        ))}
                      </span>
                    </td>
                    {/* Blank rather than a dash when nobody recorded enough to
                        measure. A "0 นาที" here would be the screen answering a
                        question its own records never asked. */}
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;color:"
                      + (row.minutesLate > 0 ? "#B42318" : "#94A3B8"))}>
                      {row.minutesLate > 0 ? lateness(row.minutesLate)
                        : row.measurable ? "ตรงเวลา" : "ยังวัดไม่ได้"}
                    </td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace")}>{row.date || "—"}</td>
                    <td style={css(CELL)}>{row.customer || "—"}</td>
                    <td style={css(CELL)}>{row.trucker || "—"}</td>
                    <td style={css(CELL)}>{row.owner || "—"}</td>
                    {/* The operator's own words, unedited, with where they wrote
                        them — the whole point of the column is that a supervisor
                        reads what a person said rather than what a rule inferred.

                        Under it, on a late row, the two readings the lateness was
                        worked out from. Most of them carry no note at all, and a
                        column of "no text" says nothing; the plan against the
                        arrival is the finding itself, and shows a plan time
                        keyed as 00:30 for what it is. */}
                    <td style={css(CELL.replace("white-space:nowrap", "white-space:normal")
                      + ";max-width:340px;color:#16232F")}>
                      {row.note && (
                        <div>
                          {row.note}
                          {row.noteFrom && (
                            <span style={css("color:#94A3B8;font-size:11px")}> · {row.noteFrom}</span>
                          )}
                        </div>
                      )}
                      {row.minutesLate > 0 && row.planned && row.arrived && (
                        <div style={css("color:#7B8CA0;font-size:11.5px;font-family:ui-monospace,monospace")}>
                          แผน {row.planned} → ถึง {row.arrived}
                        </div>
                      )}
                      {!row.note && !(row.minutesLate > 0) && (
                        <span style={css("color:#94A3B8")}>สถานะ {row.status || "—"}</span>
                      )}
                    </td>
                  </tr>
                ))}
              </Table>
            )}
          {shown.length > 200 && (
            <div style={css("padding:9px 12px;font-size:11.5px;color:#94A3B8")}>
              แสดง 200 แถวแรกจาก {shown.length} — เรียงจากร้ายแรงที่สุดแล้ว
            </div>
          )}
        </Card>
      )}

      {board && tab === "risk" && (
        <Card title="งานที่ต้องจัดการวันนี้"
          note="เรียงจากร้ายแรงที่สุด — เลยกำหนด · ไม่มีเจ้าของ · ไม่มีผู้ขนส่ง · ไม่มีรถ · งานที่รถถึงแล้วไม่นับ">
          {board.risks.length === 0
            ? <Empty>ไม่มีงานที่ต้องจัดการวันนี้</Empty>
            : (
              <Table heads={["เหตุผล", "วันที่", "ห่างกี่วัน", "ลูกค้า", "ผู้ขนส่ง", "เจ้าของงาน", "Job"]}>
                {board.risks.slice(0, 200).map((row) => (
                  <tr key={row.key} className="row-hover" style={css("cursor:pointer")}
                    onClick={() => onOpenJob(row.key)}>
                    <td style={css(CELL + ";font-weight:600;color:" + (RISK[row.why]?.tone ?? "#0A2240"))}>
                      {RISK[row.why]?.th ?? row.why}
                    </td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace")}>{row.date}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;color:"
                      + (row.daysAway < 0 ? "#B42318" : "#7B8CA0"))}>
                      {row.daysAway < 0 ? `เลยมา ${-row.daysAway} วัน` : `อีก ${row.daysAway} วัน`}
                    </td>
                    <td style={css(CELL)}>{row.customer || "—"}</td>
                    <td style={css(CELL)}>{row.trucker || "—"}</td>
                    <td style={css(CELL)}>{row.owner || "—"}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;color:#7B8CA0")}>{row.jobCode || "—"}</td>
                  </tr>
                ))}
              </Table>
            )}
          {board.risks.length > 200 && (
            <div style={css("padding:9px 12px;font-size:11.5px;color:#94A3B8")}>
              แสดง 200 แถวแรกจาก {board.risks.length} — เรียงจากร้ายแรงที่สุดแล้ว
            </div>
          )}
        </Card>
      )}

      {board && tab === "load" && (
        <Card title="ภาระงานรายคน"
          note="นับเฉพาะงานที่ยังไม่จบ — งานที่ปิดไปแล้วไม่ใช่ภาระที่ยังแบกอยู่">
          {board.loads.length === 0
            ? <Empty>ยังไม่มีงานที่มีเจ้าของ</Empty>
            : (
              <Table heads={["ผู้รับผิดชอบ", "ถืออยู่", "ต้องจัดการ", "ค้างนานสุด", "คุมแทนโดย"]}>
                {board.loads.map((row) => (
                  <tr key={row.ownerId} className="row-hover">
                    <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{row.owner}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace")}>{row.carrying.toLocaleString()}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-weight:600;color:"
                      + (row.flagged > 0 ? "#B42318" : "#16794C"))}>{row.flagged}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;color:#7B8CA0")}>
                      {row.oldestDaysWaiting > 0 ? `${row.oldestDaysWaiting} วัน` : "—"}
                    </td>
                    <td style={css(CELL + ";color:#7B8CA0")}>{row.coveredBy || "—"}</td>
                  </tr>
                ))}
              </Table>
            )}
        </Card>
      )}

      {board && tab === "delay" && (
        <Card title="ความล่าช้า 30 วันที่ผ่านมา"
          note="นับจากสาเหตุและผู้รับผิดชอบที่ผู้ปฏิบัติงานบันทึกไว้เอง ไม่ได้คำนวณใหม่">
          {board.blames.length === 0
            ? <Empty>ยังไม่มีการบันทึกความล่าช้าใน 30 วันที่ผ่านมา</Empty>
            : (
              <Table heads={["ผู้รับผิดชอบ", "จำนวนครั้ง", "เวลาที่เสียไป", "ไม่ได้บันทึกเวลา"]}>
                {board.blames.map((row) => (
                  <tr key={row.party} className="row-hover">
                    <td style={css(CELL + ";font-weight:600;color:#0A2240")}>{row.thai}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace")}>{row.cases}</td>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace")}>
                      {row.minutes > 0 ? `${row.minutes.toLocaleString()} นาที` : "—"}
                    </td>
                    {/* Said out loud rather than folded into the total: a month
                        that lost two hours across ten cases and never measured
                        thirty others did not lose two hours. */}
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;color:"
                      + (row.unmeasured > 0 ? "#B45309" : "#94A3B8"))}>
                      {row.unmeasured > 0 ? `${row.unmeasured} ครั้ง` : "—"}
                    </td>
                  </tr>
                ))}
              </Table>
            )}
        </Card>
      )}
    </div>
  );
}

/**
 * The shape of the morning, above whichever list is open.
 *
 * Five figures rather than four: the last one is how many live jobs cannot be
 * judged on time at all, because nobody recorded a plan time or an arrival. It
 * is here for the same reason the delay summary reports its unmeasured cases
 * separately — a hundred jobs nobody filled in and a hundred jobs that went
 * perfectly look identical to every other number on this screen.
 */
function Headline({ board, tally }: { board: Board; tally: Tally }) {
  const figures: [string, string, string, string][] = [
    ["งานที่ยังไม่จบ", tally.live.toLocaleString(), "#0A2240", "งานที่ยังไม่ปิดและไม่ถูกยกเลิก"],
    ["มีปัญหา", tally.withProblem.toLocaleString(),
      tally.withProblem > 0 ? "#B42318" : "#16794C", "งานที่กำลังวิ่งและมีอย่างน้อย 1 ปัญหา"],
    ["ถึงช้ากว่าแผน", tally.arrivedLate.toLocaleString(),
      tally.arrivedLate > 0 ? "#B45309" : "#16794C", `วัดจากแผนเทียบเวลาถึงจริง เกิน ${tally.lateMinutes} นาที`],
    ["ต้องจัดการวันนี้", board.risks.length.toLocaleString(),
      board.risks.length > 0 ? "#B45309" : "#16794C", "งานที่ยังขาดของก่อนออกวิ่ง"],
    ["ยังวัดไม่ได้", tally.unmeasurable.toLocaleString(), "#7B8CA0",
      "ไม่มีเวลาแผนหรือเวลาถึง จึงบอกไม่ได้ว่าตรงเวลาหรือไม่"],
  ];

  return (
    <div style={css("display:grid;gap:10px;grid-template-columns:repeat(auto-fit,minmax(168px,1fr))")}>
      {figures.map(([label, value, tone, why]) => (
        <div key={label} title={why}
          style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:11px 14px")}>
          <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;"
            + "color:#7B8CA0;font-weight:700")}>{label}</div>
          <div style={css("font-size:23px;font-weight:650;font-family:ui-monospace,monospace;"
            + "line-height:1.25;color:" + tone)}>{value}</div>
          <div style={css("font-size:11px;color:#94A3B8;margin-top:1px")}>{why}</div>
        </div>
      ))}
    </div>
  );
}

/** Minutes as a supervisor would say them out loud. */
function lateness(minutes: number): string {
  if (minutes < 60) return `${minutes} นาที`;
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours < 24) return rest === 0 ? `${hours} ชม.` : `${hours} ชม. ${rest} น.`;
  const days = Math.floor(hours / 24);
  const spare = hours % 24;
  return spare === 0 ? `${days} วัน` : `${days} วัน ${spare} ชม.`;
}

function Card({ title, note, tools, children }: {
  title: string; note: string; tools?: React.ReactNode; children: React.ReactNode;
}) {
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:11px 14px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>{title}</div>
        <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>{note}</div>
        {/* Above the scroll box rather than inside it: a filter that scrolls
            away is one nobody remembers is set. */}
        {tools}
      </div>
      <ZoomBox>{children}</ZoomBox>
    </div>
  );
}

/** One filter button: a kind and how many rows carry it. */
function Pick({ on, tone, onClick, children }: {
  on: boolean; tone: string; onClick: () => void; children: React.ReactNode;
}) {
  return (
    <button type="button" onClick={onClick}
      style={css("height:26px;padding:0 10px;border-radius:4px;font-size:11.5px;font-weight:600;"
        + "font-family:inherit;cursor:pointer;white-space:nowrap;border:1px solid " + tone
        + ";background:" + (on ? tone : "#fff") + ";color:" + (on ? "#fff" : tone))}>
      {children}
    </button>
  );
}

function Table({ heads, children }: { heads: string[]; children: React.ReactNode }) {
  return (
    <table style={css("width:100%;border-collapse:separate;border-spacing:0")}>
      <thead><tr>{heads.map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
      <tbody>{children}</tbody>
    </table>
  );
}

function Empty({ children }: { children: React.ReactNode }) {
  return (
    <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#16794C")}>{children}</div>
  );
}
