import { getPlatformUser, nameFromEmail } from "./easy-auth";
import { ACCOUNTS, DEFAULT_ROLE, matchAccount, type Account } from "./scmos/nav";

/**
 * One identity for the app: App Service Web App Login.
 *
 * This mapping decides what the screen shows — the name in the header, the role
 * on the tabs, which jobs are yours. It is not what decides whether a write is
 * allowed: the API derives the same identity from the same platform headers and
 * answers for itself, because anything the browser can reach is something the
 * browser can lie about.
 */

export type AppUser = {
  userId: string;
  email: string;
  displayName: string;
  role: string;
  /** Directory id (OP-01…). Empty when the account is not one of the eight. */
  opId: string;
  source: "webapp";
};

/**
 * Who gets more than the default. Everyone signed in reaches the app; this
 * decides what they may edit. Operation Users can only change jobs assigned to
 * them — see SUPERVISOR_ROLES in scmos/nav.ts.
 *
 * Keep in step with Auth:Roles in the API's configuration. That copy is the one
 * that is enforced.
 */
const ROLE_BY_EMAIL: Record<string, string> = {
  // "titchanatorn@leschaco.com": "Operation Supervisor",
  // "nattikorn@leschaco.com": "Assistant Manager",
  // "admin@leschaco.com": "Administrator",
};

export function roleForEmail(email: string): string {
  return ROLE_BY_EMAIL[email.trim().toLowerCase()] ?? DEFAULT_ROLE;
}

export async function getUser(): Promise<AppUser | null> {
  const platform = await getPlatformUser();
  if (!platform) return null;

  const matched = matchAccount(platform.email, platform.displayName);
  const configured = ROLE_BY_EMAIL[platform.email.trim().toLowerCase()];

  return {
    userId: platform.userId,
    email: platform.email,
    displayName: platform.displayName || nameFromEmail(platform.email),
    role: configured ?? matched?.role ?? DEFAULT_ROLE,
    opId: matched?.opId ?? "",
    source: "webapp",
  };
}

/** Maps the signed-in identity onto the account shape the UI already renders. */
export function toAccount(user: AppUser): Account {
  const parts = user.displayName.split(/\s+/).filter(Boolean);
  const init = (parts.length > 1
    ? parts[0].charAt(0) + parts[1].charAt(0)
    : user.displayName.slice(0, 2)
  ).toUpperCase();

  const known = ACCOUNTS.find((account) => account.opId === user.opId);

  return {
    user: user.email || user.userId,
    // The plan workbooks call this person "Watsana"; the directory keeps that
    // spelling so the screen greets them by the name they are used to, even
    // though sign-in introduced them as watsana.k@leschaco.co.th.
    name: known?.name || parts[0] || user.displayName,
    full: user.displayName,
    role: user.role,
    id: user.userId,
    opId: user.opId,
    init: init || known?.init || "??",
  };
}
