import { apiFetch } from "./api";
import type { Job, RawOps } from "./ops";

/**
 * Where the plan lives.
 *
 * `public/data/ops.json` is the plan as delivered; Azure SQL is the plan as
 * worked. The database wins once it holds anything, and is seeded from the file
 * the first time so nobody has to key the July plan again. Saves are whole-job
 * upserts, and the client queue keeps a failed batch available for retry.
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
  /** A span of plan dates, dd/MM/yyyy, either end optional. */
  from?: string; to?: string;
  q?: string; sort?: string; dir?: string; page?: number; per?: number;
  /** The rest of the workspace's filter bar, answered by the same endpoint. */
  customer?: string; trucker?: string; type?: string; status?: string;
  assignee?: string; kpi?: string;
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
  if (query.from) params.set("from", query.from);
  if (query.to) params.set("to", query.to);
  if (query.q) params.set("q", query.q);
  if (query.sort) params.set("sort", query.sort);
  if (query.dir) params.set("dir", query.dir);
  if (query.customer) params.set("customer", query.customer);
  if (query.trucker) params.set("trucker", query.trucker);
  if (query.type) params.set("type", query.type);
  if (query.status) params.set("status", query.status);
  if (query.assignee) params.set("assignee", query.assignee);
  if (query.kpi) params.set("kpi", query.kpi);
  params.set("page", String(query.page ?? 1));
  params.set("per", String(query.per ?? 50));

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
 * How many jobs travel in one request.
 *
 * There is no limit on how many jobs may be saved. There is a limit on how many
 * fit in one request, and the two are not the same thing — this is what keeps
 * them apart.
 *
 * A stored job measures about 760 bytes, so this is roughly 1.5 MB on the wire.
 * The API refuses a batch over 5,000 and Kestrel refuses a body over 30 MB
 * (about 41,000 jobs) before the API ever sees it, and on the far side the whole
 * batch is buffered, parsed, and built into a DataTable in memory on an instance
 * with 1.75 GB shared between two apps. A single unbounded request does not
 * remove those walls, it just arrives at them without a message anybody can
 * read. Splitting the work means an import of any size goes through, each piece
 * small enough that none of that is close.
 */
const PER_REQUEST = 2000;

/**
 * Writes jobs, with the reason for the change when there is one.
 *
 * The reason travels with the save rather than being written separately: the
 * API works out which fields actually changed and attaches it to those audit
 * rows, so "why did this carrier change" is answered by the same request that
 * changed it and cannot go missing between two calls.
 *
 * A batch larger than one request goes in pieces, one after another rather than
 * at once — the register is written by bulk copy into a temp table, and several
 * of those racing each other is a way to make a slow import into a failing one.
 * Every save is an upsert keyed by job key, so a run that stops halfway has
 * written some jobs and no half-jobs, and running it again finishes it without
 * duplicating anything. What it must not do is claim to have saved the lot, so a
 * partial run reports how far it got.
 *
 * A request that comes back 200 is not yet a save. The count the API reports is
 * checked against the jobs that were sent, per request, because a write that
 * silently persisted fewer rows than it was given is exactly the failure that
 * looks like success — the import appears to work and the register quietly
 * disagrees with the screen.
 */
export async function saveJobs(jobs: Job[], by: string, reason = ""): Promise<{ ok: boolean; message: string }> {
  if (!jobs.length) return { ok: true, message: "" };

  let saved = 0;
  for (let at = 0; at < jobs.length; at += PER_REQUEST) {
    const batch = jobs.slice(at, at + PER_REQUEST);
    try {
      const response = await apiFetch(API, {
        method: "PUT",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({ by, reason, jobs: batch.map(forStorage) }),
      });
      const body = await response.json().catch(() => ({})) as { saved?: number; error?: string };
      if (!response.ok) throw new Error(body.error || "HTTP " + response.status);

      // The API drops a job with no key at all, and collapses the same key sent
      // twice, so what it should have written is the distinct keys in this
      // request — not how many jobs went into it.
      const expected = new Set(batch.map((job) => job.key).filter(Boolean)).size;
      if (typeof body.saved !== "number") throw new Error("API ไม่ได้ยืนยันจำนวนงานที่บันทึก");
      if (body.saved !== expected) throw new Error(`ฐานข้อมูลบันทึกได้ ${body.saved} จาก ${expected} งาน`);
      saved += body.saved;
    } catch (error) {
      const why = error instanceof Error ? error.message : String(error);
      return {
        ok: false,
        message: saved > 0
          ? `บันทึกได้ ${saved} จาก ${jobs.length} งานแล้วหยุด — ${why} · บันทึกอีกครั้งเพื่อทำต่อ`
          : why,
      };
    }
  }
  return { ok: true, message: "" };
}

/**
 * The jobs whose plan changed: cancelled, or moved off their first date.
 *
 * Its own endpoint rather than the workspace's paging one. That endpoint reads
 * the whole register and counts all nine tabs before answering, which is right
 * for a grid that draws one tab and shows the numbers on the others, and wrong
 * for a screen that wants a handful of rows and none of the counts — asking it
 * for this took over a minute. The API filters these in SQL instead.
 */
export async function loadChangedJobs(): Promise<Record<string, string>[] | null> {
  try {
    const response = await apiFetch(`${API}/changed`, { headers: { accept: "application/json" } });
    if (!response.ok) return null;
    const body = await response.json() as { jobs?: Record<string, string>[] };
    return body.jobs ?? [];
  } catch {
    return null;
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
/**
 * Everything one operator holds, or only their work in one month.
 *
 * `month` is MM/yyyy, the tail of the dd/MM/yyyy the register stores. Empty
 * means all of it.
 *
 * Unlike the other two, this reports how many rows went. The administrator was
 * shown a count before deciding and is owed the real one afterwards — they are
 * not always the same number, because somebody else may have been working while
 * the dialog was open, and this register keeps no history to check against.
 */
export async function clearOwnerJobs(ownerId: string, month: string, by: string, reason: string):
  Promise<{ ok: boolean; message: string; removed: number }> {
  try {
    const response = await apiFetch(API, {
      method: "DELETE",
      headers: { "content-type": "application/json" },
      body: JSON.stringify({ ownerId, month, by, reason }),
    });
    const body = await response.json().catch(() => ({})) as { removed?: number; error?: string };
    if (!response.ok) throw new Error(body.error || "HTTP " + response.status);
    return { ok: true, message: "", removed: body.removed ?? 0 };
  } catch (error) {
    return { ok: false, message: error instanceof Error ? error.message : String(error), removed: 0 };
  }
}

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
