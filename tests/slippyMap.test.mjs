import assert from "node:assert/strict";
import test from "node:test";
import {
  ATTRIBUTION, MAX_ZOOM, TILE, bounds, pointsFrom, project, tileUrl, view,
} from "../app/scmos/slippyMap.ts";

/**
 * The arithmetic behind the route picture on the quotation calculator.
 *
 * Written out rather than pulled in, so it is tested against the published
 * numbers rather than against itself. The projection in particular has one
 * right answer that everybody else's map agrees on, and if this file drifts
 * from it the road is drawn beside the wrong towns.
 */

/** The journey the calculator was tested on: Laem Chabang to Bangkok Port. */
const LCB = { lon: 100.8833, lat: 13.0827 };
const BKK = { lon: 100.5686, lat: 13.7038 };

test("Web Mercator agrees with everybody else's map", () => {
  // Zoom 0 is one tile for the whole world, so null island is dead centre.
  const origin = project({ lon: 0, lat: 0 }, 0);
  assert.equal(origin.x, 0.5);
  assert.ok(Math.abs(origin.y - 0.5) < 1e-9);

  /*
   * Laem Chabang at zoom 12. Worked out with the canonical form of the
   * projection — x = (lon+180)/360 * 2^z, y = (1 - asinh(tan lat)/pi)/2 * 2^z —
   * which is a different expression from the one under test: the module uses
   * log(tan + sec), and asinh(x) = ln(x + sqrt(x^2+1)) with sqrt(tan^2+1) = sec.
   * Same mathematics, arrived at separately, so agreeing means something.
   *
   * The first numbers written here were 3220/1937, from memory rather than from
   * the sum. They were wrong and this test said so.
   */
  const lcb = project(LCB, 12);
  assert.equal(Math.floor(lcb.x), 3195);
  assert.equal(Math.floor(lcb.y), 1897);
  assert.ok(Math.abs(lcb.x - 3195.8278) < 1e-3);
  assert.ok(Math.abs(lcb.y - 1897.8374) < 1e-3);

  const bkk = project(BKK, 12);
  assert.equal(Math.floor(bkk.x), 3192);
  assert.equal(Math.floor(bkk.y), 1890);
});

test("north is up and east is right", () => {
  const north = project({ lon: 100, lat: 14 }, 10);
  const south = project({ lon: 100, lat: 13 }, 10);
  const east = project({ lon: 101, lat: 13 }, 10);
  assert.ok(north.y < south.y, "further north is a smaller y");
  assert.ok(east.x > south.x, "further east is a larger x");
});

test("the poles are clamped rather than run to infinity", () => {
  // Mercator's own limit. A coordinate past it would take the whole picture
  // with it, so one bad point spoils one point.
  for (const lat of [90, -90, 89.9, -95]) {
    const at = project({ lon: 0, lat }, 8);
    assert.ok(Number.isFinite(at.y), `latitude ${lat} must stay finite`);
  }
});

test("the box is the smallest one holding every point", () => {
  const box = bounds([LCB, BKK]);
  assert.equal(box.west, 100.5686);
  assert.equal(box.east, 100.8833);
  assert.equal(box.south, 13.0827);
  assert.equal(box.north, 13.7038);
});

test("a point nobody could read does not widen the box to the whole world", () => {
  const box = bounds([LCB, { lon: NaN, lat: 13 }, BKK]);
  assert.equal(box.west, 100.5686);
  assert.ok(Number.isFinite(box.east));
  assert.equal(bounds([]), null);
  assert.equal(bounds([{ lon: NaN, lat: NaN }]), null);
});

test("the zoom is chosen so the whole journey fits", () => {
  // The reason it is chosen rather than fixed: a fifteen-kilometre run and a
  // four-hundred-kilometre one must each fill the panel, not be a dot and an
  // overflow.
  const near = view([LCB, { lon: 100.9, lat: 13.1 }], 600, 360);
  const far = view([LCB, { lon: 98.98, lat: 18.79 }], 600, 360);
  assert.ok(near.zoom > far.zoom, "a short journey zooms in further");
  assert.ok(far.zoom >= 1 && near.zoom <= MAX_ZOOM);
});

test("every point of the road lands inside the picture", () => {
  const road = [LCB, { lon: 100.7, lat: 13.4 }, BKK];
  const drawn = view(road, 600, 360);
  assert.equal(drawn.line.length, 3);
  for (const at of drawn.line) {
    assert.ok(at.x >= 0 && at.x <= 600, `x ${at.x} is off the picture`);
    assert.ok(at.y >= 0 && at.y <= 360, `y ${at.y} is off the picture`);
  }
});

test("the road is drawn in the order it is travelled", () => {
  const drawn = view([LCB, BKK], 600, 360);
  assert.ok(drawn.line[0].x > drawn.line[1].x, "Laem Chabang is east of Bangkok");
  assert.ok(drawn.line[0].y > drawn.line[1].y, "and south of it");
});

test("the tiles cover the picture and no more", () => {
  const drawn = view([LCB, BKK], 600, 360);
  assert.ok(drawn.tiles.length > 0);
  // Every tile must touch the picture; one that does not is a request nobody
  // needed made against a volunteer-run tile server.
  for (const tile of drawn.tiles) {
    assert.ok(tile.left + TILE > 0 && tile.left < 600, `tile at ${tile.left} is off-picture`);
    assert.ok(tile.top + TILE > 0 && tile.top < 360, `tile at ${tile.top} is off-picture`);
    assert.ok(tile.x >= 0 && tile.x < 2 ** tile.z, "tile x must exist at this zoom");
    assert.ok(tile.y >= 0 && tile.y < 2 ** tile.z, "tile y must exist at this zoom");
  }
});

test("a picture is never asked for a tile above or below the world", () => {
  // Near the pole the rows run off the end. Wrapping them would fetch the wrong
  // tile and draw it as though it belonged there.
  const drawn = view([{ lon: 0, lat: 84.9 }, { lon: 0.001, lat: 84.91 }], 600, 360);
  for (const tile of drawn.tiles) assert.ok(tile.y >= 0 && tile.y < 2 ** tile.z);
});

test("one point still makes a picture", () => {
  // Half a journey: the geocoder found one end and the route failed. Better a
  // map of the end it knows than a blank panel.
  const drawn = view([LCB], 600, 360);
  assert.equal(drawn.line.length, 1);
  assert.ok(drawn.tiles.length > 0);
  assert.equal(view([], 600, 360), null);
});

test("tiles come from OpenStreetMap and need no key", () => {
  const url = tileUrl({ x: 3220, y: 1937, z: 12 });
  assert.equal(url, "https://tile.openstreetmap.org/12/3220/1937.png");
  assert.doesNotMatch(url, /key|token|api/i, "no key belongs in a tile URL");
  assert.match(ATTRIBUTION, /OpenStreetMap/, "their licence requires the credit");
});

test("the flat pairs the API sends become points", () => {
  assert.deepEqual(pointsFrom([100.8833, 13.0827, 100.5686, 13.7038]), [LCB, BKK]);
  assert.deepEqual(pointsFrom([]), []);
  assert.deepEqual(pointsFrom(undefined), []);
  // A trailing half-pair is dropped rather than read as a point at zero.
  assert.deepEqual(pointsFrom([100.8833, 13.0827, 100.5686]), [LCB]);
});
