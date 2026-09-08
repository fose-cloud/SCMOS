/** Browser CSRF guard. Host retains the public hostname when Next's internal URL is localhost. */
export function allowedOperationsControlRequest(request: Request): boolean {
  if (request.headers.get("x-scmos-ai-control") !== "1") return false;
  const site = request.headers.get("sec-fetch-site");
  if (site !== null && site !== "same-origin") return false;
  const origin = request.headers.get("origin");
  if (origin === null) return true; // non-browser callers still require server authentication + admin.
  try {
    const url = new URL(request.url);
    const host = request.headers.get("host") ?? url.host;
    const protocol = request.headers.get("x-forwarded-proto")?.split(",")[0].trim() ?? url.protocol.slice(0, -1);
    return (protocol === "https" || protocol === "http") && origin === `${protocol}://${host}`;
  } catch { return false; }
}
