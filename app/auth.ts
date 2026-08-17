import { getPlatformUser, nameFromEmail } from "./easy-auth";
import type { Account } from "./scmos/nav";

/**
 * One identity for the app: App Service Web App Login.
 *
 * This reads the platform principal so the first paint has a name in the header
 * instead of a blank. It deliberately decides **nothing else**.
 *
 * It used to decide two things, and both were second opinions the API never saw:
 * a `ROLE_BY_EMAIL` table that the code itself told you to "keep in step with
 * Auth:Roles", and a `matchAccount` that mapped an email to a directory person
 * alongside `StaffDirectory.Match` in C#. Both were empty, so nothing had gone
 * wrong yet — but the day real staff signed in, the browser and the API would
 * have been two opinions about who somebody is and what they may do, and the one
 * users see is not the one enforced.
 *
 * Role and owner id now come from `/api/me`, which reads the same platform
 * headers and is the copy that answers for every write. Until that lands the
 * browser knows a name and nothing else, which is the safe direction to be
 * wrong in: no owner id means no jobs look like yours, and no capabilities means
 * no write control is offered.
 */

export type AppUser = {
  userId: string;
  email: string;
  displayName: string;
  source: "webapp";
};

export async function getUser(): Promise<AppUser | null> {
  const platform = await getPlatformUser();
  if (!platform) return null;

  return {
    userId: platform.userId,
    email: platform.email,
    displayName: platform.displayName || nameFromEmail(platform.email),
    source: "webapp",
  };
}

/**
 * The signed-in identity in the shape the UI renders, with role and owner id
 * left empty for `/api/me` to fill.
 */
export function toAccount(user: AppUser): Account {
  const parts = user.displayName.split(/\s+/).filter(Boolean);
  const init = (parts.length > 1
    ? parts[0].charAt(0) + parts[1].charAt(0)
    : user.displayName.slice(0, 2)
  ).toUpperCase();

  return {
    user: user.email || user.userId,
    name: parts[0] || user.displayName,
    full: user.displayName,
    role: "",
    id: user.userId,
    opId: "",
    init: init || "??",
  };
}
