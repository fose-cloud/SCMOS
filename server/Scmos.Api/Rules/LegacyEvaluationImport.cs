using System.Globalization;
using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// The evaluations done before SCMOS, read as they were written (2 Oct 2026, Annual Evaluation Phase 11). Pure.
///
/// <para>
/// Two workbooks, both the department's: the year's results — one row per evaluator, each carrier's first row carrying
/// its final percentage and its result — and the year's ISO / Q-Mark table. Only what they say is kept: a carrier, its
/// final percentage and the result word for the results; held or not, per certificate, for the other. No KPI detail is
/// reconstructed, and no pass mark is read off a column of "PASS".
/// </para>
///
/// <para>
/// A row names its carrier as ABS writes it — "A C N TRANSPORT, CHONBURI (#188052)" — so the number in brackets is the
/// ABS number and the join to the Supplier Register; the name is matched only where the number cannot be, and never to a
/// register row that carries a different number (another branch).
/// </para>
/// </summary>
public static partial class LegacyEvaluationImport
{
    public const string Results = "results";
    public const string Certificates = "certificates";

    public sealed record ResultRow(int Row, string Name, string Number, decimal? FinalPercent, string Result, int Evaluators,
        IReadOnlyList<string> Problems);

    public sealed record CertificateRow(int Row, string Name, string Number, IReadOnlyDictionary<string, bool?> Held, IReadOnlyList<string> Problems);

    /// <summary>A register row as a match is made against it: its ABS number and the keys of every name it goes by.</summary>
    public sealed record Candidate(int Id, string AbsNo, IReadOnlyList<string> Keys);

    /// <param name="How">abs — by the ABS number · name — by the company's name or one of its aliases.</param>
    public sealed record Match(int SupplierId, string How);

    /// <summary>The headings, as the comparison reads them: lower case, letters and digits only.</summary>
    public static string Heading(string text) => new((text ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Which workbook a heading row is — results, certificates — or null for neither.</summary>
    public static string? KindOf(IReadOnlyList<string> header)
    {
        var headings = header.Select(Heading).ToList();
        if (headings.Contains("percentage") && headings.Contains("result")) return Results;
        if (headings.Count(heading => SupplierCertificates.TypeOfHeading(heading) is not null) > 0 && NameColumn(headings) >= 0) return Certificates;
        return null;
    }

    /// <summary>"A C N TRANSPORT, CHONBURI (#188052)" → ("A C N TRANSPORT, CHONBURI", "188052").</summary>
    public static (string Name, string Number) Split(string cell)
    {
        var text = (cell ?? "").Trim();
        var found = AbsNumber().Match(text);
        return found.Success ? (text.Remove(found.Index, found.Length).Trim().TrimEnd(',').Trim(), found.Groups[1].Value) : (text, "");
    }

    /// <summary>
    /// The results: a carrier's block begins at the row that carries a percentage or a result; the rows after it under the
    /// same name are its other evaluators.
    /// </summary>
    public static IReadOnlyList<ResultRow> ReadResults(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0) return [];
        var headings = rows[0].Select(Heading).ToList();
        int nameAt = NameColumn(headings), finalAt = headings.IndexOf("percentage"), resultAt = headings.IndexOf("result");
        if (nameAt < 0 || finalAt < 0 || resultAt < 0) return [];
        var found = new List<(int Row, string Name, string Number, string Key, string Final, string Result, int Evaluators)>();
        for (var index = 1; index < rows.Count; index++)
        {
            var name = Cell(rows[index], nameAt);
            if (name.Length == 0) continue;
            var (company, number) = Split(name);
            var key = SupplierRegister.Key(company) + "#" + number;
            string final = Cell(rows[index], finalAt), result = Cell(rows[index], resultAt);
            if (final.Length > 0 || result.Length > 0) found.Add((index + 1, company, number, key, final, result, 1));
            else if (found.Count > 0 && found[^1].Key == key) found[^1] = found[^1] with { Evaluators = found[^1].Evaluators + 1 };
        }
        return found.Select(one =>
        {
            var problems = new List<string>();
            decimal? final = decimal.TryParse(one.Final, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? Math.Round(value, 4, MidpointRounding.AwayFromZero) : null;
            if (final is null) problems.Add("ไม่มีเปอร์เซ็นต์รวม");
            else if (final is < 0 or > 100) problems.Add($"เปอร์เซ็นต์รวม {one.Final} อยู่นอก 0–100");
            if (one.Result.Length == 0) problems.Add("ไม่มีผลประเมิน");
            if (found.Count(other => other.Key == one.Key) > 1) problems.Add("ผู้ขนส่งรายนี้มีผลรวมมากกว่าหนึ่งแถว");
            return new ResultRow(one.Row, one.Name, one.Number, final, one.Result.Length > 60 ? one.Result[..60] : one.Result, one.Evaluators, problems);
        }).ToList();
    }

    /// <summary>The certificates table: one row a carrier, a column a certificate.</summary>
    public static IReadOnlyList<CertificateRow> ReadCertificates(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0) return [];
        var headings = rows[0].Select(Heading).ToList();
        var nameAt = NameColumn(headings);
        var columns = headings.Select((heading, at) => (Type: SupplierCertificates.TypeOfHeading(heading), At: at)).Where(one => one.Type is not null).ToList();
        if (nameAt < 0 || columns.Count == 0) return [];
        var found = new List<CertificateRow>();
        for (var index = 1; index < rows.Count; index++)
        {
            var name = Cell(rows[index], nameAt);
            if (name.Length == 0) continue;
            var (company, number) = Split(name);
            var held = columns.ToDictionary(one => one.Type!, one => SupplierCertificates.HeldOf(Cell(rows[index], one.At)));
            var problems = columns.Where(one => held[one.Type!] is null)
                .Select(one => $"{SupplierCertificates.LabelOf(one.Type!)}: อ่านค่า \"{Cell(rows[index], one.At)}\" ไม่ออก").ToList();
            found.Add(new CertificateRow(index + 1, company, number, held, problems));
        }
        return found;
    }

    /// <summary>
    /// The register row a carrier is: by its ABS number; else by its name — whole, or before the first comma — against the
    /// register's names and aliases, never to a row that has a different ABS number. Null when nothing, or more than one
    /// row, fits.
    /// </summary>
    public static Match? MatchOf(string name, string number, IReadOnlyList<Candidate> candidates)
    {
        if (number.Length > 0)
        {
            var byNumber = candidates.Where(one => one.AbsNo == number).ToList();
            if (byNumber.Count == 1) return new Match(byNumber[0].Id, "abs");
        }
        var keys = new[] { name, name.Split(',')[0] }.Select(SupplierRegister.Key).Where(key => key.Length >= 3).Distinct().ToList();
        var byName = candidates.Where(one => (one.AbsNo.Length == 0 || one.AbsNo == number) && one.Keys.Any(keys.Contains))
            .Select(one => one.Id).Distinct().ToList();
        return byName.Count == 1 ? new Match(byName[0], "name") : null;
    }

    private static int NameColumn(IReadOnlyList<string> headings) =>
        headings.Select((heading, at) => (heading, at)).Where(one => one.heading is "name" || one.heading.Contains("company"))
            .Select(one => one.at).DefaultIfEmpty(-1).First();

    private static string Cell(IReadOnlyList<string> row, int at) => at < row.Count ? (row[at] ?? "").Trim() : "";

    [GeneratedRegex(@"\(\s*#\s*(\d+)\s*\)")]
    private static partial Regex AbsNumber();
}
