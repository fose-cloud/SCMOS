type QueueRow = { state: string; requestedBy: string; payload: { key: string } };
export function filterOperationsQueue<T extends QueueRow>(rows: T[], state: string, search: string): T[] {
  const query = search.trim().toLocaleLowerCase();
  return rows.filter(row => (state === "all" || row.state === state)
    && (!query || [row.payload.key, row.requestedBy].some(value => value.toLocaleLowerCase().includes(query))));
}
