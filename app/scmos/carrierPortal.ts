import type { Screen } from "./nav";

/**
 * The Subcontractor's own screens (29 Sep 2026): which menu entry is which
 * view, and the two reads that are theirs alone — the rate book's lanes for
 * their company and their line of the scorecard. The server cuts both down to
 * the account's company; these only refuse an answer of the wrong shape.
 */

export type CarrierView = "dashboard" | "new" | "myjob" | "postpone" | "rates" | "billing" | "kpi";

export const CARRIER_VIEW: Partial<Record<Screen, CarrierView>> = {
  carrier: "dashboard", carriernew: "new", carriermyjob: "myjob", carrierpostpone: "postpone",
  carrierrates: "rates", carrierbilling: "billing", carrierkpi: "kpi",
};

export type Band = { label: string; min: number; max: number; position: number };
export type Lane = {
  id: number; service: string; customer: string; from: string; to: string; county: string; remark: string;
  prices: Record<string, (number | null)[]>;
};
export type CarrierRates = { supplierId: number; supplierName: string; bands: Band[]; lanes: Lane[] };

export type TrendPoint = { period: string; value: number | null; base: number };
export type Measure = {
  id: string; thai: string; available: boolean; value: number | null; base: number; unit: string; note: string;
  target: number | null; meetsTarget: boolean | null; trend: TrendPoint[] | null;
};
export type ScoreLine = {
  id: string; english: string; thai: string; weight: number; percent: number | null;
  count: number; base: number; target: number; note: string;
};
export type Tally = {
  transportAccidentMajor: number; transportAccidentMinor: number; loadingAccident: number;
  complaints: number; breakdownNoComplaint: number;
};
export type CarrierScore = {
  carrier: string; shipments: number; lines: ScoreLine[]; weighted: number | null; weightAvailable: number;
  ungradedAccidents: number; tally: Tally;
};
export type CarrierKpi = {
  supplierId: number; supplierName: string; year: string; month: string;
  onTime: Measure | null; score: CarrierScore | null;
};

type Obj = Record<string, unknown>;
const obj = (v: unknown): v is Obj => typeof v === "object" && v !== null && !Array.isArray(v);
const text = (v: unknown, max = 400) => typeof v === "string" && v.length <= max;
const num = (v: unknown) => typeof v === "number" && Number.isFinite(v);
const count = (v: unknown) => num(v) && Number.isInteger(v) && (v as number) >= 0;
const maybeNum = (v: unknown) => v === null || num(v);

function band(v: unknown): v is Band {
  return obj(v) && text(v.label, 60) && num(v.min) && num(v.max) && count(v.position);
}
function lane(v: unknown, width: number): v is Lane {
  return obj(v) && num(v.id) && ["service", "customer", "from", "to", "county", "remark"].every(k => text(v[k], 400))
    && obj(v.prices) && Object.entries(v.prices).every(([vehicle, row]) => vehicle.length <= 60
      && Array.isArray(row) && row.length <= Math.max(width, 1) && row.every(maybeNum));
}

export function parseCarrierRates(v: unknown): CarrierRates {
  if (!(obj(v) && count(v.supplierId) && text(v.supplierName, 200) && Array.isArray(v.bands) && v.bands.length <= 40
    && v.bands.every(band) && Array.isArray(v.lanes) && v.lanes.length <= 20000)) throw new Error("invalid_response");
  const width = v.bands.length === 0 ? 0 : Math.max(...(v.bands as Band[]).map(b => b.position)) + 1;
  if (!v.lanes.every(one => lane(one, width))) throw new Error("invalid_response");
  return v as unknown as CarrierRates;
}

function measure(v: unknown): v is Measure {
  return obj(v) && text(v.id, 60) && text(v.thai, 200) && typeof v.available === "boolean" && maybeNum(v.value)
    && count(v.base) && text(v.unit, 20) && text(v.note, 1000) && maybeNum(v.target ?? null)
    && (v.meetsTarget == null || typeof v.meetsTarget === "boolean")
    && (v.trend == null || Array.isArray(v.trend) && v.trend.length <= 36
      && v.trend.every(p => obj(p) && text(p.period, 10) && maybeNum(p.value) && count(p.base)));
}
function scoreLine(v: unknown): v is ScoreLine {
  return obj(v) && text(v.id, 60) && text(v.english, 200) && text(v.thai, 200) && num(v.weight) && maybeNum(v.percent)
    && count(v.count) && count(v.base) && num(v.target) && text(v.note, 1000);
}
function score(v: unknown): v is CarrierScore {
  const t = obj(v) ? v.tally : null;
  return obj(v) && text(v.carrier, 200) && count(v.shipments) && Array.isArray(v.lines) && v.lines.length <= 20
    && v.lines.every(scoreLine) && maybeNum(v.weighted) && num(v.weightAvailable) && count(v.ungradedAccidents)
    && obj(t) && ["transportAccidentMajor", "transportAccidentMinor", "loadingAccident", "complaints", "breakdownNoComplaint"]
      .every(k => count(t[k]));
}

export function parseCarrierKpi(v: unknown): CarrierKpi {
  if (!(obj(v) && count(v.supplierId) && text(v.supplierName, 200) && /^\d{4}$/.test(String(v.year))
    && /^(\d{2})?$/.test(String(v.month)) && (v.onTime === null || measure(v.onTime))
    && (v.score === null || score(v.score)))) throw new Error("invalid_response");
  return v as unknown as CarrierKpi;
}

/** "95.2%", or a dash where nothing could be measured. */
export function percentText(value: number | null | undefined): string {
  return value == null ? "—" : `${value.toFixed(1)}%`;
}

/** The vehicles a lane is priced for, in the order they were written. */
export function vehiclesOf(lane: Lane): string[] {
  return Object.keys(lane.prices);
}

/** Lanes a search reads through: service, customer, from, to, county and remark, any word. */
export function filterLanes(lanes: Lane[], service: string, query: string): Lane[] {
  const words = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  return lanes.filter(one => (!service || one.service === service)
    && words.every(word => [one.service, one.customer, one.from, one.to, one.county, one.remark]
      .some(field => field.toLowerCase().includes(word))));
}
