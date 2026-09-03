import assert from "node:assert/strict";
import test from "node:test";

import { applyEditHistory, editHistoryShortcut } from "../app/scmos/editHistory.ts";

test("undo captures the values needed to redo the same action", () => {
  const rows = [
    { key: "A", customer: "NEW", trucker: "JTC" },
    { key: "B", customer: "HENKEL", trucker: "NEW TRUCK" },
  ];
  const changed = [];
  const undo = {
    label: "วาง 2 ช่อง", at: "10:00", edits: [
      { key: "A", before: { customer: "OLD" } },
      { key: "B", before: { trucker: "WEALTHY" } },
    ],
  };

  const undone = applyEditHistory(rows, undo, () => true,
    (row, field, from, to) => changed.push([row.key, field, from, to]));
  assert.equal(rows[0].customer, "OLD");
  assert.equal(rows[1].trucker, "WEALTHY");
  assert.deepEqual(undone.inverse, [
    { key: "A", before: { customer: "NEW" } },
    { key: "B", before: { trucker: "NEW TRUCK" } },
  ]);
  assert.equal(undone.touched.length, 2);
  assert.equal(changed.length, 2);

  const redone = applyEditHistory(rows, { ...undo, edits: undone.inverse }, () => true, () => {});
  assert.equal(rows[0].customer, "NEW");
  assert.equal(rows[1].trucker, "NEW TRUCK");
  assert.deepEqual(redone.inverse, undo.edits);
});

test("history moves skip missing and newly read-only jobs without losing the rest", () => {
  const rows = [
    { key: "mine", value: "new", editable: true },
    { key: "theirs", value: "new", editable: false },
  ];
  const result = applyEditHistory(rows, {
    label: "edit", at: "10:00", edits: [
      { key: "mine", before: { value: "old" } },
      { key: "theirs", before: { value: "old" } },
      { key: "gone", before: { value: "old" } },
    ],
  }, (row) => row.editable, () => {});

  assert.equal(rows[0].value, "old");
  assert.equal(rows[1].value, "new");
  assert.equal(result.refused, 1);
  assert.equal(result.gone, 1);
  assert.deepEqual(result.inverse, [{ key: "mine", before: { value: "new" } }]);
});

test("My Job history shortcuts include the requested and standard redo keys", () => {
  assert.equal(editHistoryShortcut({ key: "z", ctrlKey: true }), "undo");
  assert.equal(editHistoryShortcut({ key: "x", ctrlKey: true }), "redo");
  assert.equal(editHistoryShortcut({ key: "y", ctrlKey: true }), "redo");
  assert.equal(editHistoryShortcut({ key: "Z", metaKey: true, shiftKey: true }), "redo");
  assert.equal(editHistoryShortcut({ key: "x" }), null);
  assert.equal(editHistoryShortcut({ key: "x", ctrlKey: true, altKey: true }), null);
});
