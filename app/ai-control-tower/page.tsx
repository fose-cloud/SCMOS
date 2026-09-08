import { SCMOSApp } from "../SCMOSApp";
import { getUser, toAccount } from "../auth";
import { SIGN_OUT_PATH } from "../easy-auth";

// Identical platform identity and production gate to Home; no separate AI login.
export const dynamic = "force-dynamic";

export default async function AiControlTowerPage() {
  const user = await getUser();
  return <SCMOSApp initialUser={user ? toAccount(user) : null}
    signOutHref={user ? SIGN_OUT_PATH : null}
    demo={process.env.NODE_ENV !== "production"} initialScreen="ai" />;
}
