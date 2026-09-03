"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";
import { ZoomBox } from "../TableFrame";

/**
 * The supervisor's three questions.
 *
 * The shipment view next door answers "where is this journey", which is the
 * question of whoever is carrying it. These are the other three: what is about
 * to go wrong today, who is carrying too much, and where the month's time went.
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
type Board = {
  risks: RiskRow[]; loads: LoadRow[]; blames: BlameRow[]; today: string; live: number;
};

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
  const [tab, setTab] = useState<"risk" | "load" | "delay">("risk");
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

  if (denied) {
    return (
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:30px;text-align:center;font-size:12.5px;color:#7B8CA0")}>
        หน้านี้สำหรับระดับหัวหน้างานขึ้นไป
      </div>
    );
  }

  const tabs: [typeof tab, string, number | null][] = [
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

      {!board && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
          กำลังอ่านข้อมูลทั้งทะเบียน…
        </div>
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

function Card({ title, note, children }: { title: string; note: string; children: React.ReactNode }) {
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:11px 14px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:12.5px;font-weight:650;color:#0A2240")}>{title}</div>
        <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:2px")}>{note}</div>
      </div>
      <ZoomBox>{children}</ZoomBox>
    </div>
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
