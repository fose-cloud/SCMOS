"use client";

import { useState, type ReactNode } from "react";
import { css } from "../theme";

type Props = {
  username: string;
  password: string;
  error: string;
  onUsername: (value: string) => void;
  onPassword: (value: string) => void;
  onSignIn: () => void;
};

/**
 * The source comp is drawn on a fixed 1920×1080 canvas, so its type scale is
 * roughly 1.5× what a real login should be. The composition is kept as drawn —
 * photo, route network, floating badges, centred card — but the card and its
 * type are set at conventional sizes rather than scaled to the viewport.
 */

/** Route network drawn behind the card — nodes are ports, lines are lanes. */
const ROUTES = [
  "M150 410 L360 300 L520 190 L760 250 L1010 150 L1180 300 L1360 210 L1620 300 L1810 200",
  "M120 640 L330 560 L520 190",
  "M330 560 L610 700 L860 620 L1080 720 L1330 640 L1560 760 L1830 660",
  "M610 700 L520 950 L840 1000 L1120 900 L1330 640",
  "M1010 150 L1080 720",
  "M760 250 L860 620",
  "M1620 300 L1560 760",
  "M1180 300 L1330 640",
  "M150 410 L120 640",
  "M360 300 L610 700",
];

const LIVE_ROUTES = [
  "M150 410 L360 300 L520 190 L760 250 L1010 150",
  "M1080 720 L1330 640 L1560 760 L1830 660",
];

const NODES: [number, number, number][] = [
  [150, 410, 13], [360, 300, 9], [520, 190, 7], [760, 250, 10], [1010, 150, 12],
  [1180, 300, 8], [1360, 210, 10], [1620, 300, 13], [1810, 200, 8], [120, 640, 10],
  [330, 560, 7], [610, 700, 12], [860, 620, 8], [1080, 720, 10], [1330, 640, 13],
  [1560, 760, 9], [1830, 660, 7], [520, 950, 11], [840, 1000, 8], [1120, 900, 10],
];

const ICON = {
  fill: "none",
  stroke: "#12699f",
  strokeWidth: 1.5,
  strokeLinecap: "round" as const,
  strokeLinejoin: "round" as const,
};

const CARD_WIDTH = 460;

type Badge = {
  /** Which side of the card the badge sits on. */
  side: "left" | "right";
  /** Clearance in px from that card edge — constant at every viewport width. */
  gap: number;
  top: number;
  size: number;
  pulses: boolean;
  icon: () => ReactNode;
};

/**
 * Anchored to the card's edges rather than to viewport percentages. The card is
 * a fixed 460px, so percentage positions drift inward as the window narrows and
 * end up straddling its border; a fixed gap keeps the arrangement intact.
 */
const BADGES: Badge[] = [
  { side: "left", gap: 214, top: 3, size: 84, pulses: true, icon: () => (<><path d="M3 4h3l1 11H5" /><path d="M7 15h9V6h-9" /><circle cx="9" cy="18.5" r="1.6" /><circle cx="16" cy="18.5" r="1.6" /><path d="M18 9h3l1.5 3.5V15h-4.5" /></>) },
  { side: "left", gap: 74, top: 6, size: 62, pulses: false, icon: () => (<><rect x="4" y="3" width="13" height="17" rx="2" /><path d="M8 8h6M8 12h6M8 16h4" /><path d="M17 9l3 2-3 2" /></>) },
  { side: "right", gap: 44, top: 5, size: 118, pulses: true, icon: () => (<><path d="M3 15l1.6 4.2a2 2 0 001.9 1.3h11a2 2 0 001.9-1.3L21 15z" /><path d="M5 15V9h14v6" /><path d="M8 9V6h5v3" /><path d="M12 3v3" /><path d="M8.5 11.5h7" /></>) },
  { side: "right", gap: 308, top: 16, size: 90, pulses: false, icon: () => (<><circle cx="12" cy="12" r="9" /><path d="M3 12h18" /><path d="M12 3c3 3.2 3 14 0 18-3-4-3-14.8 0-18z" /><path d="M5 6.5c4.4 2 9.6 2 14 0M5 17.5c4.4-2 9.6-2 14 0" /></>) },
  { side: "left", gap: 271, top: 43, size: 94, pulses: false, icon: () => <path d="M2.5 13.5l19-6-4.5 6.8 1 5-5.5-4-4.5 3 1.3-4.3z" /> },
  { side: "right", gap: 173, top: 46, size: 74, pulses: false, icon: () => (<><path d="M3 8l9-4 9 4-9 4z" /><path d="M3 8v8l9 4 9-4V8" /><path d="M12 12v8" /></>) },
  { side: "right", gap: 212, top: 75, size: 100, pulses: true, icon: () => (<><circle cx="12" cy="12" r="9" /><path d="M12 7.2V12l3.4 2.2" /></>) },
  { side: "left", gap: 286, top: 72, size: 70, pulses: false, icon: () => (<><path d="M3 8.5L12 4l9 4.5v7L12 20l-9-4.5z" /><path d="M3 8.5l9 4.5 9-4.5M12 13v7" /></>) },
];

const FIELD =
  "display:flex;align-items:center;gap:12px;height:48px;padding:0 14px;box-sizing:border-box;" +
  "border:1.5px solid #d3dde8;border-radius:9px;background:#fbfdff;cursor:text";
const INPUT =
  "flex:1;min-width:0;border:0;outline:0;background:transparent;font-family:inherit;font-size:14.5px;color:#132b45";
const SMALL_ACTION =
  "border:0;background:transparent;padding:0;cursor:pointer;font-family:inherit;font-size:13.5px";

export function Login(p: Props) {
  const [revealed, setRevealed] = useState(false);
  const [remember, setRemember] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");

  function submit() {
    if (busy) return;
    setBusy(true);
    setNotice("");
    // Brief hold so the button state is legible before the app swaps in.
    setTimeout(() => { setBusy(false); p.onSignIn(); }, 320);
  }

  return (
    <div
      style={{
        ...css("position:fixed;inset:0;z-index:90;overflow:auto;font-family:'Source Sans 3',system-ui,sans-serif;background-color:#0d2440;background-size:cover;background-position:center"),
        // Drop a photo at public/login-bg.jpg and it takes over; until then the
        // gradients below stand in for it.
        backgroundImage:
          "url(/login-bg.jpg)," +
          "radial-gradient(120% 120% at 20% 0%, #17456f 0%, rgba(23,69,111,0) 55%)," +
          "radial-gradient(100% 100% at 85% 20%, #12557f 0%, rgba(18,85,127,0) 50%)," +
          "linear-gradient(180deg, #0d2440 0%, #0a1b30 100%)",
      }}
    >
      <div style={css("position:absolute;inset:0;pointer-events:none;background:linear-gradient(180deg,rgba(10,32,58,.34) 0%,rgba(9,27,50,.20) 38%,rgba(7,22,42,.52) 100%)")} />
      <div style={css("position:absolute;inset:0;pointer-events:none;background:radial-gradient(58% 62% at 50% 52%,rgba(6,20,38,.62) 0%,rgba(6,20,38,.30) 45%,rgba(6,20,38,0) 78%)")} />
      <div style={css("position:absolute;inset:0;pointer-events:none;background:linear-gradient(90deg,rgba(12,40,74,.30),rgba(12,58,102,.10) 45%,rgba(10,36,68,.34))")} />

      <svg
        viewBox="0 0 1920 1080"
        preserveAspectRatio="xMidYMid slice"
        style={css("position:absolute;inset:0;width:100%;height:100%;pointer-events:none;opacity:.45")}
        fill="none"
        stroke="#dceaf7"
        strokeWidth={1.1}
        aria-hidden="true"
      >
        <g opacity=".5">{ROUTES.map((d) => <path key={d} d={d} />)}</g>
        <g className="ls-anim" strokeDasharray="6 12" opacity=".55" style={{ animation: "lsDash 9s linear infinite" }}>
          {LIVE_ROUTES.map((d) => <path key={d} d={d} />)}
        </g>
        <g fill="rgba(226,239,250,.14)">
          {NODES.map(([cx, cy, r]) => <circle key={`${cx}-${cy}`} cx={cx} cy={cy} r={r} />)}
        </g>
      </svg>

      {BADGES.map((b, i) => (
        <div
          key={`${b.side}-${b.gap}-${b.top}`}
          className={"ls-badge" + (b.pulses ? " ls-anim" : "")}
          aria-hidden="true"
          style={{
            ...css(
              "position:absolute;border-radius:50%;background:rgba(255,255,255,.78);display:flex;align-items:center;" +
              "justify-content:center;box-shadow:0 8px 26px rgba(6,22,44,.2);opacity:.85;pointer-events:none",
            ),
            [b.side === "left" ? "right" : "left"]: `calc(50% + ${CARD_WIDTH / 2 + b.gap}px)`,
            top: b.top + "%",
            width: b.size,
            height: b.size,
            animation: b.pulses ? `lsPulse ${11 + i}s ease-in-out infinite` : undefined,
          }}
        >
          <svg width={Math.round(b.size * 0.48)} height={Math.round(b.size * 0.48)} viewBox="0 0 24 24" {...ICON}>
            {b.icon()}
          </svg>
        </div>
      ))}

      <div style={css("position:relative;min-height:100%;display:flex;align-items:center;justify-content:center;padding:48px 20px 84px")}>
        <form
          onSubmit={(e) => { e.preventDefault(); submit(); }}
          style={css(
            "width:min(" + CARD_WIDTH + "px,100%);box-sizing:border-box;padding:38px 40px 42px;border-radius:14px;" +
            "background:rgba(255,255,255,.96);backdrop-filter:blur(14px);border:1px solid rgba(255,255,255,.7);" +
            "box-shadow:0 36px 80px -26px rgba(4,17,36,.6),0 6px 20px rgba(4,17,36,.2)",
          )}
        >
          <div style={css("display:flex;align-items:center;justify-content:center;gap:14px;margin-bottom:26px")}>
            <svg width="44" height="44" viewBox="0 0 86 86" aria-label="Leschaco mark">
              <circle cx="43" cy="43" r="42" fill="#0e2b4d" />
              <g stroke="#ffffff" strokeWidth="3.4" fill="none" strokeLinecap="round">
                <path d="M6 52c14-16 34-25 56-27" />
                <path d="M8 60c15-14 33-22 54-24" />
                <path d="M12 67c15-12 31-19 50-21" />
                <path d="M18 73c13-10 27-16 43-18" />
              </g>
            </svg>
            <span style={css("font-family:'Source Serif 4',Georgia,serif;font-weight:600;font-size:30px;letter-spacing:.05em;color:#0e2b4d;line-height:1")}>
              LESCHACO
            </span>
          </div>

          <h1 style={css("margin:0 0 8px;text-align:center;font-size:23px;font-weight:600;letter-spacing:-.01em;color:#0e2b4d")}>
            Welcome back.
          </h1>
          <p style={css("margin:0 0 28px;text-align:center;font-size:14.5px;font-weight:400;color:#5c6f85")}>
            Please sign in to continue to your account.
          </p>

          <label style={css(FIELD + ";margin-bottom:12px")}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5c6f85" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
              <circle cx="12" cy="8" r="4" />
              <path d="M4.5 20c1.2-3.8 4-5.6 7.5-5.6s6.3 1.8 7.5 5.6" />
            </svg>
            <input
              value={p.username}
              onChange={(e) => p.onUsername(e.target.value)}
              placeholder="name@leschaco.com"
              autoComplete="username"
              aria-label="Email or username"
              style={css(INPUT)}
            />
          </label>

          <label style={css(FIELD + ";margin-bottom:18px")}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="#5c6f85" strokeWidth="1.7" strokeLinecap="round" aria-hidden="true">
              <rect x="4" y="10.5" width="16" height="10" rx="2.5" />
              <path d="M8 10.5V8a4 4 0 018 0v2.5" />
              <circle cx="12" cy="15.5" r="1.3" fill="#5c6f85" stroke="none" />
            </svg>
            <input
              type={revealed ? "text" : "password"}
              value={p.password}
              onChange={(e) => p.onPassword(e.target.value)}
              placeholder="••••••••••"
              autoComplete="current-password"
              aria-label="Password"
              style={css(INPUT + ";letter-spacing:.06em")}
            />
            <button
              type="button"
              className="ls-reveal"
              onClick={() => setRevealed((v) => !v)}
              aria-label={revealed ? "Hide password" : "Show password"}
              style={css("display:flex;align-items:center;justify-content:center;width:30px;height:30px;flex:none;border:0;border-radius:7px;background:transparent;cursor:pointer;color:#5c6f85")}
            >
              {revealed ? (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
                  <path d="M2.5 12S6 5.8 12 5.8 21.5 12 21.5 12 18 18.2 12 18.2 2.5 12 2.5 12z" />
                  <circle cx="12" cy="12" r="3" />
                </svg>
              ) : (
                <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round">
                  <path d="M2.5 12S6 5.8 12 5.8c1.6 0 3 .4 4.2 1" />
                  <path d="M19.4 8.6c1.3 1.4 2.1 3.4 2.1 3.4S18 18.2 12 18.2c-1.7 0-3.2-.5-4.4-1.2" />
                  <path d="M4 4l16 16" />
                </svg>
              )}
            </button>
          </label>

          <div style={css("display:flex;align-items:center;justify-content:space-between;gap:14px;margin-bottom:20px")}>
            <button
              type="button"
              onClick={() => setRemember((v) => !v)}
              aria-pressed={remember}
              style={css(SMALL_ACTION + ";display:flex;align-items:center;gap:9px;color:#3c536b")}
            >
              <span style={css(
                "display:flex;align-items:center;justify-content:center;width:18px;height:18px;flex:none;border-radius:4px;border:1.5px solid " +
                (remember ? "#0e2b4d" : "#b9c7d6") + ";background:" + (remember ? "#0e2b4d" : "#ffffff"),
              )}>
                {remember && (
                  <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="#ffffff" strokeWidth="3.4" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M5 12.5l4.5 4.5L19 7" />
                  </svg>
                )}
              </span>
              Remember me
            </button>
            <button
              type="button"
              onClick={() => setNotice("Password resets are handled by your IT administrator.")}
              style={css(SMALL_ACTION + ";color:#1668b8")}
            >
              Forgot password?
            </button>
          </div>

          {(p.error || notice) && (
            <div style={css("margin:-8px 0 16px;font-size:13px;line-height:1.45;color:" + (p.error ? "#b42318" : "#5c6f85"))}>
              {p.error || notice}
            </div>
          )}

          <button
            type="submit"
            className="ls-submit"
            style={css(
              "width:100%;height:48px;border:0;border-radius:9px;background:#0e2b4d;color:#fff;font-family:inherit;" +
              "font-size:15.5px;font-weight:600;letter-spacing:.01em;cursor:pointer;" +
              "box-shadow:0 10px 24px -12px rgba(14,43,77,.7);transition:background .18s,transform .12s",
            )}
          >
            {busy ? "Signing in…" : "Log In"}
          </button>
        </form>
      </div>

      <div style={css("position:absolute;left:0;right:0;bottom:22px;display:flex;justify-content:center;gap:20px;flex-wrap:wrap;font-size:12px;color:rgba(230,240,250,.55);pointer-events:none")}>
        <span>Leschaco Transportation Management System</span><span>·</span>
        <span>Global Freight Operations</span><span>·</span>
        <span>v4.2.1</span>
      </div>
    </div>
  );
}
