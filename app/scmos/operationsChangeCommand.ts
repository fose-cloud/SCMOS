export type ChangeFields = Record<string, string>;
export type ChangePreview = { key: string; version: string; values: ChangeFields; enabled: boolean;
  assignees: { id: string; name: string }[]; statuses: string[] };
export type ChangeDraft = { preview: ChangePreview; changes: ChangeFields; reason: string };
export const CHANGE_EXAMPLE = "เสนอแก้งาน KEY; วันที่ 15/09/2026; เวลา 09:30; เหตุผล ลูกค้าขอเลื่อน";
export const isChangeCommand = (text: string) => /^(?:เสนอแก้งาน|\/แก้งาน)(?:\s|$)/u.test(text.trim());
const fields = ["date", "planTime", "status", "opId"];
function object(v: unknown): Record<string, unknown> {
  if (!v || typeof v !== "object" || Array.isArray(v)) throw new Error("invalid_draft");
  return v as Record<string, unknown>;
}
function text(v: unknown, max: number): string {
  if (typeof v !== "string" || v.length > max) throw new Error("invalid_draft");
  return v;
}
function values(v: unknown, full: boolean): ChangeFields {
  const entries = Object.entries(object(v));
  if (entries.length < 1 || entries.length > 4 || (full && entries.length !== 4)
    || entries.some(([key]) => !fields.includes(key))) throw new Error("invalid_draft");
  return Object.fromEntries(entries.map(([key, value]) => [key, text(value, 200)]));
}
export function parseChangePreview(v: unknown): ChangePreview {
  const p = object(v);
  const key = text(p.key, 80), version = text(p.version, 64);
  if (!key || !/^[A-F0-9]{64}$/i.test(version) || typeof p.enabled !== "boolean"
    || !Array.isArray(p.assignees) || p.assignees.length > 1000
    || !Array.isArray(p.statuses) || p.statuses.length > 100) throw new Error("invalid_draft");
  return { key, version, enabled: p.enabled, values: values(p.values, true),
    assignees: p.assignees.map(v => { const a = object(v); return { id: text(a.id, 80), name: text(a.name, 200) }; }),
    statuses: p.statuses.map(v => text(v, 60)) };
}
export function parseChangeDraft(v: unknown): ChangeDraft {
  const draft = object(v);
  if (draft.code !== "draft") throw new Error("invalid_draft");
  const reason = text(draft.reason, 400);
  if (!reason.trim()) throw new Error("invalid_draft");
  return { preview: parseChangePreview(draft.preview), changes: values(draft.changes, false), reason };
}
