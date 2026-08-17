import { SCMOSApp } from "./SCMOSApp";
import { getUser, toAccount } from "./auth";
import { SIGN_OUT_PATH } from "./easy-auth";

// Identity comes from per-request headers, so this page cannot be prerendered.
export const dynamic = "force-dynamic";

export default async function Home() {
  const user = await getUser();

  return (
    <SCMOSApp
      initialUser={user ? toAccount(user) : null}
      signOutHref={user ? SIGN_OUT_PATH : null}
    />
  );
}
