import { apiFetch } from "./api";
import type { Job, RawOps } from "./ops";

/**
 * Where the plan lives.
 *
 * `public/data/ops.json` is the plan as delivered; Azure SQL is the plan as
 * worked. The database wins once it holds anything, and is seeded from the file
 * the first time so nobody has to key the July plan again. Every save is an
 * upsert of whole jobs, so a failed save loses nothing but the last edit.
 */

export type LoadResult = {
  jobs: Record<string, string>[] | null;
  /** Where the jobs came from, so the workspace can say so out loud. */
  source: "database" | "file" | "file-only";
  updatedAt: string;
  /** Set when the database could not be reached; the app runs read-only from the file. */
  error: string;
};

const API = "/api/jobs";

/** Strips the fields the workspace recomputes on load — they are not worth storing. */
function forStorage(job: Job): Record<string, unknown> {
  const { issues, fixes, flags, action, prio, ...rest } = job;
  void issues; void fixes; void flags; void action; void prio;
  return rest;
}

export async function loadJobs(): Promise<LoadResult> {
  try {
    const response = await apiFetch(API, { headers: { accept: "application/json" } });
    if (!response.ok) throw new Error("HTTP " + response.status);
    const body = await response.json() as { jobs?: Record<string, string>[]; updatedAt?: string };
    const jobs = body.jobs ?? [];
    if (jobs.length) return { jobs, source: "database", updatedAt: body.updatedAt ?? "", error: "" };
    return { jobs: null, source: "file", updatedAt: "", error: "" };
  } catch (error) {
    return {
      jobs: null,
      source: "file-only",
      updatedAt: "",
      error: error instanceof Error ? error.message : String(error),
    };
  }
}

/** What one page of the register looks like, as the API answers it. */
export type JobPage = {
  jobs: Record<string, string>[];
  total: number;
  pageCount: number;
  page: number;
  /** Live count for every tab, over the whole register, not just this page. */
  counts: Record<string, number>;
  /** Every date in the current selection, for the calendar strip. */
  dates: string[];
  updatedAt: string;
};

export type PageQuery = {
  tab: string; cat?: string; year?: string; month?: string; day?: string;
  q?: string; sort?: string; dir?: string; page?: number; per?: number;
};

/**
 * One page of the register, filtered and counted by the API.
 *
 * The alternative — and what this replaces — is fetching all 2,626 jobs and
 * throwing away 2,601 of them: 2.6 MB on every load, most of it other people's
 * work. This asks for the twenty-five rows the screen is about to draw and the
 * counts that go on the tab strip, and gets 20 KB.
 *
 * Notice what is not in the query: whose jobs count as "mine". The API decides
 * that from the signed-in identity. If it were a parameter, anybody could read
 * a colleague's workspace by naming their operator id.
 */
export async function loadJobsPage(query: PageQuery): Promise<JobPage | null> {
  const params = new URLSearchParams({ tab: query.tab });
  if (query.cat) params.set("cat", query.cat);
  if (query.year) params.set("year", query.year);
  if (query.month) params.set("month", query.month);
  if (query.day) params.set("day", query.day);
  if (query.q) params.set("q", query.q);
  if (query.sort) params.set("sort", query.sort);
  if (query.dir) params.set("dir", query.dir);
  params.set("page", String(query.page ?? 1));
  params.set("per", String(query.per ?? 25));

  try {
    const response = await apiFetch(`${API}/page?${params}`, {
      headers: { accept: "application/json" },
    });
    if (!response.ok) return null;
    return await response.json() as JobPage;
  } catch {
    // The caller falls back to the full register, which is slower and still
    // correct — a page that cannot be fetched must not empty the screen.
    return null;
  }
}

/** Reads the delivered plan file, used to seed an empty database. */
export async function loadPlanFile(): Promise<RawOps> {
  const response = await fetch("/data/ops.json");
  if (!response.ok) throw new Error("ops.json HTTP " + response.status);
  return await response.json() as RawOps;
}

/**
 * Writes jobs, with the reason for the change when there is one.
 *
 * The reason travels with the save rather than being written separately: the
 * API works out which fields actually changed and attaches it to those audit
 * rows, so "why did this carrier change" is answered by the same request that
 * changed it and cannot go missing between two calls.
 */
export async function saveJobs(jobs: Job[], by: string, reason = ""): Promise<{ ok: boolean; message: string }> {
  if (!jobs.length) return { ok: true, message: "" };
  try {
    const response = await apiFetch(API, {
      method: "PUT",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ by, reason, jobs: jobs.map(forStorage) }),
    });
    if (!response.ok) {
      const body = await response.json().catch(() => ({})) as { error?: string };
      throw new Error(body.error || "HTTP " + response.status);
    }
    return { ok: true, message: "" };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

/** Removes specific jobs — used when merging duplicates away. */
export async function deleteJobs(keys: string[], by: string): Promise<{ ok: boolean; message: string }> {
  if (!keys.length) return { ok: true, message: "" };
  try {
    const response = await apiFetch(API, {
      method: "DELETE",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ keys, by }),
    });
    if (!response.ok) throw new Error("HTTP " + response.status);
    return { ok: true, message: "" };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}

/** Empties the register so the plan file can be loaded in fresh. */
export async function clearJobs(by: string): Promise<{ ok: boolean; message: string }> {
  try {
    const response = await apiFetch(API, {
      method: "DELETE",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ all: true, by }),
    });
    if (!response.ok) throw new Error("HTTP " + response.status);
    return { ok: true, message: "" };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error) };
  }
}
