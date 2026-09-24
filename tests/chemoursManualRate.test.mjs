import assert from "node:assert/strict";
import test from "node:test";

import { upsertManualChemoursRate } from "../app/scmos/chemoursManualRate.ts";

const card = {
  file: "stored",
  bands: [{ label: "28.01-30.00", min: 28.01, max: 30 }, { label: "30.01-32.00", min: 30.01, max: 32 }],
  lanes: [{
    id: "1", carrier: "SSL", service: "DELIVERY", customer: "CHEMOURS",
    from: "A", to: "B", county: "10110", remark: "", prices: { "4W": [100, 110] },
  }],
  issues: [], notes: [], unpriced: [],
};

test("manual Chemours rate updates an existing vehicle without duplicating its route", () => {
  const next = upsertManualChemoursRate(card, {
    kind: "COST", carrier: "ssl", from: "A", to: "B", postalCode: "10110", cargoType: "",
    prices: { "4W": [120, 130], "6W": [200, 220], "10W": [null, null] },
  });

  assert.equal(next.lanes.length, 1);
  assert.deepEqual(next.lanes[0].prices, { "4W": [120, 130], "6W": [200, 220] });
  assert.deepEqual(card.lanes[0].prices, { "4W": [100, 110] });
});
test("manual Chemours rate creates a new route and keeps blank vehicle columns out", () => {
  const next = upsertManualChemoursRate(card, {
    kind: "COST", carrier: "SSL", from: "A", to: "C", postalCode: "20220", cargoType: "",
    prices: { "4W": [null, null], "6W": [300, 330], "10W": [null, null] },
  });

  assert.equal(next.lanes.length, 2);
  assert.equal(next.lanes[1].to, "C");
  assert.deepEqual(next.lanes[1].prices, { "6W": [300, 330] });
});

test("selling rates with different cargo types remain separate", () => {
  const selling = {
    ...card,
    lanes: [{ ...card.lanes[0], carrier: "LESCHACO", remark: "DG" }],
  };
  const next = upsertManualChemoursRate(selling, {
    kind: "SELL", carrier: "LESCHACO", from: "A", to: "B", postalCode: "10110", cargoType: "Non-DG",
    prices: { "4W": [500, 550] },
  });

  assert.equal(next.lanes.length, 2);
  assert.equal(next.lanes[1].remark, "Non-DG");
});
