"use client";

import { useEffect, useState } from "react";
import { METRIC_LABEL, METRIC_STATUS, bangkokTime, shown } from "../scmos/annualEvaluation";
import { css } from "../scmos/theme";

/**
 * The annual evaluation for people outside SCMOS (1 Oct 2026, Annual Evaluation Phase 6). No sign-in and no SCMOS menu:
 * the link's token, in this page's fragment, is the whole of who the visitor is — it names one carrier, and nothing here
 * can change which. The fragment never reaches a server; the token goes to the API in a header.
 */

type Metric = { code: string; status: string; value: number | null };
type Question = { code: string; text: string; textTh: string; required: boolean; commentRequiredAtOrBelow: number };
type Answer = { code: string; rating: number | null; notApplicable: boolean; comment: string };
type View = {
  campaign: string; year: number; periodStart: string; periodEnd: string; dueOn: string | null; carrier: string; evaluator: string; department: string;
  submitted: boolean; submittedAt: string | null; performance: Metric[]; questions: Question[]; answers: Answer[]; comment: string;
};

const SCALE: [number, string, string][] = [
  [5, "ดีเยี่ยม", "Excellent"], [4, "ดี", "Good"], [3, "พอใช้", "Fair"], [2, "ควรปรับปรุง", "Poor"], [1, "ต้องแก้ไขเร่งด่วน", "Very Poor"],
];

const PAGE = "min-height:100vh;background:#F4F7FA;font-family:'IBM Plex Sans','IBM Plex Sans Thai',sans-serif;color:#16232F;";
const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:8px;padding:16px 18px;";

export default function EvaluationPage() {
  const [token, setToken] = useState<string | null>(null);
  const [view, setView] = useState<View | null>(null);
  const [failure, setFailure] = useState("");
  const [answers, setAnswers] = useState<Record<string, Answer>>({});
  const [comment, setComment] = useState("");
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState("");

  useEffect(() => {
    const reread = () => window.location.reload();
    window.addEventListener("hashchange", reread);
    return () => window.removeEventListener("hashchange", reread);
  }, []);

  useEffect(() => {
    const fromLink = window.location.hash.replace(/^#/, "").trim();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setToken(fromLink);
    if (!fromLink) { setFailure("ลิงก์ไม่ถูกต้อง"); return; }
    (async () => {
      try {
        const response = await fetch("/api/external/evaluation", { headers: { accept: "application/json", "x-evaluation-token": fromLink }, cache: "no-store" });
        const body = await response.json().catch(() => null) as { view?: View; error?: string } | null;
        if (!response.ok || !body?.view) throw new Error(body?.error ?? `เปิดแบบประเมินไม่ได้ (${response.status})`);
        setView(body.view);
      } catch (error) {
        setFailure(error instanceof Error ? error.message : String(error));
      }
    })();
  }, []);

  const set = (code: string, change: Partial<Answer>) =>
    setAnswers((all) => ({ ...all, [code]: { ...(all[code] ?? { code, rating: null, notApplicable: false, comment: "" }), ...change } }));

  async function submit() {
    if (!view || !token || busy) return;
    setBusy(true);
    setProblem("");
    try {
      const response = await fetch("/api/external/evaluation/submit", {
        method: "POST", cache: "no-store",
        headers: { "content-type": "application/json", accept: "application/json", "x-evaluation-token": token },
        body: JSON.stringify({ answers: view.questions.map((question) => answers[question.code] ?? { code: question.code, rating: null, notApplicable: false, comment: "" }), comment }),
      });
      const body = await response.json().catch(() => null) as { view?: View; error?: string } | null;
      if (!response.ok || !body?.view) { setProblem(body?.error ?? `ส่งไม่สำเร็จ (${response.status})`); return; }
      setView(body.view);
      window.scrollTo({ top: 0 });
    } finally { setBusy(false); }
  }

  if (failure) {
    return <main style={css(PAGE + "display:flex;align-items:center;justify-content:center;padding:16px")}>
      <div style={css(CARD + "max-width:420px;text-align:center")}>
        <div style={css("font-size:15px;font-weight:700;color:#0A2240;margin-bottom:6px")}>Carrier Annual Evaluation</div>
        <div style={css("font-size:13px;color:#B42318")}>{failure}</div>
      </div>
    </main>;
  }
  if (!view) return <main style={css(PAGE + "display:flex;align-items:center;justify-content:center")}><span style={css("color:#7B8CA0;font-size:13px")}>กำลังโหลด…</span></main>;

  const missing = view.questions.filter((question) => {
    const answer = answers[question.code];
    const rated = answer && !answer.notApplicable && answer.rating !== null;
    const needsComment = rated && answer.rating! <= question.commentRequiredAtOrBelow && !answer.comment.trim();
    return (question.required && !(answer?.notApplicable || rated)) || needsComment;
  });

  return (
    <main style={css(PAGE + "padding:16px")}>
      <div style={css("max-width:760px;margin:0 auto;display:flex;flex-direction:column;gap:12px")}>
        <header style={css("background:#0A2240;color:#fff;border-radius:8px;padding:18px 20px")}>
          <div style={css("font-size:11px;letter-spacing:.12em;opacity:.75")}>LESCHACO · CARRIER ANNUAL EVALUATION {view.year}</div>
          <div style={css("font-size:20px;font-weight:700;margin-top:6px")}>{view.carrier}</div>
          <div style={css("font-size:12.5px;opacity:.85;margin-top:4px")}>
            ช่วงประเมิน {view.periodStart} – {view.periodEnd}{view.dueOn ? ` · ปิดรับ ${view.dueOn}` : ""}
          </div>
          <div style={css("font-size:12.5px;opacity:.85")}>ผู้ประเมิน {view.evaluator} · {view.department}</div>
        </header>

        {view.submitted ? (
          <div style={css(CARD + "border-left:4px solid #16794C")}>
            <div style={css("font-size:15px;font-weight:700;color:#16794C")}>ส่งแบบประเมินแล้ว ขอบคุณครับ</div>
            <div style={css("font-size:12px;color:#5A6B7D;margin-top:3px")}>{bangkokTime(view.submittedAt)}</div>
          </div>
        ) : null}

        {view.performance.length > 0 && (
          <section style={css(CARD)}>
            <div style={css("font-size:13px;font-weight:700;color:#0A2240;margin-bottom:10px")}>ผลการดำเนินงานในช่วงประเมิน</div>
            <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(128px,1fr));gap:8px")}>
              {view.performance.map((metric) => (
                <div key={metric.code} style={css("border:1px solid #E3E8EE;border-radius:6px;padding:9px 11px")}>
                  <div style={css("font-size:11px;color:#7B8CA0")}>{METRIC_LABEL[metric.code] ?? metric.code}</div>
                  <div style={css("font-size:17px;font-weight:700;color:#0A2240;font-family:'IBM Plex Mono',monospace")}>
                    {metric.value === null ? <span style={css("font-size:12px;color:#B45309")}>{METRIC_STATUS[metric.status] || "—"}</span>
                      : Number.isInteger(metric.value) ? metric.value : shown(metric.value)}
                  </div>
                  {metric.value !== null && metric.status === "insufficient-data" && <div style={css("font-size:10.5px;color:#B45309")}>ข้อมูลน้อย</div>}
                </div>
              ))}
            </div>
          </section>
        )}

        {view.questions.map((question, index) => {
          const answer = view.submitted ? view.answers.find((one) => one.code === question.code) : answers[question.code];
          const needsComment = !!answer && !answer.notApplicable && answer.rating !== null && answer.rating <= question.commentRequiredAtOrBelow;
          return (
            <section key={question.code} style={css(CARD + "display:flex;flex-direction:column;gap:9px")}>
              <div>
                <div style={css("font-size:14px;font-weight:650;color:#0A2240")}>{index + 1}. {question.textTh || question.text}{question.required ? " *" : ""}</div>
                {question.textTh && <div style={css("font-size:11.5px;color:#7B8CA0")}>{question.text}</div>}
              </div>
              <div role="radiogroup" aria-label={question.textTh || question.text} style={css("display:flex;gap:6px;flex-wrap:wrap")}>
                {SCALE.map(([value, thai, english]) => {
                  const on = !!answer && !answer.notApplicable && answer.rating === value;
                  return (
                    <button key={value} type="button" role="radio" aria-checked={on} aria-label={`${value} ${thai}`} disabled={view.submitted}
                      onClick={() => set(question.code, { rating: value, notApplicable: false })}
                      style={css(`min-width:92px;padding:7px 9px;border-radius:6px;border:1px solid ${on ? "#0A2240" : "#C9D6E2"};background:${on ? "#0A2240" : "#fff"};`
                        + `color:${on ? "#fff" : "#16232F"};font-family:inherit;cursor:${view.submitted ? "default" : "pointer"};text-align:center`)}>
                      <div style={css("font-size:15px;font-weight:700")}>{value}</div>
                      <div style={css("font-size:11px")}>{thai}</div>
                      <div style={css("font-size:10px;opacity:.7")}>{english}</div>
                    </button>
                  );
                })}
                <button type="button" role="radio" aria-checked={!!answer?.notApplicable} aria-label="N/A ไม่มีข้อมูลพอ" disabled={view.submitted}
                  onClick={() => set(question.code, { rating: null, notApplicable: true })}
                  style={css(`min-width:92px;padding:7px 9px;border-radius:6px;border:1px dashed ${answer?.notApplicable ? "#0A2240" : "#C9D6E2"};`
                    + `background:${answer?.notApplicable ? "#E9EFF5" : "#fff"};color:#16232F;font-family:inherit;cursor:${view.submitted ? "default" : "pointer"}`)}>
                  <div style={css("font-size:13px;font-weight:700")}>N/A</div>
                  <div style={css("font-size:11px")}>ไม่มีข้อมูลพอ</div>
                </button>
              </div>
              <label style={css("display:flex;flex-direction:column;gap:3px")}>
                <span style={css("font-size:11.5px;color:" + (needsComment ? "#B42318" : "#7B8CA0"))}>
                  {needsComment ? "เหตุผล (จำเป็นเมื่อให้คะแนนต่ำ)" : "ความเห็น (ถ้ามี)"}
                </span>
                <textarea aria-label={`ความเห็นข้อ ${index + 1}`} value={answer?.comment ?? ""} disabled={view.submitted} rows={2} maxLength={2000}
                  onChange={(e) => set(question.code, { comment: e.target.value })}
                  style={css("border:1px solid #C9D6E2;border-radius:6px;padding:7px 9px;font-family:inherit;font-size:13px;resize:vertical")} />
              </label>
            </section>
          );
        })}

        <section style={css(CARD + "display:flex;flex-direction:column;gap:8px")}>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css("font-size:13px;font-weight:650;color:#0A2240")}>ความเห็นเพิ่มเติม</span>
            <textarea aria-label="ความเห็นเพิ่มเติม" value={view.submitted ? view.comment : comment} disabled={view.submitted} rows={3} maxLength={4000}
              onChange={(e) => setComment(e.target.value)}
              style={css("border:1px solid #C9D6E2;border-radius:6px;padding:7px 9px;font-family:inherit;font-size:13px;resize:vertical")} />
          </label>
          {!view.submitted && (
            <>
              {problem && <div style={css("font-size:12.5px;color:#B42318")}>{problem}</div>}
              {missing.length > 0 && <div style={css("font-size:12px;color:#B45309")}>ยังตอบไม่ครบ {missing.length} ข้อ</div>}
              <button type="button" disabled={busy || missing.length > 0} onClick={() => void submit()}
                style={css(`height:42px;border:0;border-radius:6px;background:${busy || missing.length > 0 ? "#C3CFDB" : "#16794C"};color:#fff;font-size:14px;font-weight:700;`
                  + `font-family:inherit;cursor:${busy || missing.length > 0 ? "default" : "pointer"}`)}>
                {busy ? "กำลังส่ง…" : "ส่งแบบประเมิน (ส่งได้ครั้งเดียว)"}
              </button>
            </>
          )}
        </section>
      </div>
    </main>
  );
}
