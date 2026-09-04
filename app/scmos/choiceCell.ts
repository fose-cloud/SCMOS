/**
 * What a dropdown column will accept from a paste.
 *
 * Customer, Trucker and Type are dropdowns because the value has to be one of
 * the register's own names — a job against "SANGJAA" is a job nobody can bill,
 * and the list is what stops that being typed. Copy and paste were wanted on
 * those columns all the same, which is reasonable: forty rows of carrier come
 * off a spreadsheet in one gesture and picking them one at a time is the work
 * the grid exists to remove.
 *
 * So the paste is allowed and the guarantee is kept: a pasted value has to
 * resolve to something already on the list, and what gets written is the list's
 * spelling rather than the clipboard's. Free typing stays closed — the cell is
 * still a dropdown, and nothing here opens an editor.
 *
 * A leaf module: it imports nothing, so the rule can be checked without the app.
 */

/**
 * The option a pasted value means, or null when it means none of them.
 *
 * <ul>
 *   <li>Empty clears the cell. A job with no carrier yet is a real state, and
 *       Delete over a block has to keep working.</li>
 *   <li>Case and surrounding space are the clipboard's, not the value's —
 *       "sangja " off a spreadsheet is SANGJA. What is stored is the list's
 *       spelling, so the register does not gain a second way to write a name
 *       it already has.</li>
 *   <li>Anything else is refused. Not corrected, not written through: the
 *       column's promise is that every value in it came off the list.</li>
 * </ul>
 */
export function resolveChoice(value: string, options: readonly string[]): string | null {
  const wanted = (value ?? "").trim();
  if (wanted.length === 0) return "";

  const folded = wanted.toLowerCase();
  const hit = options.find((one) => (one ?? "").trim().toLowerCase() === folded);
  return hit === undefined ? null : hit;
}

/** One cell of a block, resolved against the list its column allows. */
export type ChoiceOutcome<TRow, TField> =
  | { ok: true; row: TRow; field: TField; value: string }
  | { ok: false; row: TRow; field: TField; value: string };

/**
 * A pasted block, split into what the dropdowns will take and what they refuse.
 *
 * `optionsFor` returns null for a column that is not a dropdown — those pass
 * through untouched, because a free-text column is free text and this rule has
 * nothing to say about it.
 *
 * Refusals are returned rather than dropped so the screen can say how many
 * there were. A paste that silently lands on nine cells of ten looks like a
 * paste that worked.
 */
export function resolveBlock<TRow, TField>(
  edits: readonly { row: TRow; field: TField; value: string }[],
  optionsFor: (field: TField, row: TRow) => readonly string[] | null,
): { allowed: { row: TRow; field: TField; value: string }[]; refused: number } {
  const allowed: { row: TRow; field: TField; value: string }[] = [];
  let refused = 0;

  for (const edit of edits) {
    const options = optionsFor(edit.field, edit.row);
    if (options === null) { allowed.push(edit); continue; }

    const resolved = resolveChoice(edit.value, options);
    if (resolved === null) { refused++; continue; }
    allowed.push({ row: edit.row, field: edit.field, value: resolved });
  }

  return { allowed, refused };
}
