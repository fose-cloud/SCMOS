/**
 * The diesel price, which is what chooses a rate.
 *
 * **Superseded as the pricing basis by dieselMonth.ts.** The account team's
 * rule is that a month is priced at the average of its daily pump prices, not
 * at whatever the price happened to be on the day somebody looked. What is left
 * here is the current published figure and its provenance — useful as the
 * default in a box somebody is typing into, and as one day's input for
 * an average, but not as the rate a trip is billed at.
 *
 * Every lane on the Chemours cards is eleven prices, one per band of the fuel
 * clause, and the diesel figure picks which one applies. So the number is not
 * decoration on a job — it is half of how the trip's cost was arrived at, and a
 * trip whose diesel rate was not recorded cannot be reconciled against an
 * invoice months later without guessing what the pump said that week.
 *
 * PTT OR publish the retail price at https://www.pttor.com/th/oil_price. It is
 * read by hand and written down here rather than fetched by the app: a rate
 * that decides money should not change because a third party edited a web page
 * overnight, and a scrape that quietly failed would leave a stale figure
 * looking current. Update it here, in one place, and the date says how old it is.
 *
 * No imports: this is a constant and a little arithmetic on it.
 */

/**
 * Bangkok retail diesel, ฿/litre, and when it took effect.
 *
 * Was 32.94 until 2026-09-07, which by then was two bands low — the clause
 * steps about 3% a band, so every price read on the rate screen at the default
 * was roughly 6% under what the contract actually charged unless somebody
 * happened to retype the figure.
 */
export const DIESEL = {
  price: 39.14,
  /** As PTT OR write it, Buddhist era, which is what the source page shows. */
  effective: "02-09-2569 05:00",
  source: "PTT OR · pttor.com/th/oil_price",
  /** B20, kept because some hauliers quote against it. Not used to price anything here. */
  b20: 34.14,
} as const;

/** The default as the screens want it: a string, because they are text boxes. */
export const DIESEL_DEFAULT = String(DIESEL.price);

/**
 * A diesel figure off a job, as a number.
 *
 * Null when there is nothing readable. A trip with no diesel rate recorded is
 * not a trip that ran on free fuel — the figure is simply unknown, and a zero
 * would sort below every real band and price the trip at the cheapest one.
 */
export function dieselRate(value: string | number | undefined | null): number | null {
  if (typeof value === "number") return Number.isFinite(value) && value > 0 ? value : null;
  const text = String(value ?? "").replace(/[,\s฿]/g, "").trim();
  if (!text) return null;
  const number = Number(text);
  return Number.isFinite(number) && number > 0 ? number : null;
}

/**
 * Whether a diesel figure is plausible as a pump price.
 *
 * Wide on purpose. It is here to catch a litre price typed into the wrong
 * column — a trip cost of 3,480 or a postcode of 10160 — not to have an opinion
 * about the market. Thai retail diesel has spent the last decade between about
 * 20 and 35, so anything from 10 to 99 passes.
 */
export function looksLikeDiesel(value: string | number | undefined | null): boolean {
  const rate = dieselRate(value);
  return rate !== null && rate >= 10 && rate < 100;
}
