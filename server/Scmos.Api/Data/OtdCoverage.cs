using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// How much of the register on-time delivery can actually see, with
/// <c>--report-otd</c>.
///
/// <para>
/// Written because the LINE plan proposed promoting seven job columns so that
/// OTD could be served by SQL, and OTD turned out to exist already and to be
/// limited by missing data rather than by missing columns. That claim was
/// measured on a development copy, and a claim about the register that decides
/// whether somebody writes a migration should be measured on the register.
/// </para>
///
/// <para>
/// <b>It reads and prints. There is no --apply and there is nothing to undo.</b>
/// Run through the workflow, where the connection string already lives, so
/// nobody has to copy a secret anywhere to count rows.
/// </para>
///
/// <para>
/// Every count comes from <see cref="JobRules.IsMeasurable"/> — the same rule
/// <c>KpiService</c> uses — rather than from a second reading of "measurable".
/// A report that disagreed with the screen it is about would be worse than no
/// report, and this file exists precisely because a second copy of a rule was
/// about to be written.
/// </para>
/// </summary>
public static class OtdCoverage
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        _ = args;
        using var scope = app.Services.CreateScope();
        var register = scope.ServiceProvider.GetRequiredService<JobRegisterCache>();

        var snapshot = await register.ReadAsync(default);
        var jobs = snapshot.Rows
            .Select(row => row.Record)
            .Where(record => record is not null)
            .Select(record => record!)
            .ToList();

        Console.WriteLine();
        Console.WriteLine("What on-time delivery can see, on the register as it stands.");
        Console.WriteLine($"Read at {snapshot.UpdatedAt:yyyy-MM-dd HH:mm}Z · {snapshot.Count} rows · "
            + $"{jobs.Count} parsed into records.");
        Console.WriteLine();

        /* --------------------------------------------- the four fields */

        // Which of the four IsMeasurable needs is missing, counted separately.
        // "Not measurable" on its own does not tell anybody what to go and fill
        // in, and filling one of four changes nothing on its own.
        int date = 0, arrDate = 0, arrTime = 0, planTime = 0;
        foreach (var job in jobs)
        {
            if (Formats.DateNumber(job.Date) > 0) date++;
            if (Formats.DateNumber(job.ArrDate) > 0) arrDate++;
            if (Formats.TimeMinutes(job.ArrTime) is not null) arrTime++;
            if (Formats.TimeMinutes(job.PlanTime) is not null) planTime++;
        }

        Console.WriteLine("  Each field the measurement needs, read by the same parser the rules use:");
        Console.WriteLine($"    date       {date,6} / {jobs.Count}");
        Console.WriteLine($"    planTime   {planTime,6} / {jobs.Count}");
        Console.WriteLine($"    arrDate    {arrDate,6} / {jobs.Count}");
        Console.WriteLine($"    arrTime    {arrTime,6} / {jobs.Count}");
        Console.WriteLine();

        /* ------------------------------------------------ by category */

        Console.WriteLine("  By category:");
        Console.WriteLine($"    {"category",-12}{"jobs",8}{"planTime",10}{"measurable",12}{"on time",9}");
        foreach (var group in jobs.GroupBy(job => job.Cat).OrderByDescending(one => one.Count()))
        {
            var all = group.ToList();
            var withPlan = all.Count(job => Formats.TimeMinutes(job.PlanTime) is not null);
            var measurable = all.Where(JobRules.IsMeasurable).ToList();
            var onTime = measurable.Count(JobRules.IsOnTime);
            Console.WriteLine($"    {(group.Key.Length > 0 ? group.Key : "(blank)"),-12}"
                + $"{all.Count,8}{withPlan,10}{measurable.Count,12}{Rate(onTime, measurable.Count),9}");
        }
        Console.WriteLine();

        /* --------------------------------------- where it matters most */

        // A job still on the road cannot be late yet. The honest denominator for
        // "how much of our work can we score" is the work that finished.
        var finished = jobs
            .Where(job => JobStatus.IsDone(job.Status)
                || string.Equals(job.Status, JobStatus.Delivered, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var finishedMeasurable = finished.Where(JobRules.IsMeasurable).ToList();

        Console.WriteLine("  Among jobs that actually finished (DELIVERED or COMPLETED):");
        Console.WriteLine($"    {finished.Count} finished, {finishedMeasurable.Count} measurable, "
            + $"{Rate(finishedMeasurable.Count, finished.Count)} of them scoreable.");
        if (finishedMeasurable.Count > 0)
        {
            var late = finishedMeasurable.Count(job => JobRules.LateBeyond(job));
            Console.WriteLine($"    of those, {finishedMeasurable.Count - late} on time and {late} "
                + $"late by more than {JobRules.LateMinutes} minutes.");
        }
        Console.WriteLine();

        /* ------------------------------------------------ the whole register */

        var totalMeasurable = jobs.Count(JobRules.IsMeasurable);
        Console.WriteLine($"  Whole register: {totalMeasurable} of {jobs.Count} measurable "
            + $"({Rate(totalMeasurable, jobs.Count)}).");
        Console.WriteLine();
        Console.WriteLine("  Nothing was written. This report only counts.");
        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// A percentage, or a dash when there is nothing to divide by.
    ///
    /// Never 0% for an empty set: no jobs measured is not the same as none on
    /// time, and printing a number there is how a blank category comes to look
    /// like a failing one.
    /// </summary>
    private static string Rate(int part, int whole) =>
        whole == 0 ? "—" : $"{Math.Round(part * 100.0 / whole)}%";
}
