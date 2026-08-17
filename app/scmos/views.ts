/**
 * Personal workspace views.
 *
 * Operators work the same slice of the plan every morning — their own jobs, one
 * category, one trucker. A view stores that arrangement under a name so it can
 * be restored in one click. Kept in localStorage: it is a per-person UI
 * preference, not shared operational data, so it does not belong in the register.
 */

export type ViewState = {
  tab: string;
  cat: string;
  cust: string;
  trucker: string;
  date: string;
  kpi: string;
  assignee: string;
  /** Added with the process board; views saved before it simply lack these. */
  status?: string;
  type?: string;
  year?: string;
  month?: string;
  q: string;
};

export type SavedView = { name: string; savedAt: string; state: ViewState };

const KEY = "scmos.workspace.views";
const LIMIT = 12;

function storage(): Storage | null {
  // Called during the server render too, where there is no window.
  try {
    return typeof window === "undefined" ? null : window.localStorage;
  } catch {
    return null; // private mode / storage disabled
  }
}

export function listViews(): SavedView[] {
  const store = storage();
  if (!store) return [];
  try {
    const parsed = JSON.parse(store.getItem(KEY) ?? "[]") as SavedView[];
    return Array.isArray(parsed) ? parsed.filter((v) => v && typeof v.name === "string") : [];
  } catch {
    return [];
  }
}

function write(views: SavedView[]): SavedView[] {
  const store = storage();
  if (store) {
    try {
      store.setItem(KEY, JSON.stringify(views));
    } catch {
      // Quota or private mode — the views simply do not persist.
    }
  }
  return views;
}

/** Saving under an existing name overwrites it, which is what "update" means here. */
export function saveView(name: string, state: ViewState): SavedView[] {
  const trimmed = name.trim();
  if (!trimmed) return listViews();
  const entry: SavedView = { name: trimmed, savedAt: new Date().toISOString(), state };
  const rest = listViews().filter((v) => v.name !== trimmed);
  return write([entry, ...rest].slice(0, LIMIT));
}

export function deleteView(name: string): SavedView[] {
  return write(listViews().filter((v) => v.name !== name));
}

/** Human-readable summary of what a view actually filters, for the list. */
export function describeView(state: ViewState): string {
  const parts: string[] = [state.tab];
  if (state.cat && state.cat !== "ALL") parts.push(state.cat);
  if (state.assignee && state.assignee !== "All Team") parts.push(state.assignee);
  if (state.cust && state.cust !== "ALL") parts.push(state.cust);
  if (state.trucker && state.trucker !== "ALL") parts.push(state.trucker);
  if (state.type && state.type !== "ALL") parts.push(state.type);
  if (state.status && state.status !== "ALL") parts.push(state.status);
  if (state.month && state.month !== "ALL") parts.push("เดือน " + state.month);
  if (state.year && state.year !== "ALL") parts.push(state.year);
  if (state.date && state.date !== "ALL") parts.push(state.date);
  if (state.kpi && state.kpi !== "All") parts.push(state.kpi);
  if (state.q) parts.push(`“${state.q}”`);
  return parts.join(" · ");
}
