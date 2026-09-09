/**
 * How the Communication Center reads a message to somebody.
 *
 * The screen's wording and colouring, kept out of the component so it can be
 * checked without a browser. What is displayed about a link is the thing an
 * operator decides on — "0.96, container" against "น่าจะใช่" is the difference
 * between a number and a recommendation — so it is worth being deliberate about
 * and worth a test.
 *
 * No imports on purpose. The API answers the same questions in
 * `Rules/MailReview.cs`; the two are checked against each other by
 * `--check-review`, because a view this file asks for that the API does not
 * know would quietly widen the inbox to everything.
 */

/** The views the inbox offers, in the order it offers them. */
export const VIEWS: { key: string; label: string; th: string }[] = [
  // Waiting first: it is the reason to open this screen at all.
  { key: "WAITING", label: "Waiting", th: "รอตัดสิน" },
  { key: "LINKED", label: "Linked", th: "จับคู่แล้ว" },
  { key: "UNLINKED", label: "Unlinked", th: "ยังไม่จับคู่" },
  { key: "ALL", label: "All", th: "ทั้งหมด" },
];

/** Where a message has got to, in the words the screen uses. */
export function statusLabel(status: string): string {
  switch (status) {
    case "RECEIVED": return "รอประมวลผล";
    case "PROCESSING": return "กำลังอ่าน";
    case "PROCESSED": return "เรียบร้อย";
    case "NEED_REVIEW": return "รอตัดสิน";
    case "FAILED": return "อ่านไม่สำเร็จ";
    default: return status || "—";
  }
}

/**
 * The colour a status is shown in.
 *
 * Only two things are coloured to draw the eye: what needs a person, and what
 * went wrong. Everything else is grey, because a list where every row is
 * coloured has no colour left to say "this one".
 */
export function statusTone(status: string): "amber" | "red" | "gray" {
  if (status === "NEED_REVIEW") return "amber";
  if (status === "FAILED") return "red";
  return "gray";
}

/** What a link's status means, in words rather than a code. */
export function linkLabel(status: string): string {
  switch (status) {
    case "CONFIRMED": return "ยืนยันแล้ว";
    case "REJECTED": return "ปฏิเสธแล้ว";
    case "SUGGESTED": return "รอยืนยัน";
    default: return status || "—";
  }
}

export function linkTone(status: string): "green" | "gray" | "amber" {
  if (status === "CONFIRMED") return "green";
  if (status === "REJECTED") return "gray";
  return "amber";
}

/**
 * How sure the machine was, in words.
 *
 * <p>The thresholds are the specification's, and the same ones the API links
 * and offers at: at or above 0.95 it was attached without asking, from 0.70 it
 * was offered. A person confirming a link should be told which of those two
 * happened, because "the machine was certain and I am checking it" and "the
 * machine was unsure and asked me" are different jobs.</p>
 *
 * <p>A hand-made link carries no confidence at all, and says so rather than
 * showing 0% — nobody computed anything, and 0% would read as "certainly
 * wrong".</p>
 */
export function confidenceLabel(confidence: number, matchedOn: string): string {
  if (matchedOn === "PERSON") return "คนจับคู่เอง";
  if (!(confidence > 0)) return "—";
  const percent = Math.round(confidence * 100);
  if (confidence >= 0.95) return `${percent}% · มั่นใจสูง`;
  if (confidence >= 0.7) return `${percent}% · น่าจะใช่`;
  return `${percent}%`;
}

/** What kind of identifier carried a link, in the operators' words. */
export function matchedOnLabel(kind: string): string {
  switch (kind) {
    case "CONTAINER": return "เลขตู้";
    case "JOB_CODE": return "เลขงาน";
    case "BOOKING": return "เลข Booking";
    case "BL": return "เลข B/L";
    case "PERSON": return "คนจับคู่";
    default: return kind || "—";
  }
}

/**
 * One line saying what a message is attached to, for the list.
 *
 * <p>Rejected links are not counted. A rejection is a record that somebody
 * looked and said no; showing it as an attachment would put mail in front of
 * people that they have already dealt with.</p>
 */
export function linkSummary(links: { jobKey: string; status: string }[]): string {
  const live = (links ?? []).filter((one) => one.status !== "REJECTED");
  if (live.length === 0) return "ยังไม่จับคู่";
  if (live.length === 1) return live[0].jobKey;
  return `${live[0].jobKey} +${live.length - 1}`;
}

/**
 * When it arrived, as somebody would say it.
 *
 * Relative for the last day, because that is the window in which "2 ชม.ที่แล้ว"
 * answers the question being asked; a date after that, because "37 ชม.ที่แล้ว"
 * does not.
 */
export function whenLabel(iso: string, now: Date = new Date()): string {
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return "—";

  const minutes = Math.floor((now.getTime() - at.getTime()) / 60000);
  if (minutes < 0) return at.toLocaleDateString("th-TH");
  if (minutes < 1) return "เมื่อครู่";
  if (minutes < 60) return `${minutes} นาทีที่แล้ว`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} ชม.ที่แล้ว`;

  return at.toLocaleDateString("th-TH", { day: "2-digit", month: "short", year: "2-digit" });
}

/** A file size somebody can read, from the bytes the store holds. */
export function sizeLabel(bytes: number): string {
  if (!(bytes > 0)) return "—";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
