import assert from "node:assert/strict";
import test from "node:test";

import {
  ANY_CATEGORY,
  DEFAULT_CATEGORY,
  JOB_CATEGORIES,
  categoriesOfferedOn,
  categoryForNewRow,
} from "../app/scmos/newRowCategory.ts";

/*
 * The bug this was written for: inserting a row on The Chemours' domestic grid
 * created an IMPORT job. It appeared under Import in My Job — on somebody
 * else's screen — and nothing on the Chemours grid showed where it had gone,
 * because that grid can only display DELIVERY.
 */
test("a locked grid decides the category, whatever the filter says", () => {
  assert.equal(categoryForNewRow("DELIVERY", ANY_CATEGORY), "DELIVERY");
  assert.equal(categoryForNewRow("DELIVERY", "IMPORT"), "DELIVERY");
  assert.equal(categoryForNewRow("DELIVERY", "EXPORT"), "DELIVERY");
  assert.equal(categoryForNewRow("DELIVERY", ""), "DELIVERY");
});

test("the Chemours domestic grid never produces an import job", () => {
  // The exact shape of the reported bug: that grid hides the filter chips, so
  // the filter is always "ALL" there and the default used to win.
  assert.notEqual(categoryForNewRow("DELIVERY", ANY_CATEGORY), DEFAULT_CATEGORY);
});

test("with no lock, the row lands in the section being looked at", () => {
  assert.equal(categoryForNewRow(undefined, "IMPORT"), "IMPORT");
  assert.equal(categoryForNewRow(undefined, "EXPORT"), "EXPORT");
  assert.equal(categoryForNewRow(undefined, "DELIVERY"), "DELIVERY");
  assert.equal(categoryForNewRow(null, "EXPORT"), "EXPORT");
});

test("with no lock and no filter, it falls back to a category that is displayed", () => {
  // A guess, but a visible one. The grid splits by category when nothing is
  // filtered, so the row shows up somewhere a person can correct it.
  assert.equal(categoryForNewRow(undefined, ANY_CATEGORY), DEFAULT_CATEGORY);
  assert.equal(categoryForNewRow(undefined, undefined), DEFAULT_CATEGORY);
  assert.equal(categoryForNewRow(null, null), DEFAULT_CATEGORY);
  assert.equal(categoryForNewRow("", ""), DEFAULT_CATEGORY);
});

test("it never returns a category no grid can display", () => {
  const answers = [
    categoryForNewRow(undefined, ANY_CATEGORY),
    categoryForNewRow(undefined, "nonsense"),
    categoryForNewRow("nonsense", ANY_CATEGORY),
    categoryForNewRow(undefined, undefined),
    categoryForNewRow("  ", "  "),
  ];
  for (const answer of answers) {
    assert.ok(JOB_CATEGORIES.includes(answer), `${answer} is not a job category`);
  }
});

test("a category nobody recognises is ignored rather than written to a job", () => {
  assert.equal(categoryForNewRow("SOMETHING", ANY_CATEGORY), DEFAULT_CATEGORY);
  assert.equal(categoryForNewRow(undefined, "SOMETHING"), DEFAULT_CATEGORY);
  // But a real one that arrives oddly cased or padded is still real.
  assert.equal(categoryForNewRow(" delivery ", ANY_CATEGORY), "DELIVERY");
  assert.equal(categoryForNewRow(undefined, "export"), "EXPORT");
});

test("ALL is a filter, never a category a job can be saved with", () => {
  assert.ok(!JOB_CATEGORIES.includes(ANY_CATEGORY));
  assert.equal(categoryForNewRow(ANY_CATEGORY, ANY_CATEGORY), DEFAULT_CATEGORY);
});

/*
 * Keeping the row where it was put. The category is an editable cell, so
 * creating it as DELIVERY is only half the job — on a grid that shows delivery
 * work and nothing else, every other option makes the row disappear from the
 * screen that changed it.
 */
test("a locked grid offers only its own category", () => {
  assert.deepEqual(categoriesOfferedOn("DELIVERY"), ["DELIVERY"]);
  assert.deepEqual(categoriesOfferedOn(" delivery "), ["DELIVERY"]);
});

test("so a job inserted on the Chemours grid cannot be moved off it", () => {
  const offered = categoriesOfferedOn("DELIVERY");
  assert.ok(!offered.includes("IMPORT"));
  assert.ok(!offered.includes("EXPORT"));
  // This is also the list a pasted value is judged against, so pasting
  // "IMPORT" into that column is refused rather than quietly moving the job.
  assert.equal(offered.length, 1);
});

test("My Job still offers every category, because a wrong one has to be fixable", () => {
  assert.deepEqual(categoriesOfferedOn(undefined), [...JOB_CATEGORIES]);
  assert.deepEqual(categoriesOfferedOn(null), [...JOB_CATEGORIES]);
  assert.deepEqual(categoriesOfferedOn(ANY_CATEGORY), [...JOB_CATEGORIES]);
  assert.deepEqual(categoriesOfferedOn("nonsense"), [...JOB_CATEGORIES]);
});

test("what a locked grid creates is what it offers, so the two cannot disagree", () => {
  for (const locked of JOB_CATEGORIES) {
    const made = categoryForNewRow(locked, ANY_CATEGORY);
    assert.deepEqual(categoriesOfferedOn(locked), [made],
      `a grid locked to ${locked} creates ${made} and must offer exactly that`);
  }
});
