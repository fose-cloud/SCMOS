import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { draftGroups, growDrafts, isDraft, sheetToday } from "../app/scmos/rateSheetDrafts.ts";

/*
 * Several new rows on the rate sheet at once. Two decisions live in the
 * module: what a paste lands on when it runs past the new rows, and how the
 * rows are grouped into the inquiries the register numbers. Both rewrite or
 * create commercial records, so both are pinned here rather than trusted to
 * the screen.
 */

let id = 0;
const blank = () => ({ laneId: -(++id), date: "", customer: "", fromPlace: "", toPlace: "", prices: {} });
const lane = (laneId) => ({ laneId, date: "01/09/2026", customer: "Saved", fromPlace: "A", toPlace: "B", prices: {} });
const edit = (row, field, value) => ({ row, field, value });

test("a paste that runs past the last new row grows the new rows instead of writing over saved lanes", () => {
  const drafts = [blank()];
  const rows = [lane(10), lane(11), lane(12)];
  const edits = [
    edit(drafts[0], "customer", "Clariant"), edit(rows[0], "customer", "Clariant"), edit(rows[1], "customer", "Clariant"),
  ];
  const grown = growDrafts(edits, drafts, rows, blank);
  assert.equal(grown.grown, 2);
  assert.equal(grown.drafts.length, 3);
  assert.ok(grown.edits.every((one) => isDraft(one.row)), "no edit is left pointing at a saved lane");
  // The first saved row's share lands on the first added draft, in order.
  assert.equal(grown.edits[1].row, grown.drafts[1]);
  assert.equal(grown.edits[2].row, grown.drafts[2]);
  assert.deepEqual(drafts.length, 1, "the caller's array is not mutated");
});

test("a paste inside the new rows, or one that starts on a saved lane, is left alone", () => {
  const drafts = [blank(), blank()];
  const rows = [lane(10), lane(11)];
  const inside = [edit(drafts[0], "customer", "X"), edit(drafts[1], "customer", "Y")];
  assert.equal(growDrafts(inside, drafts, rows, blank).grown, 0);
  const onSaved = [edit(rows[0], "customer", "X"), edit(rows[1], "customer", "Y")];
  assert.equal(growDrafts(onSaved, drafts, rows, blank).grown, 0);
  assert.deepEqual(growDrafts(onSaved, drafts, rows, blank).edits, onSaved);
});

test("a hand-picked range with a gap is not read as a block that ran on", () => {
  const drafts = [blank()];
  const rows = [lane(10), lane(11), lane(12)];
  // Touches the draft and the third saved row, not the first two: somebody
  // chose those cells. Growing here would turn a deliberate edit into a new row.
  const gapped = [edit(drafts[0], "customer", "X"), edit(rows[2], "customer", "Y")];
  assert.equal(growDrafts(gapped, drafts, rows, blank).grown, 0);
  assert.equal(growDrafts([], [], rows, blank).grown, 0, "and nothing happens with no drafts at all");
});

test("rows with the same customer and date file as one inquiry, in the order typed", () => {
  const today = "14/09/2026";
  const rows = [
    { ...blank(), customer: "Clariant (Thailand) Ltd.", date: "", fromPlace: "LCH", toPlace: "BKK" },
    { ...blank(), customer: "Unique Coating", date: "11/09/2026", fromPlace: "Hazchem", toPlace: "LCH" },
    { ...blank(), customer: "clariant (thailand) ltd. ", date: "", fromPlace: "BKK Port", toPlace: "LCH" },
    { ...blank(), customer: "Clariant (Thailand) Ltd.", date: "11/09/2026", fromPlace: "X", toPlace: "Y" },
  ];
  const groups = draftGroups(rows, today);
  assert.deepEqual(groups.map((one) => [one.customer, one.inquiredOn, one.rows.length]), [
    ["Clariant (Thailand) Ltd.", today, 2],   // case and trailing space do not make a second number
    ["Unique Coating", "11/09/2026", 1],
    ["Clariant (Thailand) Ltd.", "11/09/2026", 1], // a different day is a different inquiry
  ]);
  assert.equal(groups[0].rows[1].fromPlace, "BKK Port", "lanes keep the order they were typed in");
});

test("today is written the way the sheet writes a date", () => {
  assert.equal(sheetToday(new Date(2026, 8, 4)), "04/09/2026");
});

test("the sheet saves every new row through the grouping, and inserts one more per press", () => {
  const screen = readFileSync(new URL("../app/scmos/screens/RateSheet.tsx", import.meta.url), "utf8");
  assert.match(screen, /const groups = draftGroups\(next, sheetToday\(\)\);/);
  assert.match(screen, /for \(const group of groups\) \{[\s\S]*?apiFetch\("\/api\/rate-inquiries"/);
  assert.match(screen, /setDrafts\(\(was\) => \[\.\.\.was, added\]\)/);
  // A paste is offered the chance to grow the drafts before anything is written.
  assert.match(screen, /if \(how === "paste"\) \(\{ edits, drafts: held, grown \} = growDrafts\(edits, drafts, rows, blankDraft\)\);/);
  // The one-row sentinel is gone: nothing may compare a lane id to a fixed draft id.
  assert.doesNotMatch(screen, /=== DRAFT\b/);
});
