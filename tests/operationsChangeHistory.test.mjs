import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
const source = readFileSync(new URL("../app/scmos/screens/OperationsChanges.tsx", import.meta.url), "utf8");
test("all visible proposals can be inspected, while decisions require pending and approval permission", () => {
  assert.ok(source.includes("ดูรายละเอียด / ประวัติ"));
  const guard = source.indexOf('{review.state === "pending" && review.canApprove && <>');
  const confirm = source.indexOf('await post(`/${review.id}/confirm`');
  const close = source.indexOf("</div></>}", guard);
  assert.ok(guard >= 0 && confirm > guard && close > confirm);
  assert.ok(source.includes("review.decidedBy"));
  assert.ok(source.includes("stamp(review.decidedAt)"));
  assert.ok(source.includes('CODES[row.state] ?? "สถานะไม่ทราบ"'));
});
