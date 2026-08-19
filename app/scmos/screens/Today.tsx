"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
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
  truckConfirmation: "#0A2240", onTimePickup: "#1D5FA8", onTimeDelivery: "#16794C",
  openIncident: "#B42318", openCarPar: "#B45309", documentWarning: "#B45309", capacityRisk: "#B42318",
};

export function Today({ onDrill }: { onDrill: (screen: string) => void }) {
  const [board, setBoard] = useState<Board | null>(null);
  const [error, setError] = useState("");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/dashboard/today", { headers: { accept: "application/json" } });
      if (cancelled) return;
      if (!response.ok) { setError("HTTP " + response.status); return; }
      setBoard(await response.json() as Board);
    })();
    return () => { cancelled = true; };
  }, []);

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
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(168px,1fr));gap:11px")}>
      {figures.map((figure) => {
        const tone = TONE[figure.id] ?? "#0A2240";
        const unmeasured = figure.value === null;
        return (
          <button key={figure.id} onClick={onDrill}
            style={css(`text-align:left;background:#fff;border-top:3px solid ${unmeasured ? "#C3CFDB" : tone};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px;cursor:pointer;font-family:inherit`)}>
            <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>
              {figure.english}
            </div>
            <div style={css("font-size:11.5px;color:#94A3B8")}>{figure.thai}</div>
            <div style={css(`font-family:ui-monospace,monospace;font-weight:600;line-height:1.25;margin-top:3px;` +
              (unmeasured
                // Same slot, different weight: an unmeasured figure must not
                // read as a number at a glance.
                ? "font-size:13px;color:#94A3B8"
                : `font-size:25px;color:${tone}`))}>
              {unmeasured
                ? "ยังวัดไม่ได้"
                : percent ? `${figure.value!.toFixed(1)}%` : figure.value!.toLocaleString()}
            </div>
            {figure.note && (
              <div style={css("font-size:11px;color:#94A3B8;margin-top:3px;line-height:1.45")}>{figure.note}</div>
            )}
          </button>
        );
      })}
    </div>
  );
}
