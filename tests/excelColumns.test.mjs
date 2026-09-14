import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const workspace = readFileSync(new URL("../app/scmos/screens/Workspace.tsx", import.meta.url), "utf8");
const excel = readFileSync(new URL("../app/scmos/excel.ts", import.meta.url), "utf8");

function section(source, from, to) {
  const start = source.indexOf(from);
  const end = source.indexOf(to, start + from.length);
  assert.notEqual(start, -1, from);
  assert.notEqual(end, -1, to);
  return source.slice(start, end);
}

function ordered(source, values) {
  let after = -1;
  for (const value of values) {
    const at = source.indexOf(value, after + 1);
    assert.notEqual(at, -1, value);
    assert.ok(at > after, value);
    after = at;
  }
}

test("Import grid keeps Remark immediately after Reason / Delay", () => {
  const headers = section(workspace, "  IMPORT:", "\n  EXPORT:");
  ordered(headers, ['["Reason / Delay"]', '["Remark"]', '["Pickup Plan Date"]']);

  const cells = section(workspace, 'if (layout === "IMPORT")', 'if (layout === "EXPORT")');
  ordered(cells, ['ed(j, "reason"', 'ed(j, "remark"', 'ed(j, "pickupPlan"']);
});

test("Import Excel keeps Remark immediately after Reason / Delay", () => {
  const columns = section(excel, "const IMPORT_COLUMNS:", "const EXPORT_COLUMNS:");
  ordered(columns, ['header: "Reason / Delay"', 'header: "Remark"', 'header: "OT"']);
});

test("Domestic grid runs the customer's references CUSTOMER PO., PRODUCT NAME, SAP ORDER, DELIVER NO.", () => {
  const headers = section(workspace, "  DELIVERY:", "\n  // Mixed lists");
  ordered(headers, ['["SID NO."]', '["CUSTOMER PO."]', '["PRODUCT NAME"]', '["SAP ORDER"]', '["DELIVER NO."]', '["Customer List"]']);

  const cells = section(workspace, 'if (layout === "DELIVERY")', "return head.concat([\n      catCell");
  ordered(cells, ['ed(j, "sid"', 'ed(j, "customerPo"', 'ed(j, "product"', 'ed(j, "sapOrder"', 'ed(j, "deliverNo"', 'edPick(j, "customer"']);
});

test("Domestic Excel carries the same four references in the same order", () => {
  const columns = section(excel, "const DELIVERY_COLUMNS:", "const ALL_COLUMNS:");
  ordered(columns, ['header: "SID No."', 'header: "Customer PO"', 'header: "Product"', 'header: "SAP Order"', 'header: "Deliver No."', 'header: "Customer"']);
});

test("the mixed Excel layout inherits Import Remark without adding a duplicate", () => {
  const columns = section(excel, "const ALL_COLUMNS:", "export function columnsFor");
  assert.equal((columns.match(/header: "Remark"/g) ?? []).length, 0);
});
