"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import {
  probeStateFor, probeTone, type ExternalSystem, type ProbeState,
} from "../externalSystems";

/**
 * A system SCMOS is meant to talk to, before it has been connected.
 *
 * One screen for ABS, CCS, LINE and Outlook. It was ABS's alone and the
 * obvious way to add the other three was to copy it; the definitions moved to
 * externalSystems.ts instead, and this reads them.
 *
 * Deliberately empty of features. It holds the place in the menu and proves
 * the route, the permission check and the API path line up, so building the
 * real thing is a matter of filling this in rather than threading a new screen
 * through five files first.
 *
 * It does make one real request, to an endpoint that does not exist yet. That
 * is the point: the panel reports what actually came back, so the day the
 * endpoint appears this screen starts saying so on its own, and until then
 * nobody has to guess whether the gap is here or at the other end.
 */
export function ExternalSystemScreen({ system }: { system: ExternalSystem }) {
  const [probe, setProbe] = useState<{ state: ProbeState; detail: string }>(
    { state: "checking", detail: "" });

  // No "checking" set in here: that is the state this screen already opens in,
  // and setting it again on mount is a second render before the first has been
  // painted. The retry button below arms it, because there it is a real change.
  const check = useCallback(async () => {
    try {
      const response = await apiFetch(system.endpoint, { headers: { accept: "application/json" } });
      const state = probeStateFor(response.status);
      setProbe({
        state,
        detail:
          state === "absent" ? "ยังไม่มี endpoint นี้ในฝั่ง API — เป็นสถานะที่ถูกต้องสำหรับตอนนี้"
          : state === "denied" ? `API ตอบ ${response.status} — บัญชีนี้ยังไม่มีสิทธิ์เรียกส่วนนี้`
          : state === "ready" ? "ต่อกับ API ได้แล้ว — พร้อมใส่หน้าจอจริง"
          : `API ตอบ ${response.status}`,
      });
    } catch (error) {
      setProbe({ state: "error", detail: error instanceof Error ? error.message : String(error) });
    }
  }, [system.endpoint]);

  // Fetching on mount. Every setState inside is after an await, so it runs
  // in a microtask rather than while this body does — the rule cannot see
  // past the await and reads it as a synchronous set. Genuine ones in this
  // codebase have been fixed; this idiom has no other spelling.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void check(); }, [check]);

  // Switching menu is a different system, and the last one's answer is not an
  // answer about this one. Read during render on the value changing rather
  // than in an effect, which would show the wrong panel for a frame first.
  const [shown, setShown] = useState(system.id);
  if (shown !== system.id) { setShown(system.id); setProbe({ state: "checking", detail: "" }); }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:34px 30px;text-align:center")}>
        <div style={css("font-size:15px;font-weight:700;color:#0F2B46;margin-bottom:7px")}>{system.name}</div>
        <div style={css("font-size:13px;color:#5A6B7D;line-height:1.75;max-width:560px;margin:0 auto")}>
          {system.purpose}
          <br />
          หน้านี้ตั้งใจเว้นว่างไว้ — เมนู เส้นทาง และการตรวจสิทธิ์พร้อมแล้ว เหลือแค่ต่อข้อมูลเข้ามา
        </div>
      </div>

      <Panel
        tone={probeTone(probe.state)}
        title={
          probe.state === "checking" ? "กำลังตรวจการเชื่อมต่อ…"
            : probe.state === "absent" ? "ยังไม่ได้เชื่อม API"
            : probe.state === "ready" ? "เชื่อม API ได้แล้ว"
            : probe.state === "denied" ? "เรียก API ไม่ผ่านสิทธิ์"
            : "เรียก API ไม่สำเร็จ"
        }
        action={probe.state === "checking" ? undefined : {
          label: "ตรวจอีกครั้ง",
          onClick: () => { setProbe({ state: "checking", detail: "" }); void check(); },
        }}
      >
        {probe.detail || "…"}
        <div style={css("margin-top:9px;font-size:11.5px;color:#7B8CA0")}>
          ตรวจจาก <code style={css("font-family:ui-monospace,SFMono-Regular,Menlo,monospace")}>{system.endpoint}</code>
        </div>
      </Panel>

      {/* What has to be settled before this can be built. Written down on the
          screen rather than in a note somewhere, because this is the page
          somebody opens when they come back to finish the job. */}
      <div style={css("background:#fff;border:1px solid #E3E8EE;border-radius:6px;padding:18px 20px")}>
        <div style={css("font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:9px")}>
          ต้องรู้อะไรบ้างก่อนต่อ
        </div>
        <ul style={css("margin:0;padding-left:19px;font-size:12.5px;color:#3F5265;line-height:1.9")}>
          {system.unknowns.map((one) => <li key={one}>{one}</li>)}
        </ul>
        {system.spec && (
          <div style={css("margin-top:11px;font-size:12px;color:#3F5265;line-height:1.7")}>
            {system.spec}
          </div>
        )}
        <div style={css("margin-top:11px;font-size:11.5px;color:#7B8CA0;line-height:1.7")}>
          ฝั่ง SCMOS จะเรียกผ่าน API ของตัวเองเสมอ ไม่เรียกระบบปลายทางจากเบราว์เซอร์ตรง ๆ —
          เพื่อให้คีย์อยู่ฝั่งเซิร์ฟเวอร์ และให้การตรวจสิทธิ์อยู่ที่เดียวกับทุกเมนู
        </div>
      </div>
    </div>
  );
}

function Panel({ tone, title, children, action }: {
  tone: "ok" | "warn" | "idle";
  title: string;
  children: React.ReactNode;
  action?: { label: string; onClick: () => void };
}) {
  const skin = tone === "ok"
    ? { bg: "#F0F8F3", border: "#BFE0CD", bar: "#16794C", text: "#16794C" }
    : tone === "warn"
      ? { bg: "#FFF8F0", border: "#F0D8B8", bar: "#B45309", text: "#B45309" }
      : { bg: "#F8FAFC", border: "#E3E8EE", bar: "#7B8CA0", text: "#5A6B7D" };

  return (
    <div style={css(`background:${skin.bg};border:1px solid ${skin.border};border-left:3px solid ${skin.bar};border-radius:5px;padding:13px 16px;display:flex;gap:14px;align-items:flex-start;flex-wrap:wrap`)}>
      <div style={css("flex:1;min-width:250px")}>
        <div style={css(`font-size:13px;font-weight:650;color:${skin.text};margin-bottom:3px`)}>{title}</div>
        <div style={css("font-size:12.5px;color:#5A6B7D;line-height:1.65")}>{children}</div>
      </div>
      {action && (
        <button onClick={action.onClick}
          style={css(`height:30px;padding:0 14px;border:1px solid ${skin.bar};background:#fff;color:${skin.text};border-radius:4px;font-size:12.5px;font-weight:600;cursor:pointer`)}>
          {action.label}
        </button>
      )}
    </div>
  );
}
