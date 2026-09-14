import assert from "node:assert/strict";
import test from "node:test";
import { filterOperationsQueue } from "../app/scmos/operationsQueue.ts";
test("queue filters intersect status and case-insensitive job/requester search without changing input", () => {
  const rows = [
    { state: "pending", requestedBy: "Operator A", payload: { key: "IMP-1" } },
    { state: "applied", requestedBy: "Operator B", payload: { key: "IMP-2" } },
  ];
  assert.deepEqual(filterOperationsQueue(rows, "pending", " operator "), [rows[0]]);
  assert.deepEqual(filterOperationsQueue(rows, "all", "imp-2"), [rows[1]]);
  assert.deepEqual(filterOperationsQueue(rows, "pending", "imp-2"), []);
  assert.deepEqual(filterOperationsQueue(rows, "all", " "), rows);
  assert.equal(rows.length, 2);
  assert.deepEqual(filterOperationsQueue([], "all", ""), []);
});
