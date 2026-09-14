using System.Text.Json;
using Scmos.Api.Rules;
using F = Scmos.Api.Rules.JobTransfer.Fate;

namespace Scmos.Api.Data;

/// <summary>
/// Which of a person's jobs a transfer takes, with <c>--check-job-transfer</c>.
///
/// <para>
/// This rewrites who owns work, in bulk, from two dates somebody typed into a
/// form. The ways it can be wrong are not symmetric: too narrow and a job sits
/// unworked for a week with nobody's name on it; too wide and a year of
/// somebody's completed jobs is credited to whoever covered their holiday.
/// The edges are the point — the first and last day of the leave, the job
/// with no date, the one already finished — so each is checked by name.
/// </para>
/// </summary>
public static class JobTransferCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-job-transfer")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        Console.WriteLine();
        Console.WriteLine("Reading the dates off the form.");
        Console.WriteLine();

        var (week, _) = JobTransfer.ReadPeriod("15/09/2026", "20/09/2026");
        Check(week is { From: 20260915, To: 20260920 }, "a week's leave, both ends");
        Check(JobTransfer.ReadPeriod("", "").Period is { Unbounded: true }, "nothing typed is every job");
        Check(JobTransfer.ReadPeriod("15/09/2026", "").Period is { From: 20260915, To: 0 },
            "only a start is open-ended — the person is not coming back");
        Check(JobTransfer.ReadPeriod("", "20/09/2026").Period is { From: 0, To: 20260920 },
            "only an end is everything up to it");

        // The one that matters. A date typed short is not a blank date: read as
        // one, "15/9/2026" for a week's leave becomes every job the person has.
        Check(JobTransfer.ReadPeriod("15/9/2026", "20/09/2026").Period is null,
            "a date that will not parse is refused, not read as blank");
        Check(JobTransfer.ReadPeriod("20/09/2026", "15/09/2026").Period is null,
            "and an end before its start is refused");
        Check(JobTransfer.ReadPeriod("  ", "\t").Period is { Unbounded: true },
            "whitespace is blank");

        Console.WriteLine();
        Console.WriteLine("Which jobs go.");
        Console.WriteLine();

        var leave = week!;
        Check(JobTransfer.FateOf(leave, "15/09/2026", "READY") == F.Moves, "the first day of the leave");
        Check(JobTransfer.FateOf(leave, "20/09/2026", "READY") == F.Moves, "and the last — both ends inclusive");
        Check(JobTransfer.FateOf(leave, "17/09/2026", "IN_TRANSIT") == F.Moves, "and the days between");
        Check(JobTransfer.FateOf(leave, "14/09/2026", "READY") == F.Outside, "the day before stays");
        Check(JobTransfer.FateOf(leave, "21/09/2026", "READY") == F.Outside, "and the day after");
        Check(JobTransfer.FateOf(leave, "17/09/2025", "READY") == F.Outside,
            "and the same week a year ago — years compare, not just days");

        Check(JobTransfer.FateOf(leave, "", "READY") == F.Undated,
            "a job with no date cannot be inside a leave, and is counted rather than moved");
        Check(JobTransfer.FateOf(leave, "Sept 17", "READY") == F.Undated, "nor one with a date nobody can read");
        Check(JobTransfer.FateOf(new JobTransfer.Period(0, 0), "", "READY") == F.Moves,
            "unless every job is going, when the date does not matter");

        // Finished work stays with whoever finished it. The register is also
        // the record of who did what, and a holiday cover is not a KPI transfer.
        Check(JobTransfer.FateOf(leave, "17/09/2026", "COMPLETED") == F.ClosedOut, "a completed job stays");
        Check(JobTransfer.FateOf(leave, "17/09/2026", "CANCELLED") == F.ClosedOut, "so does a cancelled one");
        Check(JobTransfer.FateOf(new JobTransfer.Period(0, 0), "17/09/2026", "COMPLETED") == F.ClosedOut,
            "even when every job is going — history is not work");
        Check(JobTransfer.FateOf(leave, "17/09/2026", "Completed") == F.ClosedOut,
            "however the status is cased");
        Check(JobTransfer.FateOf(leave, "17/09/2026", "Delivery Completed") == F.ClosedOut,
            "and in the old free-text spelling");
        Check(JobTransfer.FateOf(leave, "17/09/2026", "HOLD") == F.Moves,
            "but a job on hold is still work, and goes");
        Check(JobTransfer.FateOf(leave, "17/09/2026", "") == F.Moves, "as does one with no status at all");

        Console.WriteLine();
        Console.WriteLine("The stored job, after.");
        Console.WriteLine();

        var stored = """{"key":"J1","op":"Watsana","opId":"OP-01","customer":"CHEMOURS","remark":"ด่วน"}""";
        var moved = JobTransfer.Reassigned(stored, "Uthai", "OP-02");
        using (var after = JsonDocument.Parse(moved))
        {
            var root = after.RootElement;
            Check(root.GetProperty("op").GetString() == "Uthai"
                && root.GetProperty("opId").GetString() == "OP-02",
                "the name and the id change together");
            Check(root.GetProperty("customer").GetString() == "CHEMOURS"
                && root.GetProperty("remark").GetString() == "ด่วน"
                && root.GetProperty("key").GetString() == "J1",
                "and nothing else is touched, Thai included");
        }
        Check(JobTransfer.Reassigned("""{"key":"J2"}""", "Uthai", "OP-02").Contains("\"opId\":\"OP-02\""),
            "a row keyed before owner ids existed gets one");
        Check(JobTransfer.Reassigned("not json", "Uthai", "OP-02") == "not json",
            "and a row that will not parse is left alone rather than replaced with nothing");

        Console.WriteLine();
        Console.WriteLine("Said the way it was typed.");
        Console.WriteLine();

        Check(leave.Describe("15/09/2026", "20/09/2026") == "15/09/2026 – 20/09/2026", "a leave");
        Check(new JobTransfer.Period(0, 0).Describe("", "") == "ทุกงาน", "everything");
        Check(new JobTransfer.Period(20260915, 0).Describe("15/09/2026", "") == "ตั้งแต่ 15/09/2026", "from a day on");

        Console.WriteLine();
        Console.WriteLine(failed == 0
            ? "All job transfer checks passed."
            : $"{failed} job transfer check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
