"use client";

import { useEffect } from "react";
import { apiFetch } from "./api";
import { useRemembered } from "./pageCache";

/**
 * The haulage companies, as the register lists them.
 *
 * The workspace used to offer whatever spellings the jobs already contained,
 * most used first. That is a suggestion built from the problem it was meant to
 * solve: TATIYAPOL, TATIYAPON and TTP all appeared, all looked equally
 * official, and picking any of them was how the fourth spelling got created.
 * The names come from the subcontractor register now, and nothing else may be
 * chosen.
 *
 * The spellings come too. A job written months ago says "SJ", and the register
 * knows SJ means Sangja Transport — so the grid can say which company a row
 * belongs to without rewriting what somebody typed.
 */
export type Carriers = {
  /** Official names, alphabetical. What a user may choose from. */
  names: string[];
  /** The company a spelling means, or null when the register has never seen it. */
  companyOf: (spelling: string) => string | null;
  /** False until the register has answered — nothing is refused before then. */
  ready: boolean;
};

type Row = { name: string; status: string; aliases: string[] };

/** Letters and digits only, upper case, so "A.C.N" and "A C N" are one key. */
const key = (value: string) =>
  value.trim().toUpperCase().replace(/[^A-Z0-9]/g, "");

export function useCarriers(): Carriers {
  const [rows, setRows] = useRemembered<Row[]>("carriers");

  useEffect(() => {
    let cancelled = false;
    (async () => {
      // Approved only. A haulier still being audited, or suspended after an
      // accident, is exactly the one nobody should be able to pick off a
      // dropdown by accident.
      const response = await apiFetch("/api/suppliers?status=approved",
        { headers: { accept: "application/json" } });
      if (!response.ok || cancelled) return;
      const body = await response.json() as Row[];
      if (!cancelled) setRows(body.map((row) => ({
        name: row.name, status: row.status, aliases: row.aliases ?? [],
      })));
    })();
    return () => { cancelled = true; };
  }, [setRows]);

  const names = (rows ?? []).map((row) => row.name)
    .sort((a, b) => a.localeCompare(b));

  const bySpelling = new Map<string, string>();
  for (const row of rows ?? []) {
    bySpelling.set(key(row.name), row.name);
    for (const alias of row.aliases) bySpelling.set(key(alias), row.name);
  }

  return {
    names,
    companyOf: (spelling) => bySpelling.get(key(spelling)) ?? null,
    // An empty register is not the same as one that has not answered. Before
    // it answers the grid must not start telling people that every carrier on
    // every job is unrecognised.
    ready: rows !== null && rows.length > 0,
  };
}
