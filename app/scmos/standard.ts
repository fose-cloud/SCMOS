import { STATUS_LADDER } from "./theme";
import { lateLabel, lateMinutes } from "./util";

/**
 * The SCMOS operational data standard.
 *
 * Every figure the dashboard reports is computed from these fields, so a value
 * that does not parse is not a cosmetic problem — it silently drops a job out of
 * the KPI it belongs to. The rules below were derived from the 381 real jobs in
 * the July plan rather than invented, and split into two tiers:
 *
 *   normalise — unambiguous clean-ups applied automatically (thousand
 *               separators, phone dashes, stray zero-width characters). The
 *               operator's intent is not in question, only the typing.
 *   validate  — values a human has to resolve (a B/L number typed into the
 *               container field, a free-text status). Never guessed at.
 */

export type Severity = "error" | "warning";

export type Issue = {
  field: string;
  label: string;
  value: string;
  message: string;
  expected: string;
  example: string;
  severity: Severity;
};

/** An automatic correction, kept so the change stays auditable. */
export type Fix = { field: string; label: string; from: string; to: string; note: string };

type Rule = {
  field: string;
  label: string;
  expected: string;
  example: string;
  test: (value: string) => boolean;
  /** Returns a corrected value plus a note, or null when it cannot be fixed safely. */
  normalise?: (value: string) => { value: string; note: string } | null;
  /** Only checked for these categories; omitted means every category. */
  cats?: string[];
  severity?: Severity;
};

/* ------------------------------------------------------------ primitives */

/**
 * A date in the right shape *and* on the calendar.
 *
 * The shape alone accepted 31/02/2026, which is a typo that then travels: it
 * sorts, it filters, it lands in a report, and nobody notices because it looks
 * like a date. Checked against the 825 dates in the delivered plan before
 * tightening this — none of them fail it, so nothing that exists is being
 * called wrong.
 */
function isRealDate(value: string): boolean {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(value);
  if (!m) return false;
  const day = Number(m[1]);
  const month = Number(m[2]);
  const year = Number(m[3]);
  const at = new Date(Date.UTC(year, month - 1, day));
  return at.getUTCDate() === day && at.getUTCMonth() === month - 1 && at.getUTCFullYear() === year;
}
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;
const CONTAINER = /^[A-Z]{4}\d{7}$/;
const PHONE = /^0\d{1,2}-\d{7,8}$/;
const NUMBER = /^\d+(\.\d+)?$/;

/**
 * Thai plates come in the old series (70-1600, ผห-7787), the heavy-vehicle
 * three-digit series (700-2716) and the current one (1ฒผ9205). A province may
 * be appended — with or without a space — and is stripped before matching.
 */
const PLATE_CORE = /^([0-9]{1,3}|[ก-ฮ]{1,3}|[0-9][ก-ฮ]{2})[-\s]?\d{3,4}$/;

/**
 * Zero-width and non-breaking characters pasted in from Excel and LINE.
 * One real driver number ends in U+200B, which silently fails every match.
 */
const INVISIBLE = /[\u200B-\u200D\uFEFF\u00A0]/g;

export function clean(value: unknown): string {
  return String(value ?? "").replace(INVISIBLE, "").trim();
}

/**
 * The operators' several ways of writing "there isn't one".
 *
 * Treating these as data is how a blank column becomes a false KPI. The same
 * list exists in Formats.Clean on the API, and the two had drifted: the browser
 * blanked "--" and "na" while the API did not, and the API blanked an en dash
 * and "null" while the browser did not. A value one side calls empty and the
 * other calls malformed is a job that is fine on screen and flagged in the
 * register, or the reverse. Change one, change the other.
 */
export const BLANK_VALUE = /^(-+|—|–|n\/?a|none|null|ไม่มี)$/i;

/**
 * "70-4466 สุโขทัย" · "74-6705.ส.ป" · "700-4761กทม" -> the plate alone.
 * Matches the whole Thai block, not just ก-ฮ: province names carry vowels and
 * tone marks (สุโขทัย) that sit outside the consonant range.
 */
function stripProvince(plate: string): string {
  return plate.replace(/[\s.]*[฀-๿][฀-๿\s.]*$/, "").trim();
}

/**
 * Puts a date into DD/MM/YYYY when the reading is not in doubt.
 *
 * The plan files arrive with 24/7/26, 13/07/26, 7/14/26 and 2026-07-24 all in
 * play, and one column can carry two orders at once — the July upload has 189
 * closing dates written day-first and 145 written month-first. So the order is
 * only taken when one component settles it (a 24 cannot be a month, a 14 cannot
 * be a day-first month). Anything that reads both ways — 01/07/26 — is left for
 * a person, because guessing turns 1 July into 7 January silently.
 */
export function normaliseDate(value: string): { value: string; note: string } | null {
  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})$/.exec(value);
  const parts = iso
    ? { a: Number(iso[3]), b: Number(iso[2]), y: iso[1] }
    : (() => {
      const m = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})$/.exec(value);
      return m ? { a: Number(m[1]), b: Number(m[2]), y: m[3] } : null;
    })();
  if (!parts) return null;

  let day = parts.a;
  let month = parts.b;
  if (!iso) {
    if (parts.a > 12 && parts.b <= 12) { day = parts.a; month = parts.b; }
    else if (parts.b > 12 && parts.a <= 12) { day = parts.b; month = parts.a; }
    else return null; // reads both ways — flag it instead
  }
  if (day < 1 || day > 31 || month < 1 || month > 12) return null;

  let year = Number(parts.y);
  if (parts.y.length === 2) year += 2000;
  else if (parts.y.length !== 4) return null;
  // Thai Buddhist years land in the 2500s; the plan is kept in Gregorian.
  if (year > 2400) year -= 543;
  if (year < 2000 || year > 2100) return null;

  const fixed = String(day).padStart(2, "0") + "/" + String(month).padStart(2, "0") + "/" + year;
  return fixed === value ? null : { value: fixed, note: "จัดรูปแบบวันที่เป็น DD/MM/YYYY" };
}

/**
 * Accepts 09.00, 09,00, 9:00, 16'30, 22.:00 and "9.00 น." — every separator the
 * plan files have turned up so far. The hour and minute have to be digits; a
 * cell that says "รอรถ" is a note, not a time, and stays flagged.
 */
function normaliseTime(value: string): { value: string; note: string } | null {
  const m = /^(\d{1,2})\s*[.:,'’]+\s*(\d{2})/.exec(value.replace(/\s*น\.?\s*$/, ""));
  if (!m) return null;
  const hour = Number(m[1]);
  const minute = Number(m[2]);
  if (hour > 23 || minute > 59) return null;
  const fixed = String(hour).padStart(2, "0") + ":" + m[2];
  return fixed === value ? null : { value: fixed, note: "จัดรูปแบบเวลาเป็น HH:MM" };
}

/** "2,352.24 +2,014.00" -> 4366.24 — two pieces loaded on one truck. */
function sumWeight(value: string): number | null {
  const parts = value.split("+").map((p) => p.replace(/,/g, "").trim()).filter(Boolean);
  if (parts.length < 2) return null;
  let total = 0;
  for (const p of parts) {
    if (!NUMBER.test(p)) return null;
    total += Number(p);
  }
  return Math.round(total * 100) / 100;
}

/* ----------------------------------------------------------------- rules */

export const RULES: Rule[] = [
  {
    field: "date", label: "Plan date", expected: "DD/MM/YYYY", example: "24/07/2026",
    test: isRealDate, normalise: normaliseDate,
  },
  {
    field: "planTime", label: "Plan loading time", expected: "HH:MM (24 ชม.)", example: "08:00",
    test: (v) => TIME.test(v), normalise: normaliseTime,
  },
  {
    field: "arrDate", label: "Arrival date", expected: "DD/MM/YYYY", example: "24/07/2026",
    test: isRealDate, normalise: normaliseDate,
  },
  {
    field: "arrTime", label: "Arrival time", expected: "HH:MM (24 ชม.)", example: "15:00",
    test: (v) => TIME.test(v), normalise: normaliseTime,
  },
  {
    field: "closingDate", label: "Closing date", expected: "DD/MM/YYYY", example: "26/07/2026",
    cats: ["EXPORT"], test: isRealDate, normalise: normaliseDate,
  },
  {
    field: "closingTime", label: "Closing time", expected: "HH:MM (24 ชม.)", example: "16:00",
    cats: ["EXPORT"], test: (v) => TIME.test(v), normalise: normaliseTime,
  },
  {
    // The fourth time column on the grid, and the only one that had no rule —
    // so "8:00" typed into it stayed "8:00" while the other three were tidied
    // to 08:00. Not restricted by category: it belongs to import, but a time
    // sitting in the field on any job is still a time and should read the same.
    field: "pickupTime", label: "Pickup plan time", expected: "HH:MM (24 ชม.)", example: "08:00",
    test: (v) => TIME.test(v), normalise: normaliseTime,
  },
  {
    field: "container", label: "Container no.", expected: "ตัวอักษร 4 ตัว + ตัวเลข 7 ตัว", example: "MRSU4470591",
    test: (v) => CONTAINER.test(v),
    normalise: (v) => {
      const up = v.toUpperCase().replace(/[\s-]/g, "");
      if (up === v || !CONTAINER.test(up)) return null;
      return { value: up, note: "ตัดช่องว่างและทำเป็นตัวพิมพ์ใหญ่" };
    },
  },
  {
    field: "weight", label: "Weight (kg)", expected: "ตัวเลขล้วน หน่วยกิโลกรัม", example: "8689",
    test: (v) => NUMBER.test(v),
    normalise: (v) => {
      const summed = sumWeight(v);
      if (summed !== null) {
        return { value: String(summed), note: "รวมน้ำหนักหลายรายการเป็นยอดเดียว" };
      }
      // "1,848 .00" — separators plus a stray space before the decimal — and
      // ".2,502.000" — a stray leading dot from the spreadsheet.
      const stripped = v.replace(/^[.\s]+/, "").replace(/[,\s]/g, "");
      if (stripped !== v && NUMBER.test(stripped)) {
        return { value: stripped, note: "ตัดเครื่องหมายคั่นหลักพันและอักขระนำหน้า" };
      }
      return null;
    },
  },
  {
    field: "contact", label: "Driver contact", expected: "0XX-XXXXXXX", example: "087-6149047",
    test: (v) => PHONE.test(v),
    normalise: (v) => {
      let digits = v.replace(/\D/g, "");
      // Thai numbers all start with 0; a nine-digit mobile lost it in Excel.
      if (digits.length === 9 && !digits.startsWith("0")) digits = "0" + digits;
      if (digits.length < 9 || digits.length > 10 || !digits.startsWith("0")) return null;
      const fixed = digits.slice(0, 3) + "-" + digits.slice(3);
      return fixed === v ? null : { value: fixed, note: "จัดรูปแบบเบอร์โทรเป็น 0XX-XXXXXXX" };
    },
  },
  {
    field: "licence", label: "Truck licence", expected: "ทะเบียนไทย", example: "70-1600",
    test: (v) => PLATE_CORE.test(stripProvince(v)),
  },
];

/* ------------------------------------------------------------- normalise */

/**
 * Applies every safe correction in place and returns what changed. A "-" in any
 * field is the operators' way of writing "none", so it becomes empty.
 */
export function normaliseJob(job: Record<string, unknown>): Fix[] {
  const fixes: Fix[] = [];

  for (const rule of RULES) {
    const raw = String(job[rule.field] ?? "");
    const value = clean(raw);

    if (value !== raw) job[rule.field] = value;
    if (!value) continue;

    if (BLANK_VALUE.test(value)) {
      job[rule.field] = "";
      continue;
    }
    if (rule.test(value)) continue;

    const fixed = rule.normalise?.(value);
    if (fixed && rule.test(fixed.value)) {
      job[rule.field] = fixed.value;
      fixes.push({ field: rule.field, label: rule.label, from: value, to: fixed.value, note: fixed.note });
    }
  }

  const moved = movePickupNote(job);
  if (moved) fixes.push(moved);

  const pickup = splitPickup(job);
  if (pickup) fixes.push(pickup);

  return fixes;
}

/**
 * A pickup note holds a date and a time in one sentence.
 *
 * The import sheets record it as free text — "รับตู้ 24.07.26 .01.00 น." — one
 * cell carrying two facts, which is why it could never be sorted or compared.
 * The two are pulled apart into their own fields.
 *
 * The fragments go through `normaliseDate` and `normaliseTime` rather than
 * being parsed here, so the pickup note gets the same reading of a Buddhist
 * year, the same list of separators, and the same refusal to guess between
 * 1 July and 7 January that every other date on the job gets. A note neither
 * can read is left exactly as it was written: it is still what somebody
 * recorded, and an unreadable note is better than a wrong date.
 */
const PICKUP_DATE = /(\d{1,2}\s*[./-]\s*\d{1,2}\s*[./-]\s*\d{2,4})/;
const PICKUP_TIME = /(\d{1,2}\s*[.:,'’]\s*\d{2})/;
const IS_DATE = /^\d{2}\/\d{2}\/\d{4}$/;
const IS_TIME = /^\d{2}:\d{2}$/;

/**
 * A fragment as a date, whether it needed correcting or was already right.
 *
 * `normaliseDate` refuses 02.07.26 on purpose — it reads both ways and guessing
 * turns 2 July into 7 February in silence. Here the row itself settles it: a
 * container is collected within days of the loading date the job already
 * carries, so of the two readings the one that lands near that date is the one
 * the operator meant. Both readings landing near it, or neither, and the note
 * is left exactly as written.
 *
 * This is evidence off the same row, not a house rule about which number comes
 * first. A pickup note on a job with no plan date is still refused.
 */
function asDate(fragment: string, planDate: string): string {
  const tidy = fragment.replace(/\s+/g, "");
  if (IS_DATE.test(tidy)) return tidy;

  const direct = normaliseDate(tidy);
  if (direct) return direct.value;

  const m = /^(\d{1,2})[/.-](\d{1,2})[/.-](\d{2,4})$/.exec(tidy);
  if (!m) return "";
  const plan = dayNumberOf(planDate);
  if (plan === null) return "";

  const dayFirst = readAs(m[1], m[2], m[3]);
  const monthFirst = readAs(m[2], m[1], m[3]);
  if (dayFirst === monthFirst) return dayFirst;

  const byDay = distance(dayFirst, plan);
  const byMonth = distance(monthFirst, plan);
  // The nearer reading wins, and only if it is near at all. Equal distances,
  // or both a season away, and the note stays as written.
  if (byDay === byMonth) return "";
  const [best, gap] = byDay < byMonth ? [dayFirst, byDay] : [monthFirst, byMonth];
  return gap <= 30 ? best : "";
}

/**
 * dd/MM/yyyy from three fragments, taken at face value.
 *
 * Deliberately does not go back through `normaliseDate`: that is the function
 * that refused the ambiguous reading in the first place, and asking it again
 * returns nothing both ways round. This builds each candidate so the caller can
 * weigh them against the loading date. The year rules are the same ones —
 * two digits are this century, a Buddhist year is converted.
 */
function readAs(day: string, month: string, year: string): string {
  const d = Number(day);
  const m = Number(month);
  if (!(d >= 1 && d <= 31) || !(m >= 1 && m <= 12)) return "";

  let y = Number(year);
  if (year.length === 2) y += 2000;
  else if (year.length !== 4) return "";
  if (y > 2400) y -= 543;
  if (y < 2000 || y > 2100) return "";

  return `${String(d).padStart(2, "0")}/${String(m).padStart(2, "0")}/${y}`;
}

/** dd/MM/yyyy as a count of days, or null. */
function dayNumberOf(date: string): number | null {
  const m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec((date ?? "").trim());
  return m ? Math.floor(Date.UTC(+m[3], +m[2] - 1, +m[1]) / 86_400_000) : null;
}

/**
 * How many days a candidate sits from the loading date.
 *
 * Collecting a container ahead of the loading date is the whole point of the
 * note, so the pickup is days from the load, not months. A reading that lands a
 * season away is the wrong one — Infinity so it always loses.
 */
function distance(candidate: string, plan: number): number {
  const at = dayNumberOf(candidate);
  return at === null ? Number.POSITIVE_INFINITY : Math.abs(at - plan);
}

function asTime(fragment: string): string {
  const tidy = fragment.replace(/\s+/g, "");
  if (IS_TIME.test(tidy)) return tidy;
  return normaliseTime(tidy)?.value ?? "";
}

/**
 * Text that is a note about collecting a container, not a loading time.
 *
 * "รับตู้", a trailing "น.", or a dotted date like 01.04.26 — the three shapes
 * the plan sheets actually use. Kept beside the rule that reads it rather than
 * in whichever screen noticed the problem.
 */
const PICKUP_TEXT = /รับตู้|น\.\s*$|\d{1,2}\.\d{2}\.\d{2}/;

/**
 * A pickup note typed into the loading-time column, put where it belongs.
 *
 * PLAN LOADING TIME is a time. When it holds "รับตู้ 01.04.26 08.00" the job has
 * no loading time at all — the on-time calculation silently skips it, and the
 * column reads as though somebody planned an appointment. The note itself is
 * worth keeping; it is the column that is wrong.
 *
 * This ran only when somebody opened the cleanup screen and pressed the button,
 * which meant a fresh import carried the problem until they did. It runs on
 * every import now, and on every load, because it is a reading of the data
 * rather than a decision about it.
 *
 * Nothing is thrown away. An empty pickup column takes the note; a pickup column
 * that already says the same thing keeps what it has; one that says something
 * different keeps that, and the note goes to the remark rather than overwriting
 * an appointment somebody recorded.
 */
function movePickupNote(job: Record<string, unknown>): Fix | null {
  const planTime = clean(job.planTime);
  if (!planTime || TIME.test(planTime) || !PICKUP_TEXT.test(planTime)) return null;

  const pickup = clean(job.pickupPlan);
  let note = "ย้ายไปช่อง Pickup Plan — ไม่ใช่เวลานัดโหลด";

  if (!pickup) {
    job.pickupPlan = planTime;
  } else if (pickup === planTime) {
    note = "ซ้ำกับ Pickup Plan ที่มีอยู่แล้ว";
  } else {
    job.remark = [clean(job.remark), planTime].filter(Boolean).join(" · ");
    note = "ย้ายไปหมายเหตุ — Pickup Plan มีข้อความอื่นอยู่แล้ว";
  }

  job.planTime = "";
  return { field: "planTime", label: "Plan loading time", from: planTime, to: "", note };
}

function splitPickup(job: Record<string, unknown>): Fix | null {
  const note = clean(job.pickupPlan);
  // Nothing to do once it is a plain date — this has already been split, or was
  // typed straight into the date column.
  if (!note || IS_DATE.test(note)) return null;

  const dateAt = PICKUP_DATE.exec(note);
  if (!dateAt) return null;
  const date = asDate(dateAt[1], clean(job.date));
  if (!date) return null;

  // The time is looked for after the date, so the day of a "24.07.26" is not
  // mistaken for an hour.
  const after = note.slice(dateAt.index + dateAt[1].length);
  const timeAt = PICKUP_TIME.exec(after);
  const time = timeAt ? asTime(timeAt[1]) : "";

  job.pickupPlan = date;
  if (time && !clean(job.pickupTime)) job.pickupTime = time;

  return {
    field: "pickupPlan",
    label: "Pickup plan",
    from: note,
    to: time ? `${date} ${time}` : date,
    note: time ? "แยกเป็นวันที่และเวลารับตู้" : "อ่านวันที่รับตู้ออกจากข้อความ",
  };
}

/**
 * A date somebody typed, read day-first.
 *
 * `normaliseDate` refuses 01/08/2569, because 01 and 08 both read as a month
 * and guessing turns the first of August into the eighth of January. That
 * refusal is right for a value off a spreadsheet nobody can ask about.
 *
 * It is wrong for a box a person has just typed into under a label that says
 * วว/ดด/ปปปป. There the first number is the day because that is what the field
 * asked for, and refusing means a filter that silently returns nothing — which
 * is exactly what it did before this existed.
 *
 * Same year rules as everywhere else: two digits are this century, and a
 * Buddhist year is converted, so 01/08/2569 is the first of August 2026.
 */
export function dayFirstDate(value: string): string {
  const m = /^(\d{1,2})\s*[/.-]\s*(\d{1,2})\s*[/.-]\s*(\d{2,4})$/.exec((value ?? "").trim());
  if (!m) {
    // An ISO date is unambiguous, so it is read as written.
    return normaliseDate((value ?? "").trim())?.value
      ?? (IS_DATE.test((value ?? "").trim()) ? (value ?? "").trim() : "");
  }
  return readAs(m[1], m[2], m[3]);
}

export function ruleFor(field: string): Rule | undefined {
  return RULES.find((r) => r.field === field);
}

/**
 * Normalises a single field after an inline edit. Returns the fix that was
 * applied, or null when the value was already fine or cannot be corrected
 * without guessing — the caller then warns and leaves the typing intact.
 */
export function normaliseField(job: Record<string, unknown>, field: string): Fix | null {
  const rule = ruleFor(field);
  if (!rule) return null;

  const raw = String(job[field] ?? "");
  const value = clean(raw);
  if (value !== raw) job[field] = value;
  if (!value || rule.test(value)) return null;

  const fixed = rule.normalise?.(value);
  if (!fixed || !rule.test(fixed.value)) return null;

  job[field] = fixed.value;
  return { field, label: rule.label, from: value, to: fixed.value, note: fixed.note };
}

/* -------------------------------------------------------------- validate */

/** Statuses accepted for a category, used both to validate and to populate the picker. */
export function statusesFor(cat: string): string[] {
  return STATUS_LADDER[cat] || STATUS_LADDER.IMPORT;
}

/**
 * The free-text status the plan files carry, as the ladder code it means.
 *
 * Two vocabularies reach us. The operators type Thai — "รอรถ", "ส่งมอบเสร็จ" —
 * and the older English exports carry phrases like "Waiting Truck" that were
 * the status set before it became a controlled one. Both are read here, in one
 * place, because this used to be answered twice: the import invented its own
 * default and cleanup.ts kept a table that mapped Thai onto the *old* English
 * words and then checked them against the *new* code ladder, so it matched
 * nothing at all and had been quietly doing nothing.
 *
 * Anything unrecognised returns null and the original text stays on the job,
 * flagged. Guessing would turn "Delay due to Traffic" into a stage nobody
 * chose; leaving it visible puts it in front of the person who knows.
 *
 * Mirrors JobStatus.FromLegacy on the server — the API stores what it is sent,
 * so the two have to agree. Change one, change the other.
 */
const THAI_STATUS: [RegExp, string, string?][] = [
  [/^ได้รับงาน/, "truck confirmed"],
  [/^รอรถ|รถอัพเดท|รออัพเดท/, "waiting truck"],
  [/^กำลังรอรับตู้|^รอรับตู้/, "truck confirmed"],
  [/ได้ตู้แล้ว|กำลังเดินทาง|ออกจากท่า/, "in transit"],
  [/^ถึงโรงงาน|^ถึงลูกค้า/, "arrived customer", "arrived plant"],
  [/^รอการ์ด|^รอเอกสาร|^รอข้อมูล/, "waiting information"],
  [/^ส่งมอบเสร็จ|^ส่งเสร็จ/, "delivery completed"],
  [/^เสร็จ|^ปิดงาน/, "completed"],
  [/^ยกเลิก/, "cancelled"],
  [/^ล่าช้า|^ดีเลย์/, "delayed"],
];

const LEGACY_STATUS: Record<string, string> = {
  "new": "RECEIVED",
  "waiting information": "WAITING_CS",
  "waiting truck": "WAITING_SUPPLIER",
  "scheduled": "WAITING_SUPPLIER",
  "truck confirmed": "SUPPLIER_CONFIRMED",
  "truck assigned": "TRUCK_ASSIGNED",
  "driver assigned": "TRUCK_ASSIGNED",
  "empty pickup": "PICKED_UP",
  "container pickup": "PICKED_UP",
  "pickup": "PICKED_UP",
  "arrived plant": "PICKED_UP",
  "loading": "LOADING",
  "loading completed": "LOADING",
  "departed port": "IN_TRANSIT",
  "departed plant": "IN_TRANSIT",
  "in transit": "IN_TRANSIT",
  "arrived customer": "DELIVERED",
  "delivery started": "DELIVERED",
  "port return": "DELIVERED",
  "empty return pending": "DELIVERED",
  "delivered": "DELIVERED",
  "empty returned": "CONTAINER_RETURNED",
  "gate-in completed": "CONTAINER_RETURNED",
  "delivery completed": "COMPLETED",
  "completed": "COMPLETED",
  "cancelled": "CANCELLED",
  // A delay says a job stopped but not where it stopped, so it becomes a hold,
  // which is what it actually is. The stage still needs a person.
  "delayed": "HOLD",
};

/**
 * The status a job should carry, or null to leave what it has alone.
 *
 * Null means two different things and both are correct: the value is already a
 * ladder code, or it is text nobody here can safely read.
 */
export function legacyStatus(value: unknown, cat: string): string | null {
  const text = clean(value);
  if (!text) return null;

  const ladder = statusesFor(cat);
  if (ladder.indexOf(text) >= 0) return null;

  let phrase = text.toLowerCase();
  for (const [test, importPhrase, exportPhrase] of THAI_STATUS) {
    if (!test.test(text)) continue;
    phrase = cat === "EXPORT" && exportPhrase ? exportPhrase : importPhrase;
    break;
  }

  const code = LEGACY_STATUS[phrase];
  // A code off the wrong ladder is not an answer — LOADING is a real step for
  // an export and meaningless for an import, and writing it anyway would put a
  // job on a rung its own category does not have.
  return code && ladder.indexOf(code) >= 0 ? code : null;
}

/** What a row imported with no status of its own starts as: waiting on a carrier. */
export const DEFAULT_STATUS = "WAITING_SUPPLIER";

/**
 * Where each kind of job explains a delay.
 *
 * Not one field for all three, because the grids do not carry one field for all
 * three: the import grid has REASON/DELAY and no remark column, export and
 * delivery have REMARK and no reason column. Pointing the rule at a field the
 * operator cannot see would raise an error nobody is able to clear, which is
 * worse than not asking — they would learn to ignore the red.
 */
const DELAY_REASON_FIELD: Record<string, [field: string, label: string, column: string]> = {
  IMPORT: ["reason", "Reason / Delay", "REASON/DELAY"],
  EXPORT: ["remark", "Remark", "REMARK"],
  DELIVERY: ["remark", "Remark", "REMARK"],
};

export function validateJob(job: Record<string, unknown>): Issue[] {
  const issues: Issue[] = [];
  const cat = String(job.cat ?? "IMPORT");

  for (const rule of RULES) {
    if (rule.cats && rule.cats.indexOf(cat) < 0) continue;
    const value = clean(job[rule.field]);
    if (!value) continue;
    if (rule.test(value)) continue;
    issues.push({
      field: rule.field,
      label: rule.label,
      value,
      message: "รูปแบบไม่ถูกต้อง",
      expected: rule.expected,
      example: rule.example,
      severity: rule.severity ?? "error",
    });
  }

  const status = clean(job.status);
  const allowed = statusesFor(cat);
  if (status && allowed.indexOf(status) < 0) {
    issues.push({
      field: "status",
      label: "Status",
      value: status,
      message: "ไม่อยู่ในชุดสถานะของงาน " + cat,
      expected: "เลือกจากรายการสถานะ " + cat,
      example: allowed.slice(0, 3).join(" / "),
      severity: "error",
    });
  }

  // A late job has to say why.
  //
  // Late is the plan against the arrival — the planned loading date and time
  // against the date and time the truck actually got there — which is the same
  // subtraction the KPI uses, so a job the dashboard counts as late is exactly a
  // job this asks about. No grace period: the figure reported upward has none,
  // and somebody will later be asked to explain every minute of it.
  //
  // It is an error rather than one of the missing-value flags because those are
  // cleared once a job is done, and this is the one question that only gets
  // asked afterwards. Management reads a delay report weeks later; a reason
  // that disappeared the moment the job closed is a reason nobody ever wrote.
  const late = lateMinutes({
    date: clean(job.date),
    planTime: clean(job.planTime),
    arrDate: clean(job.arrDate),
    arrTime: clean(job.arrTime),
  });
  if (late !== null && late > 0) {
    const [field, label, column] = DELAY_REASON_FIELD[cat] ?? DELAY_REASON_FIELD.IMPORT;
    if (!clean(job[field])) {
      issues.push({
        field,
        label,
        value: "",
        message: `รถถึงช้ากว่าแผน ${lateLabel(late)} แต่ยังไม่ได้ระบุสาเหตุ`,
        expected: `ระบุสาเหตุที่ล่าช้าในช่อง ${column}`,
        example: "รถติดในท่า",
        severity: "error",
      });
    }
  }

  // An arrival time without its date cannot be placed on the calendar, so the
  // on-time calculation would silently skip the job.
  if (clean(job.arrTime) && !clean(job.arrDate)) {
    issues.push({
      field: "arrDate", label: "Arrival date", value: "",
      message: "มีเวลาถึงแต่ไม่มีวันที่ถึง",
      expected: "DD/MM/YYYY", example: "24/07/2026", severity: "warning",
    });
  }

  return issues;
}
