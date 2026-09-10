namespace Scmos.Api.Rules;

/// <summary>
/// Reading the company's ASL/BSL list into the supplier register.
///
/// <para>
/// The list is the approved-supplier register kept in ABS, and it covers every
/// service provider the company buys from — 642 of them, of which only about
/// eighty carry anything on a truck. The rest are freight agents, liners,
/// customs brokers and container depots. They belong in the register, and they
/// must not turn up wherever the register is being asked "who could run this
/// job" or "how did our carriers score" — see <see cref="LooksLikeCarrier"/>,
/// which is the whole reason this file exists rather than the import being a
/// loop that copies columns.
/// </para>
///
/// <para>
/// Everything here is decided from the spreadsheet's own text, so it can be
/// checked without a database and without the file.
/// </para>
/// </summary>
public static class SupplierRegister
{
    /// <summary>
    /// A company name reduced to letters and digits, so "A.C.N" and "ACN" are
    /// one key.
    ///
    /// <para>
    /// This is how the seeder already reconciles TATIYAPOL, TTP and TATIYAPON
    /// against the register, and the ASL/BSL import has to match the same way
    /// or it would create a second row for a carrier that is already there
    /// under a different punctuation. Written once, here, because every rule
    /// this codebase has written twice has silently come to disagree.
    /// </para>
    /// </summary>
    public static string Key(string name) =>
        new((name ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// A cell, with the spreadsheet's placeholders read as empty.
    ///
    /// <para>
    /// The file writes "-" and "N/A" where somebody had nothing to put, and it
    /// carries leading spaces on pasted web addresses and phone numbers. Stored
    /// as they are, those become 300 fax numbers of "-" and a column that
    /// cannot be sorted or searched. An absent value should look absent.
    /// </para>
    /// </summary>
    public static string Tidy(object? cell)
    {
        var text = (cell?.ToString() ?? "").Trim();
        if (text.Length == 0) return "";

        // Collapse the runs of spaces and non-breaking spaces that come out of
        // a spreadsheet, so two entries of the same thing compare equal.
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        return Placeholder(text) ? "" : text;
    }

    /// <summary>Whether a cell is somebody writing "there is nothing here".</summary>
    public static bool Placeholder(string text) =>
        text is "-" or "--" or "." or "_"
        || text.Equals("n/a", StringComparison.OrdinalIgnoreCase)
        || text.Equals("na", StringComparison.OrdinalIgnoreCase)
        || text.Equals("none", StringComparison.OrdinalIgnoreCase)
        || text.Equals("null", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The ABS number, as a number written as text.
    ///
    /// <para>
    /// It arrives from the spreadsheet three ways: as text with spaces around
    /// it, as a number, and as a number Excel has stored as a double, which
    /// <c>ToString</c> renders as "331817" or, for the unlucky ones, as
    /// "331817.0". All three are the same supplier, and a register where the
    /// same company appears under both spellings is a register nobody can join
    /// to ABS.
    /// </para>
    /// </summary>
    public static string AbsNo(object? cell)
    {
        var text = Tidy(cell);
        if (text.Length == 0) return "";
        if (double.TryParse(text, out var number) && number == Math.Floor(number) && Math.Abs(number) < 1e15)
            return ((long)number).ToString();
        return text;
    }

    /* --------------------------------------------------------- which list */

    public const string Asl = "ASL";
    public const string Bsl = "BSL";

    /// <summary>
    /// Which of the two lists this row is on, or empty for neither.
    ///
    /// Only the two the file actually uses. A third value would be a change to
    /// how the company classifies its suppliers, and quietly accepting one here
    /// would put a value in the column that no screen knows how to filter on.
    /// </summary>
    public static string ListOf(object? cell)
    {
        var text = Tidy(cell).ToUpperInvariant();
        return text is Asl or Bsl ? text : "";
    }

    /* --------------------------------------------------- the same company */

    /// <summary>
    /// The shortest spelling that may be matched to a longer one.
    ///
    /// <para>
    /// Three characters. Below that "SJ" would reach for anything beginning SJ
    /// and the uniqueness test below is the only thing standing between it and
    /// the wrong company — which is a guard worth having, not one worth
    /// relying on alone.
    /// </para>
    /// </summary>
    public const int ShortestPrefix = 3;

    /// <summary>
    /// Whether a short trading name could be this long legal name at all.
    ///
    /// <para>
    /// The register spells a carrier the way the plan spells it — 9ISARA,
    /// KANIN, NEXTGEN — and procurement's list spells it the way the company is
    /// registered: "9 Isara Transport Co., Ltd.". Matched on the letters alone
    /// the two never meet, and the import would put a second row in the register
    /// for a carrier that already has one, splitting its jobs, its rate cards
    /// and its score in half.
    /// </para>
    ///
    /// <para>
    /// This is only the first of three tests, and the weakest. The caller must
    /// also find that exactly one company in the list fits, and that
    /// <see cref="EndsOnAWord"/> agrees.
    /// </para>
    /// </summary>
    public static bool CouldBeTheSame(string shortName, string longName)
    {
        var shortKey = Key(shortName);
        var longKey = Key(longName);
        return shortKey.Length >= ShortestPrefix
            && longKey.Length > shortKey.Length
            && longKey.StartsWith(shortKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the short name stops where one of the long name's words stops.
    ///
    /// <para>
    /// This is the test that separates a real match from a coincidence, and it
    /// was added because uniqueness alone was not enough. Run over the real
    /// file, prefix-and-unique matched 21 carriers, of which 19 were plainly
    /// right and one was plainly wrong: <b>W.A.K</b> matched <b>Wako Logistics
    /// (Thailand) Co., Ltd.</b>, because "WAK" begins the word "Wako". An
    /// initialism that happens to start somebody else's name is exactly the
    /// wrong merge to make silently — it would pay one company's invoices
    /// against another's scorecard.
    /// </para>
    ///
    /// <para>
    /// KANIN stops where "Kanin" stops; 9ISARA stops where "9 Isara" stops;
    /// A.C.N stops where "A C N" stops. WAK stops in the middle of "Wako", and
    /// THAIKOT in the middle of "Thaikotchasarn" — which is very probably the
    /// same company, and is still not something to decide from a spreadsheet.
    /// Both are reported for a person instead.
    /// </para>
    /// </summary>
    public static bool EndsOnAWord(string shortName, string longName)
    {
        var shortKey = Key(shortName);
        if (shortKey.Length == 0) return false;

        var built = new System.Text.StringBuilder(shortKey.Length);
        foreach (var word in Words(longName))
        {
            built.Append(word);
            if (built.Length == shortKey.Length)
                return string.CompareOrdinal(built.ToString(), shortKey) == 0;
            if (built.Length > shortKey.Length) return false;
        }
        return false;
    }

    /// <summary>
    /// A name's words, letters and digits only.
    ///
    /// "9 Isara" is two words and "A.C.N" is three, so a short name that spans
    /// several of them still lands on a boundary — which is the point, because
    /// that is exactly how these names are written.
    /// </summary>
    private static IEnumerable<string> Words(string name)
    {
        var word = new System.Text.StringBuilder();
        foreach (var character in (name ?? "").ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character)) { word.Append(character); continue; }
            if (word.Length > 0) { yield return word.ToString(); word.Clear(); }
        }
        if (word.Length > 0) yield return word.ToString();
    }

    /* ------------------------------------------------------ who is a truck */

    /// <summary>
    /// The words in a Type of Service that mean this company moves cargo by
    /// road for us.
    ///
    /// <para>
    /// Matched as substrings because the column has 115 distinct spellings for
    /// what is really about twenty things — "Sea Freight Agents/Liner" and "Sea
    /// Freight Agents / Liner" are the same answer typed twice — and a list of
    /// exact values would go stale the first time somebody added a space.
    /// </para>
    /// </summary>
    private static readonly string[] CarrierWords =
        ["transport", "trucking", "haulage", "iso tank", "tank container"];

    /// <summary>
    /// Whether a row from the list is a road carrier.
    ///
    /// <para>
    /// This is the guard on everything the register feeds. The carrier
    /// scorecard, the annual evaluation and the list of who a job may be given
    /// to are all questions about companies that drive; answering them from a
    /// register that now also holds 560 customs brokers would put a column of
    /// unscored agents in front of the department and make the scorecard look
    /// broken.
    /// </para>
    ///
    /// <para>
    /// <b>It is never the only test.</b> A company already carrying work in the
    /// register is a carrier whatever the spreadsheet calls it — the register is
    /// the evidence and the list is somebody's description. The importer takes
    /// the union of the two, which is why this answers only the description
    /// half.
    /// </para>
    /// </summary>
    public static bool LooksLikeCarrier(string typeOfService)
    {
        var text = (typeOfService ?? "").ToLowerInvariant();
        return text.Length > 0 && CarrierWords.Any(word => text.Contains(word, StringComparison.Ordinal));
    }

    /* ------------------------------------------------------- what to store */

    /// <summary>
    /// Whether a row can be stored at all.
    ///
    /// A row with no company name is not a supplier; it is a blank line at the
    /// bottom of a sheet, and the importer must skip it rather than create a
    /// supplier called "" that can never be matched or deleted.
    /// </summary>
    public static bool Usable(string supplierName) => Key(supplierName).Length > 0;

    /// <summary>
    /// The status a newly imported supplier is given.
    ///
    /// <para>
    /// On the company's approved list is not the same as approved to be given
    /// work by this department: the ASL/BSL list says procurement accepted
    /// them, and SCMOS's own approval is about insurance, licences and an audit
    /// that this import has no knowledge of. So an imported company that has
    /// never carried anything starts as <c>pending-audit</c> — visible, usable
    /// as a record, and not silently promoted into the pool a job can be handed
    /// to.
    /// </para>
    ///
    /// <para>
    /// One already carrying work in the register is left alone entirely. It was
    /// approved because it has been doing the work, and demoting it on the
    /// strength of a spreadsheet would stop jobs being assignable to companies
    /// that are, in fact, driving them today.
    /// </para>
    /// </summary>
    public const string NewStatus = "pending-audit";

    /// <summary>Where an imported spelling says it came from, in the alias table.</summary>
    public const string AliasSource = "asl-bsl";
}
