import assert from "node:assert/strict";
import test from "node:test";
import { directionsLink, hasRoute } from "../app/scmos/mapsLink.ts";

/**
 * The link out to Google Maps beside the route picture.
 *
 * What is left of an embedded map that needed a key. The picture is drawn from
 * OpenStreetMap tiles now — see slippyMap — and this is the way to open the
 * same two ends somewhere they can be dragged around.
 */

test("both ends make a link, and it needs no key", () => {
  const link = directionsLink("LCB Port", "Amata City");
  assert.equal(link, "https://www.google.com/maps/dir/LCB%20Port/Amata%20City");
  assert.doesNotMatch(link, /key|token|api/i, "no key belongs in a link handed to a browser tab");
});

test("half a journey is not a journey", () => {
  assert.equal(directionsLink("LCB Port", ""), null);
  assert.equal(directionsLink("", "Amata City"), null);
  assert.equal(directionsLink("  ", "  "), null);
  assert.equal(hasRoute("LCB Port", ""), false);
  assert.equal(hasRoute("LCB Port", "Amata City"), true);
});

test("a slash in a place name does not become a third stop", () => {
  // "W/H OPTIDUR" unencoded would make Google read a route through "W" to "H
  // OPTIDUR" — a plausible-looking map of a journey that does not exist.
  const link = directionsLink("W/H OPTIDUR", "Amata City");
  assert.equal(link.split("/").length, "https://www.google.com/maps/dir/x/y".split("/").length);
  assert.match(link, /W%2FH%20OPTIDUR/);
});

test("Thai place names survive being put in a URL", () => {
  const link = directionsLink("ท่าเรือแหลมฉบัง", "ท่าเรือกรุงเทพ");
  assert.equal(link,
    "https://www.google.com/maps/dir/"
    + encodeURIComponent("ท่าเรือแหลมฉบัง") + "/" + encodeURIComponent("ท่าเรือกรุงเทพ"));
});
