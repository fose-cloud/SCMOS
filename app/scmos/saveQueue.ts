export type SaveResult = { ok: boolean; message: string };

type Keyed = { key: string };
type Saver<T> = (batch: T[], reason: string) => Promise<SaveResult>;

/**
 * Serialises whole-job writes and keeps failed work available for retry.
 *
 * A save takes a snapshot out of the pending map while it is in flight. Newer
 * edits can therefore continue to queue without being included accidentally.
 * If the request fails, only keys that have not since been edited are restored;
 * a newer copy of the same job always wins.
 */
export class SaveQueue<T extends Keyed> {
  private readonly pending = new Map<string, T>();
  /** The batch a save is carrying right now, until it lands or comes back. */
  private readonly inFlight = new Map<string, T>();
  private pendingReason = "";
  private tail: Promise<void> = Promise.resolve();

  get size() {
    return this.pending.size;
  }

  /**
   * This screen's version of an item the database has not got yet — queued
   * or on its way. A page re-read while an edit is still travelling would
   * otherwise put the old value back for the second the write takes.
   */
  peek(key: string): T | undefined {
    return this.pending.get(key) ?? this.inFlight.get(key);
  }

  enqueue(items: T[], reason = "") {
    items.forEach((item) => this.pending.set(item.key, item));
    if (reason) this.pendingReason = reason;
  }

  flush(save: Saver<T>): Promise<SaveResult> {
    const run = async (): Promise<SaveResult> => {
      const batch = [...this.pending.values()];
      if (!batch.length) return { ok: true, message: "" };

      this.pending.clear();
      batch.forEach((item) => this.inFlight.set(item.key, item));
      const reason = this.pendingReason;
      this.pendingReason = "";

      let result: SaveResult;
      try {
        result = await save(batch, reason);
      } catch (error) {
        result = { ok: false, message: error instanceof Error ? error.message : String(error) };
      } finally {
        batch.forEach((item) => { if (this.inFlight.get(item.key) === item) this.inFlight.delete(item.key); });
      }

      if (!result.ok) {
        batch.forEach((item) => {
          if (!this.pending.has(item.key)) this.pending.set(item.key, item);
        });
        if (!this.pendingReason && reason) this.pendingReason = reason;
      }

      return result;
    };

    const queued = this.tail.then(run, run);
    this.tail = queued.then(() => undefined, () => undefined);
    return queued;
  }
}
