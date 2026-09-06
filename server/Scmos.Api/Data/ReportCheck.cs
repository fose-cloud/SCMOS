using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Runs the monthly report rule against fixtures, with <c>--check-report</c>.
///
/// <para>
/// A report is the one artefact that leaves the building. A wrong figure on a
/// screen is corrected by the next person who looks at it; a wrong figure in a
/// PDF sent to a customer is quoted back at a meeting. So what is checked here
/// is mostly what the report must <b>refuse</b> to say: no percentage without a
/// base, no zero standing in for a gap, and no rate off a sample too small to
/// carry one.
/// </para>
/// </summary>
public static class ReportCheck
{
    private static JobRecord Job(string date, string plan, string arrDate, string arrTime) =>
        new() { Date = date, PlanTime = plan, ArrDate = arrDate, ArrTime = arrTime };

    /// <summary>A trip that arrived when it said it would.</summary>
    private static MonthlyReport.Trip OnTime(string carrier = "JTC") =>
        new("LOTUS ASIA", carrier, Job("07/07/2026", "09:00", "07/07/2026", "08:30"));

    /// <summary>A trip that did not.</summary>
    private static MonthlyReport.Trip Late(string carrier = "JTC") =>
        new("LOTUS ASIA", carrier, Job("07/07/2026", "09:00", "07/07/2026", "11:45"));

    /// <summary>A trip nobody recorded an arrival for.</summary>
    private static MonthlyReport.Trip Unmeasured(string carrier = "JTC") =>
        new("LOTUS ASIA", carrier, Job("07/07/2026", "09:00", "", ""));

    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-report")) return null;

        var failed = 0;
        Console.WriteLine("The monthly report: what it counts, and what it refuses to claim.");
        Console.WriteLine();

        /* ---- counting ---- */
        var mixed = new List<MonthlyReport.Trip>();
        mixed.AddRange(Enumerable.Range(0, 8).Select(_ => OnTime()));
        mixed.AddRange(Enumerable.Range(0, 2).Select(_ => Late()));
        var line = MonthlyReport.Line("LOTUS ASIA", mixed);

        failed += Say("every trip is counted, measurable or not", line.Trips, 10);
        failed += Say("the base is the ones that could be judged", line.Measurable, 10);
        failed += Say("on time is counted", line.OnTime, 8);
        failed += Say("late is the rest of the base, not the rest of the month", line.Late, 2);
        failed += Say("and the rate is over the base", line.Otd, 80.0);

        /* ---- the gap is not a score ---- */
        Console.WriteLine();
        var blind = Enumerable.Range(0, 20).Select(_ => Unmeasured()).ToList();
        var blindLine = MonthlyReport.Line("WANHUA", blind);
        failed += Say("twenty unmeasured trips are still twenty trips", blindLine.Trips, 20);
        failed += Say("none of them can be judged", blindLine.Measurable, 0);
        // The failure this whole file exists to prevent.
        failed += Say("and the rate is nothing, not nought", blindLine.Otd, (double?)null);
        failed += Say("late is not invented either", blindLine.Late, 0);
        failed += Say("coverage says so plainly", blindLine.Coverage, 0);
        failed += Say("and the reader is told, in words",
            MonthlyReport.Confidence(blindLine).Contains("วัดไม่ได้เลย"), true);

        Console.WriteLine();
        // Half measured is the ordinary case, and the report must say which half.
        var half = new List<MonthlyReport.Trip>();
        half.AddRange(Enumerable.Range(0, 10).Select(_ => OnTime()));
        half.AddRange(Enumerable.Range(0, 10).Select(_ => Unmeasured()));
        var halfLine = MonthlyReport.Line("SHPP", half);
        failed += Say("a half-measured month rates only what it measured", halfLine.Otd, 100.0);
        failed += Say("but the base admits how few that was", halfLine.Measurable, 10);
        failed += Say("out of the whole month", halfLine.Trips, 20);
        failed += Say("and coverage is the ratio", halfLine.Coverage, 50);
        failed += Say("which the reader is warned about",
            MonthlyReport.Confidence(halfLine).Contains("ยังไม่ควรใช้ตัดสิน"), true);

        /* ---- a rate needs a sample ---- */
        Console.WriteLine();
        var two = new List<MonthlyReport.Trip> { OnTime("PPK"), OnTime("PPK") };
        var thin = MonthlyReport.Line("PPK", two);
        failed += Say("two trips on time is not a hundred percent", thin.Otd, (double?)null);
        failed += Say("though both are still counted", thin.OnTime, 2);
        var five = Enumerable.Range(0, 5).Select(_ => OnTime("PK")).ToList();
        failed += Say("five is where a rate starts being one",
            MonthlyReport.Line("PK", five).Otd, 100.0);

        /* ---- the vendor table ---- */
        Console.WriteLine();
        var many = new List<MonthlyReport.Trip>();
        many.AddRange(Enumerable.Range(0, 24).Select(_ => OnTime("JTC")));
        many.AddRange(Enumerable.Range(0, 3).Select(_ => Late("JTC")));
        many.AddRange(Enumerable.Range(0, 17).Select(_ => OnTime("SHORE")));
        many.AddRange(Enumerable.Range(0, 2).Select(_ => Unmeasured("")));
        var report = MonthlyReport.Build("LOTUS ASIA", "07/2026", many, [], []);

        failed += Say("a carrier with no name is not a vendor", report.Vendors.Count, 2);
        failed += Say("the busiest is first", report.Vendors[0].Name, "JTC");
        failed += Say("with its own trips", report.Vendors[0].Trips, 27);
        failed += Say("and its own rate", report.Vendors[0].Otd, Math.Round(2400.0 / 27, 1));
        failed += Say("the second carrier is counted separately", report.Vendors[1].Trips, 17);
        // The unnamed pair belong to the month even though they belong to no
        // carrier, or the summary would not add up to the register.
        failed += Say("the summary keeps the trips no carrier owns", report.Summary.Trips, 46);
        failed += Say("the vendor rows do not", report.Vendors.Sum(one => one.Trips), 44);

        /* ---- the target is stated, not assumed ---- */
        Console.WriteLine();
        failed += Say("the target travels with the report", report.Target, 85.0);
        failed += Say("a month above it is said to have met it",
            MonthlyReport.MeetsTarget(new ReportLine("x", 10, 10, 9, 1, 90.0), 85.0), true);
        failed += Say("a month below it is not",
            MonthlyReport.MeetsTarget(new ReportLine("x", 10, 10, 8, 2, 80.0), 85.0), false);
        // Neither passed nor failed: unknown. A report that marked an unmeasured
        // month as "target missed" would be inventing the finding it exists to
        // avoid inventing.
        failed += Say("and a month nobody could measure did neither",
            MonthlyReport.MeetsTarget(new ReportLine("x", 10, 0, 0, 0, null), 85.0), (bool?)null);

        /* ---- an empty month ---- */
        Console.WriteLine();
        var empty = MonthlyReport.Build("NOBODY", "07/2026", [], [], []);
        failed += Say("a customer with no trips has no rate", empty.Summary.Otd, (double?)null);
        failed += Say("no vendors", empty.Vendors.Count, 0);
        failed += Say("and divides by nothing", empty.Summary.Coverage, 0);
        failed += Say("and says so", MonthlyReport.Confidence(empty.Summary), "ไม่มีเที่ยวในเดือนนี้");

        /* ---- the commentary is fenced ---- */
        Console.WriteLine();
        var facts = ReportCommentary.Facts(MonthlyReport.Build("LOTUS ASIA", "07/2026", mixed, [], []));
        failed += Say("the facts carry the base, not only the rate", facts.Contains("could be measured"), true);
        // "rate" is ambiguous in this domain — an on-time rate is a percentage.
        // What must never travel is money, so money is what this looks for.
        string[] money = ["baht", "THB", "฿", "price", "cost", "charge", "tariff"];
        failed += Say("and no price is ever sent",
            money.Any(one => facts.Contains(one, StringComparison.OrdinalIgnoreCase)), false);
        failed += Say("nor a driver or a phone number",
            facts.Contains("driver", StringComparison.OrdinalIgnoreCase)
            || facts.Contains("phone", StringComparison.OrdinalIgnoreCase), false);

        Console.WriteLine();
        // The failure this fence exists for: a figure that was never measured,
        // written confidently beside figures that were.
        failed += Say("a draft quoting the report's own numbers is allowed",
            ReportCommentary.Judge("เดือนนี้วิ่ง 10 เที่ยว ตรงเวลา 8 เที่ยว คิดเป็น 80%", facts).Text is not null, true);
        failed += Say("a draft inventing a comparison is refused",
            ReportCommentary.Judge("OTD ดีขึ้น 4.7 จุดจากเดือนก่อน", facts).Text, (string?)null);
        failed += Say("and the refusal names the figure it could not find",
            ReportCommentary.Judge("OTD ดีขึ้น 4.7 จุดจากเดือนก่อน", facts).Refusal?.Contains("4.7"), true);
        failed += Say("a rejected draft is thrown away whole, not trimmed",
            ReportCommentary.Judge("ตรงเวลา 8 เที่ยว แต่ลดลง 12.3%", facts).Text, (string?)null);

        Console.WriteLine();
        // Prose counts as well as cites, and refusing "the three carriers" would
        // make the fence unusable.
        failed += Say("small whole numbers read as prose, not as statistics",
            ReportCommentary.Invented("ผู้ขนส่ง 3 รายต่ำกว่าเป้า", facts).Count, 0);
        failed += Say("a thousands separator is the same figure",
            ReportCommentary.Invented("1,000 เที่ยว", "trips: 1000").Count, 0);
        failed += Say("an empty draft is refused", ReportCommentary.Judge("  ", facts).Text, (string?)null);
        failed += Say("so is one too long to be a summary",
            ReportCommentary.Judge(new string('ก', 1400), facts).Text, (string?)null);

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "The report counts what it can and refuses to score what it cannot."
            : $"{failed} problem(s).");
        return failed == 0 ? 0 : 1;
    }

    private static int Say<T>(string why, T got, T want)
    {
        var ok = EqualityComparer<T>.Default.Equals(got, want);
        Console.WriteLine($"{(ok ? "ok  " : "FAIL")}  {why,-58} {(ok ? "" : $"got {got?.ToString() ?? "null"}  want {want?.ToString() ?? "null"}")}");
        return ok ? 0 : 1;
    }
}
