import type { Account } from "./nav";

/**
 * Every call to the backend goes through here.
 *
 * The paths are unchanged — `/api/jobs`, `/api/ai-extract` — because the web app
 * proxies them on to the .NET API on the same origin. What this adds is the one
 * thing the proxy cannot work out for itself: which demo account is signed in
 * when there is no identity provider at all.
 *
 * Deployed, that header is absent and irrelevant. App Service has already signed
 * the user in and the platform's own principal is what the API reads; the API
 * ignores this header outside Development, so a browser cannot use it to become
 * somebody else.
 */

let devUser = "";

/**
 * Called whenever the demo account changes. A no-op once real sign-in is in
 * front.
 *
 * Written to a cookie as well as held here, because a header can only be added
 * by code that makes the request. An `<img src>` or an `<iframe src>` is issued
 * by the browser itself, so evidence on a case arrived at the API with no
 * identity at all and came back 401 — a picture that exists, is served
 * correctly, and will not display. Deployed there is no such gap: App Service
 * signs the user in at the front door and puts its principal on every request
 * including those ones. This is the local stand-in for that, and it is inert
 * anywhere else — the API only reads the demo account in Development
 * (UserAccessor.Current).
 */
export function setDevUser(account: Account | null) {
  devUser = account?.user ?? "";
  if (typeof document === "undefined") return;
  document.cookie = devUser.length > 0
    ? `scmos-dev-user=${encodeURIComponent(devUser)}; path=/; SameSite=Lax`
    : "scmos-dev-user=; path=/; Max-Age=0; SameSite=Lax";
}

/**
 * How many calls are out, and who wants to know.
 *
 * Screens now draw what they last held rather than a placeholder, which is the
 * point — but it means a figure on the screen is last time's answer until this
 * time's arrives, and nothing about it looks any different. So the fact that a
 * request is in the air is published, and the header says so. Without it a
 * number that is twenty minutes old is indistinguishable from one that is
 * current, and somebody plans a truck around it.
 */
let outstanding = 0;
const watchers = new Set<(busy: boolean) => void>();

/** Told whenever the app starts or stops waiting on the API. */
export function onFetching(watch: (busy: boolean) => void): () => void {
  watchers.add(watch);
  watch(outstanding > 0);
  return () => { watchers.delete(watch); };
}

function settle(delta: number) {
  const was = outstanding > 0;
  outstanding = Math.max(0, outstanding + delta);
  const now = outstanding > 0;
  if (was !== now) watchers.forEach((watch) => watch(now));
}

export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (devUser) headers.set("x-scmos-dev-user", devUser);
  settle(1);
  return fetch(path, { ...init, headers, cache: "no-store" })
    .finally(() => settle(-1));
}
