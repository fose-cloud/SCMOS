import test from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { applyDelta, needsFullReload, SYNC_EVERY_MS } from "../app/scmos/registerSync.ts";
import { SaveQueue } from "../app/scmos/saveQueue.ts";

/*
 * The register on screen following the one in the database without F5.
 * What a delta does to the rows in memory, when a delta is not enough, and
 * that a screen's own travelling edit is never overwritten by a stale read.
 */

const job = (key, remark = "") => ({ key, id: key, remark });

test("a changed row replaces its own, a new row goes in front, and a row being edited here is left alone", () => {
  const jobs = [job("A", "old"), job("B", "old"), job("C", "old")];
  const done = applyDelta(jobs, [job("B", "theirs"), job("N", "new"), job("C", "theirs")], (key) => key === "C");
  assert.deepEqual(done, { replaced: 1, added: 1, kept: 1 });
  assert.equal(jobs[0].key, "N", "the new row is in front, where a row inserted here goes too");
  assert.equal(jobs.find((j) => j.key === "B").remark, "theirs");
  assert.equal(jobs.find((j) => j.key === "C").remark, "old", "the row under this screen's edit keeps this screen's value");
  assert.equal(jobs.length, 4);
});

test("two new rows land in order, and positions stay right after the first shift", () => {
  const jobs = [job("A")];
  applyDelta(jobs, [job("X"), job("Y"), job("A", "changed")], () => false);
  assert.deepEqual(jobs.map((j) => j.key), ["Y", "X", "A"]);
  assert.equal(jobs[2].remark, "changed", "A was still found after two rows were put in front of it");
});

test("the whole register is re-read when the API says so, or when a row went away", () => {
  assert.equal(needsFullReload({ full: true, count: 10 }, 10), true);
  assert.equal(needsFullReload({ full: false, count: 9 }, 10), true, "one fewer in the database: a deletion");
  assert.equal(needsFullReload({ full: false, count: 10 }, 10), false);
});

test("the queue can say what it is carrying, queued or in flight, and forgets it once it lands", async () => {
  const queue = new SaveQueue();
  queue.enqueue([{ key: "A", v: 1 }]);
  assert.equal(queue.peek("A").v, 1, "queued");
  let release;
  const landed = new Promise((resolve) => { release = resolve; });
  const flushing = queue.flush(async () => { await landed; return { ok: true, message: "" }; });
  // The save starts on the next turn of the queue, not on the call.
  await new Promise((resolve) => setTimeout(resolve, 0));
  assert.equal(queue.size, 0, "the batch left the queue");
  assert.equal(queue.peek("A").v, 1, "but is still this screen's version while it travels");
  release();
  await flushing;
  assert.equal(queue.peek("A"), undefined, "and the database's version is the truth once it has landed");
});

test("the workspace re-reads its page after the write lands, lays its own edits over a page, and asks every twenty seconds while visible", () => {
  const app = readFileSync(new URL("../app/SCMOSApp.tsx", import.meta.url), "utf8");
  assert.match(app, /if \(result\.ok\) setRevision\(\(r\) => r \+ 1\);/);
  assert.match(app, /prep\(\{ jobs: answer\.jobs \}\)\.jobs\.map\(\(job\) => own\.peek\(job\.key\) \?\? job\)/);
  assert.match(app, /window\.setInterval\(tick, SYNC_EVERY_MS\)/);
  assert.match(app, /if \(document\.visibilityState === "visible"\) void syncRef\.current\(\)/);
  assert.equal(SYNC_EVERY_MS, 20_000);
  // The API side: rows after a stamp, capped, with the count and a full-precision stamp.
  const repo = readFileSync(new URL("../server/Scmos.Api/Data/JobsRepository.cs", import.meta.url), "utf8");
  assert.match(repo, /public const int DeltaLimit = 400;/);
  assert.match(repo, /if \(changed\.Count > DeltaLimit\) return \("\[\]", count, newest, true\);/);
});
