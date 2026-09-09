"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { css } from "../theme";
import {
  VIEWS, confidenceLabel, linkLabel, linkSummary, linkTone, matchedOnLabel,
  sizeLabel, statusLabel, statusTone, whenLabel,
} from "../mailInbox";

/**
 * The Communication Center — the shared mailbox, and which job each message is
 * about.
 *
 * <b>The work is the middle column.</b> A message the rules were certain about
 * is already attached and needs nobody; one they were unsure about is offered
 * with the reason, and somebody says yes or no. So the screen opens on what is
 * waiting rather than on everything, and the identifiers the extractor read are
 * shown beside the suggestion — the first thing anybody checks is whether the
 * number the machine matched on is the number they can see in the mail.
 *
 * Reading is ViewMailbox, which operators hold. Deciding is EditAnyJob, because
 * it changes what the register says about a job — so the buttons are hidden
 * rather than offered and refused.
 */

const LABEL = "font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600";
const CARD = "background:#fff;border:1px solid #D8E0E8;border-radius:5px";
const BUTTON = "height:28px;padding:0 12px;border-radius:4px;font-size:12px;font-family:inherit;cursor:pointer;border:1px solid";
const TONES: Record<string, string> = {
  amber: "background:#FEF6E7;color:#8A5B00;border:1px solid #F2D9A7",
  red: "background:#FDECEC;color:#9B1C1C;border:1px solid #F5C2C2",
  gray: "background:#F1F5F9;color:#475569;border:1px solid #DCE3EA",
  green: "background:#E9F6EE;color:#186A3B;border:1px solid #BFE3CD",
};

const chip = (tone: string) =>
  `${TONES[tone] ?? TONES.gray};display:inline-block;padding:1px 7px;border-radius:9px;font-size:10.5px;font-weight:600;white-space:nowrap`;

type Link = {
  jobKey: string; status: string; confidence: number;
  matchedOn: string; matchedValue?: string; confirmedBy?: string;
};

type Row = {
  id: number; subject: string; fromAddress: string; fromName: string;
  receivedAt: string; hasAttachments: boolean; status: string; error: string;
  links: Link[];
};

type Inbox = { total: number; waiting: number; view: string; messages: Row[] };

type Detail = {
  id: number; mailbox: string; subject: string; fromAddress: string; fromName: string;
  receivedAt: string; status: string; error: string;
  bodyText: string; bodyHtml: string;
  participants: { kind: string; address: string; displayName: string }[];
  entities: { kind: string; value: string; inSubject: boolean; wellFormed: boolean }[];
  links: Link[];
  attachments: { id: number; fileName: string; contentType: string; sizeBytes: number }[];
};

export function Outlook({ canDecide, onToast }: {
  /** Whether this account may say which job a message belongs to. */
  canDecide: boolean;
  onToast: (message: string) => void;
}) {
  const [view, setView] = useState(VIEWS[0].key);
  const [inbox, setInbox] = useState<Inbox | null>(null);
  const [openId, setOpenId] = useState<number | null>(null);
  const [detail, setDetail] = useState<Detail | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  /*
   * Bumped to ask for the list again — after a decision, which changes both the
   * message's status and therefore which view it belongs in.
   *
   * A counter rather than a callback the effect calls. The fetch lives inside
   * the effect so that nothing it does can run synchronously with the render,
   * which is the shape the detail effect below already has.
   */
  const [refresh, setRefresh] = useState(0);

  useEffect(() => {
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch(`/api/mail?view=${encodeURIComponent(view)}&per=50`,
          { headers: { accept: "application/json" } });
        if (!alive) return;
        if (!response.ok) {
          // The commonest reason this screen is empty is that nobody has
          // granted Mail.Read yet, and the second is that this account may not
          // read it. Both are worth saying rather than showing an empty list.
          setError(response.status === 403
            ? "บัญชีนี้ไม่มีสิทธิ์อ่านศูนย์รวมการติดต่อ"
            : `อ่านรายการไม่สำเร็จ (${response.status})`);
          setInbox(null);
          return;
        }
        const page = await response.json();
        if (!alive) return;
        setError("");
        setInbox(page);
      } catch {
        if (alive) setError("ต่อกับระบบไม่ได้");
      }
    })();
    return () => { alive = false; };
  }, [view, refresh]);

  // Only ever fetches. Clearing the panel belongs to `open`, which is what
  // closing a row actually is — doing it here would be a render that exists to
  // undo the last one.
  useEffect(() => {
    if (openId === null) return;
    let alive = true;
    (async () => {
      try {
        const response = await apiFetch(`/api/mail/${openId}`, { headers: { accept: "application/json" } });
        if (alive && response.ok) setDetail(await response.json());
      } catch { /* the list still shows what it had */ }
    })();
    return () => { alive = false; };
  }, [openId]);

  /** Open a message, or close the one that is open. */
  function open(id: number) {
    const closing = id === openId;
    setDetail(null);
    setOpenId(closing ? null : id);
  }

  async function decide(jobKey: string, status: string) {
    if (openId === null || busy) return;
    setBusy(true);
    try {
      const response = await apiFetch(`/api/mail/${openId}/decide`, {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ jobKey, status }),
      });
      const reply = await response.json().catch(() => ({}));
      if (!response.ok) {
        onToast(reply.error ?? `บันทึกไม่สำเร็จ (${response.status})`);
        return;
      }
      onToast(status === "CONFIRMED" ? `ยืนยันว่าเป็นงาน ${jobKey}` : `ปฏิเสธการจับคู่กับ ${jobKey}`);
      // Both halves, because the decision changes the message's own status and
      // therefore which view it belongs in.
      const again = await apiFetch(`/api/mail/${openId}`, { headers: { accept: "application/json" } });
      if (again.ok) setDetail(await again.json());
      setRefresh((held) => held + 1);
    } finally {
      setBusy(false);
    }
  }

  const rows = inbox?.messages ?? [];

  return (
    <div style={css("padding:14px 16px 24px")}>
      <div style={css("display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin-bottom:12px")}>
        {VIEWS.map((one) => (
          <button
            key={one.key}
            type="button"
            onClick={() => { setView(one.key); setDetail(null); setOpenId(null); }}
            style={css(`${BUTTON};${one.key === view
              ? "background:#0A2240;color:#fff;border-color:#0A2240"
              : "background:#fff;color:#0A2240;border-color:#D3DBE3"}`)}
          >
            {one.th}
            {one.key === "WAITING" && (inbox?.waiting ?? 0) > 0 ? ` · ${inbox?.waiting}` : ""}
          </button>
        ))}
        <span style={css("flex:1")} />
        <span style={css("font-size:11.5px;color:#64748B")}>
          {inbox ? `${rows.length} จาก ${inbox.total} ฉบับ` : ""}
        </span>
      </div>

      {error && (
        <div style={css(`${CARD};padding:12px 14px;margin-bottom:12px;color:#9B1C1C`)}>{error}</div>
      )}

      {!error && rows.length === 0 && (
        /* Empty because nothing has arrived, which until the Entra grant is made
           is the expected state — so it says which of the two it is rather than
           leaving somebody to wonder whether the screen is broken. */
        <div style={css(`${CARD};padding:16px 18px;color:#475569;font-size:12.5px;line-height:1.7`)}>
          <div style={css("font-weight:600;color:#0A2240;margin-bottom:4px")}>ยังไม่มีอีเมลในระบบ</div>
          อีเมลจะเข้ามาเองเมื่อผู้ดูแลระบบให้สิทธิ์ <code>Mail.Read</code> กับ managed identity ของ API
          และตั้งค่า <code>Graph__Mailboxes</code> แล้ว — ตรวจสถานะได้ที่เมนู Integrations
        </div>
      )}

      <div style={css("display:grid;grid-template-columns:minmax(320px,1fr) minmax(0,1.25fr);gap:14px;align-items:start")}>
        {rows.length > 0 && (
          <div style={css(`${CARD};overflow:hidden`)}>
            {rows.map((row) => (
              <button
                key={row.id}
                type="button"
                onClick={() => open(row.id)}
                style={css("display:block;width:100%;text-align:left;font-family:inherit;cursor:pointer;"
                  + `padding:9px 12px;border:none;border-bottom:1px solid #EDF1F5;`
                  + `background:${row.id === openId ? "#F4F8FC" : "#fff"}`)}
              >
                <div style={css("display:flex;gap:8px;align-items:baseline")}>
                  <span style={css("font-size:12.5px;font-weight:600;color:#0A2240;flex:1;"
                    + "overflow:hidden;text-overflow:ellipsis;white-space:nowrap")}>
                    {row.subject || "(ไม่มีหัวเรื่อง)"}
                  </span>
                  <span style={css("font-size:10.5px;color:#7B8CA0;white-space:nowrap")}>
                    {whenLabel(row.receivedAt)}
                  </span>
                </div>
                <div style={css("display:flex;gap:8px;align-items:center;margin-top:3px;flex-wrap:wrap")}>
                  <span style={css("font-size:11.5px;color:#475569")}>
                    {row.fromName || row.fromAddress || "—"}
                  </span>
                  <span style={css(chip(statusTone(row.status)))}>{statusLabel(row.status)}</span>
                  <span style={css("font-size:11px;color:#64748B")}>{linkSummary(row.links)}</span>
                  {row.hasAttachments && <span style={css("font-size:11px;color:#64748B")}>📎</span>}
                </div>
              </button>
            ))}
          </div>
        )}

        {detail && (
          <div style={css(`${CARD};padding:14px 16px`)}>
            <div style={css("font-size:14px;font-weight:600;color:#0A2240;margin-bottom:2px")}>
              {detail.subject || "(ไม่มีหัวเรื่อง)"}
            </div>
            <div style={css("font-size:11.5px;color:#64748B;margin-bottom:12px")}>
              {detail.fromName ? `${detail.fromName} · ` : ""}{detail.fromAddress}
              {detail.mailbox ? ` → ${detail.mailbox}` : ""}
            </div>

            <div style={css(LABEL)}>งานที่จับคู่</div>
            <div style={css("margin:6px 0 14px")}>
              {detail.links.length === 0 && (
                <div style={css("font-size:12px;color:#64748B")}>ยังไม่มีการจับคู่กับงานใด</div>
              )}
              {detail.links.map((link) => (
                <div key={link.jobKey} style={css("display:flex;gap:8px;align-items:center;flex-wrap:wrap;"
                  + "padding:7px 0;border-bottom:1px solid #EDF1F5")}>
                  <span style={css("font-size:12.5px;font-weight:600;color:#0A2240")}>{link.jobKey}</span>
                  <span style={css(chip(linkTone(link.status)))}>{linkLabel(link.status)}</span>
                  <span style={css("font-size:11px;color:#64748B")}>
                    {matchedOnLabel(link.matchedOn)}
                    {link.matchedValue ? ` ${link.matchedValue}` : ""}
                    {" · "}
                    {confidenceLabel(link.confidence, link.matchedOn)}
                  </span>
                  {link.confirmedBy && (
                    <span style={css("font-size:10.5px;color:#7B8CA0")}>โดย {link.confirmedBy}</span>
                  )}
                  <span style={css("flex:1")} />
                  {/* Offered only to accounts that may act. A button that
                      collects a refusal after the thinking is done is worse
                      than no button. */}
                  {canDecide && link.status !== "CONFIRMED" && (
                    <button type="button" disabled={busy}
                      onClick={() => void decide(link.jobKey, "CONFIRMED")}
                      style={css(`${BUTTON};background:#0A6E3B;color:#fff;border-color:#0A6E3B`)}>
                      ยืนยัน
                    </button>
                  )}
                  {canDecide && link.status !== "REJECTED" && (
                    <button type="button" disabled={busy}
                      onClick={() => void decide(link.jobKey, "REJECTED")}
                      style={css(`${BUTTON};background:#fff;color:#9B1C1C;border-color:#F5C2C2`)}>
                      ไม่ใช่
                    </button>
                  )}
                </div>
              ))}
            </div>

            {/* What the extractor read. Shown beside the suggestion because the
                first thing anybody checks is whether the number the machine
                matched on is the number in front of them. */}
            <div style={css(LABEL)}>เลขที่อ่านได้จากอีเมล</div>
            <div style={css("margin:6px 0 14px;display:flex;gap:6px;flex-wrap:wrap")}>
              {detail.entities.length === 0 && (
                <span style={css("font-size:12px;color:#64748B")}>ไม่พบเลขอ้างอิงในอีเมลนี้</span>
              )}
              {detail.entities.map((one) => (
                <span key={`${one.kind}|${one.value}`} style={css(chip(one.wellFormed ? "gray" : "amber"))}>
                  {matchedOnLabel(one.kind)} {one.value}{one.inSubject ? " · หัวเรื่อง" : ""}
                </span>
              ))}
            </div>

            {detail.attachments.length > 0 && (
              <>
                <div style={css(LABEL)}>ไฟล์แนบ</div>
                <div style={css("margin:6px 0 14px;font-size:12px;color:#475569")}>
                  {detail.attachments.map((file) => (
                    <div key={file.id}>{file.fileName} · {sizeLabel(file.sizeBytes)}</div>
                  ))}
                </div>
              </>
            )}

            <div style={css(LABEL)}>เนื้อหา</div>
            <div style={css("margin-top:6px;font-size:12px;color:#334155;line-height:1.6;"
              + "white-space:pre-wrap;max-height:340px;overflow:auto;"
              + "border:1px solid #EDF1F5;border-radius:4px;padding:10px 12px")}>
              {/* The text, never the HTML. A shared mailbox receives whatever
                  anybody sends, and rendering a stranger's markup inside the
                  register is a decision nobody needs to make to read an arrival
                  notice. */}
              {detail.bodyText || "(ไม่มีเนื้อหาแบบข้อความ)"}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
