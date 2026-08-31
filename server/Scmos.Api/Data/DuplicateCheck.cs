using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Proves the register can tell when it is holding one company twice.
///
/// Run with <c>--check-duplicates</c>. It writes out a register by hand — the
/// shapes that have actually gone wrong, plus the ones that must not be
/// touched — and prints what the grouping makes of it beside what it should be.
/// Nothing is read and nothing is written, so it is safe to run anywhere.
///
/// It exists because this has now been wrong twice and both times invisibly. A
/// duplicate the screen does not detect looks exactly like a register with no
/// duplicates in it: the panel simply does not appear, and the only way anybody
/// finds out is by scrolling past two rows of the same haulier and noticing.
/// </summary>
public static class DuplicateCheck
{
    /// <summary>
    /// Null when this is not the flag being asked for; otherwise the exit code,
    /// so a failing check can stop a build rather than only saying so.
    /// </summary>
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-duplicates")) return null;

        var rows = new List<(int Id, string Name)>
        {
            // The pair that started this. Crossed over: each row is named what
            // the other one is spelled, and neither name stems to the other.
            (1, "DGT"),
            (2, "DGT Cross Haul Co., Ltd."),

            // The plain case: the short name the team types, and the official
            // one the import brought in. These do stem alike.
            (3, "SANGJA"),
            (4, "Sangja Transport Co., Ltd."),

            // Three rows for one firm, joined end to end rather than all to
            // one — 5 is spelled on 6, 6 is spelled on 7, and 5 and 7 have
            // nothing directly in common.
            (5, "JTC"),
            (6, "JTC Logistics"),
            (7, "JTC Logistics Co., Ltd."),

            // Two firms that must stay two. The names share a beginning and
            // nothing else, which is exactly the coincidence a looser rule
            // would merge.
            (8, "PK Transport Co., Ltd."),
            (9, "PKN Logistics Co., Ltd."),

            // A company on its own, with a spelling of its own.
            (10, "Wealthy Logistic Co., Ltd."),

            // A short name that could be either of two firms. It must join
            // neither — joining both would quietly make Thai Kot and Thai
            // Smile one company, which is the failure that costs money.
            (11, "THAI"),
            (12, "Thai Kot Transport Co., Ltd."),
            (13, "Thai Smile Logistics Co., Ltd."),
        };

        var aliases = new List<(int SupplierId, string Alias)>
        {
            (1, "DGT CROSS HAUL CO., LTD."),      // the crossed pair
            (2, "DGT"),

            (3, "SANGJA"), (3, "SJ"),
            (4, "SANGJA TRANSPORT CO., LTD."),

            (5, "JTC"),
            (6, "JTC"),                            // 6 holds 5's name
            (6, "JTC LOGISTICS"),
            (7, "JTC LOGISTICS"),                  // 7 holds 6's name
            (7, "JTC LOGISTICS CO., LTD."),

            (8, "PK TRANSPORT CO., LTD."), (8, "PK"),
            (9, "PKN LOGISTICS CO., LTD."),

            (10, "WEALTHY LOGISTIC CO., LTD."), (10, "WEALTHY"),

            (11, "THAI"),
            (12, "THAI KOT TRANSPORT CO., LTD."),
            (13, "THAI SMILE LOGISTICS CO., LTD."),
        };

        var sets = SupplierService.SameCompany(rows, aliases);
        bool Together(int a, int b) => sets[a] == sets[b];

        var checks = new (string What, bool Got, bool Want)[]
        {
            ("DGT and DGT Cross Haul are one company", Together(1, 2), true),
            ("SANGJA and Sangja Transport are one company", Together(3, 4), true),

            // The chain. 5 and 7 are joined only through 6.
            ("JTC and JTC Logistics are one company", Together(5, 6), true),
            ("JTC Logistics and JTC Logistics Co., Ltd. are one", Together(6, 7), true),
            ("JTC and JTC Logistics Co., Ltd. are one, through the middle", Together(5, 7), true),

            ("PK and PKN are NOT one company", Together(8, 9), false),
            ("DGT and SANGJA are NOT one company", Together(1, 3), false),
            ("Wealthy is nobody else", Together(10, 1) || Together(10, 8), false),

            // An ambiguous short name joins nothing, and above all does not
            // drag the two firms it could mean into one another.
            ("THAI is not Thai Kot — it could be either", Together(11, 12), false),
            ("THAI is not Thai Smile — same reason", Together(11, 13), false),
            ("Thai Kot and Thai Smile stay two companies", Together(12, 13), false),
        };

        var failed = 0;
        Console.WriteLine();
        Console.WriteLine("  duplicate check — thirteen rows, nine companies, two traps");
        Console.WriteLine();
        foreach (var (what, got, want) in checks)
        {
            var ok = got == want;
            if (!ok) failed++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what,-52} {(got ? "together" : "apart"),-9}"
                + (ok ? "" : $"want {(want ? "together" : "apart")}"));
        }

        // How many companies the register really holds, which is the number the
        // screen is really claiming when it shows a group.
        var companies = sets.Values.Distinct().Count();
        var right = companies == 9;
        if (!right) failed++;
        Console.WriteLine();
        Console.WriteLine($"    {(right ? "ok  " : "FAIL")}  {"thirteen rows are how many companies",-52} {companies,-9}want 9");
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "  the register can see what it is holding twice."
            : $"  {failed} wrong.");
        Console.WriteLine();
        return failed == 0 ? 0 : 1;
    }
}
