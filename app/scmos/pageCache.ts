import { useCallback, useEffect, useState } from "react";
import type { JobPage, PageQuery } from "./store";

/**
 * The last thing each screen was shown, kept so the next visit draws before the
 * network does.
 *
 * Opening a menu used to mean waiting: a placeholder while the request goes
 * out, and on the first visit of the day two minutes of it while the database
 * wakes up — every menu, every time, even when the answer is the one already on
 * the screen. What the API last said is almost always what it is about to say
 * again, so it is drawn immediately and replaced the moment the real answer
 * lands. Nothing here is authoritative: the API is asked again every single
 * time, and what is drawn in the meantime is last time's answer, not this
 * one's.
 *
 * <b>sessionStorage, not localStorage.</b> What is kept carries customer names,
 * driver names and driver phone numbers. localStorage would leave them on the
 * disk of whatever machine opened the app, readable by anything with access to
 * the browser profile, surviving sign-out and the end of the shift.
 * sessionStorage is scoped to the tab and gone when it closes, which is the
 * longest this is allowed to live.
 *
 * Keyed by the signed-in operator as well as by the screen, so a shared machine
 * cannot show one person what the last person was looking at.
 */

// v2: what is stored changed shape when the cache stopped being
// the workspace's alone. A v1 entry read as a v2 one is a screen drawn
// from undefined, so the name is bumped and the old ones simply expire.
const FAMILY = "scmos.page.";
const PREFIX = FAMILY + "v2.";

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

/**
 * Everything that changes which rows come back, in one string.
 *
 * Anything omitted here is a filter that could be showing the wrong rows, so
 * every field of the query goes in even when it is usually empty.
 */
export function pageCacheKey(opId: string, query: PageQuery): string {
  return [
    opId,
    query.tab, query.focus ?? "", query.cat ?? "", query.year ?? "", query.month ?? "", query.day ?? "",
    query.from ?? "", query.to ?? "",
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

/** Who is signed in, folded into every key. Set at sign-in beside setDevUser. */
let operator = "";

export function rememberOperator(opId: string): void {
  if (opId === operator) return;
  operator = opId;
}

/**
 * The last value stored under this key, or null when there is none, it is too
 * old, or it cannot be read.
 */
export function readCached<T>(key: string): T | null {
  const store = storage();
  if (!store) return null;
  try {
    const raw = store.getItem(PREFIX + operator + "|" + key);
    if (!raw) return null;
    const stored = JSON.parse(raw) as { at: number; value: T };
    if (!stored || typeof stored.at !== "number") return null;
    if (Date.now() - stored.at > MAX_AGE_MS) {
      store.removeItem(PREFIX + operator + "|" + key);
      return null;
    }
    return stored.value;
  } catch {
    return null;
  }
}

export function writeCached<T>(key: string, value: T): void {
  const store = storage();
  if (!store) return;
  try {
    const body = JSON.stringify({ at: Date.now(), value });
    if (body.length > MAX_BYTES) return;
    store.setItem(PREFIX + operator + "|" + key, body);
  } catch {
    // A full quota is not worth an error path: the next load simply waits.
    forget();
  }
}

/**
 * State that survives leaving the screen and coming back to it.
 *
 * A drop-in for <c>useState&lt;T | null&gt;(null)</c>: same tuple, same setter,
 * except that it starts at what the screen last held instead of at null and
 * writes through on every change. A screen written the usual way — seed null,
 * fetch in an effect, draw a placeholder while it is null — therefore stops
 * drawing the placeholder on every visit after the first, and shows last time's
 * answer until this time's arrives.
 *
 * Setting it back to null forgets the screen, which is what a sign-out or a
 * wholesale replacement of the data wants.
 */
export function useRemembered<T>(key: string): [
  T | null,
  (next: T | null | ((current: T | null) => T | null)) => void,
] {
  // The key travels with the value. A screen keyed by a filter — the date on
  // the pre-run list, the period on the KPI card — changes key while it stays
  // mounted, and holding the two together is what lets the render notice and
  // re-seed instead of leaving last month's figures under this month's
  // heading.
  //
  // `dirty` marks a value this screen actually set, as against one just read
  // back out of the store. Only the former is written, so returning to a
  // screen does not keep pushing the twenty-minute clock forward on figures
  // that have not been refreshed since.
  const [state, setState] = useState<{ key: string; value: T | null; dirty: boolean }>(
    () => ({ key, value: readCached<T>(key), dirty: false }));

  const fresh = state.key === key ? state.value : readCached<T>(key);
  if (state.key !== key) setState({ key, value: fresh, dirty: false });

  const keep = useCallback((next: T | null | ((current: T | null) => T | null)) => {
    setState((current) => ({
      key,
      value: typeof next === "function"
        ? (next as (c: T | null) => T | null)(current.key === key ? current.value : null)
        : next,
      dirty: true,
    }));
  }, [key]);

  // Written after the render that committed it, so the render itself stays free
  // of side effects.
  useEffect(() => {
    if (!state.dirty || state.key !== key) return;
    if (state.value === null) forgetOne(key); else writeCached(key, state.value);
  }, [key, state]);

  return [fresh, keep];
}

export function readCachedPage(key: string): JobPage | null {
  return readCached<JobPage>(key);
}

export function writeCachedPage(key: string, page: JobPage): void {
  writeCached(key, page);
}

/** Drops one screen's saved value, leaving the rest alone. */
function forgetOne(key: string): void {
  const store = storage();
  if (!store) return;
  try {
    store.removeItem(PREFIX + operator + "|" + key);
  } catch {
    // Nothing to do.
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
      // Every version, not just this one. An entry written by an older
      // build of the app holds the same customer names, driver names and
      // driver phone numbers, and sign-out is supposed to be the end of them —
      // matching only the current prefix left them sitting in the tab.
      if (key?.startsWith(FAMILY)) keys.push(key);
    }
    keys.forEach((key) => store.removeItem(key));
  } catch {
    // Nothing to do — the entries expire on their own.
  }
}
