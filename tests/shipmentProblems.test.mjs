import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(path, import.meta.url), "utf8");

const board = read("../app/scmos/screens/MonitorBoard.tsx");
const rules = read("../server/Scmos.Api/Rules/ProblemRules.cs");
const jobRules = read("../server/Scmos.Api/Rules/JobRules.cs");
const scorecard = read("../server/Scmos.Api/Services/CarrierScorecard.cs");
const monitor = read("../server/Scmos.Api/Services/MonitorService.cs");

/**
 * What a supervisor sees on the shipment monitor, and what it must never say.
 *
 * The screen's whole worth is that its numbers can be taken at face value in a
 * carrier meeting. Two things would quietly destroy that, and neither would
 * break a build: reading an unfilled column as good news, and growing a second
 * opinion about what "late" means.
 */

test("problems are what the screen opens on", () => {
  // The question the menu was developed to answer. A supervisor who has to
  // find the right tab first is a supervisor who reads the wrong one.
  assert.match(board, /useState<"problem" \| "risk" \| "load" \| "delay">\("problem"\)/);
});

test("an unmeasured shipment is never reported as on time", () => {
  // The failure this guards is silent: a job with no arrival time recorded is
  // not a job that arrived on time, and a "0 นาที" in that cell would be the
  // screen answering a question its own records never asked.
  assert.match(board, /row\.measurable \? "ตรงเวลา" : "ยังวัดไม่ได้"/);
  assert.match(rules, /public static Row\? Judge/);
  assert.match(rules, /late is not null,/, "Measurable must come from the measurement itself");
});

test("how many cannot be judged at all is on the headline, not left off it", () => {
  // 1,463 of 2,093 live jobs on the register this was built against — mostly a
  // plan time nobody filled in. Drop this figure and the other four read as if
  // the whole register had been measured.
  assert.match(board, /\["ยังวัดไม่ได้", tally\.unmeasurable\.toLocaleString\(\)/);
  assert.match(monitor, /public record ProblemTally\(int Live, int WithProblem, int Unmeasurable/);
});

test("there is one reading of late, and the screen quotes it rather than its own", () => {
  // It was private to the carrier scorecard. Copying the number onto this
  // screen would have let a shipment be late in a carrier's score and on time
  // in their supervisor's morning, which is the shape of bug this codebase
  // keeps finding.
  assert.match(jobRules, /public const int LateMinutes = 30;/);
  assert.match(scorecard, /private const int LateMinutes = JobRules\.LateMinutes;/);
  assert.doesNotMatch(scorecard, /LateMinutes = 30;\s*$/m);
  assert.match(rules, /late > JobRules\.LateMinutes/);
  // The threshold reaches the screen from the API, so the sentence under the
  // heading cannot go on saying thirty after somebody changes it.
  assert.match(board, /เกิน \$\{tally\.lateMinutes\} นาที/);
});

test("the words shown are the operator's, and the screen says whose column they were in", () => {
  // Nothing here reads free text and decides what it means. What puts a job on
  // the list is that somebody wrote something, or that something was measured.
  assert.match(rules, /private static \(string Note, Source From\) Words/);
  assert.match(board, /\{row\.noteFrom\}/);
  assert.match(rules, /Source \{ None, Incident, DelayRecord, Milestone, Reason \}/);
});

test("a late row carries the two readings it was worked out from", () => {
  // Most late rows have no note at all; a column of "no text" teaches nobody
  // anything, and the plan against the arrival showed a plan time keyed as
  // 00:30 for what it was.
  assert.match(board, /แผน \{row\.planned\} → ถึง \{row\.arrived\}/);
  assert.match(monitor, /string Planned, string Arrived\);/);
});

test("finished and cancelled work is not something to do this morning", () => {
  assert.match(rules,
    /if \(JobRules\.IsDone\(job\.Status\) \|\| WorkspaceTabs\.IsCancelled\(job\.Status\)\) return null;/);
});

test("the list can be narrowed, over all of it rather than the page on screen", () => {
  // 290 rows in worst-first order is a list somebody reads the top of and
  // abandons. Filtering is only honest here because `problems` is every problem
  // in the register — the API sends the complete list, not a page — so a count
  // taken in the browser is the register's count.
  assert.match(board, /const shown = useMemo\(/);
  assert.match(board, /if \(kind && !row\.problems\.includes\(kind\)\) return false;/);
  // The note is part of the search: "รถติดในท่า" is how somebody looks for every
  // job held up in the port, and thirty of them said exactly that.
  assert.match(board, /row\.customer, row\.trucker, row\.owner, row\.jobCode, row\.note, row\.status/);
  // The row cap follows the filter. Reading "200 of 290" while looking at seven
  // filtered rows is the screen describing a list it is not showing.
  assert.match(board, /แสดง 200 แถวแรกจาก \{shown\.length\}/);
  assert.doesNotMatch(board, /\{problems\.slice\(0, 200\)/);
});

test("an empty filter says the list is not empty", () => {
  // Otherwise a filter that matches nothing looks exactly like a morning with
  // nothing wrong, which is the one thing this screen must never say by
  // accident.
  assert.match(board, /ไม่มีงานที่ตรงกับตัวกรอง — จาก \{problems\.length\} รายการ/);
});

test("the filter buttons are labelled by the API, not by a copy of its words", () => {
  // Each row carries the Thai beside the machine name; the buttons are built
  // from that. A kind reworded on the server is reworded here with it, and the
  // only thing kept locally is a sort key that cannot hide an unknown name.
  assert.match(board, /seen\.set\(name, \{ thai: row\.problemsThai\[at\] \?\? name, count: 1 \}\)/);
  assert.match(board, /const ORDER = \["Incident", "DelayOpen", "StageDelayed", "ArrivedLate", "DelayNoted"\]/);
});

test("the risk list was left as narrow as it was", () => {
  // The list of what needs somebody today is worth reading because it is short.
  // The new question is a second list, not four more reasons bolted onto that
  // one — see MonitorCheck, which says the same thing from the other side.
  const monitorRules = read("../server/Scmos.Api/Rules/MonitorRules.cs");
  const kinds = monitorRules.match(/public enum Risk\s*\{[\s\S]*?\n {4}\}/)[0];
  assert.equal((kinds.match(/^ {8}[A-Z]\w+,$/gm) ?? []).length, 4,
    "the risk list grew a kind — it is meant to stay at four");
});
