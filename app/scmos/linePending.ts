/**
 * What a haulier's LINE message is waiting to do to a job, as the workspace
 * reads it.
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
  /** "text" or "image". */
  kind: string;
  text: string;
  /** The container read off a photo, when the message is one. */
  reading: string;
  hasImage: boolean;
  group: string;
  /** What approving writes: the status as this job's ladder names it, or the container. */
  to: string;
  /** The arrival the message reports; atSend when it is the send time, the message carrying no clock. */
  arrival: { date: string; time: string; atSend?: boolean };
  /** When the message says the truck will arrive — "ประมาณ 10.00" — as HH:mm, or empty. Shown, never written. */
  eta?: string;
  /** The truck's details the message carries — "ทะเบียน 70-1234 · คนขับ สมชาย · เบอร์ 081-…" — or empty. */
  details?: string;
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

/** How many messages wait on each job — what the grid marks a row with. */
export function pendingCounts(items: readonly LinePending[] | null | undefined): Record<string, number> {
  const counts: Record<string, number> = {};
  for (const [key, list] of Object.entries(pendingByKey(items))) counts[key] = list.length;
  return counts;
}

/**
 * What approving would write, in one line: "สถานะ → DELIVERED · เวลาถึง 05:00
 * 16/09/2026", or "เลขตู้ TEMU5246902". Empty when the message can be
 * applied to nothing — then the verdict is what the drawer shows instead.
 */
export function pendingWrites(item: LinePending): string {
  const to = String(item.to ?? "").trim();
  if (item.kind === "image") return to.length > 0 ? `เลขตู้ ${to}` : "";
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
  return parts.join(" · ");
}

/** The message as a person reads it: the text, or what the photo showed. */
export function pendingText(item: LinePending): string {
  if (item.kind !== "image") return String(item.text ?? "");
  const reading = String(item.reading ?? "").trim();
  return reading.length > 0 ? `รูปตู้ · ${reading}` : "รูป";
}
