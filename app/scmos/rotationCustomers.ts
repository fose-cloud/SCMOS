"use client";

import { useEffect } from "react";
import { apiFetch } from "./api";
import { useRemembered } from "./pageCache";

/** The customer names maintained by Job Rotation. */
export type RotationCustomers = {
  names: string[];
  knows: (name: string) => boolean;
  ready: boolean;
};

const key = (value: string) => value.trim().toLocaleUpperCase();

/**
 * Supplies the My Job customer dropdown without loading the rotation report.
 * The endpoint selects only the distinct customer column, while the last
 * answer is remembered for this browser tab so reopening My Job is instant.
 */
export function useRotationCustomers(): RotationCustomers {
  const [remembered, setRemembered] = useRemembered<string[]>("rotationCustomers");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      const response = await apiFetch("/api/rotation/customers",
        { headers: { accept: "application/json" } });
      if (!response.ok || cancelled) return;
      const body = await response.json() as { customers?: string[] };
      const unique = new Map<string, string>();
      for (const raw of body.customers ?? []) {
        const name = String(raw ?? "").trim();
        if (name) unique.set(key(name), name);
      }
      if (!cancelled) {
        setRemembered([...unique.values()].sort((a, b) => a.localeCompare(b)));
      }
    })();
    return () => { cancelled = true; };
  }, [setRemembered]);

  const names = remembered ?? [];
  const known = new Set(names.map(key));
  return {
    names,
    knows: (name) => known.has(key(name)),
    ready: remembered !== null,
  };
}
