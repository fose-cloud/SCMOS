import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { boardFromCarrier } from "../app/scmos/capacity.ts";
import { driverFormProblem, filterTrucks, paperTone, rowState, truckFormProblem } from "../app/scmos/fleet.ts";

const read = (path) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

const TRUCK_PAPERS = [
  { code: "truck-registration-book", english: "Vehicle registration book", thai: "เล่มทะเบียนรถ", expires: false },
  { code: "truck-motor-insurance", english: "Motor insurance", thai: "ประกันรถ", expires: true },
  { code: "truck-cargo-insurance", english: "Cargo insurance", thai: "ประกันสินค้า", expires: true },
];
const DRIVER_PAPERS = [{ code: "driver-licence", english: "Driving licence", thai: "ใบขับขี่", expires: true }];
const file = { name: "a.pdf" };

test("a truck is not sent until its plate, head or tail, type and all three papers are there", () => {
  const form = { plate: "70-1234 กทม", kind: "head", vehicleType: "40F", dgCapable: false };
  const all = Object.fromEntries(TRUCK_PAPERS.map((need) => [need.code, { file, expiry: "" }]));
  assert.equal(truckFormProblem(form, all, TRUCK_PAPERS), "");
  assert.equal(truckFormProblem({ ...form, plate: " - " }, all, TRUCK_PAPERS), "ระบุทะเบียนรถ");
  assert.equal(truckFormProblem({ ...form, kind: "" }, all, TRUCK_PAPERS), "เลือกหัวหรือหาง");
  assert.equal(truckFormProblem({ ...form, vehicleType: "" }, all, TRUCK_PAPERS), "เลือกประเภทรถ");
  const twoOfThree = Object.fromEntries(Object.entries(all).filter(([code]) => code !== "truck-cargo-insurance"));
  assert.equal(truckFormProblem(form, twoOfThree, TRUCK_PAPERS), "แนบ ประกันสินค้า");
  assert.equal(truckFormProblem(form, {}, TRUCK_PAPERS), "แนบ เล่มทะเบียนรถ, ประกันรถ, ประกันสินค้า");
});

test("a driver is not sent without a name, a licence number and the licence itself", () => {
  const form = { name: "สมชาย ใจดี", phone: "", licenceNo: "3ขบ.00597/63" };
  const papers = { "driver-licence": { file, expiry: "2027-01-31" } };
  assert.equal(driverFormProblem(form, papers, DRIVER_PAPERS), "");
  assert.equal(driverFormProblem({ ...form, name: " " }, papers, DRIVER_PAPERS), "ระบุชื่อพนักงานขับรถ");
  assert.equal(driverFormProblem({ ...form, licenceNo: "12" }, papers, DRIVER_PAPERS), "ระบุเลขที่ใบขับขี่");
  assert.equal(driverFormProblem(form, {}, DRIVER_PAPERS), "แนบ ใบขับขี่");
});

test("a paper's state reads the way the compliance file does", () => {
  assert.equal(paperTone("missing", null).text, "ยังไม่มี");
  assert.equal(paperTone("valid", null).text, "มีแล้ว");
  assert.equal(paperTone("valid", 200).text, "เหลือ 200 วัน");
  assert.equal(paperTone("expiring", 12).text, "ใกล้หมดอายุ · 12 วัน");
  assert.equal(paperTone("expired", -3).text, "หมดอายุแล้ว · เกิน 3 วัน");
  assert.equal(paperTone("no-expiry", null).text, "ไม่ได้ระบุวันหมดอายุ");
  assert.equal(rowState("missing").text, "เอกสารไม่ครบ");
  assert.equal(rowState("valid").text, "เอกสารครบ");
});

test("the truck list filters by head or tail and hides retired trucks unless asked", () => {
  const row = (over) => ({ id: 1, supplierId: 3, supplier: "ALPHA", plate: "70-1234", kind: "head", vehicleType: "40F",
    dgCapable: false, status: "active", state: "valid", papers: [], createdBy: "", createdAt: null, ...over });
  const trucks = [row({}), row({ id: 2, plate: "71-0001", kind: "tail" }), row({ id: 3, plate: "70-9999", status: "inactive" })];
  assert.deepEqual(filterTrucks(trucks, "", "", false).map((one) => one.id), [1, 2]);
  assert.deepEqual(filterTrucks(trucks, "", "tail", false).map((one) => one.id), [2]);
  assert.deepEqual(filterTrucks(trucks, "", "", true).map((one) => one.id), [1, 2, 3]);
  assert.deepEqual(filterTrucks(trucks, "71", "", true).map((one) => one.id), [2]);
});

test("a carrier's capacity draws on the department's board: its jobs are the demand, unreported rows stay marked", () => {
  const board = boardFromCarrier({
    supplierId: 3, supplierName: "ALPHA", dates: ["01/10/2026", "02/10/2026"], vehicleTypes: ["20F", "40F"],
    cells: [
      { date: "01/10/2026", vehicleType: "20F", reported: true, available: 5, committed: 6, jobs: 2, updatedBy: "a", updatedAt: null, spare: -1 },
      { date: "01/10/2026", vehicleType: "40F", reported: false, available: 0, committed: 0, jobs: 3, updatedBy: "", updatedAt: null, spare: 0 },
    ],
  });
  assert.deepEqual(board.days[0], { date: "01/10/2026", available: 5, committed: 6, demand: 5, short: true });
  assert.deepEqual(board.days[1], { date: "02/10/2026", available: 0, committed: 0, demand: 0, short: false });
  assert.equal(board.cells[0].short, true);
  assert.equal(board.cells[1].reported, false);
  assert.equal(board.cells[1].short, false);
  assert.equal(board.anyReported, true);
});

test("the fleet tables read the carrier's own route on its screen and only the carrier's screen writes", () => {
  const source = read("app/scmos/screens/FleetRegister.tsx");
  assert.match(source, /carrier \? "\/api\/carrier\/fleet" : "\/api\/fleet"/);
  // Every write goes to the carrier's own routes, which take the company from the account.
  const writes = [...source.matchAll(/await send\(([^,]+),/g)].map((match) => match[1]);
  assert.ok(writes.length >= 4);
  for (const path of writes) assert.match(path, /^[`"]\/api\/carrier\/fleet\//);
  // The add buttons exist only in the carrier's branch.
  assert.match(source, /\{carrier && <button onClick=\{\(\) => open\("truck"\)\}/);
  assert.match(source, /\{carrier && <button onClick=\{\(\) => open\("driver"\)\}/);
});
