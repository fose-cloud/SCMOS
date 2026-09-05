/**
 * The route a quotation is pricing, on a map.
 *
 * The calculator asks for an origin, a destination and a distance in
 * kilometres, and the third is typed from memory. Two of every five journeys in
 * the register are quoted more than once, which is why the distance is
 * remembered — but the first person to type it still has nothing to check it
 * against. A map of the two ends is that check.
 *
 * <b>No key is written here, and none is ever asked for.</b> The embed key
 * arrives as an argument from a server-rendered prop, so it is set as an
 * application setting and changed without a rebuild. It is visible to anyone
 * who opens the app — that is what an embed key is, and Google's answer to it
 * is an HTTP referrer restriction on the key rather than hiding it.
 *
 * Everything here is a string built from two place names. There is no request,
 * no library and no key handling, which is what lets it be tested on its own.
 */

/** Google's own host for both the embed and the ordinary map link. */
const EMBED = "https://www.google.com/maps/embed/v1/directions";
const LINK = "https://www.google.com/maps/dir/";

/**
 * Which country's places to prefer when a name is ambiguous.
 *
 * "Amata City" and "Bangpakong" are the register's words, written for people
 * who know they are in Thailand. Without the bias Google is free to answer with
 * somewhere else entirely, and a distance read off that map would be wrong in a
 * way nobody would question — it came from Google.
 */
export const REGION = "TH";

const clean = (place: string | undefined): string => (place ?? "").trim();

/**
 * The embed URL for a journey, or null when it cannot be drawn.
 *
 * Null rather than a broken URL in the two cases that matter: no key
 * configured, and a journey with only one end named. A blank iframe on a
 * pricing screen reads as "Google has nothing for this route", which is a
 * different and much worse statement than "this app has not been given a key".
 */
export function directionsEmbed(key: string, from: string, to: string): string | null {
  const origin = clean(from);
  const destination = clean(to);
  if (!clean(key) || !origin || !destination) return null;

  const query = new URLSearchParams({
    key: clean(key),
    origin,
    destination,
    // Lorries. Walking or transit directions between a port and an industrial
    // estate would be an answer to a question nobody asked.
    mode: "driving",
    region: REGION,
  });
  return `${EMBED}?${query.toString()}`;
}

/**
 * The same route on Google Maps proper, in a new tab.
 *
 * Needs no key at all, so it is offered whether or not one is configured — and
 * it is the honest fallback for the case where somebody wants to drag the route
 * around, read the distance, or check an alternative the embed will not show.
 */
export function directionsLink(from: string, to: string): string | null {
  const origin = clean(from);
  const destination = clean(to);
  if (!origin || !destination) return null;
  return LINK + [origin, destination].map(encodeURIComponent).join("/");
}

/** Whether there is enough to draw anything: both ends named. */
export const hasRoute = (from: string, to: string): boolean =>
  clean(from).length > 0 && clean(to).length > 0;
