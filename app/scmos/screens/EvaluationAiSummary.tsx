"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { SUMMARY_PARTS, bangkokTime, citedLine, type EvaluationAiSummary as Summary, type EvaluationSummaryState } from "../annualEvaluation";
import { css } from "../theme";
import { Badge, LABEL, MONO, OUTLINE, TITLE } from "./ActionPlanParts";

/**
 * The AI summary beside a carrier's evaluation (Annual Evaluation, Phase 12). Every line the API kept cites its facts;
 * each citation opens the fact it rests on. Nothing here changes a score or a decision. Shown only once a summary
 * exists or this account may make one.
 */
export function EvaluationAiSummary({ base, snapshotVersion, resultVersion, onToast }: {
  /** The carrier's API path, `/api/annual-evaluations/{id}/carriers/{carrier}`. */
  base: string;
  /** The current snapshot's and score's versions — a summary of earlier ones says so. */
  snapshotVersion: number | null;
  resultVersion: number | null;
  onToast: (message: string) => void;
}) {
  const [state, setState] = useState<EvaluationSummaryState | null>(null);
  const [fact, setFact] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const response = await apiFetch(`${base}/ai-summary`, { headers: { accept: "application/json" } });
    setState(response.ok ? await response.json() as EvaluationSummaryState : null);
  }, [base]);

  // Every setState in load is after an await; see CarrierPortal's note on the same idiom.
  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  async function summarize() {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`${base}/ai-summary`, { method: "POST", headers: { accept: "application/json" } });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string; summary?: Summary };
      onToast(reply.message ?? reply.error ?? `ไม่สำเร็จ (${response.status})`);
      const made = reply.summary;
      if (response.ok && made) { setFact(null); setState((now) => now && { ...now, latest: made }); }
    } finally { setBusy(false); }
  }

  if (!state || (!state.latest && !state.availability.canRun)) return null;
  const latest = state.latest;
  const facts = new Map((latest?.facts ?? []).map((one) => [one.id, one]));
  const shownFact = fact ? facts.get(fact) : undefined;
  const older = latest !== null && ((snapshotVersion !== null && latest.snapshotVersion !== snapshotVersion)
    || (resultVersion !== null && latest.resultVersion !== resultVersion));

  return (
    <section aria-label="สรุปด้วย AI" style={css("display:flex;flex-direction:column;gap:8px;border:1px solid #DCE6F0;border-radius:6px;padding:10px 12px;background:#FAFCFE")}>
      <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
        <span style={css(TITLE)}>สรุปด้วย AI</span>
        {latest?.mock && <Badge label="MOCK" tone="#7C3AED" background="#F3E8FF" />}
        {older && <Badge label={`จาก snapshot ครั้งที่ ${latest.snapshotVersion ?? "—"} · คะแนนครั้งที่ ${latest.resultVersion ?? "—"}`} tone="#B45309" background="#FEF3C7" />}
        <span style={css("flex:1")} />
        {state.availability.canRun && (
          <button type="button" disabled={busy} style={css(OUTLINE + ";height:26px")} onClick={() => void summarize()}>
            {busy ? "กำลังสรุป…" : latest ? "สรุปใหม่" : "สรุป"}
          </button>
        )}
      </div>
      {latest && (
        <>
          {SUMMARY_PARTS.map((part) => {
            const lines = latest[part.key].split("\n").filter((line) => line.trim().length > 0);
            if (lines.length === 0) return null;
            return (
              <div key={part.key} style={css("display:flex;flex-direction:column;gap:3px")}>
                <span style={css(LABEL + "font-weight:600")}>{part.label}</span>
                {lines.map((line, at) => {
                  const { text, cites } = citedLine(line);
                  return (
                    <div key={at} style={css("font-size:12.5px;color:#0F2B46;line-height:1.5")}>
                      {text}
                      {cites.map((id) => (
                        <button key={id} type="button" title={facts.get(id)?.text ?? id} aria-pressed={fact === id}
                          onClick={() => setFact(fact === id ? null : id)}
                          style={css(MONO + "font-size:10.5px;margin-left:4px;padding:0 5px;border-radius:3px;cursor:pointer;border:1px solid #BFD3E8;"
                            + (fact === id ? "background:#1D5FA8;color:#fff" : "background:#fff;color:#1D5FA8"))}>{id}</button>
                      ))}
                    </div>
                  );
                })}
              </div>
            );
          })}
          {fact && shownFact && (
            <div style={css("font-size:12px;color:#334155;background:#EEF4FA;border-radius:4px;padding:6px 9px")}>
              <span style={css(MONO + "font-weight:700;color:#1D5FA8;margin-right:6px")}>{fact}</span>{shownFact.text}
            </div>
          )}
          <div style={css(LABEL)}>
            {latest.model} · {latest.requestedBy} · {bangkokTime(latest.requestedAt)}
            {latest.dropped > 0 && ` · ตัดบรรทัดที่ไม่มีหลักฐาน ${latest.dropped}`}
          </div>
        </>
      )}
    </section>
  );
}
