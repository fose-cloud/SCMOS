using System.Text.RegularExpressions;
using Scmos.Api.Rules;
using Scmos.Api.Services;
using S = Scmos.Api.Rules.SupplierCompliance.State;

namespace Scmos.Api.Data;

/// <summary>
/// The paperwork a subcontractor must hold, with <c>--check-compliance</c>.
///
/// <para>
/// Two halves. The states — what "expiring" means, which of five gaps a
/// supplier's status takes — are decided in <see cref="SupplierCompliance"/>
/// and checked here against dates that do not move. The second half is the
/// vocabulary, which is written twice: once for the API and once for the
/// screen's column headings. Neither can read the other at runtime, so this
/// reads the screen's copy off disk and fails if the two have drifted.
/// </para>
/// </summary>
public static class SupplierComplianceCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-compliance")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("What a subcontractor has to hold.");
        Console.WriteLine();

        Check(SupplierCompliance.Required.Length == 5, "five documents are required");
        Check(SupplierCompliance.Required.Select(one => one.Code).Distinct().Count() == 5,
            "each has its own code, so a file can be told which column it belongs under");

        // Two share a folder and two share another. The folder says where a
        // file lives; the code says what it is. Conflating them is what made
        // vehicle and cargo insurance indistinguishable.
        Check(SupplierCompliance.Required.Count(one => one.Folder == "Insurance") == 2,
            "vehicle and cargo insurance share the Insurance folder and are still two requirements");
        Check(SupplierCompliance.Required.All(one =>
                BlobPaths.SupplierFolders.Contains(one.Folder)),
            "every requirement files into a folder the path rules already know");

        Check(SupplierCompliance.Match("insurance-cargo")?.Code == "insurance-cargo",
            "a stored kind finds its requirement");
        Check(SupplierCompliance.Match("INSURANCE-CARGO")?.Code == "insurance-cargo",
            "however it is cased");
        Check(SupplierCompliance.Match("insurance") is null,
            "and a folder name is not a requirement — that was the bug");
        Check(SupplierCompliance.Match("") is null && SupplierCompliance.Match(null) is null,
            "nor is nothing at all");

        Console.WriteLine();
        Console.WriteLine("Where one document stands.");
        Console.WriteLine();

        // A fixed today, so this says the same thing every day it is run.
        var today = Formats.DateNumber("01/03/2026");
        string State(bool held, string expiry, bool expires = true) =>
            SupplierCompliance.StateOf(held, expiry, today, expires);

        Check(State(false, "") == SupplierCompliance.State.Missing, "nothing uploaded is missing");
        Check(State(true, "01/01/2026") == SupplierCompliance.State.Expired, "a date gone by is expired");
        Check(State(true, "28/02/2026") == SupplierCompliance.State.Expired, "including yesterday");
        Check(State(true, "01/03/2026") == SupplierCompliance.State.Expiring, "today is expiring, not expired");
        Check(State(true, "30/04/2026") == SupplierCompliance.State.Expiring,
            $"and so is anything inside {SupplierCompliance.WarningDays} days");
        Check(State(true, "30/06/2026") == SupplierCompliance.State.Valid, "further out is valid");
        Check(State(true, "") == SupplierCompliance.State.NoExpiry,
            "held with no date recorded is its own answer, not valid");

        // The truck annex has no expiry by nature, so an absent date is not a
        // gap in the watching — it is simply not that kind of document.
        Check(State(true, "", expires: false) == SupplierCompliance.State.Valid,
            "except where the document does not expire at all");
        Check(State(false, "", expires: false) == SupplierCompliance.State.Missing,
            "which still has to be held");

        Console.WriteLine();
        Console.WriteLine("The boundary of the warning window.");
        Console.WriteLine();

        // 60 days from 01/03/2026 is 30/04/2026. One day further is valid.
        Check(State(true, "30/04/2026") == SupplierCompliance.State.Expiring, "day 60 warns");
        Check(State(true, "01/05/2026") == SupplierCompliance.State.Valid, "day 61 does not");
        Check(SupplierCompliance.WarningDays == Notifications.ExpiryWarningDays,
            "and the screen's window is the alert's window, not a second number");
        Check(SupplierCompliance.WarningDays == DocumentService.ExpiringWithinDays,
            "and the document service's too");

        Console.WriteLine();
        Console.WriteLine("Counting the days.");
        Console.WriteLine();

        /*
         * The arithmetic worth checking. These dates are stored as YYYYMMDD
         * integers, and subtracting two of them gives 73 for the one day
         * between 28 February and 1 March. A column counting down to an expiry
         * is exactly where that would be believed.
         */
        Check(SupplierCompliance.DaysBetween(Formats.DateNumber("28/02/2026"),
            Formats.DateNumber("01/03/2026")) == 1, "28 February to 1 March is one day, not 73");
        Check(SupplierCompliance.DaysBetween(Formats.DateNumber("31/12/2026"),
            Formats.DateNumber("01/01/2027")) == 1, "and across a year end, one day, not 8,870");
        Check(SupplierCompliance.DaysBetween(Formats.DateNumber("28/02/2024"),
            Formats.DateNumber("01/03/2024")) == 2, "a leap year has the extra day in it");
        Check(SupplierCompliance.DaysBetween(Formats.DateNumber("01/03/2026"),
            Formats.DateNumber("01/01/2026")) < 0, "a date already gone is negative");

        Console.WriteLine();
        Console.WriteLine("A supplier's overall status is the worst of its five.");
        Console.WriteLine();

        Check(SupplierCompliance.Worst([S.Valid, S.Valid, S.Valid]) == S.Valid, "all in order is in order");
        Check(SupplierCompliance.Worst([S.Valid, S.Expiring, S.Valid]) == S.Expiring, "one running out shows");
        Check(SupplierCompliance.Worst([S.Expiring, S.Missing]) == S.Missing, "a gap outranks a warning");

        // The one that is not obvious. A document that was held, was checked
        // and has run out is worse than one never supplied, because somebody
        // has been relying on it and still is.
        Check(SupplierCompliance.Worst([S.Missing, S.Expired]) == S.Expired,
            "and expired outranks missing — it was true and has stopped being true");
        Check(SupplierCompliance.Worst([S.Valid, S.NoExpiry]) == S.NoExpiry,
            "a date nobody recorded is a gap in the watching");
        Check(SupplierCompliance.Worst([]) == S.Valid, "and nothing to judge is not a fault");

        Console.WriteLine();
        Console.WriteLine("And the screen lists the same five.");
        Console.WriteLine();

        var screen = FindModule();
        if (screen is null)
        {
            failed++;
            Console.WriteLine("  FAIL  could not find app/scmos/supplierCompliance.ts to check against");
        }
        else
        {
            var listed = File.ReadAllText(screen);
            var codes = Regex.Matches(listed, "code: \"([a-z-]+)\"")
                .Select(match => match.Groups[1].Value).ToList();

            foreach (var need in SupplierCompliance.Required)
            {
                var ok = codes.Contains(need.Code);
                if (!ok) failed++;
                Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {need.Code}");
                if (!ok) Console.WriteLine("        The API requires this document and the screen has no column for it.");
            }

            var extra = codes.Where(code =>
                SupplierCompliance.Required.All(need => need.Code != code)).ToList();
            if (extra.Count > 0) failed++;
            Console.WriteLine($"  {(extra.Count == 0 ? "ok  " : "FAIL")}  and offers no column the API will never fill");
            if (extra.Count > 0)
                Console.WriteLine($"        The screen lists {string.Join(", ", extra)}, which the API does not require.");

            // Order matters: the API sends the five in its own order and the
            // screen draws its headings in its own. Two orders means the
            // heading over a column and the document in it come apart.
            var sameOrder = codes.Count == SupplierCompliance.Required.Length
                && codes.Zip(SupplierCompliance.Required).All(pair => pair.First == pair.Second.Code);
            if (!sameOrder) failed++;
            Console.WriteLine($"  {(sameOrder ? "ok  " : "FAIL")}  "
                + "in the same order, so a heading sits over its own column");

            var window = Regex.Match(listed, @"WARNING_DAYS = (\d+)");
            var windowAgrees = window.Success
                && int.Parse(window.Groups[1].Value) == SupplierCompliance.WarningDays;
            if (!windowAgrees) failed++;
            Console.WriteLine($"  {(windowAgrees ? "ok  " : "FAIL")}  "
                + $"and warns at the same {SupplierCompliance.WarningDays} days");
        }

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All supplier compliance checks passed."
            : $"{failed} supplier compliance check(s) failed.");
        return failed == 0 ? 0 : 1;
    }

    /// <summary>
    /// The screen's copy of the vocabulary, found by walking up from the
    /// binary. Null when this is not running inside a checkout, which the
    /// caller reports rather than skips — a check that goes quiet when it
    /// cannot find its subject is not a check.
    /// </summary>
    private static string? FindModule()
    {
        var here = new DirectoryInfo(AppContext.BaseDirectory);
        for (var up = 0; up < 8 && here is not null; up++, here = here.Parent)
        {
            var candidate = Path.Combine(here.FullName, "app", "scmos", "supplierCompliance.ts");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
