"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import { stamp } from "./WorkflowPanel";
import { css } from "../theme";

/**
 * What the assistant may do, and what is waiting for a person.
 *
 * The matrix is read from the API rather than written here, because the API is
 * where it is enforced. A screen that listed the permissions separately could
 * disagree with the gateway, and the version people read would be the one that
 * is not in force.
 */

type Tool = { name: string; agent: string; permission: string; description: string; enabled: boolean };

type Risk = {
  question: string; headline: string; basis: string;
  recommendedActions: string[];
  groups: {
    customer: string; shipments: number; reason: string; reasonTh: string;
    examples: { jobKey: string; reference: string; date: string; carrier: string }[];
  }[];
};
type Approval = {
  id: number; tool: string; agent: string; summary: string; payload: string; state: string;
  requestedBy: string; requestedAt: string;
  decidedBy: string; decidedAt: string | null; decisionNote: string; result: string;
};

const AGENT_TH: Record<string, string> = {
  operation: "งานปฏิบัติการ", document: "เอกสาร", kpi: "KPI",
  supplier: "ผู้ขนส่ง", safety: "ความปลอดภัย", management: "ผู้บริหาร",
};
const PERMISSION_TH: Record<string, string> = {
  allow: "ทำได้", approval: "ต้องอนุมัติ", deny: "ห้าม",
};
const PERMISSION_TONE: Record<string, string> = {
  allow: "#16794C", approval: "#B45309", deny: "#B42318",
};
const STATE_TH: Record<string, string> = {
  pending: "รออนุมัติ", approved: "อนุมัติแล้ว", rejected: "ปฏิเสธแล้ว", applied: "นำไปใช้แล้ว",
};
const STATE_TONE: Record<string, string> = {
  pending: "#B45309", approved: "#16794C", rejected: "#B42318", applied: "#0A2240",
};

export function Assistant({ canApprove, onToast, onOpenJob }: {
  canApprove: boolean; onToast: (m: string) => void; onOpenJob: (key: string) => void;
}) {
  const [tools, setTools] = useState<Tool[] | null>(null);
  const [forbidden, setForbidden] = useState<string[]>([]);
  const [approvals, setApprovals] = useState<Approval[]>([]);
  const [risk, setRisk] = useState<Risk | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    const [toolsResponse, queueResponse] = await Promise.all([
      apiFetch("/api/ai/tools", { headers: { accept: "application/json" } }),
      apiFetch("/api/ai/approvals", { headers: { accept: "application/json" } }),
    ]);
    if (toolsResponse.ok) {
      const body = await toolsResponse.json() as { tools: Tool[]; forbidden: string[] };
      setTools(body.tools);
      setForbidden(body.forbidden);
    } else {
      setTools([]);
    }
    setApprovals(queueResponse.ok ? await queueResponse.json() as Approval[] : []);
  }, []);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const [toolsResponse, queueResponse, riskResponse] = await Promise.all([
        apiFetch("/api/ai/tools", { headers: { accept: "application/json" } }),
        apiFetch("/api/ai/approvals", { headers: { accept: "application/json" } }),
        apiFetch("/api/risk", { headers: { accept: "application/json" } }),
      ]);
      const catalogue = toolsResponse.ok
        ? await toolsResponse.json() as { tools: Tool[]; forbidden: string[] }
        : { tools: [] as Tool[], forbidden: [] as string[] };
      const queue = queueResponse.ok ? await queueResponse.json() as Approval[] : [];
      const answer = riskResponse.ok ? await riskResponse.json() as Risk : null;
      if (cancelled) return;
      setTools(catalogue.tools);
      setForbidden(catalogue.forbidden);
      setApprovals(queue);
      setRisk(answer);
    })();
    return () => { cancelled = true; };
  }, []);

  async function post(path: string, body: unknown) {
    if (busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/ai${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(reply.message ?? reply.error ?? "ทำรายการไม่สำเร็จ");
      await load();
    } finally { setBusy(false); }
  }

  if (!tools) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const pending = approvals.filter((a) => a.state === "pending");
  const agents = [...new Set(tools.map((tool) => tool.agent))];

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="เครื่องมือที่ทำได้เลย" value={tools.filter((t) => t.permission === "allow").length} colour="#16794C" />
        <Tile label="ต้องมีคนอนุมัติ" value={tools.filter((t) => t.permission === "approval").length} colour="#B45309" />
        <Tile label="รออนุมัติอยู่" value={pending.length} colour="#B42318" />
        <Tile label="ห้ามเด็ดขาด" value={forbidden.length} colour="#0A2240" />
      </div>

      {risk && <RiskPanel risk={risk} onOpen={onOpenJob} />}

      <div style={css("background:#FFF8F0;border:1px solid #F0D8B8;border-radius:5px;padding:12px 15px;font-size:12px;color:#7A4A16;line-height:1.65")}>
        การลบข้อมูลไม่ได้อยู่ในรายการเครื่องมือเลย และไม่มีเส้นทางในโค้ดที่จะเรียกได้ —
        ชื่อที่ขึ้นต้นด้วย <code style={css("font-family:ui-monospace,monospace")}>delete</code> หรือ{" "}
        <code style={css("font-family:ui-monospace,monospace")}>drop</code> ถูกปฏิเสธที่ประตูก่อนถึงตัวเครื่องมือ
        ข้อห้ามนี้จึงเป็นคุณสมบัติของระบบ ไม่ใช่ประโยคใน prompt ที่แก้ทีหลังได้
      </div>

      {/* ---------------------------------------------------- approvals queue */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          คิวรออนุมัติ
          {!canApprove && <span style={css("font-weight:400;color:#94A3B8;margin-left:8px")}>· ดูได้อย่างเดียว — อนุมัติได้เฉพาะระดับหัวหน้างานขึ้นไป</span>}
        </div>
        {approvals.length === 0 ? (
          <div style={css("padding:26px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ยังไม่มีรายการ — ผู้ช่วยยังไม่ได้เสนอการเปลี่ยนแปลงใด
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["เครื่องมือ", "เรื่อง", "ผู้ขอ", "เมื่อ", "สถานะ", ""].map((h) => (
                <th key={h} style={css("background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {approvals.map((row) => (
                  <tr key={row.id} style={css("border-bottom:1px solid #F1F5F9")}>
                    <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;color:#0A2240")}>{row.tool}</td>
                    <td style={css(CELL + ";max-width:340px")}>
                      <div>{row.summary}</div>
                      {row.payload && row.payload !== "{}" && (
                        <div style={css("font-family:ui-monospace,monospace;font-size:11px;color:#7B8CA0;margin-top:3px;word-break:break-all")}>{row.payload}</div>
                      )}
                      {row.decisionNote && <div style={css("font-size:11px;color:#7B8CA0;margin-top:3px")}>หมายเหตุ: {row.decisionNote}</div>}
                    </td>
                    <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D")}>{row.requestedBy}</td>
                    <td style={css(CELL + ";font-size:11.5px;color:#7B8CA0;white-space:nowrap")}>{stamp(row.requestedAt)}</td>
                    <td style={CELL_S}>
                      <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${STATE_TONE[row.state] ?? "#7B8CA0"}`)}>
                        {STATE_TH[row.state] ?? row.state}
                      </span>
                      {row.decidedBy && <div style={css("font-size:11px;color:#7B8CA0;margin-top:3px")}>{row.decidedBy}</div>}
                    </td>
                    <td style={css(CELL + ";white-space:nowrap")}>
                      {canApprove && row.state === "pending" && (
                        <span style={css("display:flex;gap:5px")}>
                          <Mini label="อนุมัติ" tone="#16794C" busy={busy}
                            onClick={() => void post(`/approvals/${row.id}`, { approved: true, note: "" })} />
                          <Mini label="ปฏิเสธ" tone="#B42318" busy={busy}
                            onClick={() => void post(`/approvals/${row.id}`, { approved: false, note: "" })} />
                        </span>
                      )}
                      {canApprove && row.state === "approved" && (
                        <Mini label="นำไปใช้" tone="#0A2240" busy={busy}
                          onClick={() => void post(`/approvals/${row.id}/applied`, { result: "นำไปใช้จากหน้าผู้ช่วย" })} />
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ------------------------------------------------------- tool matrix */}
      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;font-size:12.5px;font-weight:650;color:#0A2240")}>
          สิทธิ์ของผู้ช่วย · {tools.length} เครื่องมือ
        </div>
        {agents.map((agent) => (
          <div key={agent}>
            <div style={css("padding:7px 16px;background:#F8FAFC;border-bottom:1px solid #E9EFF5;font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>
              {AGENT_TH[agent] ?? agent}
            </div>
            {tools.filter((tool) => tool.agent === agent).map((tool) => (
              <div key={tool.name} style={css("padding:9px 16px;border-bottom:1px solid #F1F5F9;display:flex;gap:12px;align-items:baseline")}>
                <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;white-space:nowrap;background:${PERMISSION_TONE[tool.permission] ?? "#7B8CA0"}`)}>
                  {PERMISSION_TH[tool.permission] ?? tool.permission}
                </span>
                <span style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:#0A2240;min-width:170px")}>{tool.name}</span>
                <span style={css("font-size:12px;color:#5A6B7D")}>{tool.description}</span>
              </div>
            ))}
          </div>
        ))}
        <div style={css("padding:7px 16px;background:#F8FAFC;border-bottom:1px solid #E9EFF5;font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>
          ห้ามเด็ดขาด — ไม่มีเครื่องมือเหล่านี้อยู่จริง
        </div>
        <div style={css("padding:10px 16px;display:flex;gap:6px;flex-wrap:wrap")}>
          {forbidden.map((name) => (
            <span key={name} style={css("font-family:ui-monospace,monospace;font-size:11.5px;color:#B42318;background:#FEF3F2;border:1px solid #F5C9C4;border-radius:3px;padding:2px 7px")}>
              {name}
            </span>
          ))}
        </div>
      </div>
    </div>
  );
}

const CELL = "padding:8px 12px;vertical-align:top";
const CELL_S = css(CELL);

/**
 * "วันนี้มีงานอะไรเสี่ยงบ้าง" — the answer, grouped by customer.
 *
 * The basis line is not decoration. This is computed from the register's own
 * rules, and saying so is what stops the screen implying a language model
 * reviewed the day. An assistant people believe read their work, when it did
 * not, is one they will eventually trust with something it never looked at.
 */
function RiskPanel({ risk, onOpen }: { risk: Risk; onOpen: (key: string) => void }) {
  return (
    <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
      <div style={css("padding:13px 16px;border-bottom:1px solid #E9EFF5")}>
        <div style={css("font-size:12.5px;color:#7B8CA0")}>{risk.question}</div>
        <div style={css("font-size:15px;font-weight:650;color:#0A2240;margin-top:3px")}>{risk.headline}</div>
      </div>

      {risk.groups.length > 0 && (
        <div style={css("display:grid;grid-template-columns:repeat(auto-fill,minmax(250px,1fr));gap:1px;background:#E9EFF5")}>
          {risk.groups.map((group) => (
            <div key={group.customer + group.reason} style={css("background:#fff;padding:11px 15px")}>
              <div style={css("display:flex;justify-content:space-between;align-items:baseline;gap:8px")}>
                <span style={css("font-weight:650;color:#0A2240;font-size:12.5px")}>{group.customer}</span>
                <span style={css("font-family:ui-monospace,monospace;font-size:15px;font-weight:600;color:#B45309")}>{group.shipments}</span>
              </div>
              <div style={css("font-size:11.5px;color:#B42318;margin-top:2px")}>{group.reasonTh}</div>
              <div style={css("margin-top:6px;display:flex;flex-wrap:wrap;gap:4px")}>
                {group.examples.map((job) => (
                  <button key={job.jobKey} onClick={() => onOpen(job.jobKey)}
                    title={`${job.date}${job.carrier ? " · " + job.carrier : ""}`}
                    style={css("border:1px solid #D8E0E8;background:#F8FAFC;color:#5A6B7D;border-radius:3px;padding:1px 6px;font-family:ui-monospace,monospace;font-size:10.5px;cursor:pointer")}>
                    {job.reference || job.jobKey}
                  </button>
                ))}
              </div>
            </div>
          ))}
        </div>
      )}

      <div style={css("padding:12px 16px;border-top:1px solid #E9EFF5")}>
        <div style={css("font-size:11px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600;margin-bottom:5px")}>
          สิ่งที่ควรทำ
        </div>
        {risk.recommendedActions.map((action, index) => (
          <div key={action} style={css("font-size:12.5px;color:#16232F;padding:2px 0")}>{index + 1}. {action}</div>
        ))}
        <div style={css("font-size:11px;color:#94A3B8;margin-top:9px;line-height:1.6")}>{risk.basis}</div>
      </div>
    </div>
  );
}

function Mini({ label, tone, busy, onClick }: { label: string; tone: string; busy: boolean; onClick: () => void }) {
  return (
    <button onClick={onClick} disabled={busy}
      style={css(`height:26px;padding:0 10px;border:1px solid ${tone};background:#fff;color:${tone};border-radius:4px;font-size:11.5px;font-weight:600;cursor:pointer`)}
    >{label}</button>
  );
}

function Tile({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
    </div>
  );
}
