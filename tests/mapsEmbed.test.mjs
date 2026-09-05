import assert from "node:assert/strict";
import test from "node:test";
import { REGION, directionsEmbed, directionsLink, hasRoute } from "../app/scmos/mapsEmbed.ts";

/**
 * The route a quotation is pricing, on a map.
 *
 * Mostly about what must not be drawn. A pricing screen showing an empty map,
 * or a map of the wrong Amata City, is worse than one showing no map at all —
 * a distance read off either would be wrong and would carry Google's authority
 * while being wrong.
 */

test("a journey with both ends draws", () => {
  const url = directionsEmbed("KEY123", "LCB Port", "Amata City");
  const query = new URL(url).searchParams;

  assert.equal(new URL(url).pathname, "/maps/embed/v1/directions");
  assert.equal(query.get("origin"), "LCB Port");
  assert.equal(query.get("destination"), "Amata City");
  assert.equal(query.get("key"), "KEY123");
  assert.equal(query.get("mode"), "driving", "lorries, not pedestrians");
});

test("places are biased to Thailand", () => {
  // "Amata City" is the register's word, written by people who know where they
  // are. Unbiased, Google may answer with somewhere else, and a distance read
  // off that map is wrong in the one way nobody would question — it came from
  // Google.
  const query = new URL(directionsEmbed("K", "a", "b")).searchParams;
  assert.equal(query.get("region"), REGION);
  assert.equal(REGION, "TH");
});

test("no key means no map, not an empty one", () => {
  // A blank iframe on a pricing screen reads as "Google has nothing for this
  // route". That is a different statement from "nobody has given this app a
  // key", and only one of them is true.
  assert.equal(directionsEmbed("", "LCB Port", "Amata City"), null);
  assert.equal(directionsEmbed("   ", "LCB Port", "Amata City"), null);
  // An unset application setting arrives as undefined before the prop's `?? ""`
  // ever runs, so the guard is checked against that shape too.
  assert.equal(directionsEmbed(undefined, "LCB Port", "Amata City"), null);
});

test("half a journey is not a journey", () => {
  assert.equal(directionsEmbed("K", "LCB Port", ""), null);
  assert.equal(directionsEmbed("K", "", "Amata City"), null);
  assert.equal(directionsEmbed("K", "  ", "  "), null);
  assert.equal(hasRoute("LCB Port", ""), false);
  assert.equal(hasRoute("LCB Port", "Amata City"), true);
});

test("place names survive being put in a URL", () => {
  // The register holds "W/H OPTIDUR" and "Frasers Property, Bangpakong". A
  // slash or an ampersand that is not encoded silently truncates the route.
  const url = directionsEmbed("K", "W/H OPTIDUR", "Frasers Property, Bangpakong & Co");
  const query = new URL(url).searchParams;
  assert.equal(query.get("origin"), "W/H OPTIDUR");
  assert.equal(query.get("destination"), "Frasers Property, Bangpakong & Co");
});

test("Thai place names survive too", () => {
  const query = new URL(directionsEmbed("K", "ท่าเรือแหลมฉบัง", "นิคมอมตะซิตี้")).searchParams;
  assert.equal(query.get("origin"), "ท่าเรือแหลมฉบัง");
  assert.equal(query.get("destination"), "นิคมอมตะซิตี้");
});

test("the ordinary maps link needs no key at all", () => {
  // Which is why it is offered whether or not one is configured: it is the way
  // to check a distance on a site that has never been given a key.
  const link = directionsLink("LCB Port", "Amata City");
  assert.equal(link, "https://www.google.com/maps/dir/LCB%20Port/Amata%20City");
  assert.doesNotMatch(link, /key/i, "no key belongs in a link handed to a browser tab");
});

test("the link still needs both ends", () => {
  assert.equal(directionsLink("LCB Port", ""), null);
  assert.equal(directionsLink("", ""), null);
});

test("a slash in a place name does not become a third stop", () => {
  // "W/H OPTIDUR" unencoded would make Google read a route through "W" to "H
  // OPTIDUR" — a plausible-looking map of a journey that does not exist.
  const link = directionsLink("W/H OPTIDUR", "Amata City");
  assert.equal(link.split("/").length, "https://www.google.com/maps/dir/x/y".split("/").length);
  assert.match(link, /W%2FH%20OPTIDUR/);
});
