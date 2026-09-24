import type { RateLane } from "./rates";

export type ManualChemoursRate = {
  kind: "COST" | "SELL";
  carrier: string;
  from: string;
  to: string;
  postalCode: string;
  cargoType: string;
  prices: Record<string, (number | null)[]>;
};

type CardWithLanes = { lanes: RateLane[] };

const key = (value: string) => value.trim().replace(/\s+/g, " ").toLocaleUpperCase("en-US");

/**
 * Adds a manually keyed rate without producing a duplicate route.
 *
 * Imported cards commonly hold one lane per vehicle while a manually keyed
 * row can contain all three vehicles. Existing vehicle rows are updated in
 * place; a vehicle that the route did not have is added to the first matching
 * row. This matters because the table deliberately uses the first matching
 * vehicle price, so appending a duplicate would make the newly created price
 * invisible even though it had been saved.
 */
export function upsertManualChemoursRate<T extends CardWithLanes>(card: T, input: ManualChemoursRate): T {
  const carrier = input.carrier.trim();
  const from = input.from.trim();
  const to = input.to.trim();
  const postalCode = input.postalCode.trim();
  const cargoType = input.cargoType.trim();
  const lanes = card.lanes.map((lane) => ({
    ...lane,
    prices: Object.fromEntries(
      Object.entries(lane.prices).map(([vehicle, prices]) => [vehicle, [...prices]]),
    ),
  }));

  const matches = (lane: RateLane) =>
    key(lane.carrier) === key(carrier)
    && key(lane.from) === key(from)
    && key(lane.to) === key(to)
    && key(lane.county) === key(postalCode)
    && (input.kind === "COST" || key(lane.remark) === key(cargoType));

  let first = lanes.findIndex(matches);
  for (const [vehicle, prices] of Object.entries(input.prices)) {
    if (!prices.some((price) => price !== null)) continue;

    let target = lanes.findIndex((lane) => matches(lane) && Object.hasOwn(lane.prices, vehicle));
    if (target < 0) target = first;
    if (target < 0) {
      lanes.push({
        id: `manual-${Date.now()}-${lanes.length}`,
        carrier,
        service: "DELIVERY",
        customer: "CHEMOURS",
        from,
        to,
        county: postalCode,
        remark: cargoType,
        prices: {},
      });
      target = lanes.length - 1;
      first = target;
    }
    lanes[target].prices[vehicle] = [...prices];
  }

  return { ...card, lanes };
}
