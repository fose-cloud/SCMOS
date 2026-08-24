import type { Job } from "./ops";

/**
 * What the delay reasons actually say.
 *
 * Management asked for the cause behind the delays, and the register does hold
 * an answer — but not a tidy one. The reason column is typed by whoever was on
 * the phone, and reading the delivered plan it is being used for four different
 * things at once:
 *
 *   a real cause          "รถติดในท่า", "Delay due to Traffic Congestion"
 *   a status note         "ถึงโรงงาน", "Delivery Completed", "กำลังเดินทาง"
 *   a pickup appointment  "รับตู้10/07/26 01.00" — belongs in Pickup Plan
 *   a bare time           "13.00", "08.00"
 *
 * Counting the column as written would report "ถึงโรงงาน" — *arrived at the
 * plant* — as the second largest cause of lateness, which is nonsense, and it
 * would be nonsense printed under a heading management is about to act on. So
 * the three kinds of non-reason are named as such and kept out of the causes,
 * with their counts shown rather than hidden: a reason column half full of
 * status notes is itself a finding, and the fix for it is not in this file.
 *
 * The patterns are read off the real text, not invented. Anything that matches
 * none of them is "อื่น ๆ" and keeps its wording, so a new kind of delay is
 * visible as an unclassified line rather than being quietly absorbed.
 */

export type CauseKind = "cause" | "status" | "pickup" | "time" | "none";

export type Cause = {
  /** The group shown on screen and in the workbook. */
  label: string;
  kind: CauseKind;
};

/** Reason groups, in the order they are tried. */
const CAUSES: [RegExp, string][] = [
  [/รถติด|traffic|congestion|ในท่า|ท่าเรือ|ติดท่า/i, "การจราจร / ติดในท่า"],
  [/ยาง|รถเสีย|บังโคลน|เครื่อง|อุปกรณ์|หัวลาก|รถไม่พร้อม/i, "รถ / อุปกรณ์ไม่พร้อม"],
  [/พขร|รอรถ|ไม่มีรถ|รถไม่พอ|rotation|หมุนเวียน/i, "รถไม่พอ / รอพนักงานขับ"],
  [/ได้รับงาน|แจ้งช้า|แจ้งย้าย|เปลี่ยนแผน|เปลี่ยนสถานที่|hold/i, "ได้รับงานช้า / แผนเปลี่ยน"],
  [/รอเข้า|ตามรอบ|รอคิว|วนเข้า|คิว/i, "รอคิว / รอบเข้ารับ"],
  [/การ์ด|เอกสาร|ใบเปิด|ใบลาก|customs|ศุลกากร|พิธีการ/i, "เอกสาร / การ์ด"],
  [/rent|ตู้เปล่า|ไม่มีตู้|ลานตู้|demurrage|detention/i, "Rent / ตู้ไม่พร้อม"],
  [/แรงงาน|ลงสินค้า|โหลด|บรรจุ.*ช้า|รอลง/i, "แรงงาน / การลงสินค้า"],
];

/** Text that records what happened, not why it was late. */
const STATUS = /^(ถึงโรงงาน|ถึงหน้างาน|delivery completed|completed|บรรจุเสร็จ|กำลังเดินทาง|ได้รับงานแล้ว|เสร็จแล้ว|จบงาน|ส่งเสร็จ)/i;

/** A pickup appointment that was typed into the wrong column. */
const PICKUP = /^(รับตู้|รับสินค้า|pick\s*up|pickup)/i;

/** Nothing but a clock reading. */
const BARE_TIME = /^\d{1,2}[.:]\d{2}\s*(น\.?)?$/;

/**
 * What one job's reason text is.
 *
 * Order matters: the non-reasons are checked first, because "ได้รับงานแล้ว
 * กำลังเดินทาง" is a status note that happens to contain the words of a cause,
 * and reading it as one would put a job in the wrong bucket while looking
 * right.
 */
export function classifyReason(text: string): Cause {
  const value = (text ?? "").replace(/\s+/g, " ").trim();
  if (!value) return { label: "ไม่ได้ระบุสาเหตุ", kind: "none" };
  if (BARE_TIME.test(value)) return { label: "ไม่ใช่สาเหตุ · เวลาอย่างเดียว", kind: "time" };
  if (PICKUP.test(value)) return { label: "ไม่ใช่สาเหตุ · บันทึกนัดรับตู้", kind: "pickup" };
  if (STATUS.test(value)) return { label: "ไม่ใช่สาเหตุ · บันทึกสถานะ", kind: "status" };

  for (const [pattern, label] of CAUSES) {
    if (pattern.test(value)) return { label, kind: "cause" };
  }
  return { label: "อื่น ๆ", kind: "cause" };
}

/** The reason a job carries, from whichever column its category uses. */
export const reasonOf = (job: Job): string =>
  (job.reason ?? "").trim() || (job.remark ?? "").trim();

export type CauseRow = {
  label: string;
  kind: CauseKind;
  trips: number;
  minutes: number;
  /** The distinct wordings behind the group, commonest first. */
  wordings: [string, number][];
};

export type CauseBreakdown = {
  rows: CauseRow[];
  /** Late trips whose reason column explains the delay. */
  explained: number;
  /** Late trips whose column holds something that is not a reason, or nothing. */
  unexplained: number;
  total: number;
};

/**
 * Groups late trips by what their reason column says.
 *
 * Sorted by total delay rather than by count, because ten trips two minutes
 * late and one trip eight hours late are not the same problem, and a report
 * ordered by frequency alone puts the small one on top.
 */
export function delayCauses(late: { job: Job; late: number }[]): CauseBreakdown {
  const groups = new Map<string, CauseRow & { texts: Map<string, number> }>();

  late.forEach(({ job, late: minutes }) => {
    const text = reasonOf(job);
    const { label, kind } = classifyReason(text);
    const held = groups.get(label) ?? {
      label, kind, trips: 0, minutes: 0, wordings: [], texts: new Map<string, number>(),
    };
    held.trips += 1;
    held.minutes += minutes;
    if (text) held.texts.set(text, (held.texts.get(text) ?? 0) + 1);
    groups.set(label, held);
  });

  const rows: CauseRow[] = [...groups.values()]
    .map((group) => ({
      label: group.label,
      kind: group.kind,
      trips: group.trips,
      minutes: group.minutes,
      wordings: [...group.texts.entries()].sort((a, b) => b[1] - a[1]),
    }))
    // Real causes first and heaviest first; the non-reasons sit below them,
    // present but not competing for the top of the table.
    .sort((a, b) => {
      if ((a.kind === "cause") !== (b.kind === "cause")) return a.kind === "cause" ? -1 : 1;
      return b.minutes - a.minutes || b.trips - a.trips;
    });

  const explained = rows.filter((r) => r.kind === "cause").reduce((s, r) => s + r.trips, 0);
  return { rows, explained, unexplained: late.length - explained, total: late.length };
}
