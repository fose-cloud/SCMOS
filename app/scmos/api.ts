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

/** Called whenever the demo account changes. A no-op once real sign-in is in front. */
export function setDevUser(account: Account | null) {
  devUser = account?.user ?? "";
}

export function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (devUser) headers.set("x-scmos-dev-user", devUser);
  return fetch(path, { ...init, headers, cache: "no-store" });
}
