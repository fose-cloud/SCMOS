using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// What a shipping email is about, read out of its subject and body.
///
/// <para>
/// Deterministic and pure — no AI, no network, no database. The specification
/// puts classification in Phase 2 and it is right to: the identifiers below are
/// printed by the shipping lines' own systems and can be read exactly, and
/// anything read exactly should never be guessed at instead.
/// </para>
///
/// <para>
/// <b>What it does not do is decide.</b> It reports what it found and where it
/// found it; whether that is enough to attach an email to a job is the
/// matching step's question, against the confidence thresholds the plan fixes
/// — auto-link at 0.95, suggest from 0.70, unmatched below. Extraction that
/// also decided would have no way to say "I found a container number, but I am
/// not sure it is this job's", which is the answer most emails deserve.
/// </para>
/// </summary>
public static class EmailExtraction
{
    /// <summary>What kind of identifier a find is.</summary>
    public static class Kind
    {
        public const string JobCode = "JOB_CODE";
        public const string Container = "CONTAINER";
        public const string Booking = "BOOKING";
        public const string BillOfLading = "BL";
    }

    /// <summary>
    /// One identifier found in an email.
    /// </summary>
    /// <param name="Type">One of <see cref="Kind"/>.</param>
    /// <param name="Value">The identifier, as it will be matched — trimmed and upper-cased.</param>
    /// <param name="InSubject">
    /// Whether it came from the subject line. A subject is written by a person
    /// about this message; a body carries quoted history, signatures and the last
    /// five emails in the thread, so the same number found there is weaker
    /// evidence.
    /// </param>
    /// <param name="Labelled">Whether the text named it — "Container No: X" rather than a bare token.</param>
    /// <param name="WellFormed">
    /// Whether the value passes its own format's self-check. Only container
    /// numbers have one; everything else is true, because there is nothing to
    /// check against.
    /// </param>
    public record Found(string Type, string Value, bool InSubject, bool Labelled, bool WellFormed);

    /* ------------------------------------------------------------ patterns */

    /// <summary>
    /// What sits between a label and the number it names.
    ///
    /// "Container No: TEMU0404097" puts a separator after the word "No", not
    /// only after "Container". The first draft of these patterns consumed one or
    /// the other but never both, so a container named the commonest way of all
    /// was read as an unnamed one — which the checks caught. Written once and
    /// shared, because four patterns needing the same forgiveness is four
    /// chances to forgive differently.
    /// </summary>
    /// <remarks>
    /// The slash is in the separator class because the register writes bills of
    /// lading as "BL/260631000583" — the label and the number joined by one, with
    /// nothing else between them.
    /// </remarks>
    private const string Label = @"\s*(?:NO|NUMBER|REF|CODE)?\s*[.:#/\-]?\s*";

    /// <summary>
    /// A container number: three letters, an equipment category, seven digits.
    ///
    /// ISO 6346. The category letter is U for freight containers, J for
    /// detachable equipment and Z for trailers — not any letter, which would
    /// match four-letter words followed by a number.
    /// </summary>
    private static readonly Regex ContainerPattern =
        new(@"(?<![A-Z0-9])([A-Z]{3}[UJZ])[ -]?(\d{7})(?![A-Z0-9])",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A bill of lading, which the register writes as "B/L X" or "BL/X".
    ///
    /// Only ever read behind its label. The values themselves have no shape in
    /// common — SSLSGLCHCAE3579-05, 260631000583, A15GA02185, HKG6107181012 —
    /// so a pattern loose enough to catch them bare would catch everything else
    /// in the email too.
    /// </summary>
    private static readonly Regex BillOfLadingPattern =
        new(@"\bB\s*[/\\]?\s*L\b" + Label + @"([A-Z0-9][A-Z0-9\-/]{4,24})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// A booking number, also only ever read behind its label.
    ///
    /// The register's bookings are A15GX11628, BKKGG8920900, 4367142, 0427SGN,
    /// GTD1036798 and TI2LCHWSP261244. There is no shape there to match on: a
    /// pattern that accepted all of them would accept a postcode, a phone
    /// extension and half the words in a signature block.
    /// </summary>
    private static readonly Regex BookingPattern =
        new(@"\bBOOKING\b" + Label + @"([A-Z0-9][A-Z0-9\-/]{4,24})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A job number named as one, so it can be told from a bare twelve digits.</summary>
    private static readonly Regex LabelledJobPattern =
        new(@"\b(?:JOB|SCMOS)\b" + Label + @"(\d{12})(?!\d)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>A container named as one.</summary>
    private static readonly Regex LabelledContainerPattern =
        new(@"\b(?:CONTAINER|CNTR|CTNR|UNIT)\b" + Label + @"([A-Z]{3}[UJZ][ -]?\d{7})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /* ------------------------------------------------------------- reading */

    /// <summary>
    /// Everything the two pieces of text name, subject first.
    ///
    /// <para>
    /// Both are read, and each find remembers which it came from. A subject line
    /// is written about this message; a body is the thread — quoted replies,
    /// a signature, and whatever the last five emails carried. The same number
    /// means more in one than the other, and only the caller can weigh that, so
    /// this records where rather than dropping either.
    /// </para>
    ///
    /// <para>
    /// The same identifier found in both appears once, from the subject, because
    /// that is the stronger of the two.
    /// </para>
    /// </summary>
    public static List<Found> Read(string? subject, string? body)
    {
        var found = new List<Found>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (text, inSubject) in new[] { (subject, true), (body, false) })
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            var clean = Collapse(text);

            foreach (var one in ReadFrom(clean, inSubject))
            {
                // Type and value together: an email may legitimately carry the
                // same digits as both a booking and a bill of lading, and the
                // matcher weighs those differently.
                if (seen.Add($"{one.Type} {one.Value}")) found.Add(one);
            }
        }

        return found;
    }

    private static IEnumerable<Found> ReadFrom(string text, bool inSubject)
    {
        /* ---- job codes ---- */

        var labelledJobs = new HashSet<string>(
            LabelledJobPattern.Matches(text).Select(one => one.Groups[1].Value),
            StringComparer.Ordinal);

        foreach (var code in JobCodes.All(text))
        {
            yield return new(Kind.JobCode, code, inSubject, labelledJobs.Contains(code), true);
        }

        /* ---- containers ---- */

        var labelledContainers = new HashSet<string>(
            LabelledContainerPattern.Matches(text).Select(one => Tidy(one.Groups[1].Value)),
            StringComparer.Ordinal);

        foreach (Match match in ContainerPattern.Matches(text))
        {
            var value = (match.Groups[1].Value + match.Groups[2].Value).ToUpperInvariant();
            yield return new(Kind.Container, value, inSubject,
                labelledContainers.Contains(value), IsWellFormedContainer(value));
        }

        /* ---- the two that are only ever read behind a label ---- */

        foreach (Match match in BookingPattern.Matches(text))
        {
            var value = Tidy(match.Groups[1].Value);
            if (value.Length > 0) yield return new(Kind.Booking, value, inSubject, true, true);
        }

        foreach (Match match in BillOfLadingPattern.Matches(text))
        {
            var value = Tidy(match.Groups[1].Value);
            // "B/L NO" would otherwise read NO as the number.
            if (value.Length > 0 && value is not ("NO" or "NUMBER" or "REF"))
                yield return new(Kind.BillOfLading, value, inSubject, true, true);
        }
    }

    /// <summary>
    /// The ISO 6346 check digit, which says whether a container number is one.
    ///
    /// <para>
    /// Each of the first ten characters has a value — digits their own, letters
    /// 10 upward skipping every multiple of 11 — weighted by doubling powers,
    /// summed, taken modulo 11, and a result of 10 written as 0. Eleven digits
    /// that fail this are not a container number, they are eleven digits.
    /// </para>
    ///
    /// <para>
    /// <b>Checked, never used to refuse.</b> Of 1,471 distinct container numbers
    /// in the register 1,460 pass — 99% — and the eleven that fail are real
    /// containers on real jobs with a digit typed wrong. Rejecting on this would
    /// make those eleven unmatchable forever, so it is reported as a weaker find
    /// and the matcher decides.
    /// </para>
    /// </summary>
    public static bool IsWellFormedContainer(string? value)
    {
        if (value is null || value.Length != 11) return false;
        for (var i = 0; i < 4; i++) if (!char.IsAsciiLetterUpper(value[i])) return false;
        for (var i = 4; i < 11; i++) if (!char.IsAsciiDigit(value[i])) return false;

        var total = 0;
        for (var i = 0; i < 10; i++)
        {
            var c = value[i];
            var digit = char.IsAsciiDigit(c) ? c - '0' : LetterValue(c);
            total += digit << i;
        }
        return (total % 11) % 10 == value[10] - '0';
    }

    /// <summary>
    /// A=10, B=12, C=13 … Z=38: counting up from ten and stepping over every
    /// multiple of eleven, which is what makes the modulo below discriminate.
    /// </summary>
    private static int LetterValue(char letter)
    {
        var value = 10;
        for (var c = 'A'; ; c++)
        {
            if (value % 11 == 0) value++;
            if (c == letter) return value;
            value++;
        }
    }

    /// <summary>Runs of whitespace to one space, so a line break cannot hide a label.</summary>
    private static string Collapse(string text) =>
        Regex.Replace(text, @"\s+", " ").Trim();

    /// <summary>An identifier as it will be compared: trimmed of punctuation, upper case.</summary>
    private static string Tidy(string value) =>
        value.Trim().Trim('.', ',', ';', ':', '-', '/', ')', '(').Replace(" ", "").ToUpperInvariant();
}
