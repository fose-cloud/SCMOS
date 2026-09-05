"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { css } from "../theme";

/**
 * What is happening today.
 *
 * Every figure comes from the register through the API, and any figure that
 * cannot be measured says so rather than showing zero. That distinction is the
 * whole design: a management screen that renders "nobody has told us the
 * capacity" as a reassuring 0 is worse than one that renders nothing, because
 * the reader cannot tell the two apart.
 */

type Figure = { id: string; english: string; thai: string; value: number | null; base: number; unit: string; note: string };
type Board = {
  date: string;
  volume: Figure[];
  performance: Figure[];
  attention: Figure[];
};

const TONE: Record<string, string> = {
  total: "#0A2240", completed: "#16794C", inTransit: "#1D5FA8",
  pending: "#B45309", delay: "#B42318",
  truckConfirmation: "#0A2240", onTimeDelivery: "#16794C",
  openIncident: "#B42318", openCarPar: "#B45309", documentWarning: "#B45309", capacityRisk: "#B42318",
};

export function Today({ onDrill, onSettled }: {
  onDrill: (screen: string) => void;
  /** Lets secondary startup work wait until the primary board has answered. */
  onSettled: () => void;
}) {
  const [board, setBoard] = useRemembered<Board>("today");
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await apiFetch("/api/dashboard/today", { headers: { accept: "application/json" } });
        if (cancelled) return;
        if (!response.ok) { setError("HTTP " + response.status); return; }
        setBoard(await response.json() as Board);
      } catch (reason) {
        if (!cancelled) setError(reason instanceof Error ? reason.message : String(reason));
      } finally {
        if (!cancelled) onSettled();
      }
    })();
    return () => { cancelled = true; };
  }, [onSettled, setBoard]);

  /**
   * The two whole-register rates, fetched after the board is on screen.
   *
   * They are the most expensive thing the API computes — every job judged
   * against eight measures — and waiting for them held the front page on
   * "loading". The board arrives from the day's own rows; these drop into their
   * places when they are ready, and if they never arrive the placeholders stay,
   * which is the honest outcome rather than a zero.
   */
  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const response = await apiFetch("/api/dashboard/today/rates", { headers: { accept: "application/json" } });
        if (cancelled || !response.ok) return;
        const body = await response.json() as { figures?: Figure[] };
        const rates = body.figures ?? [];
        if (!rates.length) return;
        setBoard((held) => held && {
          ...held,
          performance: held.performance.map((figure) =>
            rates.find((rate) => rate.id === figure.id) ?? figure),
        });
      } catch {
        // The board stands without them; the placeholders already say so.
      }
    })();
    return () => { cancelled = true; };
  }, [setBoard]);

  if (error) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-left:3px solid #B42318;border-radius:5px;padding:20px;font-size:12.5px;color:#B42318")}>อ่านแดชบอร์ดไม่สำเร็จ · {error}</div>;
  }
  if (!board) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:flex;align-items:baseline;gap:10px;flex-wrap:wrap")}>
        <span style={css("font-size:13px;font-weight:650;color:#0A2240;letter-spacing:.04em")}>TODAY</span>
        <span style={css("font-size:12px;color:#7B8CA0")}>{board.date}</span>
      </div>

      <Row figures={board.volume} onDrill={() => onDrill("myjob")} />
      <Row figures={board.performance} onDrill={() => onDrill("kpi")} percent />
      <Row figures={board.attention} onDrill={() => onDrill("carpar")} />
    </div>
  );
}

function Row({ figures, onDrill, percent }: { figures: Figure[]; onDrill: () => void; percent?: boolean }) {
  return (
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(184px,1fr));gap:12px")}>
      {figures.map((figure, at) => (
        <Tile key={figure.id} figure={figure} onDrill={onDrill} percent={percent} at={at} />
      ))}
    </div>
  );
}

/**
 * One figure.
 *
 * The number carries the tile and everything else gets out of its way — that is
 * the whole of the redesign. Eleven boxes of equal weight with a 25px number and
 * three lines of 11px label around it is a page you read left to right like a
 * table; a page you glance at has one thing per card.
 */
function Tile({ figure, onDrill, percent, at }: {
  figure: Figure; onDrill: () => void; percent?: boolean; at: number;
}) {
  const tone = TONE[figure.id] ?? "#0A2240";
  const unmeasured = figure.value === null;

  /*
   * The figure does not animate, and that was a decision rather than an
   * omission.
   *
   * A count settling from zero is the flourish this kind of page is known for,
   * and every way of building it put a wrong number on screen for a moment:
   * rendered on the server it ships a literal 0 in the HTML, and derived after
   * mount it jumps backwards before it climbs. On a page whose whole worth is
   * that its numbers can be quoted, half a second of 0 where 125 belongs costs
   * more than the animation is worth. The cards arrive; the figures do not
   * count.
   */

  return (
    <button onClick={onDrill}
      className="sc-card sc-card-tap sc-rise"
      style={css("text-align:left;padding:14px 16px 16px;cursor:pointer;font-family:inherit;"
        // Staggered by position, briefly. Long enough to read as the page
        // arriving in order, short enough that nobody waits for the last tile.
        + `animation-delay:${at * 45}ms;`
        + `border-top:3px solid ${unmeasured ? "#C3CFDB" : tone}`)}>
      <div style={css("font-size:10px;letter-spacing:.08em;text-transform:uppercase;color:#8A9AAB;font-weight:700")}>
        {figure.english}
      </div>

      <div className="sc-figure" style={css("font-weight:650;line-height:1.1;margin-top:8px;"
        + (unmeasured
          // Same slot, different weight. An unmeasured figure must not read as
          // a number at a glance, and at this size it would.
          ? "font-size:14px;color:#94A3B8;padding:9px 0 8px"
          : `font-size:34px;color:${tone}`))}>
        {unmeasured
          ? "ยังวัดไม่ได้"
          : percent ? `${figure.value!.toFixed(1)}%` : figure.value!.toLocaleString()}
      </div>

      <div style={css("font-size:11.5px;color:#7B8CA0;margin-top:4px")}>{figure.thai}</div>
      {figure.note && (
        <div style={css("font-size:11px;color:#A3B0BF;margin-top:2px;line-height:1.45")}>{figure.note}</div>
      )}
    </button>
  );
}
