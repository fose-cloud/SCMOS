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
  private pendingReason = "";
  private tail: Promise<void> = Promise.resolve();

  get size() {
    return this.pending.size;
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
      const reason = this.pendingReason;
      this.pendingReason = "";

      let result: SaveResult;
      try {
        result = await save(batch, reason);
      } catch (error) {
        result = { ok: false, message: error instanceof Error ? error.message : String(error) };
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
