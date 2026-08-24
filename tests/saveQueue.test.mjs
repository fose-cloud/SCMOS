import assert from "node:assert/strict";
import test from "node:test";

import { SaveQueue } from "../app/scmos/saveQueue.ts";

test("failed work stays queued and keeps its reason for retry", async () => {
  const queue = new SaveQueue();
  const job = { key: "A", value: "first" };
  queue.enqueue([job], "Excel import");

  const failed = await queue.flush(async () => ({ ok: false, message: "SQL timeout" }));
  assert.deepEqual(failed, { ok: false, message: "SQL timeout" });
  assert.equal(queue.size, 1);

  let retried;
  const saved = await queue.flush(async (batch, reason) => {
    retried = { batch, reason };
    return { ok: true, message: "" };
  });

  assert.equal(saved.ok, true);
  assert.equal(queue.size, 0);
  assert.deepEqual(retried, { batch: [job], reason: "Excel import" });
});

test("concurrent flush calls execute one save at a time", async () => {
  const queue = new SaveQueue();
  const seen = [];
  let active = 0;
  let maxActive = 0;
  let releaseFirst;
  const firstGate = new Promise((resolve) => { releaseFirst = resolve; });

  const save = async (batch) => {
    active += 1;
    maxActive = Math.max(maxActive, active);
    seen.push(batch.map((item) => item.key));
    if (seen.length === 1) await firstGate;
    active -= 1;
    return { ok: true, message: "" };
  };

  queue.enqueue([{ key: "A" }]);
  const first = queue.flush(save);
  await Promise.resolve();
  queue.enqueue([{ key: "B" }]);
  const second = queue.flush(save);
  releaseFirst();

  await Promise.all([first, second]);
  assert.equal(maxActive, 1);
  assert.deepEqual(seen, [["A"], ["B"]]);
});

test("a newer edit wins when an older in-flight copy fails", async () => {
  const queue = new SaveQueue();
  let release;
  const gate = new Promise((resolve) => { release = resolve; });

  queue.enqueue([{ key: "A", value: "old" }]);
  const failed = queue.flush(async () => {
    await gate;
    return { ok: false, message: "offline" };
  });

  await Promise.resolve();
  queue.enqueue([{ key: "A", value: "new" }]);
  release();
  await failed;

  let retried;
  await queue.flush(async (batch) => {
    retried = batch;
    return { ok: true, message: "" };
  });

  assert.deepEqual(retried, [{ key: "A", value: "new" }]);
});

test("a thrown save error is returned and requeued", async () => {
  const queue = new SaveQueue();
  queue.enqueue([{ key: "A" }]);

  const result = await queue.flush(async () => { throw new Error("network down"); });

  assert.deepEqual(result, { ok: false, message: "network down" });
  assert.equal(queue.size, 1);
});
