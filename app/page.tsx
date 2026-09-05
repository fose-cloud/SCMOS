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
      // The demo gate — pick any of the accounts, no password — is how this ran
      // on a laptop before there was a sign-in at all. Deployed there is a real
      // one, so it must not exist: a build that offers both has a second door,
      // and the second door is the one without a lock.
      demo={process.env.NODE_ENV !== "production"}
      // Read here, per request, rather than through NEXT_PUBLIC_ — that form is
      // inlined at build time, so a key set on the App Service afterwards would
      // do nothing and the map would stay blank with no way to tell why.
      //
      // It reaches the browser, because an embed key is a thing browsers use.
      // Google's answer to that is an HTTP referrer restriction on the key, not
      // secrecy; see .env.example.
      mapsKey={process.env.GOOGLE_MAPS_EMBED_KEY ?? ""}
    />
  );
}
