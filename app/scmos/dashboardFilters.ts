import { chosenIn, matchesChosen } from "./filterChoices";

export type DashboardFilters = { customer: string; trucker: string };
export const ALL_DASHBOARD_FILTERS: DashboardFilters = { customer: "ALL", trucker: "ALL" };

/** OR within a picker, AND across the two dimensions. Dates are filtered separately. */
export function filterDashboardJobs<T extends { customer: string; trucker: string }>(
  jobs: T[], filters: DashboardFilters,
): T[] {
  return jobs.filter(job => matchesChosen(job.customer, filters.customer)
    && matchesChosen(job.trucker, filters.trucker));
}

/**
 * What one picker should offer, given what the other one says.
 *
 * <p>The period bar already works this way — choosing a year narrows the
 * months, choosing a month narrows the days, "so the picker can never land on
 * an empty period by accident". The two dimensions did not: both lists were
 * built from the whole register, so it was possible to choose a customer and
 * then a haulier who has never carried for them and be shown an empty
 * dashboard, with nothing on the screen saying why.</p>
 *
 * <p>Narrowed by the <i>other</i> dimension only, never by itself — a picker
 * that removed the values you had not chosen would leave you unable to widen
 * your own selection.</p>
 *
 * <p>Whatever is already ticked stays on the list even when the other filter
 * has narrowed it away. Otherwise unticking it would mean first clearing the
 * filter that hid it, which is the opposite of how somebody gets out of a
 * selection that returned nothing.</p>
 */
export function dashboardOptions<T extends { customer: string; trucker: string }>(
  jobs: T[], field: "customer" | "trucker", filters: DashboardFilters,
): string[] {
  const other = field === "customer" ? "trucker" : "customer";
  const found = new Set<string>();

  for (const job of jobs) {
    if (!job[field]) continue;
    if (matchesChosen(job[other], filters[other])) found.add(job[field]);
  }
  for (const ticked of chosenIn(filters[field])) found.add(ticked);

  return [...found].sort((a, b) => a.localeCompare(b));
}
