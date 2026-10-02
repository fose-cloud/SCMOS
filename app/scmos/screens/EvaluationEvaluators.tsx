"use client";

import { useCallback, useEffect, useState } from "react";
import { apiFetch } from "../api";
import {
  INVITATION_STATE, bangkokTime, dayInput, evaluationLink, type CampaignView, type CarrierRow, type EvaluatorRow, type InvitationRow, type IssuedLink,
} from "../annualEvaluation";
import { StatCard } from "../StatCard";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";
import { Badge, CELL, EMPTY, HEAD, INPUT, LABEL, MONO, Notice, OUTLINE, PANEL, PRIMARY, SMALL } from "./ActionPlanParts";

/**
 * The people outside SCMOS who score a campaign's carriers, and their links (1 Oct 2026, Annual Evaluation Phase 7).
 * SCMOS sends no mail: a link is made here, copied, and sent by the admin from their own Outlook. A link's token is on
 * this screen once, as it is made; a lost one is replaced, never shown again.
 */
export function EvaluationEvaluators({ campaignId, view, carriers, manage, issued, setIssued, onToast }: {
  campaignId: number; view: CampaignView; carriers: CarrierRow[]; manage: boolean;
  /** Links made and not yet dismissed — kept by the campaign screen, so changing tab does not lose them. */
  issued: IssuedLink[]; setIssued: (update: (before: IssuedLink[]) => IssuedLink[]) => void;
  onToast: (message: string) => void;
}) {
  const [rows, setRows] = useState<EvaluatorRow[] | null>(null);
  const [failure, setFailure] = useState("");
  const [busy, setBusy] = useState(false);
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [department, setDepartment] = useState("");
  const [chosen, setChosen] = useState<number[]>([]);
  const [carrier, setCarrier] = useState("");
  const [expires, setExpires] = useState(dayInput(view.campaign.dueOn));
  const [showRevoked, setShowRevoked] = useState(false);

  const load = useCallback(async () => {
    try {
      const response = await apiFetch(`/api/annual-evaluations/${campaignId}/evaluators`, { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => null) as EvaluatorRow[] | { error?: string } | null;
      if (!response.ok || !Array.isArray(body)) throw new Error((body as { error?: string } | null)?.error ?? `เปิดรายชื่อผู้ประเมินไม่ได้ (${response.status})`);
      setRows(body); setFailure("");
    } catch (problem) {
      setFailure(problem instanceof Error ? problem.message : String(problem));
    }
  }, [campaignId]);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  /** One change; any link it made is kept on screen until dismissed. True when the API took it. */
  async function send(path: string, body: unknown): Promise<boolean> {
    if (busy) return false;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/annual-evaluations/${campaignId}${path}`, {
        method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body),
      });
      const reply = await response.json().catch(() => ({})) as { message?: string; error?: string; links?: IssuedLink[] };
      onToast(reply.message ?? reply.error ?? (response.ok ? "บันทึกแล้ว" : `ไม่สำเร็จ (${response.status})`));
      if (reply.links?.length) setIssued((before) => [...reply.links!, ...before]);
      await load();
      return response.ok;
    } finally { setBusy(false); }
  }

  async function copy(text: string, what: string) {
    try {
      await navigator.clipboard.writeText(text);
      onToast(`คัดลอก${what}แล้ว`);
    } catch {
      onToast("คัดลอกไม่ได้ — เลือกข้อความในช่องแล้วคัดลอกเอง");
    }
  }

  if (failure) return <Notice tone="#B45309">{failure}</Notice>;
  if (!rows) return <Notice tone="#7B8CA0">กำลังโหลด…</Notice>;

  const status = view.campaign.status;
  const canIssue = manage && ["data-preparation", "ready", "open"].includes(status);
  const canChange = manage && !["approved", "finalized", "archived"].includes(status);
  const departments = view.departments.filter((one) => one.enabled);
  const included = carriers.filter((one) => one.included);
  const live = rows.flatMap((row) => row.invitations).filter((one) => one.state !== "revoked");
  const count = (state: string) => live.filter((one) => one.state === state).length;
  const origin = typeof window === "undefined" ? "" : window.location.origin;

  return (
    <div style={css("display:flex;flex-direction:column;gap:10px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:10px")}>
        <StatCard label="ผู้ประเมิน" value={String(rows.length)} tone="#0A2240" />
        <StatCard label="ลิงก์ที่ใช้อยู่" value={String(live.length)} tone="#0A2240" />
        <StatCard label="ยังไม่ส่ง" value={String(count("pending"))} tone="#475569" />
        <StatCard label="เปิดแล้ว ยังไม่ตอบ" value={String(count("opened"))} tone="#8A6D0B" />
        <StatCard label="ตอบแล้ว" value={live.length ? `${count("submitted")} / ${live.length}` : "0"} tone="#16794C" />
        <StatCard label="หมดอายุ" value={String(count("expired"))} tone="#B42318" />
      </div>

      {issued.length > 0 && (
        <div style={css(PANEL + "border-left:4px solid #B45309;display:flex;flex-direction:column;gap:8px")}>
          <div style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
            <span style={css("font-weight:700;color:#8A4B08")}>ลิงก์ใหม่ {issued.length} ลิงก์ — แสดงครั้งเดียว</span>
            <span style={css("flex:1")} />
            <button type="button" style={css(OUTLINE)} onClick={() => void copy(issued.map((one) =>
              [one.evaluator, one.department, one.carrier, evaluationLink(origin, one.token), dayInput(one.expiresAt)].join("\t")).join("\n"), "ทุกลิงก์")}>
              คัดลอกทั้งหมด
            </button>
            <button type="button" style={css(OUTLINE)} onClick={() => {
              if (window.confirm("ปิดรายการนี้? ลิงก์จะไม่แสดงอีก")) setIssued(() => []);
            }}>ปิด</button>
          </div>
          <ZoomBox zoomable={false} capped={false}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["ผู้ประเมิน", "ผู้ขนส่ง", "ลิงก์", "หมดอายุ", ""].map((head) => <th key={head} style={css(HEAD)}>{head}</th>)}</tr></thead>
              <tbody>
                {issued.map((one) => {
                  const link = evaluationLink(origin, one.token);
                  return (
                    <tr key={one.invitationId}>
                      <td style={css(CELL)}><div style={css("font-weight:600")}>{one.evaluator}</div><div style={css(LABEL)}>{one.department}</div></td>
                      <td style={css(CELL)}>{one.carrier}</td>
                      <td style={css(CELL)}>
                        <input aria-label={`ลิงก์ ${one.evaluator} ${one.carrier}`} readOnly value={link} onFocus={(e) => e.target.select()}
                          style={css(INPUT + MONO + "width:100%;min-width:260px;font-size:11.5px")} />
                      </td>
                      <td style={css(CELL + MONO)}>{bangkokTime(one.expiresAt).slice(0, 10)}</td>
                      <td style={css(CELL + "white-space:nowrap")}>
                        <button type="button" style={css(SMALL)} onClick={() => void copy(link, "ลิงก์")}>คัดลอก</button>
                        <button type="button" disabled={busy} style={css(SMALL + "margin-left:4px")}
                          onClick={() => void send(`/invitations/${one.invitationId}/sent`, {})}>ส่งแล้ว</button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </ZoomBox>
        </div>
      )}

      {canChange && (
        <div style={css(PANEL + "display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap")}>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>ชื่อผู้ประเมิน</span>
            <input aria-label="ชื่อผู้ประเมิน" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} style={css(INPUT + "width:200px")} />
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>อีเมล</span>
            <input aria-label="อีเมลผู้ประเมิน" type="email" value={email} maxLength={254} onChange={(e) => setEmail(e.target.value)} style={css(INPUT + "width:220px")} />
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>แผนก</span>
            <select aria-label="แผนกผู้ประเมิน" value={department} onChange={(e) => setDepartment(e.target.value)} style={css(INPUT + "min-width:160px")}>
              <option value="">เลือกแผนก…</option>
              {departments.map((one) => <option key={one.departmentId} value={one.departmentId}>{one.name}</option>)}
            </select>
          </label>
          <button type="button" disabled={busy || !name.trim() || !department} style={css(PRIMARY)} onClick={async () => {
            if (await send("/evaluators", { name: name.trim(), email: email.trim(), departmentId: Number(department) })) { setName(""); setEmail(""); }
          }}>+ เพิ่มผู้ประเมิน</button>
        </div>
      )}

      {canIssue && (
        <div style={css(PANEL + "display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap")}>
          <span style={css(LABEL + "align-self:center")}>เลือกแล้ว {chosen.length} คน</span>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>ผู้ขนส่ง</span>
            <select aria-label="ผู้ขนส่งที่จะสร้างลิงก์" value={carrier} onChange={(e) => setCarrier(e.target.value)} style={css(INPUT + "min-width:220px")}>
              <option value="">ทุกรายที่ประเมิน ({included.length})</option>
              {included.map((one) => <option key={one.id} value={one.id}>{one.name}</option>)}
            </select>
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css(LABEL)}>ใช้ได้ถึง</span>
            <input aria-label="ลิงก์ใช้ได้ถึง" type="date" value={expires} onChange={(e) => setExpires(e.target.value)} style={css(INPUT)} />
          </label>
          <button type="button" disabled={busy || chosen.length === 0} style={css(PRIMARY)} onClick={async () => {
            if (await send("/invitations", { evaluators: chosen, carriers: carrier ? [Number(carrier)] : [], expiresOn: expires || null })) setChosen([]);
          }}>สร้างลิงก์</button>
        </div>
      )}

      <div style={css("display:flex;justify-content:flex-end")}>
        <label style={css(LABEL + "display:flex;gap:6px;align-items:center;cursor:pointer")}>
          <input type="checkbox" aria-label="แสดงลิงก์ที่ยกเลิก" checked={showRevoked} onChange={(e) => setShowRevoked(e.target.checked)} />
          แสดงลิงก์ที่ยกเลิก
        </label>
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <ZoomBox>
          <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
            <thead><tr>{[canIssue ? "เลือก" : "", "ผู้ประเมิน", "ผู้ขนส่ง", "สถานะ", "ใช้ได้ถึง", "ส่ง", "เปิด", "ตอบ", ""].map((head, index) => <th key={index} style={css(HEAD)}>{head}</th>)}</tr></thead>
            <tbody>
              {rows.map((row) => {
                const shownLinks = row.invitations.filter((one) => showRevoked || one.state !== "revoked");
                const answered = row.invitations.filter((one) => one.state === "submitted").length;
                const lines: (InvitationRow | null)[] = shownLinks.length ? shownLinks : [null];
                return lines.map((invitation, index) => (
                  <tr key={`${row.id}-${invitation?.id ?? 0}`} style={css(invitation?.state === "revoked" ? "opacity:.55" : "")}>
                    {index === 0 && (
                      <>
                        <td rowSpan={lines.length} style={css(CELL + "vertical-align:top")}>
                          {canIssue && <input type="checkbox" aria-label={`เลือก ${row.name}`} checked={chosen.includes(row.id)}
                            onChange={(e) => setChosen((all) => e.target.checked ? [...all, row.id] : all.filter((one) => one !== row.id))} />}
                        </td>
                        <td rowSpan={lines.length} style={css(CELL + "vertical-align:top")}>
                          <div style={css("font-weight:600")}>{row.name}</div>
                          <div style={css(LABEL)}>{row.department}{row.email ? ` · ${row.email}` : ""}</div>
                          <div style={css(LABEL + MONO)}>ตอบแล้ว {answered}</div>
                        </td>
                      </>
                    )}
                    {invitation ? <InvitationCells invitation={invitation} busy={busy} manage={canChange} issue={canIssue} send={send} />
                      : <td colSpan={7} style={css(CELL + "color:#94A3B8")}>ยังไม่มีลิงก์</td>}
                  </tr>
                ));
              })}
              {rows.length === 0 && <tr><td colSpan={9} style={css(EMPTY)}>ยังไม่มีผู้ประเมิน</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
    </div>
  );
}

function InvitationCells({ invitation, busy, manage, issue, send }: {
  invitation: InvitationRow; busy: boolean; manage: boolean; issue: boolean; send: (path: string, body: unknown) => Promise<boolean>;
}) {
  const state = INVITATION_STATE[invitation.state] ?? { label: invitation.state, tone: "#475569", background: "#F1F5F9" };
  const open = !["submitted", "revoked"].includes(invitation.state);
  const path = `/invitations/${invitation.id}`;
  return (
    <>
      <td style={css(CELL)}>{invitation.carrier}</td>
      <td style={css(CELL)} title={invitation.revokeReason || undefined}><Badge label={state.label} tone={state.tone} background={state.background} /></td>
      <td style={css(CELL + MONO)}>{bangkokTime(invitation.expiresAt).slice(0, 10)}</td>
      <td style={css(CELL + MONO + "font-size:11.5px")}>{bangkokTime(invitation.sentAt)}</td>
      <td style={css(CELL + MONO + "font-size:11.5px")}>{bangkokTime(invitation.openedAt)}</td>
      <td style={css(CELL + MONO + "font-size:11.5px")}>{bangkokTime(invitation.submittedAt)}</td>
      <td style={css(CELL + "white-space:nowrap")}>
        {manage && open && (
          <div style={css("display:flex;gap:4px;flex-wrap:wrap")}>
            {invitation.state === "pending" && <button type="button" disabled={busy} style={css(SMALL)} onClick={() => void send(`${path}/sent`, {})}>ส่งแล้ว</button>}
            <button type="button" disabled={busy} style={css(SMALL)} onClick={() => {
              const day = window.prompt("ใช้ได้ถึงวันที่ (YYYY-MM-DD)", bangkokTime(invitation.expiresAt).slice(0, 10));
              if (day) void send(`${path}/extend`, { expiresOn: day.trim() });
            }}>ขยายเวลา</button>
            {issue && <button type="button" disabled={busy} style={css(SMALL)} onClick={() => {
              if (window.confirm(`สร้างลิงก์ใหม่ให้ ${invitation.carrier}? ลิงก์เดิมจะใช้ไม่ได้อีก`)) void send(`${path}/renew`, {});
            }}>ลิงก์ใหม่</button>}
            <button type="button" disabled={busy} style={css(SMALL + "color:#B42318")} onClick={() => {
              const reason = window.prompt("เหตุผลที่ยกเลิกลิงก์");
              if (reason !== null) void send(`${path}/revoke`, { reason: reason.trim() });
            }}>ยกเลิก</button>
          </div>
        )}
      </td>
    </>
  );
}
