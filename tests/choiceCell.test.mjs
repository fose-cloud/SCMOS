import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { resolveBlock, resolveChoice } from "../app/scmos/choiceCell.ts";

/**
 * Pasting into a dropdown column.
 *
 * Customer, Trucker and Type are dropdowns because the register has to
 * recognise the value — a job against a carrier nobody can bill is worse than
 * a job with no carrier. Copy and paste were wanted on them all the same, and
 * both things can be true: the block lands, and every value in it came off the
 * list.
 */

const CARRIERS = ["SANGJA", "SSL", "W.A.K", "NEXT GEN", "A.C.N"];

test("a value on the list is taken, in the list's own spelling", () => {
  assert.equal(resolveChoice("SANGJA", CARRIERS), "SANGJA");
  // Off a spreadsheet these arrive in whatever case and spacing the file had.
  // What gets stored is the register's spelling, so the register does not gain
  // a second way to write a name it already has.
  assert.equal(resolveChoice("sangja", CARRIERS), "SANGJA");
  assert.equal(resolveChoice("  SangJa  ", CARRIERS), "SANGJA");
  assert.equal(resolveChoice("next gen", CARRIERS), "NEXT GEN");
  assert.equal(resolveChoice("w.a.k", CARRIERS), "W.A.K");
});

test("a value that is not on the list is refused, not corrected", () => {
  // The whole point of the column. A near-miss is the dangerous case: it looks
  // right in a spreadsheet and is a different company, or no company at all.
  assert.equal(resolveChoice("SANGJAA", CARRIERS), null);
  assert.equal(resolveChoice("SANG JA", CARRIERS), null);
  assert.equal(resolveChoice("WAK", CARRIERS), null, "punctuation is part of the name");
  assert.equal(resolveChoice("NEXTGEN", CARRIERS), null, "so is the space");
  assert.equal(resolveChoice("Some Haulier Ltd", CARRIERS), null);
});

test("empty clears the cell, so Delete keeps working over a block", () => {
  // A job with no carrier assigned yet is a real state, and Delete over a
  // rectangle has to keep emptying these columns like any other.
  assert.equal(resolveChoice("", CARRIERS), "");
  assert.equal(resolveChoice("   ", CARRIERS), "");
});

test("a column that is not a dropdown is left alone", () => {
  const edits = [
    { row: "j1", field: "container", value: "TEMU1234567" },
    { row: "j1", field: "remark", value: "anything at all" },
  ];
  const { allowed, refused } = resolveBlock(edits, () => null);
  assert.equal(refused, 0);
  assert.deepEqual(allowed, edits, "free-text columns pass through untouched");
});

test("a block is split into what lands and what is counted", () => {
  const edits = [
    { row: "j1", field: "trucker", value: "sangja" },
    { row: "j2", field: "trucker", value: "SANGJAA" },
    { row: "j3", field: "trucker", value: "SSL" },
    { row: "j4", field: "remark", value: "free text" },
    { row: "j5", field: "trucker", value: "" },
  ];
  const { allowed, refused } = resolveBlock(edits, (field) =>
    (field === "trucker" ? CARRIERS : null));

  assert.equal(refused, 1, "only SANGJAA is refused");
  assert.deepEqual(allowed, [
    { row: "j1", field: "trucker", value: "SANGJA" },
    { row: "j3", field: "trucker", value: "SSL" },
    { row: "j4", field: "remark", value: "free text" },
    { row: "j5", field: "trucker", value: "" },
  ]);
});

test("refusals are counted rather than dropped", () => {
  // A paste that silently lands on nine cells of ten looks like one that
  // worked. The count is what lets the screen say otherwise.
  const edits = ["a", "b", "c"].map((v, i) => ({ row: "j" + i, field: "type", value: v }));
  const { allowed, refused } = resolveBlock(edits, () => ["20F", "40F"]);
  assert.equal(allowed.length, 0);
  assert.equal(refused, 3);
});

/**
 * The screen's half of the arrangement, checked as source.
 *
 * The options a cell offers and the options a paste is judged against have to
 * be the same list. Read from two places they would drift, and the column would
 * accept values its own dropdown does not show.
 */
const workspace = readFileSync(
  new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");

test("the dropdown and the paste read one list", () => {
  assert.match(workspace, /const choicesFor = \(field: keyof Job, job: Job\): string\[\] \| null/);
  for (const field of ["customer", "trucker", "type"]) {
    assert.match(workspace, new RegExp(`edChoice\\(j, "${field}", choicesFor\\("${field}", j\\)!`),
      `the ${field} cell no longer builds its options from choicesFor`);
  }
  assert.match(workspace, /resolveBlock\(edits, choicesFor\)/,
    "a pasted block is no longer judged against the dropdowns");
});

test("category and status are guarded by the same rule", () => {
  // The three columns asked for are not the only dropdowns. A rule that
  // protected three and left two open is the half-rule that bites later.
  assert.match(workspace, /case "cat": return \["IMPORT", "EXPORT", "DELIVERY"\];/);
  assert.match(workspace, /case "status": return STATUS_LADDER\[job\.cat\]/);
});
