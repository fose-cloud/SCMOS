import assert from "node:assert/strict";
import test from "node:test";

import { readBand, readDate, readSheet, readWorkbook } from "../app/scmos/rateInquiryImport.ts";

/** The current sheet's shape, cut down to the columns each test needs. */
const MODERN = [
  [null, null, null, null, null, null, null, null, null, null, null, "Rate Base on \nFuel 30.00-32.99"],
  ["Date", "No.", "Requestor", "Customer", "From", "To", "County", "Subcon", "FCL", "LCL", "Domestic",
    "4W\nNON-DG", "6W\nNON-DG", "4W\nDG", "20'\nNON-DG", "40'/40'HQ\nNON-DG", "Remark"],
];

const row = (...cells) => cells;

test("a request with several lanes is one inquiry, not several", () => {
  // The sheet repeats the number, requestor and customer down the rows rather
  // than leaving them blank, so five lanes look like five requests until they
  // are gathered back by "No.".
  const { inquiries } = readSheet("August 2026", [...MODERN,
    row(46235, 2, "Tum", "SHPP", "Pluakdaeng", "PAT", "", "SANGJA,SSL", "x", "", "", 1400, 2700, null, null, null, ""),
    row(46235, 2, "Tum", "SHPP", "Chonburi", "PAT", "", "SANGJA,SSL", "x", "", "", 1500, 2800, null, null, null, ""),
    row(46235, 3, "Tum", "AIBEL", "SBIA", "Rayong", "", "NATNISA", "", "x", "", 2000, null, null, null, null, ""),
  ]);

  assert.equal(inquiries.length, 2);
  assert.equal(inquiries[0].lanes.length, 2);
  assert.equal(inquiries[1].lanes.length, 1);
  assert.equal(inquiries[0].customer, "SHPP");
});

test("the same number under a different customer is a different request", () => {
  // Numbering restarts and repeats across a month. Gathering on the number
  // alone would merge two customers' quotes into one.
  const { inquiries } = readSheet("August 2026", [...MODERN,
    row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", 1000, null, null, null, null, ""),
    row(46235, 1, "Tum", "AIBEL", "C", "D", "", "SSL", "x", "", "", 1000, null, null, null, null, ""),
  ]);
  assert.equal(inquiries.length, 2);
});

test("each priced column lands on the vehicle it names", () => {
  const { inquiries } = readSheet("August 2026", [...MODERN,
    row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", 1400, 2700, 1900, 7000, 9000, ""),
  ]);
  assert.deepEqual(inquiries[0].lanes[0].prices,
    { "4W": 1400, "6W": 2700, "4W DG": 1900, "20F": 7000, "40F": 9000 });
});

test("a blank price is not a price, and neither is a zero", () => {
  // Blank means nobody quoted that vehicle. Zero would be a free journey, which
  // is a mistake in the sheet rather than a rate to carry forward.
  const { inquiries } = readSheet("August 2026", [...MODERN,
    row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", 1400, null, 0, "-", "n/a", ""),
  ]);
  assert.deepEqual(inquiries[0].lanes[0].prices, { "4W": 1400 });
});

test("a price typed with a comma is still a price", () => {
  const { inquiries } = readSheet("August 2026", [...MODERN,
    row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", "12,500", null, null, null, null, ""),
  ]);
  assert.equal(inquiries[0].lanes[0].prices["4W"], 12500);
});

test("two columns that become one vehicle report a disagreement rather than overwriting", () => {
  // The older sheets give 40' and 40'HQ a column each where the current ones
  // merge them. Where those two disagree the row is named: picking one quietly
  // decides they are the same price, which is the very thing in question.
  const header = [
    [null, null, null, null, null, null, null, "Rate Base on Fuel 30.00-32.99"],
    ["No.", "Requestor", "Customer", "From", "To", "Suncon", "40' NON-DG", "40'HQ NON-DG"],
  ];
  const { inquiries, conflicts } = readSheet("APR 2026", [...header,
    row(1, "Tum", "Caldic", "A", "B", "SSL", 8000, 9500),
    row(2, "Tum", "Caldic", "C", "D", "SSL", 8000, 8000),
  ]);

  assert.equal(conflicts.length, 1, "only the row that disagrees is reported");
  assert.match(conflicts[0], /8000 vs 9500/);
  assert.equal(inquiries[0].lanes[0].prices["40F"], 8000, "the first is kept");
  assert.equal(inquiries[1].lanes[0].prices["40F"], 8000, "agreement is not a conflict");
});

test("the older sheets' DG tick moves the price to the DG vehicle", () => {
  // Those sheets price one set of columns and say what was carried with a tick
  // in a DG column, so the same column is a different vehicle row by row.
  const header = [
    [null, null, null, null, null, null, null, null, "Rate Base on Fule 30.00-32.99"],
    ["No.", "Requestor", "Customer", "From", "To", "Suncon", "DG", "Non-DG", "4W", "6W"],
  ];
  const { inquiries } = readSheet("Aug 2025", [...header,
    row(1, "Faris", "Brose", "Airport", "WHA", "Natnisa", "", "x", 2400, 4200),
    row(2, "Faris", "Brose", "Airport", "WHA", "Natnisa", "x", "", 2400, 4200),
  ]);

  assert.deepEqual(inquiries[0].lanes[0].prices, { "4W": 2400, "6W": 4200 });
  assert.deepEqual(inquiries[1].lanes[0].prices, { "4W DG": 2400, "6W DG": 4200 });
});

test("a column with no vehicle behind it is reported, never dropped", () => {
  const header = [
    [null, null, null, null, null, null, null],
    ["No.", "Requestor", "Customer", "From", "To", "Subcon", "Hovercraft"],
  ];
  const { unmapped } = readSheet("August 2026", [...header,
    row(1, "Tum", "SHPP", "A", "B", "SSL", 9000),
  ]);
  assert.deepEqual(unmapped, ["Hovercraft"]);
});

test("the header row is found rather than assumed", () => {
  // It sits at a different height on different months, and one has been edited.
  const { inquiries } = readSheet("August 2026", [
    ["Rate Inquiry — internal use"], [], [null, null, null, null, null, null, null, null, null, null, null, "Rate Base on Fuel 30.00-32.99"],
    ["Date", "No.", "Requestor", "Customer", "From", "To", "County", "Subcon", "FCL", "LCL", "Domestic", "4W\nNON-DG"],
    row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", 1400),
  ]);
  assert.equal(inquiries.length, 1);
  assert.equal(inquiries[0].fuelBand, "30.00–32.99");
});

test("a sheet with no header at all reads as nothing rather than throwing", () => {
  const read = readSheet("Remarks", [["หมายเหตุ"], ["1. อัตราค่าขนส่ง…"]]);
  assert.deepEqual(read.inquiries, []);
  assert.deepEqual(read.unmapped, []);
});

test("spacer rows are counted, not silently swallowed", () => {
  const { inquiries, skipped } = readSheet("August 2026", [...MODERN,
    row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", 1400, null, null, null, null, ""),
    row(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, "total"),
  ]);
  assert.equal(inquiries.length, 1);
  assert.equal(skipped, 1);
});

test("a date reads from a serial and from text, and says nothing when absent", () => {
  assert.equal(readDate(46235), "01/08/2026");
  assert.equal(readDate("04/08/2026"), "04/08/2026");
  assert.equal(readDate("4/8/26"), "04/08/2026");
  assert.equal(readDate(null), "");
  assert.equal(readDate("soon"), "");
});

test("the fuel band comes off the caption over the prices", () => {
  assert.equal(readBand([null, "Rate Base on \nFuel 30.00-32.99"]), "30.00–32.99");
  assert.equal(readBand([null, "Rate Base on Fuel 00.01-29.99"]), "00.01–29.99");
  assert.equal(readBand([null, "no band here"]), "");
});

test("a workbook is every sheet, and the reports add up across them", () => {
  const read = readWorkbook([
    { name: "August 2026", rows: [...MODERN,
      row(46235, 1, "Tum", "SHPP", "A", "B", "", "SSL", "x", "", "", 1400, null, null, null, null, "")] },
    { name: "September 2026", rows: [...MODERN,
      row(46266, 1, "Tum", "AIBEL", "C", "D", "", "SSL", "x", "", "", 1500, null, null, null, null, "")] },
  ]);

  assert.equal(read.inquiries.length, 2);
  assert.deepEqual(read.inquiries.map((one) => one.sheet), ["August 2026", "September 2026"]);
});

test("the older sheets continue a request by leaving the row blank", () => {
  // Those sheets fill the number, requestor and customer in once and leave the
  // rest of the request's rows empty. Read as new requests they are inquiries
  // with nobody to quote to — 1,454 of them on the real file, every one refused
  // by the register for having no customer.
  const header = [
    [null, null, null, null, null, null, null, null, "Rate Base on Fule 30.00-32.99"],
    ["No.", "Requestor", "Customer", "From", "To", "Suncon", "DG", "Non-DG", "4W"],
  ];
  const { inquiries } = readSheet("Sep 2025", [...header,
    [1, "Salinthip", "Uzma", "Uzma Rayong", "Airport", "Natnisa", "", "x", 3400],
    ["", "", "", "Uzma Rayong", "PAT", "Natnisa", "", "x", 3400],
    ["", "", "", "Uzma Rayong", "LCB", "Natnisa", "", "x", 3600],
    [2, "Patcharee", "Jasmine", "PAT", "Bangsaothong", "Natnisa", "", "x", 1400],
  ]);

  assert.equal(inquiries.length, 2, "three rows of one request are one request");
  assert.equal(inquiries[0].lanes.length, 3);
  assert.equal(inquiries[0].customer, "Uzma");
  assert.deepEqual(inquiries[0].lanes.map((l) => l.toPlace), ["Airport", "PAT", "LCB"]);
  assert.equal(inquiries[1].lanes.length, 1);
});

test("a blank continuation before any request is not swallowed into nothing", () => {
  // There is no previous inquiry to attach it to, so it stands as its own —
  // and the register will refuse it for having no customer, which is the right
  // answer for a row that names none.
  const header = [
    [null, null, null, null, null, null],
    ["No.", "Requestor", "Customer", "From", "To", "Suncon"],
  ];
  const { inquiries } = readSheet("Sep 2025", [...header,
    ["", "", "", "A", "B", "SSL"],
  ]);
  assert.equal(inquiries.length, 1);
  assert.equal(inquiries[0].customer, "");
});

test("a blank end of a lane means the same as the row above, not nowhere", () => {
  // The workbook writes each end once and leaves it empty down the rest of the
  // request. Taken literally that is 199 origins and 211 destinations that go
  // nowhere — every one of them written plainly a row or two higher.
  const header = [
    [null, null, null, null, null, null, null, null],
    ["No.", "Requestor", "Customer", "From", "To", "Subcon", "FCL", "4W"],
  ];
  const { inquiries } = readSheet("APR 2026", [...header,
    [25, "Lyn", "Suiden", "Suiden Rayong", "LCH port", "SSL", "x", 3400],
    ["", "", "Suiden", "", "BKK port", "SSL", "x", 3600],
    ["", "", "Suiden", "", "LKR ICD", "SSL", "x", 3800],
  ]);

  assert.equal(inquiries.length, 1);
  assert.deepEqual(inquiries[0].lanes.map((l) => l.fromPlace),
    ["Suiden Rayong", "Suiden Rayong", "Suiden Rayong"]);
  assert.deepEqual(inquiries[0].lanes.map((l) => l.toPlace),
    ["LCH port", "BKK port", "LKR ICD"]);
});

test("a new customer never inherits the last one's port", () => {
  // The blank only means "same as above" while the rows belong to one request.
  // Carrying it across a customer would invent a journey nobody quoted.
  const header = [
    [null, null, null, null, null, null],
    ["No.", "Requestor", "Customer", "From", "To", "Subcon"],
  ];
  const { inquiries } = readSheet("APR 2026", [...header,
    [1, "Lyn", "Suiden", "Suiden Rayong", "LCH port", "SSL"],
    [2, "Lyn", "Brenntag", "", "LKR Port", "SHORE"],
  ]);

  assert.equal(inquiries.length, 2);
  assert.equal(inquiries[1].lanes[0].fromPlace, "",
    "Brenntag's origin is missing from the sheet and stays missing");
});

test("an empty row ends the block, so nothing leaks across it", () => {
  const header = [
    [null, null, null, null, null, null],
    ["No.", "Requestor", "Customer", "From", "To", "Subcon"],
  ];
  const { inquiries } = readSheet("APR 2026", [...header,
    [1, "Lyn", "Suiden", "Suiden Rayong", "LCH port", "SSL"],
    [null, null, null, null, null, null],
    ["", "", "Suiden", "", "BKK port", "SSL"],
  ]);
  assert.equal(inquiries.length, 2);
  assert.equal(inquiries[1].lanes[0].fromPlace, "");
});
