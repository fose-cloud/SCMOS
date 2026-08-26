import { apiFetch } from "./api";

/**
 * The daily issue log, as the API serves it.
 *
 * Every vocabulary — sources, categories, severities, statuses, channels — is
 * fetched rather than written out here. The API enforces what it accepts, and a
 * second copy in the browser is a list that agrees until somebody edits one of
 * them. See `IssueForm` on the server for where they come from.
 */

export type Issue = {
  id: number;
  code: string;
  foundOn: string;
  foundAt: string;
  source: string;
  reporter: string;
  /** The reference as it was written down, kept whether or not it matched. */
  jobRef: string;
  /** The job it attached to, empty when the reference found none. */
  jobKey: string;
  detail: string;
  category: string;
  severity: string;
  impact: string;
  channel: string;
  owner: string;
  ownerId: string;
  dueOn: string;
  status: string;
  rootCause: string;
  /**
   * Who was driving, what was on the lorry, and which lorry.
   *
   * Held on the issue rather than only read off the job, because the job is
   * exactly the thing that sometimes is not there: a written reference that
   * matched nothing still describes a real problem with a real lorry. They are
   * also what a CAR/PAR escalated from here has to name, and asking somebody
   * for a container number a week later gets a different container number.
   */
  driver: string;
  containerNo: string;
  licence: string;
  /** Read off the attached job, so the log shows what the issue is about. */
  jobCustomer: string;
  jobTrucker: string;
  jobDate: string;
  /** Hours this severity allows, and whether it has run past them. */
  slaHours: number;
  overdue: boolean;
};

export type IssueForm = {
  sources: string[];
  categories: string[];
  severities: string[];
  statuses: string[];
  channels: string[];
  rootCauses: string[];
  owners: string[];
  sla: Record<string, number>;
  /** Statuses that mean the issue is finished with, decided by the API. */
  settled: string[];
};

export type Counted = { label: string; value: number };

export type IssueSummary = {
  total: number;
  outstanding: number;
  critical: number;
  overdue: number;
  onTime: number;
  bySource: Counted[];
  byStatus: Counted[];
  bySeverity: Counted[];
  byCategory: Counted[];
};

/** What a new issue carries. Everything is optional but the detail. */
export type NewIssue = Partial<Omit<Issue, "id" | "slaHours" | "overdue"
  | "jobCustomer" | "jobTrucker" | "jobDate">> & { detail: string };

async function read<T>(path: string): Promise<T | null> {
  try {
    const response = await apiFetch(path, { headers: { accept: "application/json" } });
    if (!response.ok) return null;
    return await response.json() as T;
  } catch {
    return null;
  }
}

export async function loadIssues(query: {
  status?: string; severity?: string; jobKey?: string; owner?: string;
} = {}): Promise<Issue[] | null> {
  const params = new URLSearchParams();
  Object.entries(query).forEach(([key, value]) => {
    if (value && value !== "ALL") params.set(key, value);
  });
  const body = await read<{ issues?: Issue[] }>(
    "/api/issues" + (params.toString() ? "?" + params : ""));
  return body ? body.issues ?? [] : null;
}

export const loadIssueForm = () => read<IssueForm>("/api/issues/form");
export const loadIssueSummary = () => read<IssueSummary>("/api/issues/summary");

export async function raiseIssue(issue: NewIssue): Promise<{ ok: boolean; message: string; code: string }> {
  try {
    const response = await apiFetch("/api/issues", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(issue),
    });
    const body = await response.json().catch(() => ({})) as
      { message?: string; error?: string; code?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: body.message ?? "", code: body.code ?? "" };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error), code: "" };
  }
}

export async function updateIssue(id: number, fields: Record<string, string>):
  Promise<{ ok: boolean; message: string }> {
  try {
    const response = await apiFetch(`/api/issues/${id}`, {
      method: "PATCH",
      headers: { "content-type": "application/json" },
      body: JSON.stringify(fields),
    });
    const body = await response.json().catch(() => ({})) as { message?: string; error?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: body.message ?? "" };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

/**
 * Sends a whole sheet of issues.
 *
 * Codes already in the table are skipped by the API rather than overwritten, so
 * importing the same file twice adds what is new and leaves anything somebody
 * has since worked on exactly as they left it.
 */
export async function importIssues(issues: NewIssue[]):
  Promise<{ ok: boolean; message: string; added: number; skipped: number }> {
  if (!issues.length) return { ok: true, message: "", added: 0, skipped: 0 };
  try {
    const response = await apiFetch("/api/issues/import", {
      method: "POST",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ issues }),
    });
    const body = await response.json().catch(() => ({})) as
      { added?: number; skipped?: number; error?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: "", added: body.added ?? 0, skipped: body.skipped ?? 0 };
  } catch (error) {
    return {
      ok: false,
      message: error instanceof Error ? error.message : String(error),
      added: 0,
      skipped: 0,
    };
  }
}
