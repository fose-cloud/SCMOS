/**
 * The type every job table is set in, on screen and on the clipboard.
 *
 * <p>Angsana New at 18 point, which is the department's own document face — the
 * size looks large written down and is not: Angsana New is drawn small for its
 * point size, so 18pt sits at about the height a 12px Latin face does. It is the
 * size these tables are pasted into Word and Excel at, and picking anything else
 * on screen would mean the thing people copy does not look like the thing they
 * copied it from.</p>
 *
 * <p><b>One definition, used by both.</b> The grid styled itself and the
 * clipboard payload styled itself, in two files, with two different faces and
 * two different sizes — so a table pasted into a mail already looked nothing
 * like the table it came from. Everything that draws or copies a job table reads
 * these constants now, and the check asserts the copied HTML carries the same
 * two values the screen does.</p>
 *
 * <p>No imports on purpose: this is the one fact, and it should be readable
 * from either side without dragging a component behind it.</p>
 */

/**
 * The face, with the fallbacks Windows actually has.
 *
 * AngsanaUPC and Cordia New are the two other Thai faces on a Thai Windows
 * install; `serif` last so a machine with none of them still renders Thai rather
 * than falling back to something that cannot.
 */
export const TABLE_FONT_FAMILY = "'Angsana New','AngsanaUPC','Cordia New',serif";

/** Points, not pixels — this is a size that has to mean the same thing in Word. */
export const TABLE_FONT_SIZE_PT = 18;

/** What a table's own element is set to. */
export const TABLE_FONT_CSS = `font-family:${TABLE_FONT_FAMILY};font-size:${TABLE_FONT_SIZE_PT}pt`;

/**
 * The same, for the HTML put on the clipboard.
 *
 * Word and Excel read a point size and ignore a pixel one, which is the whole
 * reason the size is carried in points everywhere rather than converted here.
 */
export const TABLE_FONT_CLIPBOARD = TABLE_FONT_CSS;
