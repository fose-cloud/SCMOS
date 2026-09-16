/**
 * How the LINE queue reads on screen.
 *
 * The API answers with a code — `not-your-job`, `many-jobs`, `ready-to-apply`.
 * A code is right for grouping and wrong for a person, so this turns each one
 * into a sentence, a tone, and a note about who has to do something about it.
 *
 * Kept apart from the screen because it is the part worth checking. A queue
 * that quietly files a refusal under the same colour as a message waiting for
 * approval is a queue that trains its reader to click through both.
 *
 * No imports on purpose — see the other modules under this folder.
 */

/** Who has to act, which is what the colour means. */
export type Tone =
  /** Ready for a person to approve. The only tone that offers a button. */
  | "ready"
  /** Somebody must set something up before this can ever work. */
  | "setup"
  /** A person has to choose or look. */
  | "attention"
  /** Correctly refused. Nothing to fix. */
  | "refused"
  /** Nothing to do; kept for the record. */
  | "quiet";

export type Outcome = {
  code: string;
  tone: Tone;
  /** What happened, in Thai. */
  label: string;
  /** What to do about it, or empty when there is nothing to do. */
  next: string;
};

/**
 * Every code the worker and the rule can produce.
 *
 * Listed rather than derived so that a code nobody has thought about shows up
 * as unknown instead of silently taking a neighbour's colour.
 */
const OUTCOMES: Outcome[] = [
  {
    code: "ready-to-apply", tone: "ready",
    label: "พร้อมอัปเดต — รอการอนุมัติ",
    next: "ตรวจแล้วกด “อนุมัติ” เพื่ออัปเดตสถานะงาน",
  },
  {
    code: "many-jobs", tone: "attention",
    label: "อ้างถึงงานได้หลายรายการ",
    next: "เลือกงานที่ต้องการอัปเดตก่อน",
  },
  {
    code: "unknown-group", tone: "setup",
    label: "ยังไม่ได้ผูกกลุ่ม LINE นี้กับผู้ขนส่ง",
    next: "ผูกกลุ่มในหัวข้อ “กลุ่ม LINE” ด้านล่าง",
  },
  {
    code: "group-inactive", tone: "setup",
    label: "กลุ่มนี้ถูกปิดใช้งาน",
    next: "เปิดใช้งานกลุ่มอีกครั้งถ้ายังใช้อยู่",
  },
  {
    code: "group-not-vendor", tone: "refused",
    label: "กลุ่มนี้ไม่ใช่กลุ่มผู้ขนส่ง",
    next: "",
  },
  {
    code: "not-your-job", tone: "refused",
    label: "งานที่อ้างถึงไม่ได้อยู่กับผู้ขนส่งรายนี้",
    next: "",
  },
  {
    code: "no-such-job", tone: "attention",
    label: "ไม่พบงานที่อ้างถึงในระบบ",
    next: "ตรวจว่าเลขงาน เลขตู้ หรือทะเบียนพิมพ์ถูก หรืองานยังไม่ได้นำเข้า",
  },
  // A message with nothing to find the job by. "no-job-number" is what the
  // rows stored before 16 Sep 2026 carry; "no-reference" is what the parser
  // says now that a container or a plate finds the job as well.
  {
    code: "no-reference", tone: "attention",
    label: "ไม่มีเลขงาน เลขตู้ หรือทะเบียนรถในข้อความ",
    next: "ถามผู้ขนส่งให้ส่งเลขตู้หรือทะเบียนรถมาด้วย",
  },
  {
    code: "no-job-number", tone: "attention",
    label: "ไม่มีเลขงานในข้อความ",
    next: "ถามผู้ขนส่งให้ส่งเลขงานมาด้วย",
  },
  {
    code: "many-containers", tone: "attention",
    label: "ข้อความมีเลขตู้หลายตู้",
    next: "อัปเดตทีละงานในตารางงาน หรือขอให้ส่งแยกข้อความ",
  },
  {
    code: "many-job-numbers", tone: "attention",
    label: "ข้อความมีเลขงานหลายเลข",
    next: "อัปเดตทีละงานในตารางงาน หรือขอให้ส่งแยกข้อความ",
  },
  {
    code: "many-times", tone: "attention",
    label: "ข้อความมีเวลาหลายค่า อ่านไม่ออกว่าเวลาไหนคือเวลาถึง",
    next: "อ่านข้อความเดิมแล้วอัปเดตเอง",
  },
  {
    code: "nothing-understood", tone: "attention",
    label: "อ้างถึงงานได้ แต่ไม่พบสถานะในข้อความ",
    next: "อ่านข้อความเดิมแล้วอัปเดตเอง",
  },
  {
    code: "no-status", tone: "quiet",
    label: "มีเลขงานแต่ไม่มีสถานะ",
    next: "",
  },
  {
    code: "backwards", tone: "attention",
    label: "ข้อความนี้จะย้อนสถานะงาน",
    next: "ถ้าถูกต้องจริง ให้แก้สถานะในตารางงานเอง",
  },
  {
    code: "job-closed", tone: "refused",
    label: "งานปิดแล้ว",
    next: "",
  },
  {
    code: "job-held", tone: "attention",
    label: "งานถูกพักไว้",
    next: "ปลดการพักงานก่อนถ้าต้องการอัปเดต",
  },
  {
    code: "job-status-unknown", tone: "attention",
    label: "สถานะปัจจุบันของงานไม่ใช่รหัสมาตรฐาน",
    next: "แก้สถานะงานให้เป็นรหัสมาตรฐานก่อน",
  },
  {
    code: "not-on-ladder", tone: "attention",
    label: "งานประเภทนี้ไม่มีขั้นตอนที่แจ้งมา",
    next: "",
  },
  {
    code: "already-there", tone: "quiet",
    label: "งานอยู่ที่สถานะนี้อยู่แล้ว",
    next: "",
  },
  {
    code: "low-confidence", tone: "attention",
    label: "อ่านข้อความได้ไม่ชัดเจนพอ",
    next: "อ่านข้อความเดิมแล้วอัปเดตเอง",
  },
  {
    code: "processing-failed", tone: "attention",
    label: "ประมวลผลไม่สำเร็จ",
    next: "ลองใหม่ภายหลัง หรือแจ้งผู้ดูแลระบบ",
  },
  {
    code: "dismissed", tone: "quiet",
    label: "ปิดโดยเจ้าหน้าที่",
    next: "",
  },
];

const BY_CODE = new Map(OUTCOMES.map((one) => [one.code, one]));

/**
 * What a code means, or an honest admission that it is not known.
 *
 * An unrecognised code keeps the code as its own label rather than being
 * folded into a general "error". If the API grows a code this screen has not
 * been taught, the reader should see the code and be able to ask about it.
 */
export function describe(code: string | undefined | null): Outcome {
  const key = String(code ?? "").trim();
  if (key.length === 0) return { code: "", tone: "quiet", label: "—", next: "" };
  return BY_CODE.get(key) ?? {
    code: key, tone: "attention", label: key,
    next: "รหัสนี้ยังไม่มีคำอธิบายในหน้าจอ — แจ้งผู้ดูแลระบบ",
  };
}

/**
 * Whether opening this row could lead to an approval.
 *
 * Two codes, not one: a number covering several rows is not ready, but it is
 * one choice away from being ready, and hiding it behind the same treatment as
 * a refusal would leave those messages unworked. 236 job numbers in the
 * register sit on more than one row, so this is the common case, not the odd
 * one.
 */
export function isActionable(code: string | undefined | null): boolean {
  const tone = describe(code).tone;
  return tone === "ready" || tone === "attention";
}

export type Counted = { tone: Tone; label: string; count: number };

/** The order the bands are shown in: what to do first, first. */
const BANDS: { tone: Tone; label: string }[] = [
  { tone: "ready", label: "รออนุมัติ" },
  { tone: "attention", label: "ต้องตรวจสอบ" },
  { tone: "setup", label: "ต้องตั้งค่า" },
  { tone: "refused", label: "ปฏิเสธแล้ว" },
  { tone: "quiet", label: "ไม่ต้องทำอะไร" },
];

/**
 * How many messages sit in each band.
 *
 * Bands with nothing in them are dropped. A row of zeroes reads as a system
 * with problems in every category; the useful thing is the two or three that
 * actually have something in them.
 */
export function summarise(codes: readonly (string | undefined | null)[]): Counted[] {
  const counts = new Map<Tone, number>();
  for (const code of codes) {
    const tone = describe(code).tone;
    counts.set(tone, (counts.get(tone) ?? 0) + 1);
  }
  return BANDS
    .map((band) => ({ ...band, count: counts.get(band.tone) ?? 0 }))
    .filter((band) => band.count > 0);
}

/** The colours each band is drawn in: border, background, text. */
export function palette(tone: Tone): { line: string; fill: string; ink: string } {
  switch (tone) {
    case "ready": return { line: "#16A34A", fill: "#F0FDF4", ink: "#15803D" };
    case "setup": return { line: "#D97706", fill: "#FFFBEB", ink: "#B45309" };
    case "attention": return { line: "#2563EB", fill: "#EFF6FF", ink: "#1D4ED8" };
    case "refused": return { line: "#DC2626", fill: "#FEF2F2", ink: "#B91C1C" };
    default: return { line: "#CBD5E1", fill: "#F8FAFC", ink: "#64748B" };
  }
}

/**
 * A confidence as a percentage, or empty when there is none.
 *
 * Zero is a real reading — the parser understood nothing — and has to show as
 * 0% rather than being treated as missing, because "we did not read this" is
 * exactly what the reviewer needs to know.
 */
export function confidenceLabel(value: number | undefined | null): string {
  if (typeof value !== "number" || !Number.isFinite(value)) return "";
  return `${Math.round(value * 100)}%`;
}

/**
 * What a message pointed at, for the queue's reference column: the job number
 * when there is one, else the container, else the plates. Since 16 Sep 2026
 * a haulier's message has carried a box and a plate and no number.
 */
export function referenceLabel(event: {
  jobNumber?: string | null; container?: string | null; plates?: readonly string[] | null;
}): string {
  const number = String(event.jobNumber ?? "").trim();
  if (number.length > 0) return number;
  const container = String(event.container ?? "").trim();
  if (container.length > 0) return container;
  const plates = (event.plates ?? []).map((one) => String(one).trim()).filter((one) => one.length > 0);
  return plates.join(" / ");
}

/**
 * The status the parser read, as a person would say it. ARRIVED is the one
 * word the parser cannot settle alone — at the plant is the delivery on an
 * import and the pickup on an export — so it is named as such.
 */
export function statusLabel(code: string | undefined | null): string {
  const key = String(code ?? "").trim();
  if (key.length === 0) return "";
  if (key === "ARRIVED") return "ถึงหน้างาน (ตามประเภทงาน)";
  return key;
}

/** When the message arrived, in Bangkok, short enough for a table cell. */
export function whenLabel(iso: string | undefined | null): string {
  const text = String(iso ?? "");
  if (text.length === 0) return "";
  const at = new Date(text);
  if (Number.isNaN(at.getTime())) return "";
  return at.toLocaleString("th-TH", {
    timeZone: "Asia/Bangkok",
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit",
  });
}
