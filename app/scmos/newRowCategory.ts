/**
 * Which category a row typed straight into a grid belongs to.
 *
 * The workspace grid is shown in two places, and they are not the same place.
 * On My Job it shows whatever the category filter says. On The Chemours'
 * domestic tab it is *locked* to DELIVERY — that report only draws one kind of
 * job, and the filter chips are hidden there because there is nothing to
 * choose.
 *
 * Everything that reads the grid already honours that lock: the query that
 * fetches the rows, the tab counts, and the state handed to the grid itself all
 * ask for `lockedCat ?? filterCat`. Inserting a row did not — it read the
 * filter alone, found "ALL" because the Chemours grid has no filter chips to
 * set it, and fell through to the default. So a row added to The Chemours'
 * domestic grid was created as an IMPORT job and appeared under Import in My
 * Job, on somebody else's screen, with nothing on the Chemours grid to show
 * where it had gone.
 *
 * It is here rather than inline in the component so that it can be checked.
 * The component it came from imports half the application and cannot be
 * exercised by a test; this can, and the bug it is fixing is one nobody would
 * see again until a row went missing.
 */

/** The categories a job can actually be. Not the filter chips, which differ. */
export const JOB_CATEGORIES = ["IMPORT", "EXPORT", "DELIVERY"] as const;

/** What the category filter reads when it is not narrowing anything. */
export const ANY_CATEGORY = "ALL";

/**
 * What a new row is when nothing says otherwise.
 *
 * A guess, and deliberately a visible one: with no filter the grid splits into
 * sections by category, so the row appears under Import where somebody can see
 * it is in the wrong place and change it in the cell. The alternative — no
 * category at all — is a row that belongs to no section and is therefore
 * displayed nowhere, which is the failure this whole function exists to stop.
 */
export const DEFAULT_CATEGORY = "IMPORT";

/**
 * The category a newly inserted row should carry.
 *
 * @param lockedCat  The only category this grid can hold, where there is one.
 * @param filterCat  What the category filter is set to, or "ALL".
 */
export function categoryForNewRow(
  lockedCat: string | null | undefined,
  filterCat: string | null | undefined,
): string {
  // A locked grid wins over everything. It is not a preference, it is the only
  // kind of job the screen can display — a row of any other category would be
  // written, saved, and then be invisible on the screen that created it.
  const locked = normalise(lockedCat);
  if (locked) return locked;

  // Then the section the person is looking at, so the row lands where they are.
  const filtered = normalise(filterCat);
  if (filtered) return filtered;

  return DEFAULT_CATEGORY;
}

/**
 * Which categories a grid may offer for an existing job.
 *
 * <p>The same lock, asked the other way round. Creating a row in the right
 * category is only half of keeping it there: the category is an editable cell,
 * and on a locked grid every option except the locked one makes the row vanish
 * from the screen that changed it. A dropdown like that is not a correction, it
 * is a trapdoor.</p>
 *
 * <p>On My Job it stays open. A job keyed under the wrong category is a real
 * thing and somebody has to be able to fix it — and a delivery job is reachable
 * there, under the DELIVERY tab, so locking the Chemours grid strands nothing.
 * It only stops the move being made by accident from a screen that shows one
 * kind of job.</p>
 *
 * <p>This is the list the paste path is judged against as well, so a pasted
 * "IMPORT" on a locked grid is refused and reported rather than quietly moving
 * the job.</p>
 */
export function categoriesOfferedOn(lockedCat: string | null | undefined): string[] {
  const locked = normalise(lockedCat);
  return locked ? [locked] : [...JOB_CATEGORIES];
}

/** A recognised category, or empty for "ALL", a blank, or anything unknown. */
function normalise(value: string | null | undefined): string {
  const text = String(value ?? "").trim().toUpperCase();
  if (!text || text === ANY_CATEGORY) return "";
  return (JOB_CATEGORIES as readonly string[]).includes(text) ? text : "";
}
