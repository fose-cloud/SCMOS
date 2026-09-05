"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

/**
 * What the dashboard would say if it could talk.
 *
 * The front page had ten figures and no opinion. Half of them were zero or
 * "cannot be measured", and whoever opened it had to work out for themselves
 * which of the rest mattered this morning. This is the same numbers, read: a
 * short list of sentences, worst first, each one clickable through to the
 * screen that answers it.
 *
 * <b>Nothing here is written in the browser.</b> Every sentence arrives from
 * the API, where the rules that counted the figures already live — the monitor's
 * risk list, the problem list, the delay register. A second opinion assembled
 * here would eventually disagree with the screen it is summarising, which is
 * the shape of bug this codebase keeps finding.
 *
 * It loads after the board, and the board is complete without it. A briefing
 * that held the front page hostage would be worse than no briefing.
 */

type Finding = {
  urgency: "Now" | "Soon" | "Watch" | "Records";
  kind: string;
  headline: string;
  detail: string;
  count: number;
  screen: string;
};
type Brief = { today: string; quiet: string; findings: Finding[] };

/** How loudly to say each rank. */
const TONE: Record<Finding["urgency"], { line: string; dot: string; label: string }> = {
  Now: { line: "#B42318", dot: "#B42318", label: "เร่งด่วน" },
  Soon: { line: "#B45309", dot: "#B45309", label: "วันนี้" },
  Watch: { line: "#1D5FA8", dot: "#1D5FA8", label: "เฝ้าดู" },
  // Deliberately grey. It is the one finding that is not about the work, and
  // colouring it like a problem would put a records gap above a late lorry.
  Records: { line: "#94A3B8", dot: "#94A3B8", label: "ข้อมูล" },
};

export function BriefingBand({ onOpen }: { onOpen: (screen: string) => void }) {
  const [brief, setBrief] = useRemembered<Brief>("briefing");
  const [denied, setDenied] = useState(false);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const reply = await apiFetch("/api/dashboard/briefing", { headers: { accept: "application/json" } });
        if (!alive) return;
        if (reply.status === 403) { setDenied(true); return; }
        if (!reply.ok) return;
        const body = await reply.json() as Brief;
        if (alive) setBrief(body);
      } catch {
        /* Keep whatever is on screen. A briefing that empties itself when the
           network blinks reads as "nothing is wrong this morning", which is the
           one thing it must never say by accident. */
      }
    })();
    return () => { alive = false; };
  }, [setBrief]);

  // Nothing at all until it has something to say. An empty bordered box where
  // the briefing will be is a worse first impression than the board alone.
  if (denied || !brief) return null;

  const findings = brief.findings ?? [];

  return (
    <div className="sc-card sc-rise" style={css("overflow:hidden")}>
      <div style={css("padding:12px 18px;border-bottom:1px solid #EEF3F8;display:flex;"
        + "align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:13px;font-weight:650;color:#0A2240;letter-spacing:-.01em")}>
          สรุปสถานการณ์
        </span>
        <span style={css("font-size:11px;color:#94A3B8")}>
          อ่านจากทะเบียนงานทั้งหมด · นับ ณ {brief.today}
        </span>
        {findings.length > 0 && (
          <span style={css("margin-left:auto;font-size:11px;color:#94A3B8")}>
            กดที่บรรทัดเพื่อเปิดหน้าที่จัดการเรื่องนั้น
          </span>
        )}
      </div>

      {findings.length === 0 ? (
        /* One line, and it is careful about which claim it is making — see
           Briefing.Quiet on the API, which distinguishes "nothing is wrong"
           from "nothing can be seen to be wrong". */
        <div style={css("padding:18px;font-size:12.5px;color:#16794C")}>{brief.quiet}</div>
      ) : (
        <div>
          {findings.map((one, at) => {
            const tone = TONE[one.urgency] ?? TONE.Watch;
            return (
              <button key={one.kind} type="button" onClick={() => onOpen(one.screen)}
                className="row-hover sc-rise"
                style={css("width:100%;display:flex;align-items:flex-start;gap:12px;text-align:left;"
                  + "padding:13px 18px;background:none;border:none;border-top:1px solid #F4F7FA;"
                  // Read in order, arriving in order. The band is the first
                  // thing on the page and the delays are short enough that the
                  // last line lands well inside the first half-second.
                  + `border-left:3px solid ${tone.line};cursor:pointer;font-family:inherit;`
                  + `animation-delay:${60 + at * 55}ms`)}>
                <span style={css(`width:7px;height:7px;border-radius:50%;background:${tone.dot};`
                  + "flex:none;margin-top:6px")} />
                <span style={css("flex:1;min-width:0")}>
                  <span style={css("display:block;font-size:13.5px;font-weight:600;color:#0A2240;"
                    + "letter-spacing:-.01em")}>
                    {one.headline}
                  </span>
                  <span style={css("display:block;font-size:11.5px;color:#7B8CA0;margin-top:3px;line-height:1.5")}>
                    {one.detail}
                  </span>
                </span>
                <span style={css(`font-size:9.5px;font-weight:700;letter-spacing:.09em;color:${tone.dot};`
                  + "flex:none;margin-top:4px;white-space:nowrap;text-transform:uppercase")}>
                  {tone.label}
                </span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
