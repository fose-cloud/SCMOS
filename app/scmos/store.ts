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
