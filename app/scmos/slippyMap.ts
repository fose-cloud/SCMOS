/**
 * Enough of a map to draw one road on.
 *
 * The quotation calculator wanted the route visible beside the distance. The
 * obvious answers both cost something this does not: Google's embed needs a key
 * behind a billing account, and a map library would be this project's fifth
 * runtime dependency — for one static picture on one screen, in a repository
 * whose whole dependency list is Next, React and a spreadsheet writer.
 *
 * What is left is the arithmetic, which is small. Web Mercator is two lines
 * each way, tiles are a 256-pixel grid indexed by that projection, and
 * OpenStreetMap serves them at a URL that needs no key, no account and no card.
 * Everything here is a number in and a number out, so it is tested on its own.
 *
 * <b>Static on purpose.</b> No panning, no zooming, no clicking — the question
 * is "does this road look like the journey I am pricing", and that is answered
 * by one picture. Wanting to drag it around is what the "open in Google Maps"
 * link beside it is for.
 */

/** OpenStreetMap's tiles are 256 pixels square, which fixes most of the sums. */
export const TILE = 256;

/**
 * How far in the map is allowed to go.
 *
 * Zoom 16 is a street. Past that a route the length of a working journey would
 * need thousands of tiles, and the point of this picture is the whole road.
 */
export const MAX_ZOOM = 16;

/** A point on the earth, in the order the routing service sends them. */
export type Point = { lon: number; lat: number };

/**
 * Longitude and latitude to Web Mercator, in tiles at this zoom.
 *
 * Latitude is clamped to the projection's own limit. Mercator cannot draw the
 * poles — the sum runs to infinity — and a route that touched one would take
 * the whole picture with it. Nothing in Thailand comes close; the clamp is
 * there so that a bad coordinate spoils one point rather than the map.
 */
export function project({ lon, lat }: Point, zoom: number): { x: number; y: number } {
  const scale = 2 ** zoom;
  const clamped = Math.max(-85.05112878, Math.min(85.05112878, lat));
  const radians = (clamped * Math.PI) / 180;
  return {
    x: ((lon + 180) / 360) * scale,
    y: ((1 - Math.log(Math.tan(radians) + 1 / Math.cos(radians)) / Math.PI) / 2) * scale,
  };
}

/** The smallest box containing every point. */
export function bounds(points: Point[]): { west: number; east: number; south: number; north: number } | null {
  if (points.length === 0) return null;
  let west = Infinity, east = -Infinity, south = Infinity, north = -Infinity;
  for (const one of points) {
    if (!Number.isFinite(one.lon) || !Number.isFinite(one.lat)) continue;
    west = Math.min(west, one.lon);
    east = Math.max(east, one.lon);
    south = Math.min(south, one.lat);
    north = Math.max(north, one.lat);
  }
  return Number.isFinite(west) ? { west, east, south, north } : null;
}

export type View = {
  zoom: number;
  /** Tiles to fetch, with where each one sits in the picture. */
  tiles: { x: number; y: number; z: number; left: number; top: number }[];
  /** The road, already in the picture's own pixels. */
  line: { x: number; y: number }[];
  width: number;
  height: number;
};

/**
 * Everything needed to draw the road inside a box of the given size.
 *
 * The zoom is chosen rather than asked for: the largest that still fits both
 * ends, so a fifteen-kilometre run and a four-hundred-kilometre one each fill
 * the panel instead of one being a dot and the other running off the edge.
 */
export function view(points: Point[], width: number, height: number, padding = 24): View | null {
  const box = bounds(points);
  if (!box) return null;

  // Largest zoom where the whole route still fits inside the padding.
  const usableWidth = Math.max(1, width - padding * 2);
  const usableHeight = Math.max(1, height - padding * 2);
  let zoom = MAX_ZOOM;
  for (; zoom > 0; zoom--) {
    const a = project({ lon: box.west, lat: box.north }, zoom);
    const b = project({ lon: box.east, lat: box.south }, zoom);
    if (Math.abs(b.x - a.x) * TILE <= usableWidth && Math.abs(b.y - a.y) * TILE <= usableHeight) break;
  }

  // Centre the box, then work out which tiles the picture lands on.
  const middle = project(
    { lon: (box.west + box.east) / 2, lat: (box.south + box.north) / 2 }, zoom);
  const originX = middle.x * TILE - width / 2;
  const originY = middle.y * TILE - height / 2;

  const span = 2 ** zoom;
  const firstX = Math.floor(originX / TILE);
  const lastX = Math.floor((originX + width) / TILE);
  const firstY = Math.floor(originY / TILE);
  const lastY = Math.floor((originY + height) / TILE);

  const tiles: View["tiles"] = [];
  for (let ty = firstY; ty <= lastY; ty++) {
    // Off the top or bottom of the world there is no tile to ask for. Wrapping
    // would fetch the wrong one and draw it as though it belonged.
    if (ty < 0 || ty >= span) continue;
    for (let tx = firstX; tx <= lastX; tx++) {
      tiles.push({
        // East-west does wrap: the world is a cylinder, and a picture spanning
        // the date line is a real, if unlikely, thing to be looking at.
        x: ((tx % span) + span) % span,
        y: ty,
        z: zoom,
        left: tx * TILE - originX,
        top: ty * TILE - originY,
      });
    }
  }

  const line = points
    .filter((one) => Number.isFinite(one.lon) && Number.isFinite(one.lat))
    .map((one) => {
      const at = project(one, zoom);
      return { x: at.x * TILE - originX, y: at.y * TILE - originY };
    });

  return { zoom, tiles, line, width, height };
}

/**
 * Where a tile comes from.
 *
 * OpenStreetMap's own servers, which need no key and no account. Their usage
 * policy asks for light, non-bulk use and this is that: a handful of tiles when
 * somebody prices a journey, cached by the browser afterwards. If this screen
 * ever becomes something people leave open, move to a paid tile host rather
 * than leaning harder on a volunteer one.
 */
export const tileUrl = ({ x, y, z }: { x: number; y: number; z: number }): string =>
  `https://tile.openstreetmap.org/${z}/${x}/${y}.png`;

/** Required by that policy, and by good manners. */
export const ATTRIBUTION = "© OpenStreetMap contributors";

/** The flat [lon, lat, lon, lat, …] the API sends, as points. */
export function pointsFrom(flat: number[] | undefined): Point[] {
  if (!flat) return [];
  const points: Point[] = [];
  for (let at = 0; at + 1 < flat.length; at += 2) points.push({ lon: flat[at], lat: flat[at + 1] });
  return points;
}
