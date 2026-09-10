/**
 * The five documents a subcontractor has to hold.
 *
 * The same list as `Rules/SupplierCompliance.cs`, which is the one the API
 * decides states from. Written twice because the screen needs the headings and
 * the API needs the rules, and neither can read the other's file at runtime —
 * so `--check-compliance` reads *this* file and fails if the two vocabularies
 * disagree. Every rule this codebase has written twice and left unchecked has
 * drifted; this one is checked.
 *
 * `code` is the join. It is what an upload writes into the document's `kind`,
 * and what the API matches on to decide which column a stored file belongs
 * under — so it is a fixed vocabulary rather than whatever somebody typed.
 *
 * A leaf module: it imports nothing, so anything may import it.
 */
export type Requirement = {
  code: string;
  /** The column heading, as the department wrote it. */
  english: string;
  /** The line underneath it, likewise. */
  thai: string;
  /** Which tree under SCMOS/Supplier/{code}/ the file goes in. */
  folder: string;
  /** Whether this one carries an expiry date at all. */
  expires: boolean;
};

export const REQUIREMENTS: Requirement[] = [
  { code: "insurance-vehicle", english: "Insurance expire", thai: "รถยนต์", folder: "Insurance", expires: true },
  { code: "insurance-cargo", english: "Insurance expire", thai: "สินค้า", folder: "Insurance", expires: true },
  { code: "transport-licence", english: "Transport Licence", thai: "ใบอนุญาตขนส่ง", folder: "License", expires: true },
  { code: "affidavit", english: "Affidavit Company", thai: "หนังสือรับรองบริษัท", folder: "Contract", expires: true },
  { code: "truck-profile", english: "Truck Profile/Annex", thai: "ทะเบียนรถในสัญญา", folder: "Contract", expires: false },
];

/** How near an expiry has to be before the screen says so. Matches the API's. */
export const WARNING_DAYS = 60;

/**
 * What each state looks like.
 *
 * Expired is red and missing is amber, which is the order the API ranks them
 * in and for the same reason: a document that was held and has run out is
 * worse than one that was never supplied, because somebody has been relying
 * on it.
 */
export const STATE_TONE: Record<string, { bg: string; ink: string; label: string }> = {
  valid: { bg: "#EAF6EF", ink: "#16794C", label: "ใช้ได้" },
  expiring: { bg: "#FFF4E5", ink: "#B45309", label: "ใกล้หมดอายุ" },
  expired: { bg: "#FDECEA", ink: "#B42318", label: "หมดอายุแล้ว" },
  missing: { bg: "#F4F6F8", ink: "#7B8CA0", label: "ยังไม่มีเอกสาร" },
  "no-expiry": { bg: "#EEF3F8", ink: "#1D5FA8", label: "ไม่ได้ระบุวันหมดอายุ" },
};

/** What one requirement's cell says, beyond the date itself. */
export function stateLabel(state: string, daysLeft: number | null): string {
  const tone = STATE_TONE[state];
  if (!tone) return state;
  if (state === "expiring" && daysLeft !== null) return `เหลือ ${daysLeft} วัน`;
  if (state === "expired" && daysLeft !== null) return `เกิน ${Math.abs(daysLeft)} วัน`;
  return tone.label;
}
