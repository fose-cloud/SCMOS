/**
 * What a person's card on Job Rotation says, worked out without a screen:
 * the initials in the tile and the words for when they were last here. A
 * leaf with no imports, so the two readings can be checked on their own —
 * rotation.ts reaches the API and cannot be loaded by a test.
 */

/**
 * "วันนี้ 09:14", "เมื่อวาน 16:40", or the date — how a person's card says
 * when they were last here. Empty when nothing is recorded, which the card
 * shows as such rather than as a time.
 */
export function lastActiveLabel(iso: string | undefined, now: Date = new Date()): string {
  if (!iso) return "";
  const at = new Date(iso);
  if (Number.isNaN(at.getTime())) return "";
  const hhmm = at.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
  const day = (d: Date) => `${d.getFullYear()}-${d.getMonth()}-${d.getDate()}`;
  const yesterday = new Date(now); yesterday.setDate(now.getDate() - 1);
  if (day(at) === day(now)) return `วันนี้ ${hhmm}`;
  if (day(at) === day(yesterday)) return `เมื่อวาน ${hhmm}`;
  return at.toLocaleDateString("en-GB", { day: "2-digit", month: "2-digit", year: "numeric" });
}

/** Two letters for the avatar: the initials of two words, or the first two of one. */
export function initialsOf(name: string, email: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length >= 2) return (words[0][0] + words[1][0]).toUpperCase();
  const base = words[0] ?? email.split("@")[0];
  const parts = base.split(/[._-]/).filter(Boolean);
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase();
  return base.slice(0, 2).toUpperCase();
}
