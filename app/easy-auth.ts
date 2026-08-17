import { headers } from "next/headers";

/**
 * App Service Web App Login.
 *
 * Azure signs the user in at the edge and hands the request on with the verified
 * principal in `X-MS-CLIENT-PRINCIPAL` — base64 JSON holding the claims. The
 * platform strips whatever the client sent, so on App Service these headers are
 * trustworthy; anywhere else they are not, which is why nothing outside this
 * file reads them and why the API re-derives the identity for itself rather than
 * believing what the browser sends.
 *
 * Locally there is no provider at all. `getPlatformUser` returns null and the
 * demo gate decides, exactly as it did before the move.
 */

export type PlatformUser = {
  userId: string;
  email: string;
  displayName: string;
};

type Claim = { typ?: string; val?: string };
type ClientPrincipal = { auth_typ?: string; claims?: Claim[] };

const EMAIL_CLAIMS = [
  "preferred_username",
  "emails",
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
];
const NAME_CLAIMS = [
  "name",
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
];
const ID_CLAIMS = [
  "oid",
  "http://schemas.microsoft.com/identity/claims/objectidentifier",
];

export async function getPlatformUser(): Promise<PlatformUser | null> {
  const store = await headers();

  let email = (store.get("x-ms-client-principal-name") ?? "").trim();
  let userId = (store.get("x-ms-client-principal-id") ?? "").trim();
  let displayName = "";

  const encoded = store.get("x-ms-client-principal");
  if (encoded) {
    const principal = decode(encoded);
    if (principal) {
      email = claim(principal, EMAIL_CLAIMS) || email;
      displayName = claim(principal, NAME_CLAIMS) || "";
      userId = claim(principal, ID_CLAIMS) || userId;
    }
  }

  if (!email && !userId) return null;
  return { userId: userId || email, email, displayName: displayName || nameFromEmail(email) };
}

/** Where App Service sends someone to sign out. */
export const SIGN_OUT_PATH = "/.auth/logout?post_logout_redirect_uri=/";

export function nameFromEmail(email: string): string {
  const local = email.includes("@") ? email.slice(0, email.indexOf("@")) : email;
  const word = local.split(/[._-]/).find(Boolean) ?? local;
  return word ? word.charAt(0).toUpperCase() + word.slice(1) : "";
}

function decode(encoded: string): ClientPrincipal | null {
  try {
    return JSON.parse(Buffer.from(encoded, "base64").toString("utf8")) as ClientPrincipal;
  } catch {
    return null;
  }
}

function claim(principal: ClientPrincipal, types: string[]): string {
  for (const type of types) {
    const found = principal.claims?.find((c) => c.typ?.toLowerCase() === type.toLowerCase());
    if (found?.val?.trim()) return found.val.trim();
  }
  return "";
}
