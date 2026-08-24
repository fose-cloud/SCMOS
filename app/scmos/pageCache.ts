import type { JobPage, PageQuery } from "./store";

/**
 * The last answer the workspace got, kept so the next visit draws before the
 * network does.
 *
 * Opening the app used to mean waiting: sign in, then watch a placeholder while
 * a page request goes out, and on the first visit of the day watch it for two
 * minutes while the database wakes up. The rows from last time are almost always
 * the rows you are about to see again, so they are drawn immediately and
 * replaced the moment the real answer lands. Nothing here is authoritative — it
 * is a picture of what the API last said, and the API is asked again every time.
 *
 * <b>sessionStorage, not localStorage.</b> These rows carry customer names,
 * driver names and driver phone numbers. localStorage would leave them on the
 * disk of whatever machine opened the app, readable by anything with access to
 * the browser profile, surviving sign-out and the end of the shift.
 * sessionStorage is scoped to the tab and gone when it closes, which is the
 * longest this is allowed to live.
 *
 * Keyed by the signed-in operator as well as the query, so a shared machine
 * cannot show one person the page the last person was looking at.
 */

const PREFIX = "scmos.page.v1.";

/**
 * How old a saved page may be before waiting is the better answer.
 *
 * Twenty minutes: long enough to cover coming back from a meeting, short enough
 * that nobody plans a truck around it. Past that the placeholder is honest and
 * the cached rows are not.
 */
const MAX_AGE_MS = 20 * 60 * 1000;

/** Above this a page is not worth storing — sessionStorage is small and shared. */
const MAX_BYTES = 400_000;

type Stored = { at: number; page: JobPage };

/**
 * Everything that changes which rows come back, in one string.
 *
 * Anything omitted here is a filter that could be showing the wrong rows, so
 * every field of the query goes in even when it is usually empty.
 */
export function pageCacheKey(opId: string, query: PageQuery): string {
  return PREFIX + [
    opId,
    query.tab, query.cat ?? "", query.year ?? "", query.month ?? "", query.day ?? "",
    query.q ?? "", query.sort ?? "", query.dir ?? "",
    query.page ?? 1, query.per ?? 25,
    query.customer ?? "", query.trucker ?? "", query.type ?? "",
    query.status ?? "", query.assignee ?? "", query.kpi ?? "",
  ].join("|");
}

function storage(): Storage | null {
  try {
    // Absent during server rendering, and blocked outright in some privacy
    // modes. Neither is a reason to fail — the app simply waits for the network
    // as it did before.
    return typeof window === "undefined" ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

export function readCachedPage(key: string): JobPage | null {
  const store = storage();
  if (!store) return null;
  try {
    const raw = store.getItem(key);
    if (!raw) return null;
    const stored = JSON.parse(raw) as Stored;
    if (!stored?.page || typeof stored.at !== "number") return null;
    if (Date.now() - stored.at > MAX_AGE_MS) {
      store.removeItem(key);
      return null;
    }
    return stored.page;
  } catch {
    return null;
  }
}

export function writeCachedPage(key: string, page: JobPage): void {
  const store = storage();
  if (!store) return;
  try {
    const body = JSON.stringify({ at: Date.now(), page } satisfies Stored);
    if (body.length > MAX_BYTES) return;
    store.setItem(key, body);
  } catch {
    // A full quota is not worth an error path: the next load simply waits.
    forget();
  }
}

/**
 * Drops every saved page.
 *
 * Called on sign-out and whenever the register is replaced wholesale, because a
 * cached page that outlives the data it came from is worse than no cache — it
 * shows deleted work to the next person who opens the tab.
 */
export function forget(): void {
  const store = storage();
  if (!store) return;
  try {
    const keys: string[] = [];
    for (let index = 0; index < store.length; index++) {
      const key = store.key(index);
      if (key?.startsWith(PREFIX)) keys.push(key);
    }
    keys.forEach((key) => store.removeItem(key));
  } catch {
    // Nothing to do — the entries expire on their own.
  }
}
