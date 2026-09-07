"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import {
  probeSystem, probeTone, type ExternalSystem, type ProbeResult,
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
  // Returning to a menu starts a fresh check, never a result from its last visit.
  return <SystemPanel key={system.endpoint} system={system} />;
}

function SystemPanel({ system }: { system: ExternalSystem }) {
  const endpoint = system.endpoint;
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<(ProbeResult & { endpoint: string; attempt: number }) | null>(null);

  // A menu change or retry immediately hides the previous answer, even before
  // the effect starts. The request belongs to this endpoint AND this attempt.
  const probe: ProbeResult = result?.endpoint === endpoint && result.attempt === attempt
    ? result : { state: "checking", detail: "" };

  useEffect(() => {
    const controller = new AbortController();
    void probeSystem(endpoint, apiFetch, controller.signal).then((answer) => {
      // Cleanup may have run after the request resolved but before this callback.
      if (answer && !controller.signal.aborted) setResult({ ...answer, endpoint, attempt });
    });
    return () => controller.abort();
  }, [endpoint, attempt]);

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
          onClick: () => setAttempt((previous) => previous + 1),
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
