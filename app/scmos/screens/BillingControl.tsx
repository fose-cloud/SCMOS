"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

export type BillingDocument = { id: number; fileName: string; kind: string; uploadedAt: string };
export type BillingInvoice = {
  id: number; invoiceNumber: string; invoiceDate: string; currency: string;
  subtotal: number; taxAmount: number; totalAmount: number; status: string; updatedAt: string;
  reviewCycle: number; reviewSubmittedAt: string; reviewDecidedAt: string; onlineApprovedAt: string;
  reviewAgeMinutes: number | null; reviewDecisionMinutes: number | null;
  reviewEvents: BillingReviewEvent[];
  validationResults: BillingValidation[];
  additionalCharges: BillingCharge[];
};
export type BillingReviewEvent = { id: number; cycle: number; action: string; fromStatus: string;
  toStatus: string; reasonCode: string; remark: string; actorName: string; at: string };
export type BillingCharge = { id: number; chargeType: string; requestedAmount: number;
  approvedAmount: number | null; currency: string; reason: string; status: string };
export type BillingValidation = { sequence: number; step: string; code: string; category: string;
  blocking: boolean; message: string; expectedAmount: number | null; actualAmount: number | null;
  currency: string; evidenceType: string; evidenceId: string; evidenceVersion: string;
  ruleSource: string; effectiveDate: string };
export type BillingCase = {
  id: number; jobKey: string; jobCode: string; customer: string; category: string;
  supplierId: number; supplier: string; status: string; deliveryCompletedAt: string;
  slaRuleCode: string; slaStartDay: string; slaTargetWorkingDays: number | null;
  slaStartDate: string; slaDueDate: string; slaState: string; daysRemaining: number | null;
  slaIssueCode: string; slaIssue: string; invoice: BillingInvoice | null; documents: BillingDocument[];
};

type Filter = "ALL" | "WAITING_CARRIER_SUBMISSION" | "DRAFT" | "SUBCON_REVIEW" | "RETURNED" | "DISPUTED" | "AWAITING_ORIGINAL" | "OVERDUE";

const returnReasons = ["MISSING_DOCUMENT", "WRONG_RATE", "WRONG_VAT", "WRONG_RECEIPT", "UNAPPROVED_CHARGE", "WRONG_JOB", "DUPLICATE", "INVOICE_DATA_ERROR", "OTHER"];

export function BillingControl({ onToast }: { onToast: (message: string) => void }) {
  const [items, setItems] = useState<BillingCase[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<Filter>("ALL");
  const [search, setSearch] = useState("");
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [acting, setActing] = useState(false);
  const [reasonCode, setReasonCode] = useState("MISSING_DOCUMENT");
  const [remark, setRemark] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const response = await apiFetch("/api/carrier-billing/cases", { headers: { accept: "application/json" } });
      const body = await response.json().catch(() => ({})) as { items?: BillingCase[]; error?: string };
      if (!response.ok) { onToast(body.error ?? `เปิด Billing Control ไม่สำเร็จ (${response.status})`); return; }
      setItems(body.items ?? []);
    } finally { setLoading(false); }
  }, [onToast]);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return items.filter((item) => {
      if (filter === "OVERDUE" && item.slaState !== "OVERDUE") return false;
      if (filter !== "ALL" && filter !== "OVERDUE" && item.status !== filter) return false;
      return !needle || [item.jobCode, item.customer, item.supplier, item.invoice?.invoiceNumber ?? ""]
        .some((value) => value.toLowerCase().includes(needle));
    });
  }, [filter, items, search]);

  const waiting = items.filter((item) => item.status === "WAITING_CARRIER_SUBMISSION").length;
  const drafts = items.filter((item) => item.status === "DRAFT").length;
  const reviews = items.filter((item) => item.status === "SUBCON_REVIEW").length;
  const overdue = items.filter((item) => item.slaState === "OVERDUE").length;
  const selected = items.find((item) => item.id === selectedId) ?? null;

  async function review(action: "APPROVE_ONLINE" | "RETURN_TO_CARRIER" | "RAISE_DISPUTE") {
    const invoiceId = selected?.invoice?.id;
    if (!invoiceId || acting) return;
    if (action !== "APPROVE_ONLINE" && reasonCode === "OTHER" && !remark.trim()) {
      onToast("กรุณาระบุรายละเอียดเมื่อเลือกเหตุผล OTHER"); return;
    }
    setActing(true);
    try {
      const response = await apiFetch(`/api/carrier-billing/review/${invoiceId}`, {
        method: "POST", headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ action, reasonCode: action === "APPROVE_ONLINE" ? "" : reasonCode, remark }),
      });
      const body = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(body.message ?? body.error ?? (response.ok ? "บันทึกผล Review แล้ว" : `บันทึกไม่สำเร็จ (${response.status})`));
      if (response.ok) { setRemark(""); await load(); }
    } finally { setActing(false); }
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px")}>
        <Metric label="Billing Eligible Jobs" value={items.length} tone="#0A5C97" />
        <Metric label="รอผู้ขนส่งวางบิล" value={waiting} tone="#B45309" />
        <Metric label="Invoice Draft" value={drafts} tone="#16794C" />
        <Metric label="รอตรวจ Subcontract" value={reviews} tone="#7C3AED" />
        <Metric label="เกิน SLA" value={overdue} tone="#B42318" />
      </div>

      <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:6px;overflow:hidden")}>
        <div style={css("padding:12px 14px;border-bottom:1px solid #E7EDF3;display:flex;gap:8px;align-items:center;flex-wrap:wrap")}>
          <div style={css("font-size:13px;font-weight:700;color:#0A2240;margin-right:auto")}>Billing Cases</div>
          <input value={search} onChange={(event) => setSearch(event.target.value)}
            placeholder="ค้นหา Job, ลูกค้า, ผู้ขนส่ง, Invoice"
            style={css("height:31px;width:min(310px,100%);padding:0 9px;border:1px solid #CAD5E0;border-radius:4px;font:inherit;font-size:11.5px")} />
          <select value={filter} onChange={(event) => setFilter(event.target.value as Filter)}
            style={css("height:31px;padding:0 8px;border:1px solid #CAD5E0;border-radius:4px;background:#fff;font:inherit;font-size:11.5px")}>
            <option value="ALL">ทั้งหมด</option><option value="WAITING_CARRIER_SUBMISSION">รอผู้ขนส่ง</option>
            <option value="DRAFT">Draft</option><option value="SUBCON_REVIEW">รอตรวจ Subcontract</option>
            <option value="RETURNED">ส่งคืนแก้ไข</option><option value="DISPUTED">ข้อพิพาท</option>
            <option value="AWAITING_ORIGINAL">รอต้นฉบับ</option><option value="OVERDUE">เกิน SLA</option>
          </select>
          <button onClick={() => void load()} disabled={loading}
            style={css("height:31px;padding:0 12px;border:1px solid #0A5C97;background:#fff;color:#0A5C97;border-radius:4px;font:inherit;font-size:11.5px;font-weight:600;cursor:pointer")}>รีเฟรช</button>
        </div>
        <ZoomBox capped={false}>
          <table style={css("width:100%;min-width:1180px;border-collapse:collapse;font-size:11.5px")}>
            <thead><tr style={css("background:#F4F7FA;color:#64748B;text-align:left")}>
              {["JOB / ลูกค้า", "ผู้ขนส่ง", "DELIVERY COMPLETE", "SLA", "กำหนดวางบิล", "สถานะ", "INVOICE", "ยอดรวม", "เอกสาร"].map((name) =>
                <th key={name} style={css("padding:9px 10px;border-bottom:1px solid #D8E0E8;font-size:10px;letter-spacing:.04em")}>{name}</th>)}
            </tr></thead>
            <tbody>
              {shown.map((item) => <tr key={item.id} onClick={() => setSelectedId(item.id)}
                style={css(`border-bottom:1px solid #E9EFF5;vertical-align:top;cursor:pointer;background:${selectedId === item.id ? "#F0F7FC" : "#fff"}`)}>
                <td style={cell}><strong>{item.jobCode || item.jobKey}</strong><br /><span style={muted}>{item.customer} · {item.category}</span></td>
                <td style={cell}>{item.supplier}</td>
                <td style={cell}>{dateTime(item.deliveryCompletedAt)}</td>
                <td style={cell}>{item.slaIssue ? <span style={css("color:#B42318")}>{item.slaIssue}</span> : <>
                  {item.slaRuleCode} · {item.slaStartDay} · {item.slaTargetWorkingDays} วันทำการ
                </>}</td>
                <td style={cell}>{item.slaDueDate || "—"}<br /><SlaLabel item={item} /></td>
                <td style={cell}><Status value={item.status} /></td>
                <td style={cell}>{item.invoice?.invoiceNumber || (item.invoice ? `Draft #${item.invoice.id}` : "—")}<br />
                  {item.invoice && <span style={muted}>{item.invoice.invoiceDate || "ยังไม่ระบุวันที่"}</span>}</td>
                <td style={css("padding:10px;color:#263B50;line-height:1.5;font-family:'IBM Plex Mono',monospace;text-align:right")}>{item.invoice ? `${item.invoice.currency} ${item.invoice.totalAmount.toLocaleString("en-US", { minimumFractionDigits: 2 })}` : "—"}</td>
                <td style={cell}>{item.documents.length ? item.documents.map((document) =>
                  <a key={document.id} href={`/api/documents/${document.id}/content`} target="_blank" rel="noreferrer"
                    style={css("display:block;color:#0A5C97;margin-bottom:3px")}>{document.fileName}</a>) : "—"}</td>
              </tr>)}
              {!loading && shown.length === 0 && <tr><td colSpan={9} style={css("padding:30px;text-align:center;color:#7B8CA0")}>ยังไม่มี Billing Case ตามเงื่อนไขนี้</td></tr>}
              {loading && <tr><td colSpan={9} style={css("padding:30px;text-align:center;color:#7B8CA0")}>กำลังโหลด…</td></tr>}
            </tbody>
          </table>
        </ZoomBox>
      </div>
      {selected?.invoice && <ReviewDetail item={selected} reasonCode={reasonCode} remark={remark}
        acting={acting} onReason={setReasonCode} onRemark={setRemark} onReview={review} />}
    </div>
  );
}

const cell = css("padding:10px;color:#263B50;line-height:1.5");
const muted = css("color:#7B8CA0;font-size:10.5px");

function Metric({ label, value, tone }: { label: string; value: number; tone: string }) {
  return <div style={css(`background:#fff;border:1px solid #D8E0E8;border-left:3px solid ${tone};border-radius:5px;padding:12px 14px`)}>
    <div style={css("font-size:11px;color:#64748B;font-weight:600")}>{label}</div>
    <div style={css("font-size:25px;color:#0A2240;font-weight:700;font-family:'IBM Plex Mono',monospace;margin-top:4px")}>{value}</div>
  </div>;
}

function Status({ value }: { value: string }) {
  const draft = value === "DRAFT";
  const blocked = value === "BLOCKED"; const validated = value === "VALIDATED";
  const tone = blocked ? ["#B42318", "#FEECE9"] : validated ? ["#16794C", "#E8F5EE"] : draft ? ["#0A5C97", "#EAF4FC"] : ["#B45309", "#FFF3E0"];
  return <span style={css(`display:inline-block;padding:3px 7px;border-radius:3px;font-size:10px;font-weight:700;color:${tone[0]};background:${tone[1]}`)}>
    {value}
  </span>;
}

function SlaLabel({ item }: { item: BillingCase }) {
  const overdue = item.slaState === "OVERDUE";
  const due = item.slaState === "DUE_TODAY" || item.slaState === "DUE_SOON";
  const text = item.daysRemaining == null ? "ยังไม่ตั้ง SLA" : item.daysRemaining < 0
    ? `เกิน ${Math.abs(item.daysRemaining)} วัน` : item.daysRemaining === 0 ? "ครบกำหนดวันนี้" : `เหลือ ${item.daysRemaining} วัน`;
  return <span style={css(`font-size:10.5px;font-weight:650;color:${overdue ? "#B42318" : due ? "#B45309" : "#16794C"}`)}>{text}</span>;
}

function ReviewDetail({ item, reasonCode, remark, acting, onReason, onRemark, onReview }: {
  item: BillingCase; reasonCode: string; remark: string; acting: boolean;
  onReason: (value: string) => void; onRemark: (value: string) => void;
  onReview: (action: "APPROVE_ONLINE" | "RETURN_TO_CARRIER" | "RAISE_DISPUTE") => void;
}) {
  const invoice = item.invoice!;
  const actionable = invoice.status === "SUBCON_REVIEW";
  const failures = invoice.validationResults.filter((result) => result.blocking);
  return <div style={css("background:#fff;border:1px solid #D8E0E8;border-radius:6px;padding:14px;display:grid;gap:12px") }>
    <div style={css("display:flex;align-items:flex-start;gap:12px;flex-wrap:wrap") }>
      <div style={css("margin-right:auto") }><strong style={css("color:#0A2240")}>Review Detail · {item.jobCode || item.jobKey}</strong>
        <div style={muted}>{item.supplier} · Invoice {invoice.invoiceNumber || `Draft #${invoice.id}`} · รอบที่ {invoice.reviewCycle || 1}</div></div>
      <Status value={invoice.status} />
    </div>
    <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:8px") }>
      <Detail label="ส่งเข้าคิวล่าสุด" value={dateTime(invoice.reviewSubmittedAt)} />
      <Detail label="เวลารอ Review" value={duration(invoice.reviewAgeMinutes)} />
      <Detail label="เวลาตัดสินรอบล่าสุด" value={duration(invoice.reviewDecisionMinutes)} />
      <Detail label="Online Approved" value={dateTime(invoice.onlineApprovedAt)} />
    </div>
    <div>
      <div style={css("font-size:11px;font-weight:700;color:#334155;margin-bottom:5px")}>Validation</div>
      {invoice.validationResults.length === 0 ? <span style={muted}>ยังไม่มีผล Validation</span> :
        <div style={css("display:flex;gap:5px;flex-wrap:wrap")}>{invoice.validationResults.map((result) =>
          <span key={`${result.sequence}-${result.step}-${result.code}`} title={result.message}
            style={css(`padding:3px 7px;border-radius:3px;font-size:10px;color:${result.blocking ? "#B42318" : "#16794C"};background:${result.blocking ? "#FEECE9" : "#E8F5EE"}`)}>
            {result.step}: {result.code}
          </span>)}</div>}
      {failures.length > 0 && <div style={css("font-size:10.5px;color:#B42318;margin-top:5px")}>พบ Blocking Validation {failures.length} รายการ</div>}
    </div>
    {actionable && <div style={css("border-top:1px solid #E7EDF3;padding-top:12px;display:grid;gap:8px") }>
      <div style={css("display:grid;grid-template-columns:minmax(180px,260px) minmax(240px,1fr);gap:8px") }>
        <select value={reasonCode} onChange={(event) => onReason(event.target.value)} style={control}>
          {returnReasons.map((reason) => <option key={reason} value={reason}>{reason}</option>)}
        </select>
        <input value={remark} onChange={(event) => onRemark(event.target.value)} placeholder="หมายเหตุ (บังคับเมื่อเลือก OTHER)" style={control} />
      </div>
      <div style={css("display:flex;gap:8px;flex-wrap:wrap") }>
        <Action label="อนุมัติ Online" tone="#16794C" disabled={acting || failures.length > 0} onClick={() => onReview("APPROVE_ONLINE")} />
        <Action label="คืนให้ผู้ขนส่งแก้ไข" tone="#B45309" disabled={acting} onClick={() => onReview("RETURN_TO_CARRIER")} />
        <Action label="เปิดข้อพิพาท" tone="#B42318" disabled={acting} onClick={() => onReview("RAISE_DISPUTE")} />
      </div>
    </div>}
    <div>
      <div style={css("font-size:11px;font-weight:700;color:#334155;margin-bottom:5px")}>Review history</div>
      {invoice.reviewEvents.length === 0 ? <span style={muted}>ยังไม่มีประวัติ Review</span> : invoice.reviewEvents.slice().reverse().map((event) =>
        <div key={event.id} style={css("display:grid;grid-template-columns:130px 170px 1fr;gap:8px;padding:6px 0;border-top:1px solid #EEF2F6;font-size:10.5px") }>
          <span>{dateTime(event.at)}</span><strong>{event.action} · รอบ {event.cycle}</strong>
          <span>{event.fromStatus} → {event.toStatus}{event.reasonCode ? ` · ${event.reasonCode}` : ""}{event.remark ? ` · ${event.remark}` : ""} · {event.actorName}</span>
        </div>)}
    </div>
  </div>;
}

function Detail({ label, value }: { label: string; value: string }) {
  return <div style={css("background:#F7F9FB;border:1px solid #E7EDF3;border-radius:4px;padding:8px") }>
    <div style={muted}>{label}</div><strong style={css("font-size:11px;color:#263B50")}>{value}</strong>
  </div>;
}

function Action({ label, tone, disabled, onClick }: { label: string; tone: string; disabled: boolean; onClick: () => void }) {
  return <button onClick={onClick} disabled={disabled} style={css(`height:32px;padding:0 13px;border:1px solid ${tone};border-radius:4px;background:${disabled ? "#F1F5F9" : "#fff"};color:${disabled ? "#94A3B8" : tone};font:inherit;font-size:11px;font-weight:700;cursor:${disabled ? "not-allowed" : "pointer"}`)}>{label}</button>;
}

const control = css("height:32px;padding:0 9px;border:1px solid #CAD5E0;border-radius:4px;background:#fff;font:inherit;font-size:11px");

function duration(minutes: number | null) {
  if (minutes == null) return "—";
  if (minutes < 60) return `${minutes} นาที`;
  const hours = Math.floor(minutes / 60); const rest = minutes % 60;
  return `${hours} ชม.${rest ? ` ${rest} นาที` : ""}`;
}

function dateTime(value: string) {
  if (!value) return "—";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString("th-TH", { dateStyle: "short", timeStyle: "short" });
}
