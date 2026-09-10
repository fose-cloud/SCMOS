namespace Scmos.Api.Rules;

/// <summary>
/// The paperwork a subcontractor has to hold, and whether they hold it.
///
/// <para>
/// Five documents, named by the department: two insurances, the transport
/// licence, the company affidavit, and the annex listing the trucks on the
/// contract. The register already stored supplier documents with an expiry, and
/// the notification service already counts the ones running out — what was
/// missing is that <b>nothing said which five were required</b>, so a supplier
/// holding four of them and a supplier holding none looked the same on screen:
/// a count of documents, with no sense of the gap.
/// </para>
///
/// <para>
/// This is the list. Everything else — the columns, the status, the alert —
/// reads it, so adding a sixth requirement is one entry here rather than an
/// edit in five places that will disagree by the second one.
/// </para>
/// </summary>
public static class SupplierCompliance
{
    /// <summary>
    /// How near an expiry has to be before it is worth saying so.
    ///
    /// Deferred to the notification rule rather than restated. The department
    /// asked for sixty days' warning and that number already existed in two
    /// places for the same purpose; a third copy is how a screen ends up
    /// showing amber for a document the alert says nothing about.
    /// </summary>
    public const int WarningDays = Notifications.ExpiryWarningDays;

    /// <summary>One of the documents a subcontractor is required to hold.</summary>
    /// <param name="Code">
    /// What the document's <c>Kind</c> is written as when it is uploaded. This
    /// is the join between a stored file and the column it belongs under, so it
    /// is a fixed vocabulary rather than whatever somebody typed.
    /// </param>
    /// <param name="English">The column heading, as the department wrote it.</param>
    /// <param name="Thai">The heading underneath, likewise.</param>
    /// <param name="Folder">
    /// Which tree in <see cref="BlobPaths.SupplierFolders"/> the file goes in.
    /// Two requirements share Insurance and two share Contract — the folder
    /// says where a file lives, the code says what it is, and they are not the
    /// same question.
    /// </param>
    /// <param name="Expires">
    /// Whether a missing expiry date is a fault. The truck annex is a list of
    /// vehicles rather than a certificate: it goes out of date when the fleet
    /// changes, not on a date anybody can write down in advance.
    /// </param>
    public sealed record Requirement(
        string Code, string English, string Thai, string Folder, bool Expires);

    public static readonly Requirement[] Required =
    [
        new("insurance-vehicle", "Insurance expire", "รถยนต์", "Insurance", true),
        new("insurance-cargo", "Insurance expire", "สินค้า", "Insurance", true),
        new("transport-licence", "Transport Licence", "ใบอนุญาตขนส่ง", "License", true),
        new("affidavit", "Affidavit Company", "หนังสือรับรองบริษัท", "Contract", true),
        new("truck-profile", "Truck Profile/Annex", "ทะเบียนรถในสัญญา", "Contract", false),
    ];

    /// <summary>The requirement a written kind means, or null when it is not one of the five.</summary>
    public static Requirement? Match(string? kind)
    {
        var text = (kind ?? "").Trim();
        if (text.Length == 0) return null;
        return Required.FirstOrDefault(one => one.Code.Equals(text, StringComparison.OrdinalIgnoreCase));
    }

    /* ------------------------------------------------------------- states */

    /// <summary>
    /// Where one requirement stands.
    ///
    /// Ordered by how much somebody should care, which is what lets a
    /// supplier's overall status be the worst of its five without a second
    /// table of precedence — see <see cref="Worst"/>.
    /// </summary>
    public static class State
    {
        /// <summary>Held, in date, and not close to running out.</summary>
        public const string Valid = "valid";

        /// <summary>Held, in date, and inside the warning window.</summary>
        public const string Expiring = "expiring";

        /// <summary>Nothing has been uploaded.</summary>
        public const string Missing = "missing";

        /// <summary>Held, and the date has passed.</summary>
        public const string Expired = "expired";

        /// <summary>Held, but nobody recorded when it runs out.</summary>
        public const string NoExpiry = "no-expiry";
    }

    /// <summary>
    /// Least to most urgent.
    ///
    /// <para>
    /// <b>Expired outranks missing</b>, which is not obvious and is deliberate.
    /// A missing document might never have been asked for; an expired one was
    /// held, was checked, and has run out — somebody was relying on it and is
    /// still relying on it. The one that was true and has stopped being true is
    /// the more dangerous of the two.
    /// </para>
    ///
    /// <para>
    /// "No expiry" sits above valid and below expiring: a certificate with no
    /// date recorded cannot be watched by anything, so it is a gap in the
    /// watching rather than a gap in the paperwork.
    /// </para>
    /// </summary>
    private static readonly string[] Severity =
        [State.Valid, State.NoExpiry, State.Expiring, State.Missing, State.Expired];

    /// <summary>Where one requirement stands, given what is held for it.</summary>
    /// <param name="held">Whether a document has been uploaded at all.</param>
    /// <param name="expiryDate">Its expiry as the register writes dates, DD/MM/YYYY.</param>
    /// <param name="today">Today as a comparable number — see <see cref="Formats.DateNumber"/>.</param>
    /// <param name="expires">Whether this requirement is one that carries a date.</param>
    public static string StateOf(bool held, string expiryDate, int today, bool expires = true)
    {
        if (!held) return State.Missing;
        if (!expires) return State.Valid;

        var due = Formats.DateNumber(expiryDate ?? "");
        if (due == 0) return State.NoExpiry;
        if (due < today) return State.Expired;
        return DaysBetween(today, due) <= WarningDays ? State.Expiring : State.Valid;
    }

    /// <summary>The state a supplier is in overall: the worst of its requirements.</summary>
    public static string Worst(IEnumerable<string> states)
    {
        var worst = State.Valid;
        foreach (var state in states)
            if (Array.IndexOf(Severity, state) > Array.IndexOf(Severity, worst)) worst = state;
        return worst;
    }

    /// <summary>
    /// How many days until an expiry, negative once it has passed.
    ///
    /// <para>
    /// Computed from the two date <i>numbers</i> the register already uses
    /// (YYYYMMDD as an int) by turning each back into a date, rather than
    /// subtracting them — 20260301 minus 20260228 is 73, not 1, and a column
    /// counting down to an expiry is exactly where that arithmetic would be
    /// believed.
    /// </para>
    /// </summary>
    public static int DaysBetween(int fromNumber, int toNumber)
    {
        var from = AsDate(fromNumber);
        var to = AsDate(toNumber);
        if (from is null || to is null) return 0;
        return to.Value.DayNumber - from.Value.DayNumber;
    }

    private static DateOnly? AsDate(int number)
    {
        if (number <= 0) return null;
        var year = number / 10000;
        var month = number / 100 % 100;
        var day = number % 100;
        if (year < 1 || month is < 1 or > 12 || day < 1) return null;
        return day > DateTime.DaysInMonth(year, month) ? null : new DateOnly(year, month, day);
    }

    /// <summary>Today, as the number the register's dates compare against.</summary>
    public static int Today() => Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));
}
