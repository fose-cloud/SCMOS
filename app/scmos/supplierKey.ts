/**
 * Which supplier a written company name means — the screens' half of the API's rule (30 Sep 2026).
 *
 * The API matches a name by `SupplierRegister.Key` over each approved supplier's name, code, legal name and
 * aliases (Services/SupplierNames.cs); the company pickers must mean the same, or a name the API accepts reads
 * as "not on the register" on screen. tests/fixtures/supplier-keys.json holds both to the same cases.
 */

/** Letters and digits of any script, upper case — "A.C.N" and "ACN" are one key, and Thai letters stay. */
export function supplierKey(value: string): string {
  return (value ?? "").toUpperCase().replace(/[^\p{L}\p{N}]/gu, "");
}

export type SupplierSpellings = { name: string; code?: string; legalName?: string; aliases?: string[] };

/**
 * Every spelling of every supplier, to its registered name. A key two suppliers share means neither — the
 * API refuses it too — and an empty key means nothing.
 */
export function spellingIndex(rows: readonly SupplierSpellings[]): Map<string, string | null> {
  const index = new Map<string, string | null>();
  for (const row of rows) {
    for (const spelling of [row.name, row.code ?? "", row.legalName ?? "", ...(row.aliases ?? [])]) {
      const key = supplierKey(spelling);
      if (!key) continue;
      const held = index.get(key);
      index.set(key, held === undefined || held === row.name ? row.name : null);
    }
  }
  return index;
}
