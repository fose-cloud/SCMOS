/**
 * Which audit rows the screen may offer to put back — the same rule as
 * Rules/AuditRevert.cs, so the checkbox appears exactly where the API will
 * accept it: a job's row, about one cell going from one value to another,
 * on a cell the trail keeps a row for.
 *
 * No imports on purpose — see the other modules under this folder.
 */

/** The actions whose rows describe one cell going from one value to another. */
export const REVERSIBLE_ACTIONS = ["assign", "update", "status", "carrier"] as const;

/** The cells the trail keeps a row for, by the label the row carries. Kept in step with Rules/AuditActions.cs. */
export const REVERSIBLE_FIELDS = ["ผู้ขนส่ง", "สถานะ", "ทะเบียนรถ", "คนขับ", "ผู้รับผิดชอบ", "วันที่งาน", "เวลานัด", "เลขตู้"] as const;

export function reversible(entity: string, action: string, field: string): boolean {
  return entity === "job"
    && (REVERSIBLE_ACTIONS as readonly string[]).includes(action)
    && (REVERSIBLE_FIELDS as readonly string[]).includes((field ?? "").trim());
}

/** What the API said about one row it was asked to put back. */
export type RevertLine = {
  id: number; jobKey: string; label: string; field: string; from: string; to: string;
  /** reverted · changed-since · not-reversible · forbidden · missing · unknown-person · failed */
  outcome: string; detail: string;
};
