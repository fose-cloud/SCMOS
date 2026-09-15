import test from "node:test";
import assert from "node:assert/strict";
import { checkTrips } from "../app/scmos/chemoursCheck.ts";
import { differencesOf, matchRow, reconcile, verdictWords } from "../app/scmos/haulierReconcile.ts";

/*
 * The haulier's file against our Domestic register: which row is which run,
 * where the two disagree, and what the register has that the file does not.
 */

const bands = [{ label: "28.01-30.00", min: 28.01, max: 30 }, { label: "30.01-32.00", min: 30.01, max: 32 }];
const lanes = [{ carrier: "THAI KOT", from: "SCGJWD Warehouse (LCH)", to: "Amatanakorn", county: "20000", prices: { "6W": [1500, 1545], "10W": [2200, 2266] } }];
const changes = [{ date: "01/09/2026", price: 29.5 }];
const ours = [
  { key: "A", date: "03/09/2026", trucker: "THAI KOT", customer: "AMPACET", wh: "JWD", zip: "20000", jobCode: "2609001", dCode: "D15440886", v6: "1" },
  { key: "B", date: "05/09/2026", trucker: "THAI KOT", customer: "SHERA", wh: "JWD", zip: "20000", jobCode: "2609002", v10: "1", returnLoad: "TRUE" },
  { key: "C", date: "07/09/2026", trucker: "THAI KOT", customer: "TOA", wh: "JWD", zip: "20000", jobCode: "2609003", v6: "1" },
];
const checks = checkTrips(ours, lanes, bands, changes, "15/09/2026");

test("a row is found by job code, D-code, TMS ID, or the day + consignee + postcode, and never twice", () => {
  const taken = new Set();
  assert.equal(matchRow({ key: "r1", date: "", trucker: "", customer: "", jobCode: "2609001" }, ours, taken).by, "jobCode");
  assert.equal(matchRow({ key: "r2", date: "", trucker: "", customer: "", dCode: "d15440886" }, ours, taken).job.key, "A");
  assert.equal(matchRow({ key: "r3", date: "05/09/2026", trucker: "", customer: "shera", zip: " 20000" }, ours, taken).by, "date+customer+zip");
  taken.add("A");
  assert.equal(matchRow({ key: "r4", date: "", trucker: "", customer: "", jobCode: "2609001" }, ours, taken).job, null, "A is already claimed by an earlier row");
});

test("each column the two sides disagree on is named both ways, and the price gap is signed", () => {
  const job = ours[0];
  const check = checks.find((one) => one.job.key === "A");
  assert.equal(check.total, 1500, "6W at 29.50 in the first band");
  const diffs = differencesOf({ key: "r", trucker: "", date: "04/09/2026", customer: "AMPACET", zip: "20230", wh: "UNITHAI", v10: "1", cost: "2,000" }, job, check);
  assert.deepEqual(diffs.map((one) => [one.field, one.theirs, one.ours]), [
    ["date", "04/09/2026", "03/09/2026"],
    ["destination", "20230", "20000"],
    ["wh", "UNITHAI", "JWD"],
    ["vehicle", "1×10W", "1×6W"],
    ["cost", "฿2,000", "฿1,500"],
  ]);
  assert.equal(diffs[4].gap, 500);
  assert.deepEqual(differencesOf({ key: "r", trucker: "", date: "03/09/2026", customer: "AMPACET", zip: "20000", v6: "1", cost: "1500" }, job, check), []);
});

test("the file against the register: matches, differences, rows we do not have, and runs the file does not bill", () => {
  const file = [
    { key: "r1", trucker: "", date: "03/09/2026", customer: "AMPACET", jobCode: "2609001", zip: "20000", v6: "1", cost: "1500" },
    // 10W with a return leg: our card says 2,200 + 1,100 = 3,300; the file bills 3,500.
    { key: "r2", trucker: "", date: "05/09/2026", customer: "SHERA", jobCode: "2609002", zip: "20000", v10: "1", cost: "3,500" },
    { key: "r3", trucker: "", date: "09/09/2026", customer: "POLYPLEX", jobCode: "2609099", zip: "10160", v6: "1", cost: "4000" },
  ];
  const done = reconcile(file, ours, checks);
  assert.deepEqual(done.lines.map((line) => line.verdict), ["match", "differs", "unmatched"]);
  assert.equal(verdictWords(done.lines[1]), "ต่างกัน: ค่าขนส่ง");
  assert.deepEqual([done.lines[1].differences[0].theirs, done.lines[1].differences[0].ours, done.lines[1].differences[0].gap], ["฿3,500", "฿3,300", 200]);
  assert.equal(verdictWords(done.lines[2]), "ไม่พบงานนี้ในตาราง Domestic");
  assert.deepEqual(done.unbilled.map((job) => job.key), ["C"]);
  assert.equal(done.theirTotal, 1500 + 3500 + 4000);
  assert.equal(done.ourTotal, 1500 + 3300);
  assert.deepEqual(done.counts, { rows: 3, matched: 2, match: 1, differs: 1, unmatched: 1, unpriced: 0 });
});
