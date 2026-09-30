import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

import { averageFor, expand } from "../app/scmos/dieselMonth.ts";
import { INVOICE_LINES, addDays, invoiceTotals, isoOf, longDate, money } from "../app/scmos/invoiceLines.ts";

/**
 * The same inputs, the same answers, on both sides: the server prices a job and totals an invoice with
 * Rules/DieselMonth.cs and Rules/InvoiceLines.cs, the browser with dieselMonth.ts and invoiceLines.ts.
 * tests/Scmos.Ai.Checks/BillingParityChecks.cs reads this same fixture.
 */
const fixture = JSON.parse(readFileSync(new URL("./fixtures/billing-parity.json", import.meta.url), "utf8"));

test("a month's diesel average is the fixture's, case by case", () => {
  for (const one of fixture.diesel.cases) {
    const got = averageFor(expand(fixture.diesel.changes, one.month, one.until || undefined), one.month);
    assert.deepEqual({ average: got.average, days: got.days, closed: got.closed },
      { average: one.average, days: one.days, closed: one.closed }, one.month);
  }
});

test("the department's sample invoice totals as printed: 8,942.00, less 78.79, is 8,863.21", () => {
  assert.deepEqual(invoiceTotals(fixture.invoice.lines), fixture.invoice.totals);
  assert.equal(money(8863.21), "8,863.21");
  assert.equal(longDate("2026-09-29"), "29 September 2026");
  assert.equal(addDays("2026-09-29", 30), "2026-10-29");
  assert.equal(isoOf("25/09/2026"), "2026-09-25");
});

test("the invoice's lines are the server's, in the same order", () => {
  const server = readFileSync(new URL("../server/Scmos.Api/Rules/InvoiceLines.cs", import.meta.url), "utf8");
  const codes = [...server.matchAll(/new\((TransportCharge|"[A-Z_]+"), (Transport|Reimbursement), "([\d.]*)", "([^"]+)"/g)]
    .map(([, code, section, number, english]) => [code === "TransportCharge" ? "TRANSPORT_CHARGE" : code.slice(1, -1), section.toUpperCase(), number, english]);
  assert.deepEqual(codes, INVOICE_LINES.map((kind) => [kind.code, kind.section, kind.number, kind.english]));
  assert.match(server, /public const decimal WithholdingRate = 0\.01m;/);
});

test("the printed invoice suppresses browser headers and footers without losing its paper margin", () => {
  const form = readFileSync("app/scmos/screens/CarrierInvoice.tsx", "utf8");
  const print = form.slice(form.indexOf("const PRINT ="));
  assert.match(print, /@page \{ size: A4; margin: 0; \}/);
  assert.match(print, /\.scmos-invoice \{[^}]*box-sizing: border-box;[^}]*padding: 10mm;/);
  assert.doesNotMatch(print, /@page \{[^}]*margin: 10mm/);
});

test("a job's vehicle, its lane's fit and the lane's price are the fixture's — Booking's own answers", async () => {
  const { vehicleForType, laneScore } = await import("../app/scmos/rateMatch.ts");
  const { priceFor } = await import("../app/scmos/rates.ts");
  for (const one of fixture.vehicles) assert.equal(vehicleForType(one.type), one.vehicle, one.type);
  for (const one of fixture.laneScores)
    assert.equal(laneScore({ customer: one.customer, destination: one.destination, plant: one.plant },
      { customer: one.laneCustomer, from: one.from, to: one.to }), one.score, `${one.customer} / ${one.laneCustomer}`);
  const bands = fixture.prices.bandMax.map((max) => ({ label: "", min: 0, max }));
  for (const one of fixture.prices.cases)
    assert.equal(priceFor({ prices: { X: one.row } }, "X", bands, one.diesel), one.price, `${one.row} @ ${one.diesel}`);
});

test("the department's Billing Control sets each invoice's 1.1 against the job's Rate", async () => {
  const { rateVerdict } = await import("../app/scmos/invoiceLines.ts");
  const line = (amount) => [{ code: "TRANSPORT_CHARGE", amount }, { code: "GATE_FEE", amount: 100 }];
  assert.equal(rateVerdict({ amount: 7879 }, line(7879)), "match");
  assert.equal(rateVerdict({ amount: 7879 }, line(7900)), "differs");
  assert.equal(rateVerdict({ amount: null }, line(7879)), "no-rate");
  assert.equal(rateVerdict({ amount: 7879 }, []), "unbilled");
  assert.equal(rateVerdict(null, undefined), "unbilled");
  const screen = readFileSync(new URL("../app/scmos/screens/BillingControl.tsx", import.meta.url), "utf8");
  assert.match(screen, /"INVOICE", "ราคาตาม Rate", "ยอดรวม"/);
  assert.match(screen, /<td style=\{cell\}><RateCell item=\{item\} \/><\/td>/);
  assert.match(screen, /<option value="RATE_DIFF">1\.1 ไม่ตรง Rate<\/option>/);
  assert.match(screen, /<InvoiceSummary item=\{item\} \/>/);
});
