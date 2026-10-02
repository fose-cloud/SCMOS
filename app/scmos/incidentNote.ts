/**
 * The note the CAR/PAR register's Excel import writes on a case whose job number did not match a job (2 Oct 2026). The
 * importer kept every column of the source row as JSON after this heading, so nothing in the file was lost; shown as it
 * is, that is a wall of braces and quotes. Read here into the columns it holds, for the case screen to show as a table.
 */
export const ORIGINAL_COLUMNS = "Original columns (unlinked source job number):";

/** The note without the source row, and the source row's columns — headings on one line, empty and "-" cells left out. */
export function importedColumns(note: string): { rest: string; columns: [string, string][] } {
  const at = note.indexOf(ORIGINAL_COLUMNS);
  if (at < 0) return { rest: note, columns: [] };
  try {
    const parsed: unknown = JSON.parse(note.slice(at + ORIGINAL_COLUMNS.length).trim());
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) return { rest: note, columns: [] };
    const columns = Object.entries(parsed as Record<string, unknown>)
      .map(([key, value]) => [key.replace(/\s*\n\s*/g, " ").trim(), String(value ?? "").trim()] as [string, string])
      .filter(([key, value]) => key.length > 0 && value.length > 0 && value !== "-");
    return { rest: note.slice(0, at).trim(), columns };
  } catch {
    return { rest: note, columns: [] };
  }
}
