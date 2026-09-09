import { matchesChosen } from "./filterChoices";

export type DashboardFilters = { customer: string; trucker: string };
export const ALL_DASHBOARD_FILTERS: DashboardFilters = { customer: "ALL", trucker: "ALL" };

/** OR within a picker, AND across the two dimensions. Dates are filtered separately. */
export function filterDashboardJobs<T extends { customer: string; trucker: string }>(
  jobs: T[], filters: DashboardFilters,
): T[] {
  return jobs.filter(job => matchesChosen(job.customer, filters.customer)
    && matchesChosen(job.trucker, filters.trucker));
}
