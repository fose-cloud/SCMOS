import { headers } from "next/headers";

/**
 * The browser's way through to the .NET API.
 *
 * The workspace still calls `/api/jobs`, same origin, exactly as it did when the
 * routes were Workers — nothing in the client code knows the backend moved. This
 * forwards those calls to ASP.NET Core and carries the identity across.
 *
 * Why a proxy rather than calling the API directly from the browser: App Service
 * Web App Login signs the user in on *this* app and sets its cookie on this
 * domain. A cross-origin call to the API would arrive with no cookie and no
 * principal, and working around that means putting an access token in reach of
 * page scripts. Same origin keeps the session where the platform put it.
 *
 * The API refuses these headers unless the proxy key comes with them, so the
 * forwarded principal cannot be forged by anyone who reaches the API another
 * way. Lock the API down with access restrictions as well — the key is the
 * second lock, not the only one.
 */

export const dynamic = "force-dynamic";

const API_BASE = (process.env.SCMOS_API_BASE_URL ?? "").replace(/\/+$/, "");
const PROXY_KEY = process.env.SCMOS_API_PROXY_KEY ?? "";

/** Passed through untouched: this is what tells the API who is asking. */
const IDENTITY_HEADERS = [
  "x-ms-client-principal",
  "x-ms-client-principal-id",
  "x-ms-client-principal-name",
  "x-ms-client-principal-idp",
];

/** Passed through because the request needs them; everything else is dropped. */
const REQUEST_HEADERS = ["content-type", "accept", "accept-language", "range"];

/**
 * Passed back because the browser needs them; everything else is dropped.
 *
 * Only content-type used to come back, which was enough for JSON and wrong for
 * everything else. A stored file arrived with its Content-Disposition stripped,
 * so every download saved as "content" with no extension whatever the file was
 * really called, and the API's range support could not be used because neither
 * the request's Range nor the reply's Accept-Ranges survived the trip.
 *
 * The two security headers matter more. A file the API has decided is safe to
 * show in the browser is served with nosniff and a policy that permits nothing
 * to run; dropping them here would quietly undo that decision.
 */
const RESPONSE_HEADERS = [
  "content-type", "content-disposition", "content-length",
  "accept-ranges", "content-range",
  "x-content-type-options", "content-security-policy",
];

/** The demo account from the cookie, when there is one. */
function devUserCookie(header: string | null): string {
  const found = (header ?? "").split(";")
    .map((one) => one.trim())
    .find((one) => one.startsWith("scmos-dev-user="));
  return found ? decodeURIComponent(found.slice("scmos-dev-user=".length)) : "";
}

async function forward(request: Request, path: string[]): Promise<Response> {
  if (!API_BASE) {
    return Response.json(
      { error: "SCMOS_API_BASE_URL is not set — the web app does not know where the API is." },
      { status: 503 },
    );
  }

  const incoming = await headers();
  const outgoing = new Headers();

  for (const name of REQUEST_HEADERS) {
    const value = incoming.get(name);
    if (value) outgoing.set(name, value);
  }
  for (const name of IDENTITY_HEADERS) {
    const value = incoming.get(name);
    if (value) outgoing.set(name, value);
  }
  if (PROXY_KEY) outgoing.set("x-scmos-proxy-key", PROXY_KEY);

  // Local development has no identity provider, so the demo account the user
  // picked on the login screen travels instead — the same demo gate as before,
  // and the API only honours it when it is running in Development.
  //
  // From the cookie when the header is absent: the browser issues an `<img>` or
  // an `<iframe>` request itself and no page code gets to add a header to it,
  // which is every piece of evidence on a CAR/PAR. Deployed, neither is needed —
  // the platform's own principal is already on the request.
  const devUser = incoming.get("x-scmos-dev-user") ?? devUserCookie(incoming.get("cookie"));
  if (devUser) outgoing.set("x-scmos-dev-user", devUser);

  const target = new URL(`${API_BASE}/api/${path.map(encodeURIComponent).join("/")}`);
  target.search = new URL(request.url).search;

  const hasBody = request.method !== "GET" && request.method !== "HEAD";

  try {
    const response = await fetch(target, {
      method: request.method,
      headers: outgoing,
      // Streamed rather than buffered, so a 12 MB scan of a delivery order does
      // not sit in this process's memory on the way past.
      body: hasBody ? request.body : undefined,
      ...(hasBody ? { duplex: "half" } : {}),
      redirect: "manual",
      cache: "no-store",
    } as RequestInit);

    const out = new Headers();
    for (const name of RESPONSE_HEADERS) {
      const value = response.headers.get(name);
      if (value) out.set(name, value);
    }
    out.set("cache-control", "no-store");

    return new Response(response.body, { status: response.status, headers: out });
  } catch (error) {
    // The API being unreachable is the one failure the workspace already knows
    // how to show: it keeps working from what it has and says the save did not
    // land, rather than losing the edit.
    return Response.json(
      { error: error instanceof Error ? error.message : "The API could not be reached" },
      { status: 502 },
    );
  }
}

type Context = { params: Promise<{ path: string[] }> };

export async function GET(request: Request, context: Context) {
  return forward(request, (await context.params).path);
}
export async function POST(request: Request, context: Context) {
  return forward(request, (await context.params).path);
}
export async function PUT(request: Request, context: Context) {
  return forward(request, (await context.params).path);
}
export async function PATCH(request: Request, context: Context) {
  return forward(request, (await context.params).path);
}
export async function DELETE(request: Request, context: Context) {
  return forward(request, (await context.params).path);
}
