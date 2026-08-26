"use client";

import { useEffect, useState } from "react";
import { apiFetch } from "../api";
import { useRemembered } from "../pageCache";
import { stamp } from "./WorkflowPanel";
import { css } from "../theme";

/**
 * Every file the system holds, and where each one is in its ten-year life.
 *
 * The retention column is the reason this screen is worth having beyond a list:
 * it says what tier a file should be sitting in and whether it is near the end
 * of its retention — and there is no delete button, because there is no delete
 * anywhere in this system. Destroying a document is a decision a person records
 * and then carries out against the storage account.
 */

type Doc = {
  id: number; scope: string; folder: string; kind: string; fileName: string;
  contentType: string; sizeBytes: number; objectKey: string; expiryDate: string;
  note: string; jobKey: string; supplierId: number | null; caseId: number | null;
  year: string; customer: string; jobRef: string;
  uploadedBy: string; uploadedAt: string; expiring: boolean; expired: boolean;
};

type RetentionItem = {
  id: number; fileName: string; objectKey: string; scope: string; folder: string;
  uploadedAt: string; ageDays: number; tier: string; state: string;
};

type Policy = { hotDays: number; coolDays: number; retentionDays: number; automaticDeletion: boolean };

const TIER_TONE: Record<string, string> = { Hot: "#B42318", Cool: "#1D5FA8", Archive: "#7B8CA0" };
const STATE_TH: Record<string, string> = {
  keep: "เก็บต่อ", review: "ใกล้ครบกำหนด", "overdue-review": "เกินกำหนดทบทวน",
};

export function Documents({ canReview }: { canReview: boolean }) {
  const [docs, setDocs] = useRemembered<Doc[]>("documents");
  const [retention, setRetention] = useState<RetentionItem[]>([]);
  const [policy, setPolicy] = useRemembered<Policy>("documents.policy");
  const [query, setQuery] = useState("");
  const [folder, setFolder] = useState("All");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const [listResponse, retentionResponse] = await Promise.all([
        apiFetch("/api/documents", { headers: { accept: "application/json" } }),
        // Retention is a supervisor-only read, so a CS account gets a 403 here
        // and simply sees no tier column rather than an error.
        apiFetch("/api/documents/retention?all=true", { headers: { accept: "application/json" } }),
      ]);
      const list = listResponse.ok ? await listResponse.json() as Doc[] : null;
      const review = retentionResponse.ok
        ? await retentionResponse.json() as { items: RetentionItem[]; policy: Policy }
        : null;
      if (cancelled) return;
      setDocs((held) => list ?? held ?? []);
      if (review) { setRetention(review.items); setPolicy(review.policy); }
    })();
    return () => { cancelled = true; };
  }, [setDocs, setPolicy]);

  if (!docs) {
    return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:34px;text-align:center;font-size:12.5px;color:#94A3B8")}>กำลังโหลด…</div>;
  }

  const tiers = new Map(retention.map((item) => [item.id, item]));
  const wanted = query.trim().toLowerCase();
  const shown = docs
    .filter((doc) => folder === "All" || doc.folder === folder)
    .filter((doc) => !wanted || [doc.fileName, doc.customer, doc.jobRef, doc.objectKey, doc.kind]
      .some((field) => (field ?? "").toLowerCase().includes(wanted)));

  const folders = [...new Set(docs.map((doc) => doc.folder))].sort();

  // Marking a file unclear lives on Document Verification, where the person
  // looking at the file already is. Putting a second copy of that control here
  // would give the register two ways to say the same thing.

  return (
    <div style={css("display:flex;flex-direction:column;gap:14px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:11px")}>
        <Tile label="ไฟล์ทั้งหมด" value={docs.length} colour="#0A2240" />
        <Tile label="ใกล้หมดอายุ" value={docs.filter((d) => d.expiring).length} colour="#B45309" />
        <Tile label="หมดอายุแล้ว" value={docs.filter((d) => d.expired).length} colour="#B42318" />
        <Tile label="ถึงรอบทบทวน" value={retention.filter((r) => r.state !== "keep").length} colour="#7B8CA0" />
      </div>

      {policy && (
        <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;padding:12px 15px;font-size:12px;color:#5A6B7D;line-height:1.65")}>
          เก็บ {Math.round(policy.retentionDays / 365)} ปี ·
          Hot {Math.round(policy.hotDays / 365)} ปีแรก · Cool ถึงปีที่ {Math.round(policy.coolDays / 365)} · Archive จนครบกำหนด ·
          <b style={css("color:#0A2240")}> ไม่มีการลบอัตโนมัติ</b> — ครบกำหนดแล้วขึ้นรอให้คนตัดสิน
          {canReview ? " และคุณมีสิทธิ์ตัดสิน" : " โดยผู้มีสิทธิ์ระดับผู้จัดการขึ้นไป"}
        </div>
      )}

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:5px;overflow:hidden")}>
        <div style={css("padding:11px 16px;border-bottom:1px solid #E9EFF5;display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap")}>
          <label style={css("display:flex;flex-direction:column;gap:3px")}>
            <span style={css("font-size:11px;color:#7B8CA0")}>โฟลเดอร์</span>
            <select value={folder} onChange={(e) => setFolder(e.target.value)}
              style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 8px;font-size:12.5px;background:#fff;min-width:130px")}>
              <option value="All">ทั้งหมด</option>
              {folders.map((f) => <option key={f} value={f}>{f}</option>)}
            </select>
          </label>
          <label style={css("display:flex;flex-direction:column;gap:3px;flex:1;min-width:200px")}>
            <span style={css("font-size:11px;color:#7B8CA0")}>ค้นหา ชื่อไฟล์ / ลูกค้า / งาน / พาธ</span>
            <input value={query} onChange={(e) => setQuery(e.target.value)}
              style={css("height:30px;border:1px solid #C9D6E2;border-radius:4px;padding:0 10px;font-size:12.5px")} />
          </label>
          <div style={css("font-size:12.5px;color:#465A6E")}><b style={css("color:#0A2240")}>{shown.length}</b> ไฟล์</div>
        </div>

        {shown.length === 0 ? (
          <div style={css("padding:30px;text-align:center;font-size:12.5px;color:#94A3B8")}>
            ยังไม่มีไฟล์ — อัปโหลดได้จากหน้า CAR/PAR, Supplier หรือ Document Verification
          </div>
        ) : (
          <div style={css("overflow-x:auto")}>
            <table style={css("width:100%;border-collapse:collapse;font-size:12.5px")}>
              <thead><tr>{["ไฟล์", "ที่เก็บ", "ผูกกับ", "ขนาด", "อายุ / ชั้น", "หมดอายุ", "อัปโหลดโดย"].map((h) => (
                <th key={h} style={css("position:sticky;top:0;background:#F8FAFC;padding:8px 12px;text-align:left;font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;color:#7B8CA0;font-weight:600;border-bottom:1px solid #E9EFF5;white-space:nowrap")}>{h}</th>
              ))}</tr></thead>
              <tbody>
                {shown.map((doc) => {
                  const item = tiers.get(doc.id);
                  return (
                    <tr key={doc.id} style={css("border-bottom:1px solid #F1F5F9;vertical-align:top")}>
                      <td style={CELL_S}>
                        <a href={`/api/documents/${doc.id}/content`} target="_blank" rel="noreferrer"
                          style={css("color:#0A5FA8;text-decoration:none;word-break:break-all")}>{doc.fileName}</a>
                        {doc.note && <div style={css("font-size:11px;color:#94A3B8;margin-top:2px")}>{doc.note}</div>}
                      </td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:10.5px;color:#7B8CA0;max-width:280px;word-break:break-all")}>
                        {doc.objectKey}
                      </td>
                      <td style={css(CELL + ";font-size:11.5px;color:#5A6B7D;white-space:nowrap")}>
                        {doc.customer || "—"}
                        {doc.jobRef && <div style={css("font-family:ui-monospace,monospace;font-size:10.5px;color:#94A3B8")}>{doc.jobRef}</div>}
                      </td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;white-space:nowrap;color:#7B8CA0")}>{size(doc.sizeBytes)}</td>
                      <td style={css(CELL + ";white-space:nowrap")}>
                        {item ? (
                          <>
                            <span style={css(`font-size:10.5px;font-weight:700;padding:2px 7px;border-radius:3px;color:#fff;background:${TIER_TONE[item.tier] ?? "#7B8CA0"}`)}>
                              {item.tier}
                            </span>
                            <div style={css("font-size:11px;color:" + (item.state === "keep" ? "#94A3B8" : "#B45309") + ";margin-top:3px")}>
                              {item.ageDays} วัน · {STATE_TH[item.state] ?? item.state}
                            </div>
                          </>
                        ) : "—"}
                      </td>
                      <td style={css(CELL + ";font-family:ui-monospace,monospace;font-size:11.5px;white-space:nowrap;color:" +
                        (doc.expired ? "#B42318" : doc.expiring ? "#B45309" : "#7B8CA0"))}>
                        {doc.expiryDate || "—"}
                      </td>
                      <td style={css(CELL + ";font-size:11.5px;color:#94A3B8;white-space:nowrap")}>
                        {doc.uploadedBy}
                        <div style={css("font-size:10.5px")}>{stamp(doc.uploadedAt)}</div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}

const CELL = "padding:8px 12px";
const CELL_S = css(CELL);

function size(bytes: number) {
  return bytes >= 1048576 ? (bytes / 1048576).toFixed(1) + " MB" : Math.max(1, Math.round(bytes / 1024)) + " KB";
}

function Tile({ label, value, colour }: { label: string; value: number; colour: string }) {
  return (
    <div style={css(`background:#fff;border-top:3px solid ${colour};border-right:1px solid #D8E0E8;border-bottom:1px solid #D8E0E8;border-left:1px solid #D8E0E8;border-radius:4px;padding:11px 14px 13px`)}>
      <div style={css("font-size:10.5px;letter-spacing:.05em;text-transform:uppercase;color:#7B8CA0;font-weight:600")}>{label}</div>
      <div style={css(`font-family:ui-monospace,monospace;font-size:24px;font-weight:600;line-height:1.25;margin-top:2px;color:${colour}`)}>{value.toLocaleString()}</div>
    </div>
  );
}
