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
  originalPackage: OriginalPackage | null;
  validationResults: BillingValidation[];
  additionalCharges: BillingCharge[];
};
export type BillingReviewEvent = { id: number; cycle: number; action: string; fromStatus: string;
  toStatus: string; reasonCode: string; remark: string; actorName: string; at: string };
export type OriginalPackage = { id: number; status: string; sentDate: string; courier: string;
  trackingNumber: string; carrierPackageReference: string; carrierRemark: string; sentBy: string;
  sentAt: string; receivedAt: string; receivedByName: string; documentCount: number | null;
  receiptPackageReference: string; receiptRemark: string };
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

export type BillingControlTowerMetrics = {
  billingCases: number; submitted: number;
  within3WorkingDays: number; within3WorkingDaysPercent: number | null;
  within4WorkingDays: number; within4WorkingDaysPercent: number | null;
  overdue: number; firstTimeRight: number; firstTimeRightBase: number;
  firstTimeRightPercent: number | null; returned: number; reviewDecisions: number;
  returnRatePercent: number | null; averageSubmissionLeadWorkingDays: number | null;
  averageInternalReviewMinutes: number | null; originalPending: number;
  averageOriginalPendingDays: number | null; oldestOriginalPendingDays: number;
  carrierAccepted: number; carrierDecisions: number; carrierAcceptancePercent: number | null;
  carrierAcceptancePending: number; truckAssignmentPending: number;
};
export type BillingControlTowerItem = {
  caseId: number; invoiceId: number | null; jobKey: string; jobCode: string; customer: string;
  supplierId: number; supplier: string; status: string; slaState: string;
  deliveryCompletedAt: string; submittedAt: string | null; submissionLeadWorkingDays: number | null;
  internalReviewMinutes: number | null; originalPendingDays: number | null;
  exceptionCount: number; blockingExceptionCount: number; firstTimeRight: boolean;
  returned: boolean; overdue: boolean; invoiceNumber: string;
};
export type BillingControlTowerView = {
  from: string; to: string; scope: string; metrics: BillingControlTowerMetrics;
  items: BillingControlTowerItem[]; exceptions: Record<string, number>;
};

type Filter = "ALL" | "WAITING_CARRIER_SUBMISSION" | "DRAFT" | "SUBCON_REVIEW" | "RETURNED" | "DISPUTED" | "AWAITING_ORIGINAL" | "ORIGINAL_RECEIVED" | "READY_FOR_FINANCE" | "OVERDUE";

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
  const [canReceiveOriginal, setCanReceiveOriginal] = useState(false);
  const [originalReceiptConfigured, setOriginalReceiptConfigured] = useState(true);
  const [tower, setTower] = useState<BillingControlTowerView | null>(null);
  const [drill, setDrill] = useState<{ label: string; caseIds: number[] } | null>(null);
  const [periodFrom, setPeriodFrom] = useState(() => dateInput(new Date(Date.now() - 89 * 86_400_000)));
  const [periodTo, setPeriodTo] = useState(() => dateInput(new Date()));
  const [supplierFilter, setSupplierFilter] = useState("");

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [response, towerResponse] = await Promise.all([
        apiFetch("/api/carrier-billing/cases", { headers: { accept: "application/json" } }),
        apiFetch(`/api/carrier-billing/control-tower?from=${periodFrom}&to=${periodTo}${supplierFilter ? `&supplierId=${supplierFilter}` : ""}`,
          { headers: { accept: "application/json" } }),
      ]);
      const body = await response.json().catch(() => ({})) as { items?: BillingCase[]; error?: string;
        canReceiveOriginal?: boolean; originalReceiptConfigured?: boolean };
      if (!response.ok) { onToast(body.error ?? `เปิด Billing Control ไม่สำเร็จ (${response.status})`); return; }
      setItems(body.items ?? []);
      setCanReceiveOriginal(body.canReceiveOriginal === true);
      setOriginalReceiptConfigured(body.originalReceiptConfigured !== false);
      const towerBody = await towerResponse.json().catch(() => ({})) as BillingControlTowerView & { error?: string };
      if (towerResponse.ok) setTower(towerBody);
      else onToast(towerBody.error ?? `เปิด Control Tower ไม่สำเร็จ (${towerResponse.status})`);
    } finally { setLoading(false); }
  }, [onToast, periodFrom, periodTo, supplierFilter]);

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { void load(); }, [load]);

  const shown = useMemo(() => {
    const needle = search.trim().toLowerCase();
    return items.filter((item) => {
      if (filter === "OVERDUE" && item.slaState !== "OVERDUE") return false;
      if (filter !== "ALL" && filter !== "OVERDUE" && item.status !== filter) return false;
      if (drill && !drill.caseIds.includes(item.id)) return false;
      return !needle || [item.jobCode, item.customer, item.supplier, item.invoice?.invoiceNumber ?? ""]
        .some((value) => value.toLowerCase().includes(needle));
    });
  }, [drill, filter, items, search]);

  const originalPending = items.filter((item) => item.status === "AWAITING_ORIGINAL").length;
  const overdue = items.filter((item) => item.slaState === "OVERDUE").length;
  const selected = items.find((item) => item.id === selectedId) ?? null;
  const metricItems = tower?.items ?? [];
  const openDrill = (label: string, predicate: (item: BillingControlTowerItem) => boolean) => {
    setFilter("ALL"); setSearch("");
    setDrill({ label, caseIds: metricItems.filter(predicate).map((item) => item.caseId) });
  };
  const percent = (value: number | null | undefined) => value == null ? "—" : `${value.toFixed(1)}%`;

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

  async function receiveOriginal(receivedAt: string, documentCount: number, packageReference: string, receiptRemark: string) {
    const invoiceId = selected?.invoice?.id;
    if (!invoiceId || acting) return;
    setActing(true);
    try {
      const response = await apiFetch(`/api/carrier-billing/original/${invoiceId}/receive`, {
        method: "POST", headers: { "content-type": "application/json", accept: "application/json" },
        body: JSON.stringify({ receivedAt, documentCount, packageReference, remark: receiptRemark }),
      });
      const body = await response.json().catch(() => ({})) as { message?: string; error?: string };
      onToast(body.message ?? body.error ?? (response.ok ? "รับเอกสารต้นฉบับแล้ว" : `บันทึกไม่สำเร็จ (${response.status})`));
      if (response.ok) await load();
    } finally { setActing(false); }
  }

  return (
    <div style={css("display:flex;flex-direction:column;gap:12px")}>
      <div style={css("background:#0A2240;color:#fff;border-radius:6px;padding:11px 14px;display:flex;gap:12px;align-items:center;flex-wrap:wrap") }>
        <strong style={css("font-size:13px")}>Billing Control Tower</strong>
        <span style={css("font-size:10.5px;color:#B8C9DA")}>{tower ? `${tower.from} ถึง ${tower.to} · ${tower.scope}` : "กำลังคำนวณ KPI…"}</span>
        <label style={css("display:flex;align-items:center;gap:5px;font-size:10px;color:#B8C9DA")}>จาก
          <input type="date" value={periodFrom} onChange={(event) => { setDrill(null); setPeriodFrom(event.target.value); }} style={towerControl} />
        </label>
        <label style={css("display:flex;align-items:center;gap:5px;font-size:10px;color:#B8C9DA")}>ถึง
          <input type="date" value={periodTo} onChange={(event) => { setDrill(null); setPeriodTo(event.target.value); }} style={towerControl} />
        </label>
        <select aria-label="ผู้ขนส่งสำหรับ Control Tower" value={supplierFilter}
          onChange={(event) => { setDrill(null); setSupplierFilter(event.target.value); }} style={towerControl}>
          <option value="">ผู้ขนส่งทั้งหมด</option>
          {[...new Map(items.map((item) => [item.supplierId, item.supplier])).entries()]
            .sort((a, b) => a[1].localeCompare(b[1])).map(([id, name]) => <option key={id} value={id}>{name}</option>)}
        </select>
        {drill && <button onClick={() => setDrill(null)} style={css("margin-left:auto;height:27px;padding:0 10px;border:1px solid #7FA4C5;border-radius:4px;background:#12385B;color:#fff;font:inherit;font-size:10.5px;cursor:pointer")}>Drill-down: {drill.label} ×</button>}
      </div>
      <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:9px")}>
        <Metric label="Billing ≤3 Working Days" value={percent(tower?.metrics.within3WorkingDaysPercent)}
          detail={`${tower?.metrics.within3WorkingDays ?? 0}/${tower?.metrics.submitted ?? 0} invoices`} tone="#16794C"
          onClick={() => openDrill("≤3 Working Days", (item) => item.submissionLeadWorkingDays != null && item.submissionLeadWorkingDays <= 3)} />
        <Metric label="Billing ≤4 Working Days" value={percent(tower?.metrics.within4WorkingDaysPercent)}
          detail={`${tower?.metrics.within4WorkingDays ?? 0}/${tower?.metrics.submitted ?? 0} invoices`} tone="#0A5C97"
          onClick={() => openDrill("≤4 Working Days", (item) => item.submissionLeadWorkingDays != null && item.submissionLeadWorkingDays <= 4)} />
        <Metric label="Overdue" value={tower?.metrics.overdue ?? overdue} detail="ยังไม่ส่งวางบิลและพ้นกำหนด" tone="#B42318"
          onClick={() => openDrill("Overdue", (item) => item.overdue)} />
        <Metric label="First-Time-Right" value={percent(tower?.metrics.firstTimeRightPercent)}
          detail={`${tower?.metrics.firstTimeRight ?? 0}/${tower?.metrics.firstTimeRightBase ?? 0} first decisions`} tone="#16794C"
          onClick={() => openDrill("First-Time-Right", (item) => item.firstTimeRight)} />
        <Metric label="Return Rate" value={percent(tower?.metrics.returnRatePercent)}
          detail={`${tower?.metrics.returned ?? 0}/${tower?.metrics.reviewDecisions ?? 0} decisions`} tone="#B45309"
          onClick={() => openDrill("Returned", (item) => item.returned)} />
        <Metric label="Avg Submission Lead" value={tower?.metrics.averageSubmissionLeadWorkingDays == null ? "—" : `${tower.metrics.averageSubmissionLeadWorkingDays} วัน`}
          detail="วันทำการจาก SLA start" tone="#0A5C97" onClick={() => openDrill("Submitted", (item) => item.submissionLeadWorkingDays != null)} />
        <Metric label="Internal Review Time" value={duration(tower?.metrics.averageInternalReviewMinutes == null ? null : Math.round(tower.metrics.averageInternalReviewMinutes))}
          detail="เฉลี่ยรอบที่ตัดสินแล้ว" tone="#7C3AED" onClick={() => openDrill("Reviewed", (item) => item.internalReviewMinutes != null)} />
        <Metric label="Original Pending Aging" value={tower?.metrics.averageOriginalPendingDays == null ? "—" : `${tower.metrics.averageOriginalPendingDays} วัน`}
          detail={`${tower?.metrics.originalPending ?? originalPending} รายการ · สูงสุด ${tower?.metrics.oldestOriginalPendingDays ?? 0} วัน`} tone="#B45309"
          onClick={() => openDrill("Original Pending", (item) => item.originalPendingDays != null)} />
        <Metric label="Carrier Acceptance" value={percent(tower?.metrics.carrierAcceptancePercent)}
          detail={`รอตอบ ${tower?.metrics.carrierAcceptancePending ?? 0}`} tone="#16794C" />
        <Metric label="Truck Assignment Pending" value={tower?.metrics.truckAssignmentPending ?? 0}
          detail="รับงานแล้วแต่ยังไม่มีรถ" tone="#B42318" />
      </div>
      {tower && Object.keys(tower.exceptions).length > 0 && <div style={css("display:flex;gap:6px;align-items:center;flex-wrap:wrap;font-size:10.5px;color:#64748B") }>
        <strong>Exceptions:</strong>{Object.entries(tower.exceptions).slice(0, 8).map(([code, count]) =>
          <span key={code} style={css("padding:3px 7px;border:1px solid #F0C7C2;background:#FFF6F5;color:#9F2D22;border-radius:3px")}>{code} · {count}</span>)}
      </div>}

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
            <option value="AWAITING_ORIGINAL">รอต้นฉบับ</option><option value="ORIGINAL_RECEIVED">รับต้นฉบับแล้ว</option>
            <option value="READY_FOR_FINANCE">พร้อมส่ง Finance</option><option value="OVERDUE">เกิน SLA</option>
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
      {selected?.invoice && <ReviewDetail key={selected.invoice.id} item={selected} reasonCode={reasonCode} remark={remark}
        acting={acting} canReceiveOriginal={canReceiveOriginal} originalReceiptConfigured={originalReceiptConfigured}
        onReason={setReasonCode} onRemark={setRemark} onReview={review} onReceiveOriginal={receiveOriginal} />}
    </div>
  );
}

const cell = css("padding:10px;color:#263B50;line-height:1.5");
const muted = css("color:#7B8CA0;font-size:10.5px");

function Metric({ label, value, detail, tone, onClick }: { label: string; value: number | string; detail?: string; tone: string; onClick?: () => void }) {
  return <div onClick={onClick} role={onClick ? "button" : undefined} tabIndex={onClick ? 0 : undefined}
    onKeyDown={(event) => { if (onClick && (event.key === "Enter" || event.key === " ")) onClick(); }}
    style={css(`background:#fff;border:1px solid #D8E0E8;border-left:3px solid ${tone};border-radius:5px;padding:11px 12px;cursor:${onClick ? "pointer" : "default"}`)}>
    <div style={css("font-size:11px;color:#64748B;font-weight:600")}>{label}</div>
    <div style={css("font-size:23px;color:#0A2240;font-weight:700;font-family:'IBM Plex Mono',monospace;margin-top:4px")}>{value}</div>
    {detail && <div style={css("font-size:9.5px;color:#7B8CA0;margin-top:2px")}>{detail}</div>}
  </div>;
}

function Status({ value }: { value: string }) {
  const draft = value === "DRAFT";
  const blocked = value === "BLOCKED" || value === "DISPUTED";
  const ready = value === "READY_FOR_FINANCE"; const validated = value === "VALIDATED" || value === "ORIGINAL_RECEIVED";
  const tone = blocked ? ["#B42318", "#FEECE9"] : ready ? ["#16794C", "#DDF5E7"] : validated ? ["#16794C", "#E8F5EE"] : draft ? ["#0A5C97", "#EAF4FC"] : ["#B45309", "#FFF3E0"];
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

function ReviewDetail({ item, reasonCode, remark, acting, canReceiveOriginal, originalReceiptConfigured,
  onReason, onRemark, onReview, onReceiveOriginal }: {
  item: BillingCase; reasonCode: string; remark: string; acting: boolean;
  canReceiveOriginal: boolean; originalReceiptConfigured: boolean;
  onReason: (value: string) => void; onRemark: (value: string) => void;
  onReview: (action: "APPROVE_ONLINE" | "RETURN_TO_CARRIER" | "RAISE_DISPUTE") => void;
  onReceiveOriginal: (receivedAt: string, documentCount: number, packageReference: string, remark: string) => void;
}) {
  const invoice = item.invoice!;
  const actionable = invoice.status === "SUBCON_REVIEW";
  const failures = invoice.validationResults.filter((result) => result.blocking);
  const [receivedAt, setReceivedAt] = useState(() => localDateTimeInput(new Date()));
  const [documentCount, setDocumentCount] = useState("1");
  const [receiptReference, setReceiptReference] = useState("");
  const [receiptRemark, setReceiptRemark] = useState("");
  const original = invoice.originalPackage;
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
    {(invoice.status === "AWAITING_ORIGINAL" || invoice.status === "ORIGINAL_RECEIVED" || invoice.status === "READY_FOR_FINANCE") &&
      <div style={css("border-top:1px solid #E7EDF3;padding-top:12px;display:grid;gap:9px") }>
        <div style={css("font-size:11px;font-weight:700;color:#334155")}>Original Document Control</div>
        <div style={css("display:grid;grid-template-columns:repeat(auto-fit,minmax(180px,1fr));gap:8px") }>
          <Detail label="Physical status" value={original?.status ?? "PENDING"} />
          <Detail label="Carrier sent" value={original?.sentDate || "ยังไม่แจ้ง"} />
          <Detail label="Courier / Tracking" value={original ? `${original.courier || "—"} · ${original.trackingNumber || "—"}` : "—"} />
          <Detail label="Received" value={dateTime(original?.receivedAt ?? "")} />
          <Detail label="Document count" value={original?.documentCount == null ? "—" : `${original.documentCount} ฉบับ`} />
          <Detail label="Received by" value={original?.receivedByName || "—"} />
        </div>
        {original?.carrierPackageReference && <div style={muted}>Carrier package ref: {original.carrierPackageReference}</div>}
        {original?.carrierRemark && <div style={muted}>Carrier remark: {original.carrierRemark}</div>}
        {original?.receiptPackageReference && <div style={muted}>Receipt package ref: {original.receiptPackageReference}</div>}
        {original?.receiptRemark && <div style={muted}>Receipt remark: {original.receiptRemark}</div>}
        {invoice.status === "AWAITING_ORIGINAL" && canReceiveOriginal && <div style={css("display:grid;gap:8px") }>
          <div style={css("display:grid;grid-template-columns:190px 120px minmax(180px,1fr);gap:8px") }>
            <input type="datetime-local" value={receivedAt} onChange={(event) => setReceivedAt(event.target.value)} style={control} />
            <input type="number" min="1" value={documentCount} onChange={(event) => setDocumentCount(event.target.value)}
              aria-label="จำนวนเอกสาร" style={control} />
            <input value={receiptReference} onChange={(event) => setReceiptReference(event.target.value)}
              placeholder="Package Reference ฝั่งรับ" style={control} />
          </div>
          <input value={receiptRemark} onChange={(event) => setReceiptRemark(event.target.value)}
            placeholder="หมายเหตุการรับเอกสาร" style={control} />
          <div><Action label="ยืนยันรับเอกสารต้นฉบับ" tone="#16794C" disabled={acting || Number(documentCount) <= 0 || !receivedAt}
            onClick={() => onReceiveOriginal(new Date(receivedAt).toISOString(), Number(documentCount), receiptReference, receiptRemark)} /></div>
        </div>}
        {invoice.status === "AWAITING_ORIGINAL" && !originalReceiptConfigured && <div style={css("font-size:10.5px;color:#B42318") }>
          ยังไม่ได้กำหนดบทบาทผู้รับเอกสารต้นฉบับใน CarrierBilling configuration
        </div>}
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
const towerControl = css("height:27px;padding:0 6px;border:1px solid #557896;border-radius:4px;background:#12385B;color:#fff;font:inherit;font-size:10px");

function duration(minutes: number | null) {
  if (minutes == null) return "—";
  if (minutes < 60) return `${minutes} นาที`;
  const hours = Math.floor(minutes / 60); const rest = minutes % 60;
  return `${hours} ชม.${rest ? ` ${rest} นาที` : ""}`;
}

function localDateTimeInput(value: Date) {
  return new Date(value.getTime() - value.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}

function dateInput(value: Date) {
  const year = value.getFullYear();
  const month = String(value.getMonth() + 1).padStart(2, "0");
  const day = String(value.getDate()).padStart(2, "0");
  return `${year}-${month}-${day}`;
}

function dateTime(value: string) {
  if (!value) return "—";
  const parsed = new Date(value);
  return Number.isNaN(parsed.getTime()) ? value : parsed.toLocaleString("th-TH", { dateStyle: "short", timeStyle: "short" });
}
