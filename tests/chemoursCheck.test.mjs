import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import {
  carrierOnCard, carrierTallies, checkTrips, mergeLanes, monthsOf, sameCarrier, trucksOf, verdictField, verdictText,
} from "../app/scmos/chemoursCheck.ts";

/*
 * The Domestic runs held against the cost card: which trips the table can
 * price, what it says they cost, and what it says about the ones it cannot.
 */

const bands = [
  { label: "28.01-30.00", min: 28.01, max: 30 },
  { label: "30.01-32.00", min: 30.01, max: 32 },
  { label: "32.01-34.00", min: 32.01, max: 34 },
];
const lanes = [
  // THAI KOT from SCGJWD to Amatanakorn 20000: three sizes priced across the bands.
  { carrier: "THAI KOT", from: "SCGJWD Warehouse (LCH)", to: "Chonburi, Amatanakorn", county: "20000",
    prices: { "4W": [1000, 1030, 1061], "6W": [1500, 1545, 1591], "10W": [2200, 2266, 2334] } },
  // The same postcode from the other warehouse, at other prices.
  { carrier: "THAI KOT", from: "Unithai (Bangna KM. 23)", to: "Chonburi, Amatanakorn", county: "20000",
    prices: { "4W": [900, 927, 955], "6W": [1400, 1442, 1485], "10W": [2000, 2060, 2122] } },
  // A lane that quotes only the 6W.
  { carrier: "THAI KOT", from: "SCGJWD Warehouse (LCH)", to: "Rayong", county: "21140",
    prices: { "6W": [1800, 1854, 1910] } },
];
// Diesel: 29.50 all of July 2026, then 31.00 from 1 August.
const changes = [{ date: "01/07/2026", price: 29.5 }, { date: "01/08/2026", price: 31 }];

const job = (over) => ({
  key: "K", date: "15/07/2026", trucker: "THAI KOT", customer: "AMPACET", wh: "JWD", zip: "20000",
  destination: "Amatanakorn", v6: "1", ...over,
});

test("a trip the table prices: the lane by postcode and warehouse, the band by the month's diesel, the size's price", () => {
  const [trip] = checkTrips([job({ v6: "1", v10: "1" })], lanes, bands, changes);
  assert.equal(trip.verdict, "");
  assert.equal(trip.carrier, "THAI KOT");
  assert.equal(trip.lane.from, "SCGJWD Warehouse (LCH)", "JWD picked the SCGJWD lane of the two at 20000");
  assert.equal(trip.diesel, 29.5);
  assert.equal(trip.dieselFrom, "month");
  assert.equal(trip.bandLabel, "28.01-30.00");
  assert.equal(trip.cost.total, 1500 + 2200);
  assert.equal(trip.total, 3700, "no return leg, so the total is the trip");
  assert.equal(trip.trucks, "1×6W · 1×10W");
  assert.equal(verdictText(trip), "ตรงตามตาราง");
});

test("a month after the last published change is read at the price still in force", () => {
  const [trip] = checkTrips([job({ date: "15/09/2026" })], lanes, bands, changes);
  assert.equal(trip.diesel, 31, "31.00 from 1 August is still the pump price in September");
  assert.equal(trip.bandLabel, "30.01-32.00");
  assert.equal(trip.verdict, "");
});

test("a trip priced off the only lane at its postcode says so when that lane leaves from another warehouse", () => {
  const [trip] = checkTrips([job({ zip: "21140", wh: "UNITHAI" })], lanes, bands, changes);
  assert.equal(trip.verdict, "", "still priced — the postcode is the join");
  assert.match(trip.originNote, /UNITHAI/);
  assert.match(trip.originNote, /SCGJWD/);
  const [agreed] = checkTrips([job({ zip: "21140", wh: "JWD" })], lanes, bands, changes);
  assert.equal(agreed.originNote, "");
});

test("the job's own diesel wins over the month, and a return leg is added at half", () => {
  const [trip] = checkTrips([job({ diesel: "31.5", returnLoad: "TRUE" })], lanes, bands, changes);
  assert.equal(trip.dieselFrom, "job");
  assert.equal(trip.bandLabel, "30.01-32.00");
  assert.equal(trip.cost.total, 1545);
  assert.equal(trip.returnKind, "standard");
  assert.equal(trip.total, 1545 + Math.round(1545 / 2));
});

test("each thing that stops a trip being priced is named, in the column it belongs to", () => {
  const trips = checkTrips([
    job({ key: "A", trucker: "SSL" }),                          // no card for this haulier
    job({ key: "B", zip: "" }),                                 // no postcode
    job({ key: "C", zip: "99999" }),                            // postcode not on the card
    job({ key: "D", wh: "" }),                                  // two lanes at 20000, no warehouse to choose
    job({ key: "E", v6: "" }),                                  // no trucks
    job({ key: "F", zip: "21140", v6: "", v4: "1" }),           // the Rayong lane prices only a 6W
    job({ key: "G", date: "15/06/2026" }),                      // before the first published price
  ], lanes, bands, changes);
  const byKey = Object.fromEntries(trips.map((trip) => [trip.job.key, trip]));
  assert.equal(byKey.A.verdict, "carrier");
  assert.equal(verdictField("carrier"), "carrier");
  assert.equal(byKey.B.verdict, "no-zip");
  assert.equal(byKey.C.verdict, "no-lane");
  assert.match(verdictText(byKey.C), /99999/);
  assert.equal(byKey.D.verdict, "ambiguous");
  assert.equal(verdictField("ambiguous"), "destination");
  assert.equal(byKey.E.verdict, "no-trucks");
  assert.equal(byKey.F.verdict, "not-quoted");
  assert.equal(verdictField("not-quoted"), "vehicle");
  assert.equal(byKey.G.verdict, "no-diesel");
  assert.equal(byKey.G.diesel, null, "June began before any price was in force, so there is no figure");
  assert.match(verdictText(byKey.G), /Oil Rate/);
  for (const trip of trips) assert.equal(trip.total, null, `${trip.job.key} must not be summed`);
});

test("a haulier's total is over the trips the table priced, and says how many it could not", () => {
  const trips = checkTrips([
    job({ key: "A" }), job({ key: "B", v6: "2" }), job({ key: "C", zip: "" }),
    job({ key: "D", trucker: "SSL" }),
  ], lanes, bands, changes);
  const tallies = carrierTallies(trips);
  assert.deepEqual(tallies.map((t) => [t.carrier, t.trips, t.priced, t.flagged, t.cost]), [
    ["THAI KOT", 3, 2, 1, 1500 + 3000],
    ["SSL", 1, 0, 1, 0],
  ]);
});

test("a haulier card read one sheet per truck size is folded to one row per route before pricing", () => {
  // As the register holds THAI KOT's card: the same route three times, one size each.
  const split = [
    { carrier: "THAI KOT", from: "SCGJWD Warehouse (LCH)", to: "Chonburi, Amatanakorn", county: "20000", prices: { "4W": [1000, 1030, 1061] } },
    { carrier: "THAI KOT", from: "SCGJWD Warehouse (LCH)", to: "Chonburi, Amatanakorn", county: " 20000", prices: { "6W": [1500, 1545, 1591] } },
    { carrier: "THAI KOT", from: "SCGJWD Warehouse (LCH)", to: "Chonburi, Amatanakorn", county: "20000", prices: { "10W": [2200, 2266, 2334] } },
    { carrier: "THAI KOT", from: "Unithai (Bangna KM. 23)", to: "Chonburi, Amatanakorn", county: "20000", prices: { "6W": [1400, 1442, 1485] } },
  ];
  const merged = mergeLanes(split);
  assert.equal(merged.length, 2, "two routes, not four rows");
  assert.deepEqual(Object.keys(merged[0].prices), ["4W", "6W", "10W"]);
  const [trip] = checkTrips([job({ v6: "1", v10: "1" })], split, bands, changes);
  assert.equal(trip.verdict, "", "the split card prices the same trip the folded one does");
  assert.equal(trip.cost.total, 3700);
});

test("the register's trucker and the card's carrier meet loosely, exact name first", () => {
  assert.equal(sameCarrier("Thai Kot Transport", "THAI KOT"), true);
  assert.equal(sameCarrier("SSL", "THAI KOT"), false);
  assert.equal(carrierOnCard("THAI KOT", ["THAI KOT (JWD)", "THAI KOT"]), "THAI KOT");
  assert.equal(carrierOnCard("thai kot transport", ["THAI KOT"]), "THAI KOT");
  assert.equal(carrierOnCard("", ["THAI KOT"]), "");
});

test("trucks read as the card names them, tail lift included; months come newest first", () => {
  assert.equal(trucksOf({ v4: "1", v10: "0.5", vtl: "1" }), "1×4W · 0.5×10W · 1×TAIL LIFT");
  assert.equal(trucksOf({}), "");
  assert.deepEqual(monthsOf([{ date: "03/07/2026" }, { date: "15/08/2026" }, { date: "x" }, { date: "20/07/2026" }]),
    ["08/2026", "07/2026"]);
});

test("the Chemours screen carries the diesel prices and the check as tabs, and the old menu entry lands on the tab", () => {
  const nav = readFileSync(new URL("../app/scmos/nav.ts", import.meta.url), "utf8");
  assert.match(nav, /chemours: \["งาน Domestic", "ค่าขนส่ง", "Oil Rate", "ตรวจสอบค่าขนส่ง", "Cargo Receipt"\]/);
  assert.doesNotMatch(nav, /\["oilrate", "Oil Rate"/, "no menu entry of its own");
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.doesNotMatch(app, /screen === "oilrate" && <OilRate/);
  assert.match(app, /if \(screen !== "oilrate"\) return;\s*setScreen\("chemours"\);\s*setTab\(OIL_TAB\);/);
  const screen = readFileSync(new URL("../app/scmos/screens/Chemours.tsx", import.meta.url), "utf8");
  assert.match(screen, /if \(tab === OIL_TAB\) \{\s*return <OilRate/);
  assert.match(screen, /if \(tab === CHECK_TAB\) \{\s*return <ChemoursCheck jobs=\{jobs\} card=\{card \?\? null\}/);
});

test("the Domestic grid reads each run at its month's average off Oil Rate, the job's own figure first", () => {
  const grid = readFileSync(new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");
  assert.match(grid, /const dieselOf = \(j: Job\)[\s\S]*?const own = dieselRate\(j\.diesel\);[\s\S]*?rateForJob\(p\.dieselDays \?\? \[\], j\.date\)/);
  assert.match(grid, /bandForDiesel\(p\.customerCard\.bands, dieselOf\(j\)\.price\)/);
  assert.doesNotMatch(grid, /bandForDiesel\(p\.customerCard\.bands, p\.diesel\)/, "no column reads the screen-wide figure ahead of the month");
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.match(app, /setDieselDays\(monthsCovered\(changes, today\)\.flatMap\(\(month\) => expand\(changes, month, today\)\)\)/);
  assert.match(app, /dieselDays=\{dieselDays\}/);
  // The Oil Rate tab keys one day at a time through its own route.
  const oil = readFileSync(new URL("../app/scmos/screens/OilRate.tsx", import.meta.url), "utf8");
  assert.match(oil, /apiFetch\(`\/api\/diesel\/\$\{date\}`, \{\s*method: "PUT"/);
  assert.match(oil, /monthDays\(held, month, today\)/);
  assert.doesNotMatch(oil, /"\/api\/diesel", \{\s*method: "PUT"/, "the whole-table replace is not what an operator presses");
});
