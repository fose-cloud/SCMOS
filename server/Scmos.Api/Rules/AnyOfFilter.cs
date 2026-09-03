namespace Scmos.Api.Rules;

/// <summary>
/// The pickers' any-of value, and the period rule that reads three of them.
///
/// Every filter on My Job's bar carries several values at once, joined with a
/// pipe — "ALLNEX|BERICAP" means either. The workspace grew the decoding and
/// the period test inside its own service, and when the rate sheet was given
/// the same bar the choice was to write them a second time or to lift them out.
///
/// Written twice they would drift, which in this codebase is not a worry but a
/// matter of record: <c>tests/noDateFilter.test.mjs</c> exists because a screen
/// and an API had already disagreed about what "no date" meant. So the pickers
/// have one reading of their own value, and a date is judged to be inside a
/// period in one place.
///
/// The client's half of this is <c>app/scmos/period.ts</c> — two runtimes, so
/// two copies are unavoidable, and the tests hold them to the same answers.
/// </summary>
public static class AnyOfFilter
{
    /// <summary>
    /// The year picker's value for "no usable date".
    ///
    /// The same word <c>NO_DATE</c> in app/scmos/period.ts uses. A row whose
    /// date will not parse is the one somebody has to go and fix, and every
    /// other choice on the bar hides it — so it is a choice of its own.
    /// </summary>
    public const string NoDate = "NONE";

    /// <summary>Words the older single-choice controls use for "no filter".</summary>
    public static bool NotSet(string? value) =>
        string.IsNullOrEmpty(value)
        || value.Equals("ALL", StringComparison.OrdinalIgnoreCase)
        || value.Equals("All Team", StringComparison.OrdinalIgnoreCase);

    /// <summary>The values a picker is carrying, or none when it is not set.</summary>
    public static string[] Wanted(string? value) =>
        NotSet(value)
            ? []
            : value!.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>An any-of filter, where an unset value means no filter.</summary>
    public static bool IsAny(string value, string? wanted)
    {
        var list = Wanted(wanted);
        return list.Length == 0
            || list.Any(one => string.Equals(value, one, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// An any-of filter over a field that holds a list.
    ///
    /// The rate sheet's Subcon column is "SANGJA,SSL,PHURADA" — 332 of the
    /// first 400 rows name more than one — so equality would match almost
    /// nothing and a bare Contains would let SSL match SSLOGISTICS. The field
    /// is split the way it was written and the parts compared whole.
    /// </summary>
    public static bool IsAnyOfList(string value, string? wanted, char separator = ',')
    {
        var list = Wanted(wanted);
        if (list.Length == 0) return true;
        var parts = value.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Any(part => list.Any(one => string.Equals(part, one, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Whether a <c>dd/MM/yyyy</c> date falls in the chosen period.
    ///
    /// A row with no usable date is out as soon as any part of the period is
    /// chosen — it cannot be shown to fall inside a month nobody can place it
    /// in — unless "no date" is one of the years asked for. An any-of picker may
    /// carry that beside a real year, so an undated row matches that choice
    /// while a dated row still gets its chance at one of the real ones.
    /// </summary>
    public static bool InPeriod(string? date, string? year, string? month, string? day)
    {
        var years = Wanted(year);
        var wantsPeriod = years.Length > 0 || Wanted(month).Length > 0 || Wanted(day).Length > 0;
        var text = date ?? "";
        var parts = text.Split('/');

        if (parts.Length != 3)
            return !wantsPeriod || years.Contains(NoDate, StringComparer.OrdinalIgnoreCase);

        if (!wantsPeriod) return true;

        var yearMatches = years.Length == 0
            || years.Any(one => !one.Equals(NoDate, StringComparison.OrdinalIgnoreCase)
                && string.Equals(parts[2], one, StringComparison.OrdinalIgnoreCase));

        // The day picker carries the whole date ("15/07/2026"), which is what
        // makes two days of different months separable choices.
        return yearMatches && IsAny(parts[1], month) && IsAny(text, day);
    }
}
