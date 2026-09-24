/**
 * The terms a customer's own agreement sets on the department's measures —
 * the web's copy of `server/Scmos.Api/Rules/CustomerTerms.cs`, for the
 * dashboard's own on-time count (`opsStats`), which is worked out in the
 * browser from the jobs on screen.
 *
 * The on-time KPI is zero grace unless a customer's registered agreement says
 * otherwise. A term can also be limited to one kind of job (currently Tank).
 * `tests/customerTerms.test.mjs` fails if this table and the API's ever differ.
 */
export type CustomerTerm = {
  customer: string;
  graceMinutes: number;
  since: string;
  scope: "all" | "tank";
};

export const CUSTOMER_TERMS: CustomerTerm[] = [
  { customer: "LOTUS", graceMinutes: 30, since: "21/09/2026", scope: "all" },
  { customer: "ALLNEX", graceMinutes: 30, since: "24/09/2026", scope: "all" },
  { customer: "SYENSQO", graceMinutes: 30, since: "24/09/2026", scope: "all" },
  { customer: "EVONIK (THAILAND) LTD.", graceMinutes: 180, since: "24/09/2026", scope: "tank" },
];

function isTank(jobType: string | undefined): boolean {
  const value = (jobType ?? "").toUpperCase().replace(/[-_]/g, " ").replace(/\s+/g, " ").trim();
  return /\b(TK|TANK|ISOTANK|ISO\s+TANK)\b/.test(value);
}

/** The matching term for a customer and job type, or null when the default applies. */
export function customerTerm(customer: string | undefined, jobType?: string): CustomerTerm | null {
  const name = (customer ?? "").trim().replace(/\s+/g, " ").toUpperCase();
  if (!name) return null;
  return CUSTOMER_TERMS.find((term) =>
    (name === term.customer || name.startsWith(term.customer + " "))
    && (term.scope === "all" || term.scope === "tank" && isTank(jobType))) ?? null;
}

/** Minutes after plan an arrival still counts on time. Zero is the department default. */
export function graceMinutes(customer: string | undefined, jobType?: string): number {
  return customerTerm(customer, jobType)?.graceMinutes ?? 0;
}
