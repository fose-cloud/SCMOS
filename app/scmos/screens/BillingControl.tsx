"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { apiFetch } from "../api";
import { ZoomBox } from "../TableFrame";
import { css } from "../theme";

export type BillingDocument = { id: number; fileName: string; kind: string; uploadedAt: string };
export type BillingInvoice = {
  id: number; invoiceNumber: string; invoiceDate: string; currency: string;
  subtotal: number; taxAmount: number; totalAmount: number; status: string; updatedAt: string;
  validationResults: BillingValidation[];
  additionalCharges: BillingCharge[];
};
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

type Filter = "ALL" | "WAITING_CARRIER_SUBMISSION" | "DRAFT" | "OVERDUE";

export function BillingControl({ onToast }: { onToast: (message: string) => void }) {
  const [items, setItems] = useState<BillingCase[]>([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<Filter>("ALL");
  const [search, setSearch] = useState("");

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
  const overdue = items.filter((item) => item.slaState === "OVERDUE").length;

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px")}>
        <Metric label="Billing Eligible Jobs" value={items.length} tone="#0A5C97" />
        <Metric label="รอผู้ขนส่งวางบิล" value={waiting} tone="#B45309" />
        <Metric label="Invoice Draft" value={drafts} tone="#16794C" />
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
            <option value="DRAFT">Draft</option><option value="OVERDUE">เกิน SLA</option>
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
              {shown.map((item) => <tr key={item.id} style={css("border-bottom:1px solid #E9EFF5;vertical-align:top")}>
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

function dateTime(value: string) {
  if (!value) return "—";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString("th-TH", { dateStyle: "short", timeStyle: "short" });
}
