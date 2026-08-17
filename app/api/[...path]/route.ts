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
const REQUEST_HEADERS = ["content-type", "accept", "accept-language"];

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
  const devUser = incoming.get("x-scmos-dev-user");
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
    const contentType = response.headers.get("content-type");
    if (contentType) out.set("content-type", contentType);
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
