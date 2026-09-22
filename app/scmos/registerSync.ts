import type { Job } from "./ops";

/**
 * Keeping the register on screen in step with the register in the database
 * without anybody pressing F5.
 *
 * Every save landed and every insert appeared — for the person who made it,
 * once. Everyone else saw the register as it stood when their page loaded,
 * and the person who made the edit saw it too, for a second, before the
 * grid re-read a page the write had not reached yet. Two things fix that:
 * the page is re-read after the write lands rather than before, and every
 * open workspace asks the API, a few times a minute, what changed since the
 * stamp it last saw, and lays those rows over its own.
 *
 * The two decisions here — what a delta does to the rows in memory, and
 * when a delta is not enough and the whole register has to come again —
 * are pure, so they are checked without a browser.
 */

/** What /api/jobs/since answers. */
export type RegisterDelta = {
  /** Rows written after the stamp, as the register stores them. */
  jobs: Record<string, string>[];
  /** How many rows the register holds now — a deletion shows up here. */
  count: number;
  /** The newest write's stamp, full precision, to ask from next time. */
  updatedAt: string;
  /** True when more changed than is worth sending row by row. */
  full: boolean;
};

/**
 * Lays changed rows over the ones in memory, in place — the register on
 * screen is mutated and touched, as every other edit path does it.
 *
 * A row somebody here is still typing into, or has queued to save, is left
 * alone: the database's version is older than what is on this screen, and
 * would put their edit back the moment it arrived. A row the register has
 * that this screen does not is put in front, where a row inserted here
 * goes too. Returns what it did, for the toast and for the tests.
 */
export function applyDelta(
  jobs: Job[],
  incoming: Job[],
  keepLocal: (key: string) => boolean,
): { replaced: number; added: number; kept: number } {
  let replaced = 0, added = 0, kept = 0;
  const at = new Map<string, number>();
  jobs.forEach((job, index) => at.set(job.key, index));
  for (const fresh of incoming) {
    if (keepLocal(fresh.key)) { kept++; continue; }
    const index = at.get(fresh.key);
    if (index === undefined) {
      jobs.unshift(fresh);
      at.forEach((position, key) => at.set(key, position + 1));
      at.set(fresh.key, 0);
      added++;
    } else {
      jobs[index] = fresh;
      replaced++;
    }
  }
  return { replaced, added, kept };
}

/**
 * Whether the delta was not enough and the register has to be re-read.
 *
 * The API says so itself when too many rows changed to send. A count that
 * does not match after the merge means a row went away — the delta carries
 * writes, not deletions — and the only honest answer is to read the whole
 * register again rather than show a row that is gone.
 */
export function needsFullReload(delta: { full: boolean; count: number }, localCount: number): boolean {
  return delta.full || delta.count !== localCount;
}

/** How often an open workspace asks what changed, while somebody is looking at it. */
export const SYNC_EVERY_MS = 20_000;

/** How often the workspace asks for the two queues (hauliers' messages, proposed corrections): every minute, and on coming back to the tab. */
export const QUEUES_EVERY_MS = 60_000;
