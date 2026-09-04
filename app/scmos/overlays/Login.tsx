"use client";

import { useState } from "react";
import { css } from "../theme";

/**
 * The way in.
 *
 * <h3>One screen, two ways in</h3>
 *
 * Production signs in through Entra — there are no SCMOS passwords, and the
 * button hands off to Microsoft. Demo builds carry a short list of accounts and
 * a password box. Those were two different screens: a designed one nobody in
 * production ever saw, and a plain grey card with a single button, which is the
 * one everybody sees every morning. They are one screen now, and the panel that
 * does the work is the only part that differs.
 *
 * <h3>What the right-hand side is saying</h3>
 *
 * Efficient transport, and the work behind it. Drawn rather than photographed:
 * a lane network with consignments moving along it, because that is the
 * business — places joined by roads, and something on every road right now. The
 * lines never all light at once and the pulses are staggered, so it reads as an
 * operation running rather than a decoration looping.
 *
 * <b>No figures.</b> A login screen is exactly where a made-up "98.7% on time"
 * would go, and this codebase has a rule about that: a number nobody can
 * reconcile is worse than no number. The three lines on the right say what the
 * system is for. They are claims about the software, which are ours to make —
 * not measurements, which are not.
 *
 * Motion stops entirely under `prefers-reduced-motion`.
 */

type Props = {
  /**
   * Which way in this build offers.
   *
   * "microsoft" is production. "demo" is the account list, and it also renders
   * the Microsoft button underneath — a demo build pointed at a real tenant
   * should still be able to use it.
   */
  mode: "microsoft" | "demo";
  /** Where the Microsoft button goes. Azure's Easy Auth endpoint in production. */
  signInHref: string;
  /** Shown under the heading when the app already knows something is wrong. */
  error?: string;
  /** Build number for the corner, as the reference has it. */
  version?: string;

  /* The demo form. Ignored entirely when mode is "microsoft". */
  username?: string;
  password?: string;
  onUsername?: (value: string) => void;
  onPassword?: (value: string) => void;
  onSignIn?: () => void;
};

/** LESCHACO navy, and the two blues the app already uses on it. */
const NAVY = "#0A2240";
const DEEP = "#061729";
const SKY = "#4E9BE8";

/**
 * The lane network, drawn on a 1000x900 canvas.
 *
 * Landscape, because the panel is. At 720x900 the SVG had to be scaled to
 * width to cover, and a third of its height was cropped — taking the top and
 * bottom lanes with it, so the network read as four faint lines because most
 * of it was outside the box.
 *
 * Not a map of anywhere. It is shaped like the work: dense at the lower left
 * where the plants and yards are, thinning as it runs out to the ports at the
 * top and right, so it reads as freight rather than as a constellation.
 */
const LANES = [
  "M 70 690 C 190 650 280 585 400 545 S 640 470 760 360",
  "M 70 690 C 210 715 330 655 470 640 S 700 600 830 520",
  "M 150 830 C 270 795 350 745 470 640",
  "M 150 830 C 340 855 520 805 640 715 S 760 610 830 520",
  "M 400 545 C 480 465 570 415 760 360",
  "M 470 640 C 540 560 610 500 760 360",
  "M 70 690 C 150 545 235 400 330 250 S 560 130 720 120",
  "M 330 250 C 450 205 570 160 720 120",
  "M 640 715 C 720 650 780 590 830 520",
  "M 760 360 C 830 300 890 240 950 190",
  "M 720 120 C 810 140 890 165 950 190",
  "M 830 520 C 890 440 920 350 950 190",
  "M 150 830 C 400 800 560 790 700 830",
];

/** Where a lane ends: a port, a yard, a plant. Bigger means busier. */
const STOPS: [number, number, number][] = [
  [70, 690, 7], [150, 830, 5.5], [400, 545, 4.5], [470, 640, 5],
  [760, 360, 6.5], [830, 520, 5], [330, 250, 4.5], [720, 120, 6],
  [640, 715, 4], [950, 190, 6.5], [700, 830, 4.5],
];

/** The lanes that carry a moving consignment, and how long each takes. */
const RUNNING: [number, number, number][] = [
  [0, 7.5, 0], [3, 9, 1.4], [6, 11, 0.6], [4, 8, 3.1],
  [1, 8.5, 2.2], [9, 6.5, 4.2], [12, 10, 1.9],
];

/**
 * What the system is for, in three lines.
 *
 * Claims about the software, not measurements of the operation. "Every trip has
 * a source" is a design promise this app keeps; "98% on time" would be a number
 * nobody could reconcile against anything.
 */
const PROMISES: [string, string][] = [
  ["ทุกเที่ยวมีที่มา", "งานทุกใบสาวกลับไปถึงคนสั่ง คนรับ และเวลาที่แก้ล่าสุดได้"],
  ["ราคาตรวจสอบได้", "อัตราค่าขนส่งทุกช่องมีที่มา ไล่ตามช่วงราคาน้ำมันได้ทั้งแถว"],
  ["ทีมเดียว เห็นภาพเดียวกัน", "แผนงาน ตู้ รถ และผู้รับเหมา อยู่บนหน้าจอเดียวกันทั้งทีม"],
];

const FIELD = "display:flex;align-items:center;gap:11px;height:46px;padding:0 13px;box-sizing:border-box;"
  + "border:1.5px solid #D5DEE8;border-radius:8px;background:#FBFCFE;cursor:text;transition:border-color .15s";
const INPUT = "flex:1;min-width:0;border:0;outline:0;background:transparent;font-family:inherit;"
  + "font-size:14px;color:#132B45";

export function Login(p: Props) {
  const [revealed, setRevealed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState("");

  function submit(event: React.FormEvent) {
    event.preventDefault();
    if (busy || !p.onSignIn) return;
    setBusy(true);
    setNotice("");
    // A brief hold so the button's state is legible before the app swaps in.
    window.setTimeout(() => { setBusy(false); p.onSignIn?.(); }, 300);
  }

  return (
    <div style={css("position:fixed;inset:0;z-index:90;display:flex;overflow:auto;background:#fff;"
      + "font-family:'Source Sans 3',system-ui,sans-serif")}>
      <style>{KEYFRAMES}</style>

      {/* ---------------------------------------------------------- the form */}
      {/*
        Two auto margins rather than justify-content:center.

        The footer's `margin-top:auto` and a centred column pull against each
        other, and the margin wins — on a tall phone that put everything at the
        top with two hundred pixels of nothing under it. Given to both the
        content and the footer, the free space is shared: the block sits between
        the top and the footer at any height, and the footer stays on the floor.
      */}
      <div style={css("flex:none;width:min(100%,460px);display:flex;flex-direction:column;"
        + "padding:44px 46px;box-sizing:border-box;background:#fff")}>

        <div style={css("margin-top:auto")} />

        <Mark />

        <div style={css("margin-top:22px;font-size:10.5px;font-weight:700;letter-spacing:.22em;color:#8AA0B6")}>
          LESCHACO (THAILAND) LTD.
        </div>
        <h1 style={css("margin:9px 0 0;font-size:29px;font-weight:700;letter-spacing:-.015em;color:" + NAVY)}>
          SCMOS
        </h1>
        <div style={css("margin-top:5px;font-size:13px;color:#5A6B7D;line-height:1.6")}>
          ระบบบริหารงานผู้รับเหมาช่วง
          <span style={css("display:block;font-size:11.5px;color:#94A3B8;margin-top:2px")}>
            Subcontractor Management Operating System
          </span>
        </div>

        {p.error && (
          <div style={css("margin-top:20px;padding:10px 13px;border:1px solid #F3C9C4;border-left:3px solid #B42318;"
            + "border-radius:6px;background:#FEF6F5;font-size:12.5px;color:#B42318;line-height:1.6")}>
            {p.error}
          </div>
        )}

        {p.mode === "demo" && (
          <form onSubmit={submit} style={css("margin-top:26px;display:flex;flex-direction:column;gap:12px")}>
            <label style={css("display:flex;flex-direction:column;gap:6px")}>
              <span style={css("font-size:12px;font-weight:600;color:#3C536B")}>ชื่อผู้ใช้</span>
              <span style={css(FIELD)}>
                <Glyph d="M12 12a4 4 0 100-8 4 4 0 000 8zM4.5 20a7.5 7.5 0 0115 0" />
                <input
                  value={p.username ?? ""}
                  onChange={(event) => p.onUsername?.(event.target.value)}
                  autoComplete="username"
                  placeholder="example@leschaco.com"
                  style={css(INPUT)} />
              </span>
            </label>

            <label style={css("display:flex;flex-direction:column;gap:6px")}>
              <span style={css("font-size:12px;font-weight:600;color:#3C536B")}>รหัสผ่าน</span>
              <span style={css(FIELD)}>
                <Glyph d="M6 10V7a6 6 0 1112 0v3M5 10h14v10H5z" />
                <input
                  value={p.password ?? ""}
                  onChange={(event) => p.onPassword?.(event.target.value)}
                  type={revealed ? "text" : "password"}
                  autoComplete="current-password"
                  placeholder="••••••••"
                  style={css(INPUT)} />
                <button type="button" onClick={() => setRevealed((was) => !was)}
                  aria-label={revealed ? "ซ่อนรหัสผ่าน" : "แสดงรหัสผ่าน"}
                  style={css("display:flex;align-items:center;justify-content:center;width:28px;height:28px;"
                    + "flex:none;border:0;border-radius:6px;background:transparent;cursor:pointer;color:#7B8CA0")}>
                  <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor"
                    strokeWidth="1.7" strokeLinecap="round">
                    <path d="M2.5 12S6 5.8 12 5.8 21.5 12 21.5 12 18 18.2 12 18.2 2.5 12 2.5 12z" />
                    <circle cx="12" cy="12" r="3" />
                    {!revealed && <path d="M4 4l16 16" />}
                  </svg>
                </button>
              </span>
            </label>

            <button type="submit" className="ls-in"
              style={css("margin-top:6px;width:100%;height:46px;border:0;border-radius:8px;background:" + NAVY
                + ";color:#fff;font-family:inherit;font-size:14.5px;font-weight:600;cursor:pointer;"
                + "box-shadow:0 10px 22px -12px rgba(10,34,64,.75)")}>
              {busy ? "กำลังเข้าสู่ระบบ…" : "เข้าสู่ระบบ"}
            </button>
          </form>
        )}

        {/*
          The Microsoft button, and in production the only way in.

          It carries the word "Microsoft" because that is what the next screen
          will say — a button labelled only "Sign in" that hands off to another
          company's page is how somebody decides they have been phished.
        */}
        <a href={p.signInHref} className="ls-in"
          style={css("margin-top:" + (p.mode === "demo" ? "18px" : "28px")
            + ";display:flex;align-items:center;justify-content:center;gap:10px;height:46px;border-radius:8px;"
            + "text-decoration:none;font-size:14.5px;font-weight:600;box-sizing:border-box;"
            + (p.mode === "demo"
              ? "border:1.5px solid #D5DEE8;background:#fff;color:#31465C"
              : "border:0;background:" + NAVY + ";color:#fff;box-shadow:0 10px 22px -12px rgba(10,34,64,.75)"))}>
          <svg width="17" height="17" viewBox="0 0 23 23" aria-hidden="true">
            <rect x="1" y="1" width="10" height="10" fill="#F25022" />
            <rect x="12" y="1" width="10" height="10" fill="#7FBA00" />
            <rect x="1" y="12" width="10" height="10" fill="#00A4EF" />
            <rect x="12" y="12" width="10" height="10" fill="#FFB900" />
          </svg>
          เข้าสู่ระบบด้วยบัญชี Microsoft
        </a>

        <div style={css("margin-top:16px;font-size:12px;color:#94A3B8;line-height:1.7")}>
          {p.mode === "microsoft"
            ? "ใช้บัญชีอีเมลของบริษัท — ระบบไม่มีรหัสผ่านของตัวเอง"
            : "โหมดทดสอบ · บัญชีจริงให้เข้าผ่าน Microsoft"}
        </div>

        <button type="button"
          onClick={() => setNotice("รหัสผ่านบัญชีบริษัทรีเซ็ตที่ฝ่าย IT — ระบบนี้ไม่ได้เก็บรหัสผ่านไว้")}
          style={css("margin-top:6px;align-self:flex-start;border:0;background:transparent;padding:0;"
            + "cursor:pointer;font-family:inherit;font-size:12.5px;color:#2E7DD1")}>
          ลืมรหัสผ่าน?
        </button>
        {notice && (
          <div style={css("margin-top:8px;font-size:12px;color:#5A6B7D;line-height:1.6")}>{notice}</div>
        )}

        <div style={css("margin-top:auto;padding-top:34px;font-size:11px;color:#B6C2CE;line-height:1.7")}>
          © {new Date().getFullYear()} Leschaco (Thailand) Ltd.
          <span style={css("display:block")}>Development by IT Department</span>
        </div>
      </div>

      {/* --------------------------------------------------------- the panel */}
      <div style={css("flex:1;min-width:0;position:relative;overflow:hidden;display:none;"
        + "background:linear-gradient(155deg," + NAVY + " 0%,#0C2F55 42%," + DEEP + " 100%)")}
        className="ls-panel">

        <Network />

        {/* Light from the top-right, so the network reads as lit rather than flat. */}
        <div style={css("position:absolute;inset:0;pointer-events:none;background:"
          + "radial-gradient(80% 60% at 82% 8%,rgba(78,155,232,.22) 0%,rgba(78,155,232,0) 62%)")} />
        <div style={css("position:absolute;inset:0;pointer-events:none;background:"
          + "linear-gradient(180deg,rgba(6,23,41,0) 40%,rgba(6,23,41,.55) 100%)")} />

        <div style={css("position:relative;height:100%;display:flex;flex-direction:column;"
          + "justify-content:space-between;padding:52px 56px;box-sizing:border-box")}>

          {/*
            `cargo-logo.png`, not `brand-leschaco.png`. The latter is a fully
            transparent PNG — max alpha 0, nothing in it at all — which is why
            the sidebar stopped using it too. It looks like a logo in a file
            listing and renders as a gap.
          */}
          <img src="/cargo-logo.png" alt="Leschaco (Thailand)"
            style={css("height:38px;width:auto;opacity:.96;align-self:flex-start")} />

          <div style={css("max-width:520px")}>
            <div style={css("font-size:11px;font-weight:700;letter-spacing:.2em;color:" + SKY + ";opacity:.9")}>
              การขนส่งที่วัดผลได้
            </div>
            <h2 style={css("margin:12px 0 0;font-size:33px;line-height:1.32;font-weight:600;color:#fff;"
              + "letter-spacing:-.01em;text-wrap:balance")}>
              ทุกเที่ยวมีคนดูแล<br />ทุกตัวเลขมีที่มา
            </h2>
            <p style={css("margin:14px 0 0;font-size:13.5px;line-height:1.85;color:#B9CFE5;max-width:430px")}>
              งานขนส่งไม่ได้จบที่รถออกจากลาน SCMOS ทำให้ทั้งทีมเห็นแผนเดียวกัน
              ตั้งแต่ใบจองจนถึงใบวางบิล และทุกอย่างที่แก้ไว้ยังตามกลับไปหาได้เสมอ
            </p>

            <div style={css("margin-top:30px;display:flex;flex-direction:column;gap:15px")}>
              {PROMISES.map(([title, detail]) => (
                <div key={title} style={css("display:flex;gap:12px;align-items:flex-start")}>
                  <svg width="19" height="19" viewBox="0 0 24 24" fill="none" stroke={SKY}
                    strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
                    style={css("flex:none;margin-top:2px")}>
                    <circle cx="12" cy="12" r="9.2" opacity=".45" />
                    <path d="M8 12.4l2.7 2.7L16.2 9.6" />
                  </svg>
                  <div>
                    <div style={css("font-size:13.5px;font-weight:600;color:#EAF3FB")}>{title}</div>
                    <div style={css("font-size:12px;color:#8FB4DC;line-height:1.7;margin-top:2px")}>{detail}</div>
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div style={css("display:flex;align-items:flex-end;justify-content:space-between;gap:20px")}>
            <div style={css("font-size:11.5px;color:#6F91B5;line-height:1.7")}>
              Subcontractor Management Operating System
              <span style={css("display:block;color:#4E6D8F")}>Leschaco (Thailand) Ltd. · Bangkok</span>
            </div>
            {p.version && (
              <span style={css("font-size:11px;font-weight:600;color:#9FC3E5;background:rgba(255,255,255,.08);"
                + "border:1px solid rgba(255,255,255,.14);border-radius:5px;padding:4px 10px;white-space:nowrap")}>
                {p.version}
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * The lane network, with something moving on it.
 *
 * `pathLength` is set to 100 on every lane so one dash pattern works for all of
 * them regardless of how long each curve actually is — otherwise the pulse on a
 * short lane is a smear and the one on a long lane is a dot.
 */
function Network() {
  return (
    <svg viewBox="0 0 1000 900" preserveAspectRatio="xMidYMid slice" aria-hidden="true"
      style={css("position:absolute;inset:0;width:100%;height:100%;pointer-events:none")}>
      <g fill="none" strokeLinecap="round">
        {LANES.map((d, at) => (
          <path key={at} d={d} pathLength={100} stroke="#5FA8EE" strokeWidth={1.15}
            opacity={0.26 + (at % 3) * 0.07} />
        ))}

        {/* The consignments: a short lit segment travelling the length of a lane. */}
        {RUNNING.map(([lane, seconds, delay], at) => (
          <path key={"run" + at} d={LANES[lane]} pathLength={100}
            stroke="#7FC4FF" strokeWidth={2} strokeDasharray="7 93"
            style={{
              filter: "drop-shadow(0 0 5px rgba(127,196,255,.85))",
              animation: `ls-run ${seconds}s linear ${delay}s infinite`,
            }} />
        ))}
      </g>

      {STOPS.map(([x, y, r], at) => (
        <g key={"stop" + at}>
          {/* The ring only on the busiest, so the eye is given somewhere to go. */}
          {r >= 6 && (
            <circle cx={x} cy={y} r={r} fill="none" stroke="#7FC4FF" strokeWidth={1.2}
              style={{ animation: `ls-ping 3.6s ease-out ${at * 0.7}s infinite`, transformOrigin: `${x}px ${y}px` }} />
          )}
          <circle cx={x} cy={y} r={r * 0.42} fill="#9FD0FF" opacity={0.9} />
          <circle cx={x} cy={y} r={r} fill="#4E9BE8" opacity={0.18} />
        </g>
      ))}
    </svg>
  );
}

/**
 * The SCMOS mark: three lanes converging into one.
 *
 * Drawn rather than dropped in as a file, so it stays crisp at any size and can
 * be recoloured with the rest of the screen. The shape is the thing the system
 * does — several carriers, several routes, one plan.
 */
function Mark() {
  return (
    <svg width="46" height="46" viewBox="0 0 48 48" aria-label="SCMOS" role="img">
      <rect x="1.5" y="1.5" width="45" height="45" rx="11" fill={NAVY} />
      <g fill="none" stroke="#7FC4FF" strokeWidth="2.4" strokeLinecap="round">
        <path d="M12 16h11" opacity=".55" />
        <path d="M12 24h16" />
        <path d="M12 32h11" opacity=".55" />
      </g>
      <path d="M30 24l7-6v12z" fill="#fff" />
    </svg>
  );
}

/** The small outline icon inside a field. */
function Glyph({ d }: { d: string }) {
  return (
    <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="#8AA0B6"
      strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round"
      style={css("flex:none")}>
      <path d={d} />
    </svg>
  );
}

/*
 * The panel is hidden below 900px rather than stacked.
 *
 * On a phone it would be a screen of decoration above the one control anybody
 * came for, and the form is the whole point of the page.
 *
 * Every animation is off under prefers-reduced-motion — the lanes still draw,
 * they simply stop moving.
 */
const KEYFRAMES = `
@keyframes ls-run { from { stroke-dashoffset: 100 } to { stroke-dashoffset: 0 } }
@keyframes ls-ping {
  0%   { transform: scale(1);   opacity: .75 }
  70%  { transform: scale(2.6); opacity: 0 }
  100% { transform: scale(2.6); opacity: 0 }
}
.ls-in { transition: filter .16s, transform .12s }
.ls-in:hover { filter: brightness(1.08) }
.ls-in:active { transform: translateY(1px) }
@media (min-width: 900px) { .ls-panel { display: block !important } }
@media (prefers-reduced-motion: reduce) {
  [style*="ls-run"], [style*="ls-ping"] { animation: none !important }
}
`;
