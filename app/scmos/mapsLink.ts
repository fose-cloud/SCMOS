/**
 * A link to the route on Google Maps.
 *
 * This began as an embedded map. The embed is gone: it needed a key behind a
 * Google Cloud billing account, and it drew a route Google had worked out for
 * itself beside a distance that came from somewhere else. The calculator now
 * draws the road OpenRouteService actually measured, on OpenStreetMap tiles,
 * with no key at all — see slippyMap.
 *
 * What survived is the part that never needed a key: a link that opens the same
 * two ends in Google Maps proper, for dragging the route around, reading an
 * alternative, or checking the picture against somebody else's map.
 */

const LINK = "https://www.google.com/maps/dir/";

const clean = (place: string | undefined): string => (place ?? "").trim();

/** The route on Google Maps, in a new tab. Needs no key and no account. */
export function directionsLink(from: string, to: string): string | null {
  const origin = clean(from);
  const destination = clean(to);
  if (!origin || !destination) return null;
  return LINK + [origin, destination].map(encodeURIComponent).join("/");
}

/** Whether there is enough to draw anything: both ends named. */
export const hasRoute = (from: string, to: string): boolean =>
  clean(from).length > 0 && clean(to).length > 0;
