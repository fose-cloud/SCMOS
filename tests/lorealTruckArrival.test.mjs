import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const loreal = readFileSync("app/scmos/screens/Loreal.tsx", "utf8");
const times = readFileSync("app/scmos/truckTimes.ts", "utf8");

test("L'OREAL Truck arrival comes from My Job ARRIVAL DATE and ARRIVAL TIME", () => {
  assert.match(loreal,
    /head: "Truck arrival", source: "register", read: \(j\) => joinDateTime\(j\.arrDate, j\.arrTime\)/);
  assert.doesNotMatch(times, /"Truck arrival":/,
    "Truck arrival must no longer read a shipment milestone");
});

test("L'OREAL explains where its read-only Truck arrival value is edited", () => {
  assert.match(loreal, /column\.head === "Truck arrival"/);
  assert.ok(loreal.includes(
    "ดึงจาก ARRIVAL DATE และ ARRIVAL TIME — แก้ที่หน้า My Job (เป็นสองช่อง)",
  ));
});
