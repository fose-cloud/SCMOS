import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

const engine = read("../server/Scmos.Api/Services/KpiEngine.cs");
const measures = read("../server/Scmos.Api/Rules/KpiMeasures.cs");
const scorecard = read("../server/Scmos.Api/Services/CarrierScorecard.cs");
const workbook = read("../server/Scmos.Api/Services/KpiWorkbook.cs");
const screen = read("../app/scmos/screens/Kpi.tsx");

test("Supplier Performance headline is the carrier scorecard, not the retired 50/30/20 formula", () => {
  assert.match(engine, /SupplierPerformance\(scorecard\)/);
  assert.match(engine, /private static Measure SupplierPerformance\(IReadOnlyList<CarrierScore> scorecard\)/);
  assert.doesNotMatch(measures, /WeightOnTime|WeightConfirmation|WeightDelayFree/);
  assert.doesNotMatch(engine, /KpiMeasures\.Score/);
});

test("the six agreed KPI weights are named once and total 100", () => {
  const expected = [
    ["MinorAccidentWeight", 15],
    ["MajorAccidentWeight", 35],
    ["DamageReportingWeight", 20],
    ["VehicleReadinessWeight", 10],
    ["OnTimeWeight", 10],
    ["SatisfactionWeight", 10],
  ];
  for (const [name, weight] of expected)
    assert.match(scorecard, new RegExp(`public const double ${name} = ${weight};`));
  assert.equal(expected.reduce((sum, [, weight]) => sum + weight, 0), 100);
});

test("screen and Excel expose the same six scorecard criteria", () => {
  for (const id of ["accident-minor", "accident-major", "damage-reporting", "vehicle-readiness", "on-time", "satisfaction"])
    assert.match(workbook, new RegExp(`Line\\("${id}"\\)`));
  assert.match(screen, /SCORECARD_FORMULA/);
  assert.match(screen, /EVONIK งาน Tank <b>180 นาที<\/b>/);
});
