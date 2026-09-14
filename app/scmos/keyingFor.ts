/**
 * Whose name goes on a job somebody keys while covering for a colleague.
 *
 * A delegation lets Watsana edit Uthai's jobs while he is on leave. It said
 * nothing about the jobs she keys *for* him during that week — every path
 * that creates a job wrote the signed-in person's name on it, so a week of
 * Uthai's shipments came back owned by Watsana, with nothing to say they
 * were ever his. This is the one decision — as whom am I keying right now —
 * read by every path that creates a job: the inserted row, the duplicated
 * row, the add form and the Excel import.
 *
 * The choice is the person's, not inferred: somebody covering a colleague
 * keys their own work in the same week, and the grid cannot tell which is
 * which. It defaults to themselves, is offered only while a grant is live,
 * and falls back to themselves the day the grant ends.
 */

/** Somebody whose work this person is covering today, as /api/me reports it. */
export type Cover = { id: string; name: string; until: string };

export type Author = { name: string; opId: string; forSomeoneElse: boolean };

/**
 * The name and id a new job gets.
 *
 * `keyingFor` is an owner id or empty for oneself. An id that is not among
 * the live covers — a grant that ended overnight, a choice remembered from a
 * previous week — is oneself, not an error: the wrong answer here is a job
 * owned by somebody nobody is covering for.
 */
export function authorOf(
  me: { name: string; opId: string },
  keyingFor: string,
  covering: readonly Cover[],
): Author {
  const cover = keyingFor ? covering.find((one) => one.id === keyingFor) : undefined;
  return cover
    ? { name: cover.name, opId: cover.id, forSomeoneElse: true }
    : { name: me.name, opId: me.opId, forSomeoneElse: false };
}

/**
 * The next choice when the button is pressed: oneself, then each person
 * covered in turn, then oneself again. One button rather than a menu,
 * because the list is one name in every case that has happened so far.
 */
export function nextKeyingFor(current: string, covering: readonly Cover[]): string {
  if (covering.length === 0) return "";
  const at = covering.findIndex((one) => one.id === current);
  if (at < 0) return covering[0].id;
  return at + 1 < covering.length ? covering[at + 1].id : "";
}

/** What the button says. The name is always there, so "me" is never ambiguous. */
export function keyingLabel(author: Author): string {
  return author.forSomeoneElse ? `ลงงานให้: ${author.name} (แทน)` : `ลงงานให้: ฉัน (${author.name})`;
}

/** Where the choice is remembered, per signed-in person, across reloads. */
export const keyingForKey = (opId: string): string => `scmos.keying-for.${opId}`;
