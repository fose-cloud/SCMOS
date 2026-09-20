/**
 * What a haulier's message is waiting to do to a job, as the workspace reads
 * it — from the LINE room, or from the haulier's own system through the
 * Carrier API (since 20 Sep 2026): one queue, and the row says which door.
 *
 * The review screen works the whole queue; the workspace only needs to know,
 * for the rows on screen, "a message is waiting on this job, and approving it
 * would write this" — so the job's owner can approve from the job itself,
 * which is where they are. Asked of the API as one list and folded by job key
 * here.
 *
 * No imports on purpose — see the other modules under this folder.
 */

export type LinePending = {
  id: number;
  jobKey: string;
  receivedAt: string;
  /** The rule's verdict as stored: ready-to-apply, backwards, no-status… */
  errorCode: string;
  detail: string;
  /** "text" from the LINE room, "tms" from the haulier's system — the feed carries no photos. */
  kind: string;
  text: string;
  group: string;
  /** What approving writes: the status as this job's ladder names it. */
  to: string;
  /** The arrival the message reports; atSend when it is the send time, the message carrying no clock. */
  arrival: { date: string; time: string; atSend?: boolean };
  /** When the message says the truck will arrive — "ประมาณ 10.00" — as HH:mm, or empty. Shown, never written. */
  eta?: string;
  /** The truck's details the message carries — "ทะเบียน 70-1234 · คนขับ สมชาย · เบอร์ 081-…" — or empty. */
  details?: string;
  /** How many jobs approving writes when the message is about every row of a job number ("3 ตู้"); 0 for this one only. */
  every?: number;
  ready: boolean;
};

/** The messages waiting on each job, newest first as the API sends them. */
export function pendingByKey(items: readonly LinePending[] | null | undefined): Record<string, LinePending[]> {
  const byKey: Record<string, LinePending[]> = {};
  for (const one of items ?? []) {
    const key = String(one.jobKey ?? "").trim();
    if (key.length === 0) continue;
    (byKey[key] ??= []).push(one);
  }
  return byKey;
}

/** Which door a message came through: the LINE room, or the haulier's TMS. */
export function sourceOf(item: Pick<LinePending, "kind">): "LINE" | "TMS" {
  return item.kind === "tms" ? "TMS" : "LINE";
}

/** The doors the messages on one job came through, LINE first, each once. */
export function sourcesOf(items: readonly LinePending[]): ("LINE" | "TMS")[] {
  const sources = new Set(items.map(sourceOf));
  return (["LINE", "TMS"] as const).filter((one) => sources.has(one));
}

/** What the grid marks a row with: how many wait, and the badge naming the door — "LINE 2", "TMS 1", "LINE 1 · TMS 1". */
export type PendingMark = { count: number; badge: string };

export function pendingMarks(items: readonly LinePending[] | null | undefined): Record<string, PendingMark> {
  const marks: Record<string, PendingMark> = {};
  for (const [key, list] of Object.entries(pendingByKey(items))) {
    const badge = sourcesOf(list).map((source) => `${source} ${list.filter((one) => sourceOf(one) === source).length}`).join(" · ");
    marks[key] = { count: list.length, badge };
  }
  return marks;
}

/**
 * What approving would write, in one line: "สถานะ → DELIVERED · เวลาถึง 05:00
 * 16/09/2026", or "เลขตู้ TEMU5246902". Empty when the message can be
 * applied to nothing — then the verdict is what the drawer shows instead.
 */
export function pendingWrites(item: LinePending): string {
  const to = String(item.to ?? "").trim();
  const parts: string[] = [];
  if (to.length > 0) parts.push(`สถานะ → ${to}`);
  const time = String(item.arrival?.time ?? "").trim();
  const date = String(item.arrival?.date ?? "").trim();
  if (time.length > 0) parts.push(`เวลาถึง ${time}${date ? " " + date : ""}${item.arrival?.atSend ? " (เวลาที่ส่งข้อความ)" : ""}`);
  // An estimate is said so the owner knows the truck is not there yet; it is
  // not written anywhere — the arrival comes with the next message.
  const eta = String(item.eta ?? "").trim();
  if (eta.length > 0 && time.length === 0) parts.push(`คาดถึง ${eta} (ไม่บันทึก)`);
  const details = String(item.details ?? "").trim();
  if (details.length > 0) parts.push(details);
  if ((item.every ?? 0) > 1) parts.push(`ทั้ง ${item.every} รายการของเลขงานนี้`);
  return parts.join(" · ");
}

/** The message as a person reads it. */
export function pendingText(item: LinePending): string {
  return String(item.text ?? "");
}
