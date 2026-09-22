/**
 * What the rules propose to change on a job's dropdown cells, waiting for
 * the job's owner — since 22 Sep 2026: "AI แก้ข้อมูลที่ไม่ตรงกับ DATA … ที่ทำ
 * Dropdown" and "รอ Operation กด Approve เหมือนไลน์".
 *
 * The API proposes nothing from the browser and writes nothing until the
 * owner says yes, on the job, the way a LINE message is approved. The
 * workspace only needs to know, for the rows on screen, "a proposal waits on
 * this job, and approving it writes this" — asked of the API as one list and
 * folded by job key here. No imports on purpose — see the other modules
 * under this folder.
 */

import type { PendingMark } from "./linePending";

export type Correction = {
  id: number;
  jobKey: string;
  jobCode: string;
  /** cat · customer · trucker · type · status */
  field: string;
  /** The column as the grid heads it — ลูกค้า, ผู้ขนส่ง, ประเภทรถ/ตู้ … */
  label: string;
  from: string;
  to: string;
  reason: string;
  proposedAt: string;
  /** Which rule proposed it — type.canonical, customer.rotation.site, reason.route … */
  rule?: string;
};

/** What the API answers on /api/corrections/pending. */
export type CorrectionFeed = { items: Correction[]; count: number; mine: number };

/** One reason an owner may pick for a late shipment — the catalogue as the rule holds it (/api/corrections/reasons). */
export type ReasonChoice = { text: string; category: string; thai: string };

/** The proposal is a delay reason — by its rule, whichever column it goes to (REASON / DELAY on an import, REMARK on an export): the owner may pick another from the catalogue before approving. */
export function isReasonProposal(one: Pick<Correction, "field" | "rule">): boolean {
  return typeof one.rule === "string" ? one.rule.startsWith("reason.") : one.field === "reason";
}

/** The proposals waiting on each job, in the order the API sent them. */
export function correctionsByKey(items: readonly Correction[] | null | undefined): Record<string, Correction[]> {
  const byKey: Record<string, Correction[]> = {};
  for (const one of items ?? []) {
    const key = String(one.jobKey ?? "").trim();
    if (key.length === 0) continue;
    (byKey[key] ??= []).push(one);
  }
  return byKey;
}

/**
 * The row's mark, with the hauliers' messages folded in: "LINE 1 · AI 3".
 * AI last, because a haulier's word about a truck outranks a spelling.
 */
export function mergeMarks(messages: Record<string, PendingMark>, corrections: readonly Correction[] | null | undefined): Record<string, PendingMark> {
  const marks: Record<string, PendingMark> = { ...messages };
  for (const [key, list] of Object.entries(correctionsByKey(corrections))) {
    const before = marks[key];
    marks[key] = {
      count: (before?.count ?? 0) + list.length,
      badge: before ? `${before.badge} · AI ${list.length}` : `AI ${list.length}`,
    };
  }
  return marks;
}

/** One proposal in one line: "ผู้ขนส่ง: SJ → Sangja Transport Co., Ltd." */
export function correctionText(one: Correction): string {
  return `${one.label}: ${one.from || "—"} → ${one.to}`;
}
