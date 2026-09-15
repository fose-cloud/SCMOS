import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { iconFor } from "../app/scmos/statIcons.ts";

/*
 * The figure cards the templates draw — icon tile, label, figure, note —
 * are one component now, and every screen's Tile is an adapter over it.
 */

test("a label earns its glyph in either language", () => {
  assert.equal(iconFor("ยกเลิก"), "cancel");
  assert.equal(iconFor("เลื่อนวัน"), "calendar");
  assert.equal(iconFor("ความล่าช้า"), "clock");
  assert.equal(iconFor("ตรงเวลา"), "check");
  assert.equal(iconFor("ต้องดำเนินการ"), "warning");
  assert.equal(iconFor("ผู้ขนส่งทั้งหมด"), "truck");
  assert.equal(iconFor("เอกสารใกล้หมดอายุ"), "warning", "expiry outranks the document it is on");
  assert.equal(iconFor("พนักงานขับรถ"), "truck", "a driver is on the road before he is a person");
  assert.equal(iconFor("อบรมแล้ว"), "users");
  assert.equal(iconFor("งานทั้งหมด"), "box");
  assert.equal(iconFor("Trips"), "truck");
  assert.equal(iconFor("something else"), "chart", "and an unknown label still gets a glyph");
});

test("every screen's Tile is an adapter over StatCard, not a drawing of its own", () => {
  const dir = new URL("../app/scmos/screens/", import.meta.url);
  const own = [];
  for (const file of readdirSync(dir)) {
    if (!file.endsWith(".tsx")) continue;
    const source = readFileSync(new URL(file, dir), "utf8");
    const at = source.indexOf("\nfunction Tile(");
    if (at < 0) continue;
    const body = source.slice(at, source.indexOf("\n}\n", at));
    // Today's tiles are the control tower's own design and stay theirs.
    if (file === "Today.tsx") continue;
    if (!body.includes("<StatCard")) own.push(file);
  }
  assert.deepEqual(own, [], "screens still drawing their own figure card");
});
