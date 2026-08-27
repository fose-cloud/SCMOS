using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Proves the scorecard counts what it says it counts.
///
/// Run with <c>--check-scorecard</c>. It builds a month by hand — two hauliers,
/// known accidents, a complaint, a breakdown — and prints what the scorecard
/// makes of it beside what it should be. Nothing is written and no real data is
/// read, so it is safe to run against anything.
///
/// It exists because the live scorecard reads a hundred percent on every line
/// for every haulier, which is either a clean month or a broken chain, and
/// staring at a screen of hundreds cannot tell you which. This can: if the
/// arithmetic is right here and the live card is still all hundreds, the answer
/// is that nothing has been logged against those jobs.
/// </summary>
public static class ScorecardCheck
{
    public static bool Run(string[] args)
    {
        if (!args.Contains("--check-scorecard")) return false;

        var jobs = new List<(string Key, string Carrier, JobRecord Record)>
        {
            // SSL: four shipments, one of them an hour late.
            Job("J1", "SSL", "01/08/2026", "08:00", "01/08/2026", "08:10"),
            Job("J2", "SSL", "02/08/2026", "08:00", "02/08/2026", "09:00"),
            Job("J3", "SSL", "03/08/2026", "08:00", "03/08/2026", "07:55"),
            Job("J4", "SSL", "04/08/2026", "08:00", "04/08/2026", "08:05"),
            // THAIKOT: two, both on time.
            Job("J5", "THAIKOT", "05/08/2026", "08:00", "05/08/2026", "07:50"),
            Job("J6", "THAIKOT", "06/08/2026", "08:00", "06/08/2026", "08:00"),
            // JTC: one shipment, and an accident nobody has graded.
            Job("J7", "JTC", "07/08/2026", "08:00", "07/08/2026", "08:00"),
        };

        var issues = new List<OperationalIssue>
        {
            // A major transport accident on SSL, reported four minutes later.
            Issue("J1", "ความปลอดภัย/อุบัติเหตุ", "Subcontractor", "01/08/2026", "09:00",
                new DateTimeOffset(2026, 8, 1, 9, 4, 0, TimeSpan.FromHours(7)), "Transport (Major)"),

            // A loading accident on SSL, reported late — twenty minutes, and an
            // accident is allowed five.
            Issue("J2", "ความปลอดภัย/อุบัติเหตุ", "Warehouse", "02/08/2026", "10:00",
                new DateTimeOffset(2026, 8, 2, 10, 20, 0, TimeSpan.FromHours(7)), "Loading"),

            // The customer complains about the hour-late shipment.
            Issue("J2", "รถเข้ารับ/ส่งล่าช้า", "Customer", "02/08/2026", "11:00",
                new DateTimeOffset(2026, 8, 2, 11, 10, 0, TimeSpan.FromHours(7)), ""),

            // A breakdown with nobody complaining about it.
            Issue("J3", "รถ/อุปกรณ์ไม่พร้อม", "Subcontractor", "03/08/2026", "07:00",
                new DateTimeOffset(2026, 8, 3, 7, 10, 0, TimeSpan.FromHours(7)), ""),

            // A breakdown the customer did complain about — counted as a
            // complaint, and not again as a clean breakdown.
            Issue("J4", "รถ/อุปกรณ์ไม่พร้อม", "Subcontractor", "04/08/2026", "07:00",
                new DateTimeOffset(2026, 8, 4, 7, 10, 0, TimeSpan.FromHours(7)), ""),
            Issue("J4", "รถเข้ารับ/ส่งล่าช้า", "CS", "04/08/2026", "07:30",
                new DateTimeOffset(2026, 8, 4, 7, 40, 0, TimeSpan.FromHours(7)), ""),

            // Logged as an accident and never graded. It must not read as a
            // clean month: half the contract's weight rides on the two accident
            // criteria, and scoring them full marks while this sits here says
            // nothing happened.
            Issue("J7", "ความปลอดภัย/อุบัติเหตุ", "Subcontractor", "07/08/2026", "09:00",
                new DateTimeOffset(2026, 8, 7, 9, 3, 0, TimeSpan.FromHours(7)), ""),

            // An issue whose reference never matched a job. Nobody's score.
            Issue("", "ความปลอดภัย/อุบัติเหตุ", "Customer", "05/08/2026", "09:00",
                new DateTimeOffset(2026, 8, 5, 9, 2, 0, TimeSpan.FromHours(7)), "Transport (Major)"),

            // Somebody looked at THAIKOT's late collection and said it belongs
            // in the loading-accident column, which is not where the category
            // and source would have put it. What a person chose wins over what
            // the register implies — that is the whole point of the field.
            Chosen("J5", "รถเข้ารับ/ส่งล่าช้า", "Customer", "05/08/2026", "09:00",
                new DateTimeOffset(2026, 8, 5, 9, 5, 0, TimeSpan.FromHours(7)),
                Rules.ScorecardColumn.LoadingAccident),
        };

        var scores = CarrierScorecard.Build(jobs, issues, []);
        var ssl = scores.First(score => score.Carrier == "SSL");
        var kot = scores.First(score => score.Carrier == "THAIKOT");
        var jtc = scores.First(score => score.Carrier == "JTC");

        var checks = new (string What, object? Got, object? Want)[]
        {
            ("SSL shipments", ssl.Shipments, 4),
            ("SSL transport accident (major)", ssl.Tally.TransportAccidentMajor, 1),
            ("SSL transport accident (minor)", ssl.Tally.TransportAccidentMinor, 0),
            ("SSL loading accident", ssl.Tally.LoadingAccident, 1),
            ("SSL complaints (internal + external)", ssl.Tally.Complaints, 2),
            ("SSL breakdown with no complaint", ssl.Tally.BreakdownNoComplaint, 1),
            ("SSL ungraded accidents", ssl.UngradedAccidents, 0),

            // One of three reports went in inside its window: the major accident
            // at four minutes. The loading accident took twenty against five.
            // The complaint about the delay is neither an accident nor damage,
            // so it is not a report at all.
            ("SSL reports due", Line(ssl, "damage-reporting").Base, 2),
            ("SSL reports in time", Line(ssl, "damage-reporting").Count, 1),
            ("SSL reporting score", Line(ssl, "damage-reporting").Percent, 50.0),

            // J2 was an hour late and the customer complained about it. One
            // shipment of four.
            ("SSL late-with-complaint", Line(ssl, "on-time").Count, 1),
            ("SSL on-time score", Line(ssl, "on-time").Percent, 75.0),

            // The On Time Delivery column asks a different question from the
            // criterion above it, and the two answers differ on this fixture by
            // design. Of SSL's four, only J3 arrived before its plan; J1 was ten
            // minutes late, J4 five, J2 an hour. The column counts on time as on
            // time, the way the headline row and the register do — one of four.
            //
            // The criterion reads 75 because the agreement marks a haulier down
            // only for lateness beyond half an hour that drew a complaint, which
            // is J2 alone. Same shipments, two questions, and the card says
            // which is which under the table.
            ("SSL on-time column, measured over", ssl.OnTime.Base, 4),
            ("SSL on-time column, met (on time means on time)", ssl.OnTime.Met, 1),
            ("SSL on-time column, percent", ssl.OnTime.Percent, 25.0),
            ("THAIKOT on-time column, percent", kot.OnTime.Percent, 100.0),

            // A major accident on one of four shipments is a 25% rate, so 75.
            ("SSL major score", Line(ssl, "accident-major").Percent, 75.0),
            ("SSL minor score", Line(ssl, "accident-minor").Percent, 100.0),
            ("SSL vehicle readiness (nothing records it)", Line(ssl, "vehicle-readiness").Percent, null),

            ("THAIKOT shipments", kot.Shipments, 2),
            // Would have been a complaint by category and source; counted where
            // the person filling the form said to count it.
            ("THAIKOT complaints (the chosen column wins)", kot.Tally.Complaints, 0),
            ("THAIKOT loading accident (chosen, not derived)", kot.Tally.LoadingAccident, 1),
            ("THAIKOT major", kot.Tally.TransportAccidentMajor, 0),

            // The row that started this: an accident on file, no kind given.
            ("JTC ungraded accidents", jtc.UngradedAccidents, 1),
            ("JTC minor cannot be scored", Line(jtc, "accident-minor").Percent, null),
            ("JTC major cannot be scored", Line(jtc, "accident-major").Percent, null),
            // Fifty points of accident weight and ten of readiness are out, so
            // forty of the hundred is what is left to score on.
            ("JTC weight available", jtc.WeightAvailable, 40.0),
            // A hundred is the honest score of the forty per cent that can be
            // judged — inventing a lower one would be no better than the
            // hundred-out-of-a-hundred it replaced. What must not happen is the
            // card presenting it as a finished mark, and that is the screen's
            // job: the row is drawn as provisional whenever this count is above
            // nought.
            ("JTC scores the measurable part", jtc.Weighted, 100.0),
            ("JTC has an accident on file to say so", jtc.Tally.TransportAccidentMajor
                + jtc.Tally.TransportAccidentMinor + jtc.Tally.LoadingAccident, 0),
            // Ninety, not seventy: the chosen column made this an accident, so a
            // report became due and the reporting criterion can now be measured.
            // Only vehicle readiness is left unmeasured, and its ten per cent is
            // the only weight the total is scaled off.
            ("THAIKOT weight available (readiness unmeasured)", kot.WeightAvailable, 90.0),
            // Found 09:00, recorded 09:05, and an accident is allowed five.
            ("THAIKOT reported inside the accident window", Line(kot, "damage-reporting").Percent, 100.0),
        };

        var failed = 0;
        Console.WriteLine();
        Console.WriteLine("  scorecard check — seven shipments, three hauliers, nine logged issues");
        Console.WriteLine();
        foreach (var (what, got, want) in checks)
        {
            var ok = Equals(got, want);
            if (!ok) failed++;
            Console.WriteLine($"    {(ok ? "ok  " : "FAIL")}  {what,-46} got {Show(got),-8} want {Show(want)}");
        }

        // The unattributed one is counted for the month and laid at nobody's
        // door, which is the whole point of reporting it separately.
        var attributed = issues.Count(issue =>
            issue.JobKey.Length > 0 && jobs.Any(job => job.Key == issue.JobKey));
        Console.WriteLine();
        Console.WriteLine($"    {(attributed == 8 ? "ok  " : "FAIL")}  {"issues that reached a job",-46} got {attributed,-8} want 8");
        Console.WriteLine($"    {"",-52}  and 1 that did not, counted for the month only");
        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "  the scorecard counts what it says it counts."
            : $"  {failed} of {checks.Length} wrong.");
        Console.WriteLine();
        return true;
    }

    private static ScoreLine Line(CarrierScore score, string id) =>
        score.Lines.First(line => line.Id == id);

    private static string Show(object? value) => value?.ToString() ?? "—";

    private static (string, string, JobRecord) Job(
        string key, string carrier, string date, string planTime, string arrDate, string arrTime) =>
        (key, carrier, new JobRecord
        {
            Key = key, Trucker = carrier, Date = date, PlanTime = planTime,
            ArrDate = arrDate, ArrTime = arrTime,
        });

    /// <summary>An issue whose scorecard column somebody set by hand.</summary>
    private static OperationalIssue Chosen(string jobKey, string category, string source,
        string foundOn, string foundAt, DateTimeOffset createdAt, string column)
    {
        var issue = Issue(jobKey, category, source, foundOn, foundAt, createdAt, "");
        issue.ScorecardColumn = column;
        return issue;
    }

    private static OperationalIssue Issue(string jobKey, string category, string source,
        string foundOn, string foundAt, DateTimeOffset createdAt, string grade) =>
        new()
        {
            JobKey = jobKey, Category = category, Source = source,
            FoundOn = foundOn, FoundAt = foundAt, CreatedAt = createdAt,
            AccidentGrade = grade,
        };
}
