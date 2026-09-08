/**
 * Typing to narrow a list of choices.
 *
 * Three pickers needed this at once — 29 vehicle types on the quotation
 * screen, the carrier register beside it, and the rate sheet's quotation
 * numbers, which is 115 entries and grows by one every time somebody asks for
 * a price. All three were scrolling lists, and the report was that finding
 * anything in them is work.
 *
 * The matching rule lives here rather than in each of them. It is two lines,
 * which is exactly the size of rule this codebase keeps finding two copies of:
 * one picker matching on a prefix while another matches anywhere is the kind of
 * difference nobody reports as a bug, they just stop trusting the search.
 *
 * A leaf module — it imports nothing, so the rule can be checked on its own.
 */

/**
 * Whether an option should stay visible for what has been typed.
 *
 * Case-insensitive, and matches anywhere in the text rather than only at the
 * start: the vehicle list is written "40'/40'HQ NON-DG" and "10 W NON-DG", so
 * somebody looking for the 40-foot column types "40" and somebody after the
 * non-dangerous ones types "non". A prefix match would find neither.
 *
 * Spaces are stripped from both sides before comparing, so "10W" finds
 * "10 W NON-DG" — the sheet writes that vehicle both ways and nobody should
 * have to remember which.
 */
export function pickMatches(option: string, query: string): boolean {
  const wanted = squash(query);
  if (wanted.length === 0) return true;
  return squash(option).includes(wanted);
}

const squash = (text: string) => (text ?? "").replace(/\s+/g, "").toLowerCase();

/**
 * How many options justify a search box.
 *
 * Below this the box is worse than the list: another thing to look at, and a
 * place the keyboard can land where nothing needs typing. Eight is about where
 * a list stops being one glance — FCL/LCL/DOMESTIC never gets one, the vehicle
 * list always does.
 */
export const SEARCH_FROM = 8;
