"use client";

import { useEffect } from "react";
import { apiFetch } from "./api";
import { useRemembered } from "./pageCache";

/**
 * The kinds of lorry and box the team plans with.
 *
 * The type column used to offer whatever spellings the jobs already held, and
 * the jobs held sixty-four spellings of about sixteen things — `1X20'`, `1X20`
 * and `1x20` all looked equally official, so picking any of them was how the
 * sixty-fifth got made. It comes from a table now, kept on the Capacity screen,
 * and nothing else may be chosen.
 *
 * Same shape as [useCarriers] on purpose: a list to choose from, a way to ask
 * whether what a row already says is on it, and a flag that is false until the
 * answer has arrived — because refusing a value before the list has loaded
 * would mark every row on the screen as wrong for a second.
 */
export type VehicleTypes = {
  /** What may be chosen, in the order the team put them in. */
  codes: string[];
  /** Whether a value written on a job is on the list. */
  knows: (code: string) => boolean;
  /** False until the list has answered. */
  ready: boolean;
};

type Row = { id: number; code: string; label: string; sort: number; active: boolean; inUse: number };

const key = (value: string) => value.trim().toUpperCase();

export function useVehicleTypes(): VehicleTypes {
  const [rows, setRows] = useRemembered<Row[]>("vehicleTypes");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/vehicle-types", { headers: { accept: "application/json" } });
      if (!response.ok || cancelled) return;
      const list = await response.json() as Row[];
      if (!cancelled) setRows(list);
    })();
    return () => { cancelled = true; };
  }, [setRows]);

  // Retired types are still readable on the jobs that carry them, so `knows`
  // counts them — a row keyed last March should not light up as a mistake
  // because the type was taken off the list this morning. Only the active ones
  // are offered for new work.
  const known = new Set((rows ?? []).map((row) => key(row.code)));
  return {
    codes: (rows ?? []).filter((row) => row.active).map((row) => row.code),
    knows: (code: string) => known.has(key(code)),
    ready: rows !== null && rows !== undefined,
  };
}
