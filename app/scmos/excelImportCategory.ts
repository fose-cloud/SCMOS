export type ImportCategory = "IMPORT" | "EXPORT" | "DELIVERY";

const CATEGORY_WORDS: { category: ImportCategory; patterns: RegExp[] }[] = [
  {
    category: "EXPORT",
    patterns: [/(^|[^A-Z])EXPORT([^A-Z]|$)/, /(^|[^A-Z])EXP([^A-Z]|$)/, /(^|[^A-Z])OUTBOUND([^A-Z]|$)/, /ส่งออก/, /ขาออก/],
  },
  {
    category: "DELIVERY",
    patterns: [/(^|[^A-Z])DELIVERY([^A-Z]|$)/, /(^|[^A-Z])DEL([^A-Z]|$)/, /จัดส่ง/, /กระจายสินค้า/],
  },
  {
    category: "IMPORT",
    patterns: [/(^|[^A-Z])IMPORT([^A-Z]|$)/, /(^|[^A-Z])IMP([^A-Z]|$)/, /(^|[^A-Z])INBOUND([^A-Z]|$)/, /นำเข้า/, /ขาเข้า/],
  },
];

function categoryIn(value: unknown): ImportCategory | null {
  const text = String(value ?? "").toUpperCase().replace(/\s+/g, " ").trim();
  if (!text) return null;
  for (const candidate of CATEGORY_WORDS) {
    if (candidate.patterns.some((pattern) => pattern.test(text))) return candidate.category;
  }
  return null;
}

/** Reads a category cell, including the abbreviations and Thai words used in plan files. */
export function declaredImportCategory(value: unknown): ImportCategory | null {
  return categoryIn(value);
}

/** Reads a category from a worksheet name without treating every unknown sheet as Import. */
export function sheetImportCategory(sheetName: string): ImportCategory | null {
  return categoryIn(sheetName);
}

/**
 * Infers a worksheet's direction from the fields that made it through header
 * matching. Export sheets have a recognisable operational shape even when the
 * tab is named only "Plan" or in Thai; ABS is conclusive, while combinations
 * such as booking + closing date are strong enough to decide safely.
 */
export function schemaImportCategory(fields: Iterable<string>): ImportCategory | null {
  const supplied = new Set(fields);

  const delivery = ["wh", "jobNo", "sid", "province", "zip", "pallet", "kgs", "v4", "v6", "v10", "vtr", "cost"]
    .reduce((score, field) => score + (supplied.has(field) ? 1 : 0), 0);
  if (delivery >= 2) return "DELIVERY";

  if (supplied.has("abs")) return "EXPORT";
  const exportScore = ["booking", "plant", "closingDate", "closingTime"]
    .reduce((score, field) => score + (supplied.has(field) ? 2 : 0), 0)
    + ["fclLcl", "returnLoc", "seal", "tare"].reduce((score, field) => score + (supplied.has(field) ? 1 : 0), 0);
  if (exportScore >= 3) return "EXPORT";

  return null;
}

/** Category cells win, then an explicit tab name, then the worksheet's schema. */
export function inferImportCategory(
  declared: unknown,
  sheetName: string,
  fields: Iterable<string>,
): ImportCategory {
  return declaredImportCategory(declared)
    ?? sheetImportCategory(sheetName)
    ?? schemaImportCategory(fields)
    ?? "IMPORT";
}
