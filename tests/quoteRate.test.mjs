import assert from "node:assert/strict";
import test from "node:test";

import { DEFAULT_CARD, DEFAULT_MARGIN, journeyKey, quote } from "../app/scmos/quoteRate.ts";

const ask = (over = {}) => ({
  vehicle: "4W", km: 100, dangerousGoods: false,
  marginPercent: DEFAULT_MARGIN, options: [], ...over,
});

test("the plain vehicles price as the team wrote them down", () => {
  // (km × perKm + base) × 1.10, worked by hand from the note.
  const at100 = (vehicle) => quote(DEFAULT_CARD, ask({ vehicle })).total;
  assert.equal(at100("4W"), Math.round((100 * 8 + 1500) * 1.1));    // 2,530
  assert.equal(at100("6W"), Math.round((100 * 18 + 2700) * 1.1));   // 4,950
  assert.equal(at100("10W"), Math.round((100 * 25 + 3500) * 1.1));  // 6,600
  assert.equal(at100("20F"), Math.round((100 * 40 + 4000) * 1.1));  // 8,800
});

test("a 40' costs the same as a 20', which is not a mistake", () => {
  // It looks like one. Across 1,708 journeys where both were quoted, the middle
  // half of the 40'/20' ratio is 1.00 to 1.00 — the team really does price them
  // alike, so a test says so before somebody "fixes" it.
  assert.equal(quote(DEFAULT_CARD, ask({ vehicle: "40F" })).total,
    quote(DEFAULT_CARD, ask({ vehicle: "20F" })).total);
});

test("the refrigerated rows carry their own per-kilometre rate, not just a multiplier", () => {
  // A 6W is 18 baht a kilometre and a 6W RF is 20, which a bare ×1.5 on the
  // plain row would miss.
  const rf = quote(DEFAULT_CARD, ask({ vehicle: "6W RF" }));
  assert.equal(rf.total, Math.round((100 * 20 + 2700) * 1.5 * 1.1)); // 8,085
  assert.notEqual(rf.total, Math.round((100 * 18 + 2700) * 1.5 * 1.1));
});

test("dangerous goods is a flat sum, and the margin is taken on top of it", () => {
  // The DG figures are the gap between a carrier's plain and DG quote for the
  // same journey — a cost — so it is marked up like every other cost. Inside,
  // 300 becomes 330 at the bottom; outside it would stay 300.
  const plain = quote(DEFAULT_CARD, ask());
  const dg = quote(DEFAULT_CARD, ask({ dangerousGoods: true }));
  assert.equal(dg.cost - plain.cost, 300, "the surcharge itself is flat");
  assert.equal(dg.total - plain.total, 330, "and the margin applies to it");
});

test("the cold multiplies the transport and leaves the DG surcharge alone", () => {
  // A refrigerated truck costs more to run. The fee for carrying something
  // hazardous does not change because the box is cold.
  const rf = quote(DEFAULT_CARD, ask({ vehicle: "4W RF", dangerousGoods: true }));
  const plainRf = quote(DEFAULT_CARD, ask({ vehicle: "4W RF" }));
  assert.equal(rf.cost - plainRf.cost, 300, "not 450");
});

test("every line is whole baht and they add up to the total", () => {
  // A quotation whose parts do not reconcile is one nobody can defend.
  const answer = quote(DEFAULT_CARD, ask({
    vehicle: "10W RF", km: 237, dangerousGoods: true, marginPercent: 12,
    options: [
      { id: "wait", label: "ค่ารอ", basis: "perHour", rate: 250, quantity: 3 },
      { id: "night", label: "ค้างคืน", basis: "flat", rate: 800, quantity: 0 },
      { id: "fuel", label: "ค่าน้ำมันส่วนเพิ่ม", basis: "percent", rate: 5, quantity: 0 },
    ],
  }));
  for (const line of answer.lines) {
    assert.equal(line.amount, Math.round(line.amount), `${line.label} is not whole baht`);
  }
  assert.equal(answer.lines.reduce((s, l) => s + l.amount, 0), answer.cost);
  assert.equal(answer.cost + answer.margin, answer.total);
});

test("a percentage option is a share of the cost, never of another percentage", () => {
  // Two 10% options are 20% of the cost, not 21%. Compounding them quietly
  // would make the order they were added change the price.
  const one = quote(DEFAULT_CARD, ask({
    options: [{ id: "a", label: "A", basis: "percent", rate: 10, quantity: 0 }],
  }));
  const two = quote(DEFAULT_CARD, ask({
    options: [
      { id: "a", label: "A", basis: "percent", rate: 10, quantity: 0 },
      { id: "b", label: "B", basis: "percent", rate: 10, quantity: 0 },
    ],
  }));
  const base = quote(DEFAULT_CARD, ask()).cost;
  assert.equal(one.cost - base, Math.round(base * 0.1));
  assert.equal(two.cost - base, 2 * Math.round(base * 0.1));
});

test("a per-kilometre option follows the distance", () => {
  const answer = quote(DEFAULT_CARD, ask({
    km: 50, options: [{ id: "f", label: "น้ำมัน", basis: "perKm", rate: 2, quantity: 0 }],
  }));
  assert.ok(answer.lines.some((l) => l.label === "น้ำมัน" && l.amount === 100));
});

test("an option worth nothing is left off the quotation entirely", () => {
  const answer = quote(DEFAULT_CARD, ask({
    options: [{ id: "w", label: "ค่ารอ", basis: "perHour", rate: 250, quantity: 0 }],
  }));
  assert.ok(!answer.lines.some((l) => l.label === "ค่ารอ"),
    "nought hours of waiting is not a line on the bill");
});

test("no distance is a question nobody asked, not a journey that is free", () => {
  for (const km of [0, -5, Number.NaN]) {
    const answer = quote(DEFAULT_CARD, ask({ km }));
    assert.equal(answer.total, 0);
    assert.equal(answer.refusals.length, 1);
    assert.match(answer.refusals[0], /ระยะทาง/);
  }
});

test("an absurd distance is queried rather than priced", () => {
  const answer = quote(DEFAULT_CARD, ask({ km: 9000 }));
  assert.equal(answer.total, 0);
  assert.match(answer.refusals[0], /3,000/);
});

test("a vehicle the card does not price says so instead of guessing", () => {
  const answer = quote(DEFAULT_CARD, ask({ vehicle: "6W HIAB" }));
  assert.equal(answer.total, 0);
  assert.match(answer.refusals[0], /ไม่มีอัตรา/);
});

test("the card is data — changing a rate changes the quote", () => {
  // The measurement said ×1.5 on a refrigerated truck is high: 10W RF came out
  // at ×1.24 across 30 pairs. Tuning that must not need a deployment.
  const tuned = DEFAULT_CARD.map((one) =>
    one.code === "10W RF" ? { ...one, chill: 1.24 } : one);
  const before = quote(DEFAULT_CARD, ask({ vehicle: "10W RF" })).total;
  const after = quote(tuned, ask({ vehicle: "10W RF" })).total;
  assert.ok(after < before);
  assert.equal(after, Math.round((100 * 28 + 3500) * 1.24 * 1.1));
});

test("one journey is one key however its ends are spelled", () => {
  // "BKK port", "BKK  port" and "bkk  Port" are one road. Typed as three, the
  // distance gets entered three times and two people quote it differently.
  assert.equal(journeyKey("BKK port", "Amata"), journeyKey("BKK  port", "amata"));
  assert.equal(journeyKey("bkk  Port", "AMATA"), journeyKey("BKK port", "Amata"));
  assert.notEqual(journeyKey("BKK port", "Amata"), journeyKey("BMT port", "Amata"));
});

test("a Thai place name survives being flattened", () => {
  assert.equal(journeyKey("แหลมฉบัง", "ระยอง"), journeyKey(" แหลมฉบัง ", "ระยอง"));
  assert.notEqual(journeyKey("แหลมฉบัง", "ระยอง"), journeyKey("แหลมฉบัง", "ชลบุรี"));
});

test("a card row missing a number refuses instead of quoting NaN", () => {
  // This is the shape the bug took: the API called the field baseCharge and the
  // calculator read base, so every lookup was undefined and the quotation
  // rendered "NaN" from the second line down — wrong, but still looking like an
  // answer somebody might send to a customer.
  const holed = DEFAULT_CARD.map((one) =>
    one.code === "4W" ? { ...one, baseCharge: undefined } : one);
  const answer = quote(holed, ask());

  assert.equal(answer.total, 0);
  assert.equal(answer.lines.length, 0);
  assert.match(answer.refusals[0], /ค่าเริ่มต้น/);
});

test("every field of the card is checked, not only the first", () => {
  for (const [field, thai] of [["perKm", "ราคาต่อกิโลเมตร"], ["baseCharge", "ค่าเริ่มต้น"],
                               ["chill", "ตัวคูณห้องเย็น"], ["dangerousGoods", "ค่า DG"]]) {
    const holed = DEFAULT_CARD.map((one) =>
      one.code === "4W" ? { ...one, [field]: undefined } : one);
    const answer = quote(holed, ask());
    assert.equal(answer.total, 0, `${field} was not checked`);
    assert.ok(answer.refusals.some((one) => one.includes(thai)), `${field} was not named`);
  }
});

test("no quotation can ever contain NaN", () => {
  // The property that matters, whatever the cause. A number nobody can read is
  // worse than a refusal that says which rate is missing.
  const holed = DEFAULT_CARD.map((one) => ({ ...one, chill: Number.NaN }));
  const answer = quote(holed, ask());
  assert.ok(answer.refusals.length > 0);
  for (const line of answer.lines) assert.ok(Number.isFinite(line.amount));
  assert.ok(Number.isFinite(answer.total));
});
