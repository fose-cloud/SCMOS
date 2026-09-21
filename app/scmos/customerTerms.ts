/**
 * The terms a customer's own agreement sets on the department's measures —
 * the web's copy of `server/Scmos.Api/Rules/CustomerTerms.cs`, for the
 * dashboard's own on-time count (`opsStats`), which is worked out in the
 * browser from the jobs on screen.
 *
 * The on-time KPI is zero grace for everyone; a customer's agreement can say
 * otherwise for that customer alone. On 21 September 2026 the department lead
 * set Lotus's: an arrival up to thirty minutes after plan is not counted late
 * in the KPI or on the dashboard. `tests/customerTerms.test.mjs` fails if this
 * table and the API's ever differ.
 */
export type CustomerTerm = { customer: string; graceMinutes: number; since: string };

export const CUSTOMER_TERMS: CustomerTerm[] = [
  { customer: "LOTUS", graceMinutes: 30, since: "21/09/2026" },
];

/** The term for a customer as the register spells them — "LOTUS" and "LOTUS ASIA" are both Lotus — or null. */
export function customerTerm(customer: string | undefined): CustomerTerm | null {
  const name = (customer ?? "").trim().replace(/\s+/g, " ").toUpperCase();
  if (!name) return null;
  return CUSTOMER_TERMS.find((term) => name === term.customer || name.startsWith(term.customer + " ")) ?? null;
}

/** Minutes after plan an arrival still counts on time for this customer — zero unless a term says otherwise. */
export function graceMinutes(customer: string | undefined): number {
  return customerTerm(customer)?.graceMinutes ?? 0;
}
