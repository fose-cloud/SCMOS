namespace Scmos.Api.Rules;

/// <summary>What one dropdown column is proposed to become, and by which rule.</summary>
public sealed record Correction(string Field, string From, string To, string Rule, string Reason);

/// <summary>
/// The lists the dropdown columns are judged against — the same ones the
/// grid offers, read once per run and handed in, so this file never touches
/// a database and the checks can hand it a list of three.
/// </summary>
/// <param name="TypeCodes">The vehicle-type codes on offer (the rule's own and the ones added since), as spelled.</param>
/// <param name="Customers">The Job Rotation's customer names, as spelled.</param>
/// <param name="CarrierOf">The company a haulier spelling means, or null when the register has never seen it.</param>
public sealed record CorrectionLists(IReadOnlyList<string> TypeCodes, IReadOnlyList<string> Customers, Func<string, string?> CarrierOf);

/// <summary>
/// The rules that propose a dropdown cell's list spelling (22 Sep 2026).
///
/// <para>
/// Five columns, five rules, and the same shape to each: a value already on
/// the list — spelled as the list spells it — proposes nothing; a value the
/// list knows under another spelling proposes the list's; a value the list
/// does not know proposes nothing and is left for a person. Nothing here
/// guesses. "LOTUS" and "LOTUS ASIA" are two customers; "1X20 DG >> 1X40 DG"
/// is a note somebody typed into the type column; both come back unchanged
/// and are counted in the report's "left alone" list, which is what that
/// list is for.
/// </para>
///
/// <para>
/// Every proposal is one cell, one job, one owner's decision. The rules do
/// not know about jobs, owners or approval — <see cref="Data.CorrectionProposer"/>
/// runs them over the register and queues what they return;
/// <see cref="Services.CorrectionService"/> writes a cell only when the owner
/// approves and the cell still says what the proposal was made against.
/// </para>
/// </summary>
public static class CorrectionRules
{
    /// <summary>The dropdown columns, in the order the grid shows them.</summary>
    public static readonly string[] Fields = ["cat", "customer", "trucker", "type", "status"];

    /// <summary>The cells a customer proposal may read as evidence of the site — never proposed themselves.</summary>
    public static readonly string[] Evidence = ["destination", "plant"];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cat"] = "หมวด", ["customer"] = "ลูกค้า", ["trucker"] = "ผู้ขนส่ง", ["type"] = "ประเภทรถ/ตู้", ["status"] = "สถานะ",
    };

    private static readonly string[] Categories = ["IMPORT", "EXPORT", "DELIVERY"];

    /// <summary>A non-breaking space, U+00A0 — three rows of the register carry one where a space belongs (see PlaceMerge).</summary>
    private static readonly string Nbsp = ((char)0xA0).ToString();

    /// <summary>
    /// The proposals for one job's dropdown cells: none for a cell that is
    /// blank, on its list, or unknown to its list.
    /// </summary>
    public static IReadOnlyList<Correction> Propose(string category, IReadOnlyDictionary<string, string> cells, CorrectionLists lists)
    {
        var found = new List<Correction>();
        foreach (var field in Fields)
        {
            var value = cells.TryGetValue(field, out var raw) ? raw ?? "" : "";
            if (Formats.Clean(value).Length == 0) continue;
            var one = field switch
            {
                "cat" => Category(value),
                "customer" => Customer(value, lists.Customers, string.Join(' ', Evidence.Select(name => cells.TryGetValue(name, out var text) ? text ?? "" : ""))),
                "trucker" => Carrier(value, lists.CarrierOf),
                "type" => VehicleType(value, lists.TypeCodes),
                "status" => Status(value, category),
                _ => null,
            };
            if (one is not null) found.Add(one);
        }
        return found;
    }

    /// <summary>IMPORT, EXPORT or DELIVERY as spelled, from any case or padding; another word proposes nothing.</summary>
    public static Correction? Category(string value)
    {
        var wanted = Squash(value).ToUpperInvariant();
        var match = Categories.FirstOrDefault(c => c == wanted);
        return match is null || match == value ? null : new("cat", value, match, "cat.case", "หมวดสะกดตามระบบ");
    }

    /// <summary>
    /// The Job Rotation's spelling of the same customer, three ways, each
    /// only when it is the one answer:
    /// <list type="number">
    /// <item>the same letters — case, spacing, punctuation and a trailing full stop aside: TOA BANGNA is TOA (Bangna), U.C is U.C.;</item>
    /// <item>the one rotation name that begins with the job's words: TERRATEC is TERRATEC MACHINERY when no other name starts with TERRATEC;</item>
    /// <item>among several that begin so — the rotation names a customer's sites, DANA (FREE ZONE) · DANA (LKB) · DANA (RAYONG) — the one whose
    /// site the job's own destination or plant names: DANA delivered to XPO-RAYONG is DANA (RAYONG).</item>
    /// </list>
    /// A name on the list as spelled proposes nothing, so LOTUS stays LOTUS beside LOTUS ASIA. Several sites and no evidence,
    /// or a name the rotation has never heard of, is left for a person — the report says which and how many.
    /// </summary>
    public static Correction? Customer(string value, IReadOnlyList<string> customers, string evidence = "")
    {
        var key = CustomerKey(value);
        if (key.Length == 0) return null;
        var exact = customers.FirstOrDefault(name => CustomerKey(name) == key);
        if (exact is not null) return exact == value ? null : new("customer", value, exact, "customer.rotation", "ชื่อลูกค้าตาม Job Rotation");

        var words = Tokens(value);
        if (words.Count == 0) return null;
        var sites = customers.Where(name => Tokens(name) is var its && its.Count > words.Count && its.Take(words.Count).SequenceEqual(words)).ToList();
        if (sites.Count == 1)
            return new("customer", value, sites[0], "customer.rotation", $"ชื่อเดียวใน Job Rotation ที่ขึ้นต้นด้วย {Squash(value)}");
        if (sites.Count == 0) return null;

        // Several sites: the job's own destination or plant has to name one of them, whole.
        var seen = Tokens(evidence).ToHashSet(StringComparer.Ordinal);
        var named = sites.Select(name => (Name: name, Site: Tokens(name).Skip(words.Count).ToList()))
            .Where(one => one.Site.All(seen.Contains)).OrderByDescending(one => one.Site.Count).ToList();
        if (named.Count == 0) return null;
        // BANGPOO and BANGPOO BKK both named: the site that says more, when it says everything the others say, is the one meant.
        var best = named[0];
        if (named.Count > 1 && (named[1].Site.Count == best.Site.Count || named.Skip(1).Any(other => !other.Site.All(best.Site.Contains)))) return null;
        return new("customer", value, best.Name, "customer.rotation.site", $"ปลายทาง/โรงงานของงานระบุ {string.Join(' ', best.Site)} — ใน Job Rotation คือ {best.Name}");
    }

    /// <summary>How many rotation names a bare customer name could mean — for the report's "left alone" list to say "หลายไซต์".</summary>
    public static int Sites(string value, IReadOnlyList<string> customers)
    {
        var words = Tokens(value);
        return words.Count == 0 ? 0 : customers.Count(name => Tokens(name) is var its && its.Count > words.Count && its.Take(words.Count).SequenceEqual(words));
    }

    private static readonly char[] Separators = [' ', '-', '/', '(', ')', ',', '.', '_'];

    /// <summary>A name as words: upper case, letters and digits only, split on spaces, hyphens and brackets — "DANA (FREE ZONE)" is DANA · FREE · ZONE.</summary>
    public static IReadOnlyList<string> Tokens(string value) =>
        (value ?? "").Replace(Nbsp, " ").Split(Separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(word => new string(word.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()))
            .Where(word => word.Length > 0).ToList();

    /// <summary>The subcontractor register's name for a spelling it knows — SJ, SANGJA, "Sangja " all mean Sangja Transport Co., Ltd.</summary>
    public static Correction? Carrier(string value, Func<string, string?> carrierOf)
    {
        var company = carrierOf(value);
        return company is null || company.Length == 0 || company == value ? null
            : new("trucker", value, company, "trucker.directory", "ชื่อผู้ขนส่งตามทะเบียนผู้รับเหมา" + (Squash(value).Equals(company, StringComparison.OrdinalIgnoreCase) ? "" : $" (เดิมสะกด {Squash(value)})"));
    }

    /// <summary>
    /// The list's spelling of the same box or lorry, by <see cref="JobVehicleType.Canonical"/>;
    /// a code the list holds as typed (one added since, or the rule's own) proposes nothing,
    /// a retired code is never proposed, and a value the rule cannot read is left alone.
    /// </summary>
    public static Correction? VehicleType(string value, IReadOnlyList<string> codes)
    {
        if (codes.Contains(value, StringComparer.Ordinal)) return null;
        var canon = JobVehicleType.Canonical(value);
        if (canon == value || JobVehicleType.Retired.Contains(canon)) return null;
        if (!codes.Contains(canon, StringComparer.Ordinal) && !JobVehicleType.IsKnown(canon)) return null;
        return new("type", value, canon, "type.canonical", "ประเภทรถ/ตู้สะกดตามรายการ");
    }

    /// <summary>
    /// A status code of the job's own ladder, from a legacy word ("truck
    /// assigned") or another case ("delivered"). A word the ladder does not
    /// know is left alone: FromLegacy files the unknown under DRAFT, and a
    /// job at a stage nobody can name is a job a person has to look at, not
    /// one to be re-filed as new.
    /// </summary>
    public static Correction? Status(string value, string category)
    {
        var cat = Squash(category).ToUpperInvariant();
        if (JobStatus.IsValid(cat, value) && JobStatus.For(cat).Contains(value, StringComparer.Ordinal)) return null;
        var upper = Squash(value).ToUpperInvariant().Replace(' ', '_');
        var code = JobStatus.For(cat).FirstOrDefault(c => c == upper) ?? JobStatus.FromLegacy(value);
        if (code == JobStatus.Draft || code == value || !JobStatus.IsValid(cat, code)) return null;
        return new("status", value, code, "status.legacy", "รหัสสถานะตามระบบ");
    }

    /// <summary>Trimmed, one space between words, a non-breaking space read as a space.</summary>
    public static string Squash(string value) =>
        string.Join(' ', (value ?? "").Replace(Nbsp, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The name as letters and digits only, upper case — the same key the carrier register uses, so punctuation and spacing never make two customers of one.</summary>
    public static string CustomerKey(string value) => new((value ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
