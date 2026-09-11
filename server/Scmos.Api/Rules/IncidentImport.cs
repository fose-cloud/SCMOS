namespace Scmos.Api.Rules;

/// <summary>
/// Reading a sheet of CAR/PAR cases into the register.
///
/// <para>
/// Unlike the ASL/BSL import, this was written without the file it will be
/// given. So it matches on <b>column headings</b> rather than positions, takes
/// Thai or English for each, and accepts several spellings of the ones the
/// team is most likely to have written — because the alternative is an
/// importer that only reads a sheet somebody guessed the shape of.
/// </para>
///
/// <para>
/// Everything it decides is decided from the sheet's own text, so it is
/// checkable without the file and without a database.
/// </para>
/// </summary>
public static class IncidentImport
{
    /// <summary>
    /// A heading reduced to letters and digits, lower-cased.
    ///
    /// "Job No.", "job_no" and "JOB NO" are one column.
    ///
    /// Thai consonants survive; its vowel and tone marks do not, because they
    /// are combining marks and <c>IsLetterOrDigit</c> is false for them — so
    /// "หัวข้อ" reduces to "หวขอ". That is fine as long as <b>both</b> sides go
    /// through here, which is what <see cref="ColumnFor"/> now does and did not
    /// at first.
    /// </summary>
    public static string Heading(string? raw) =>
        new((raw ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>
    /// What each field may be called, most specific first.
    ///
    /// Several spellings each, because this is reading somebody's spreadsheet
    /// rather than a format anybody agreed. A heading absent from the sheet
    /// leaves its field empty rather than failing the import: a case with a
    /// title and no "who" is still a case worth having, and refusing the whole
    /// file over one missing column is an import nobody can run.
    /// </summary>
    public static readonly Dictionary<string, string[]> Fields = new(StringComparer.Ordinal)
    {
        ["title"] = ["title", "หัวข้อ", "หัวข้อเคส", "เรื่อง", "subject", "case", "รายละเอียด", "detail"],
        ["jobKey"] = ["jobkey", "job", "jobno", "เลขงาน", "งาน", "absno", "abs", "jobcode", "เลขที่งาน"],
        ["kind"] = ["kind", "type", "carpar", "ประเภท", "ชนิด"],
        ["reference"] = ["CAR/PAR No.", "reference"],
        ["status"] = ["Status", "สถานะ"],
        ["category"] = ["Analysis by Problem Categories 1", "category", "หมวด", "หมวดหมู่", "ประเภทปัญหา"],
        ["what"] = ["what", "อะไร", "สิ่งที่เกิดขึ้น", "เหตุการณ์", "description"],
        ["where"] = ["where", "ที่ไหน", "สถานที่", "location"],
        ["when"] = ["when", "เมื่อไหร่", "เมื่อไร", "วันที่", "date", "วันเวลา"],
        ["who"] = ["who", "ใคร", "ผู้เกี่ยวข้อง", "ผู้รับผิดชอบ", "owner", "reporter"],
    };

    /// <summary>
    /// Which sheet column, if any, holds a field.
    ///
    /// <para>
    /// <b>Both sides are normalised.</b> The names above are written the way a
    /// person writes them — "หัวข้อ", "เลขงาน" — and <see cref="Heading"/>
    /// drops everything that is not a letter or a digit. Thai vowel and tone
    /// marks are combining marks, not letters, so <c>IsLetterOrDigit</c> is
    /// false for them and "หัวข้อ" becomes "หวขอ". Comparing a normalised
    /// heading against an unnormalised name matched nothing at all, and a sheet
    /// with perfectly good Thai headings was refused as unreadable.
    /// </para>
    /// </summary>
    public static int? ColumnFor(string field, IReadOnlyDictionary<string, int> headings)
    {
        if (!Fields.TryGetValue(field, out var names)) return null;
        foreach (var name in names)
        {
            var key = Heading(name);
            if (key.Length > 0 && headings.TryGetValue(key, out var column)) return column;
        }
        return null;
    }

    /// <summary>A cell, with the placeholders a spreadsheet uses for "nothing" read as empty.</summary>
    public static string Tidy(string? cell)
    {
        var text = (cell ?? "").Trim();
        if (text.Length == 0) return "";
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text is "-" or "--" or "." || text.Equals("n/a", StringComparison.OrdinalIgnoreCase)
            || text.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : text;
    }

    /* ------------------------------------------------------------- values */

    public const string Car = "CAR";
    public const string Par = "PAR";

    public static string StageOf(string status) => Tidy(status).ToLowerInvariant() switch
    {
        "closed" => "closed",
        "request evidence" => "follow-up",
        "waiting response" => "open",
        _ => throw new InvalidOperationException($"ไม่รู้จักสถานะ '{status}' — กรุณาตรวจไฟล์ก่อนนำเข้า")
    };

    public static string ReferenceKey(string reference) => Heading(reference);

    /// <summary>
    /// CAR or PAR, defaulting to CAR.
    ///
    /// CAR is corrective — something went wrong. PAR is preventive. A sheet
    /// that says neither is far more likely to be a log of things that went
    /// wrong than a log of things somebody prevented, so an unreadable value
    /// becomes CAR rather than being refused.
    /// </summary>
    public static string KindOf(string? cell)
    {
        var text = Tidy(cell).ToUpperInvariant();
        if (text.Contains("PAR", StringComparison.Ordinal)) return Par;
        if (text.Contains("ป้องกัน", StringComparison.Ordinal)) return Par;
        return Car;
    }

    /// <summary>
    /// The category vocabulary, in the same words the service stores.
    ///
    /// Matched on substrings in either language so "อุบัติเหตุรถชน" and
    /// "Traffic accident" both land on accident. Anything unrecognised becomes
    /// <c>other</c>, which is what the service would have done anyway — said
    /// here so the preview can show it before it happens.
    /// </summary>
    private static readonly (string Code, string[] Words)[] CategoryWords =
    [
        ("accident", ["accident", "crash", "อุบัติเหตุ", "ชน"]),
        ("damage", ["damage", "broken", "เสียหาย", "ชำรุด", "แตก"]),
        ("delay", ["delay", "late", "ล่าช้า", "สาย"]),
        ("safety", ["safety", "ความปลอดภัย", "ปลอดภัย"]),
        ("quality", ["quality", "คุณภาพ"]),
    ];

    public static string CategoryOf(string? cell)
    {
        var text = Tidy(cell).ToLowerInvariant();
        if (text.Length == 0) return "other";
        foreach (var (code, words) in CategoryWords)
            if (words.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))) return code;
        return "other";
    }

    /// <summary>
    /// Whether a row is a case at all.
    ///
    /// A title is the one thing a case cannot do without — it is what the list
    /// shows and what somebody recognises it by, and the service refuses a
    /// blank one. Rows without it are counted and skipped rather than turned
    /// into cases called "".
    /// </summary>
    public static bool Usable(string title) => Tidy(title).Length > 0;

    /// <summary>
    /// How long a title may be before it is cut.
    ///
    /// Some of these sheets put a whole paragraph in the title column. The
    /// column stores 300; anything longer is cut here rather than at
    /// SaveChanges, after the import has done its work.
    /// </summary>
    public const int TitleLength = 300;

    public static string Title(string? cell)
    {
        var text = Tidy(cell);
        return text.Length <= TitleLength ? text : text[..TitleLength];
    }
}
