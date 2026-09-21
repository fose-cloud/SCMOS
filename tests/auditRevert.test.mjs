import assert from "node:assert/strict";
import test from "node:test";

import { reversible, supersededIds, REVERSIBLE_FIELDS } from "../app/scmos/auditRevert.ts";

/*
 * Which audit rows the screen offers to put back — the same rule as
 * Rules/AuditRevert.cs — and which it marks as overtaken.
 */

test("only a job's cell change on a cell the trail keeps is offered", () => {
  assert.equal(reversible("job", "assign", "ผู้รับผิดชอบ"), true);
  assert.equal(reversible("job", "carrier", "ผู้ขนส่ง"), true);
  assert.equal(reversible("job", "update", " เลขตู้ "), true);
  assert.equal(reversible("supplier", "update", "ผู้รับผิดชอบ"), false);
  assert.equal(reversible("job", "approve", "ผู้รับผิดชอบ"), false);
  assert.equal(reversible("job", "delete", ""), false);
  assert.equal(reversible("job", "update", "หมายเหตุ"), false);
  assert.equal(REVERSIBLE_FIELDS.length, 8);
});

test("a row a later row on the same cell overtook is marked; the newest of each cell is not", () => {
  const rows = [
    { id: 1, entity: "job", entityId: "A", field: "ผู้รับผิดชอบ" },
    { id: 2, entity: "job", entityId: "A", field: "ผู้รับผิดชอบ" },
    { id: 3, entity: "job", entityId: "A", field: "สถานะ" },
    { id: 4, entity: "job", entityId: "B", field: "ผู้รับผิดชอบ" },
    { id: 5, entity: "supplier", entityId: "A", field: "ผู้รับผิดชอบ" },
  ];
  const overtaken = supersededIds(rows);
  assert.deepEqual([...overtaken].sort(), [1]);
  // Order of the input does not matter: the highest id per cell wins.
  assert.deepEqual([...supersededIds([...rows].reverse())].sort(), [1]);
  assert.deepEqual([...supersededIds([])], []);
});
