import type { CarrierCapacity } from "./carrierPortal";

/**
 * The Capacity board's shape, for both screens (30 Sep 2026): the department's `/api/capacity` answers in it,
 * and a carrier's `/api/carrier/capacity` is turned into it here, so one component draws both.
 */
export type CapacityRow = {
  supplierId: number; supplier: string; date: string; vehicleType: string;
  available: number; committed: number; demand: number;
  spare: number; short: boolean; updatedBy: string; updatedAt: string | null;
  /** False on a carrier's day and vehicle it has jobs on but has said nothing for. The department's rows are all reported. */
  reported?: boolean;
};
export type CapacityDay = { date: string; available: number; committed: number; demand: number; short: boolean };
export type Board = { days: CapacityDay[]; cells: CapacityRow[]; vehicleTypes: string[]; anyReported: boolean };

/**
 * A carrier's own capacity as the department's board reads: a day's cards add up what it reported and count
 * its own Leschaco jobs as the demand; each row keeps whether it was reported at all.
 */
export function boardFromCarrier(capacity: CarrierCapacity, cardDays = 7): Board {
  const days = capacity.dates.slice(0, cardDays).map((date) => {
    const mine = capacity.cells.filter((cell) => cell.date === date);
    const reported = mine.filter((cell) => cell.reported);
    return {
      date,
      available: reported.reduce((sum, cell) => sum + cell.available, 0),
      committed: reported.reduce((sum, cell) => sum + cell.committed, 0),
      demand: mine.reduce((sum, cell) => sum + cell.jobs, 0),
      short: reported.some((cell) => cell.committed > cell.available),
    };
  });
  const cells = capacity.cells.map((cell) => ({
    supplierId: capacity.supplierId, supplier: capacity.supplierName, date: cell.date, vehicleType: cell.vehicleType,
    available: cell.available, committed: cell.committed, demand: cell.jobs, spare: cell.spare,
    short: cell.reported && cell.committed > cell.available, updatedBy: cell.updatedBy, updatedAt: cell.updatedAt,
    reported: cell.reported,
  }));
  return { days, cells, vehicleTypes: capacity.vehicleTypes, anyReported: capacity.cells.some((cell) => cell.reported) };
}
