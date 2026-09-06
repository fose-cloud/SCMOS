using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Otd">Null when the month could not be measured. Never zero for that.</param>
public record ArchivedReport(
    long Id, string Customer, string Month, string TakenAt, string TakenBy,
    int Trips, int Measurable, double? Otd);

/// <summary>
/// Keeps reports as they read on the day they were taken.
///
/// <para>
/// See <see cref="ReportArchiveEntry"/> for why that is not the same as being
/// able to rebuild them. In short: arrival times arrive late, so the same month
/// measured twice gives two different answers, and the one a customer is
/// holding is the one that has to be recoverable.
/// </para>
/// </summary>
public class ReportArchiveService(ScmosDbContext db, MonthlyReportService reports,
    ILogger<ReportArchiveService> log)
{
    private static readonly JsonSerializerOptions Wire =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Takes a snapshot, or leaves the one already there alone.
    ///
    /// Never overwrites. A second run for a month that is already archived is
    /// the scheduler having restarted, not somebody asking for a fresher
    /// figure — and quietly replacing the snapshot would destroy the only copy
    /// of what the customer was told.
    /// </summary>
    public async Task<bool> TakeAsync(string customer, string month, string takenBy, CancellationToken token)
    {
        var already = await db.ReportArchive.AsNoTracking()
            .AnyAsync(one => one.Customer == customer && one.Month == month, token);
        if (already) return false;

        var report = await reports.BuildAsync(customer, month, token);

        db.ReportArchive.Add(new ReportArchiveEntry
        {
            Customer = customer,
            Month = month,
            TakenAt = DateTimeOffset.UtcNow,
            TakenBy = takenBy,
            // camelCase, because this text is served straight back to the
            // browser and every other response from this API is camelCase.
            Document = JsonSerializer.Serialize(
                MonthlyReportService.Document(report, takenBy, MonthlyReportService.Stamp()), Wire),
            Trips = report.Summary.Trips,
            Measurable = report.Summary.Measurable,
            Otd = report.Summary.Otd,
        });

        try
        {
            await db.SaveChangesAsync(token);
            return true;
        }
        catch (DbUpdateException)
        {
            // The unique index settles a race between a scheduler that restarted
            // and a person pressing the button in the same second. Losing is the
            // correct outcome: a snapshot already exists.
            db.ChangeTracker.Clear();
            log.LogInformation("Report for {Customer} {Month} was already archived", customer, month);
            return false;
        }
    }

    /// <summary>Every snapshot, newest month first, without carrying the documents.</summary>
    public async Task<IReadOnlyList<ArchivedReport>> ListAsync(string? month, CancellationToken token)
    {
        var wanted = (month ?? "").Trim();
        var rows = await db.ReportArchive.AsNoTracking()
            .Where(one => wanted.Length == 0 || one.Month == wanted)
            .OrderByDescending(one => one.TakenAt)
            .Take(500)
            .Select(one => new
            {
                one.Id, one.Customer, one.Month, one.TakenAt, one.TakenBy,
                one.Trips, one.Measurable, one.Otd,
            })
            .ToListAsync(token);

        return rows
            // Newest month first, then the busiest customer inside it — which is
            // the order somebody looking for "September" reads them in.
            .OrderByDescending(one => one.Month.Length == 7 ? one.Month[3..] + one.Month[..2] : "",
                StringComparer.Ordinal)
            .ThenByDescending(one => one.Trips)
            .Select(one => new ArchivedReport(
                one.Id, one.Customer, one.Month,
                one.TakenAt.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),
                one.TakenBy, one.Trips, one.Measurable, one.Otd))
            .ToList();
    }

    /// <summary>The stored document, exactly as it was answered that day.</summary>
    public async Task<string?> ReadAsync(long id, CancellationToken token) =>
        await db.ReportArchive.AsNoTracking()
            .Where(one => one.Id == id)
            .Select(one => one.Document)
            .FirstOrDefaultAsync(token);

    /// <summary>
    /// Archives a whole month, one snapshot per customer that had trips in it.
    ///
    /// Every customer, not only the measurable ones. A month where WANHUA ran
    /// fifty-eight trips and none could be timed is a fact about that month, and
    /// an archive that silently skipped them would leave somebody searching for
    /// a report that was never taken and never said so.
    /// </summary>
    public async Task<int> TakeMonthAsync(string month, string takenBy, CancellationToken token)
    {
        var choices = await reports.ChoicesAsync(token);
        var taken = 0;

        foreach (var customer in choices.Customers)
        {
            token.ThrowIfCancellationRequested();
            var report = await reports.BuildAsync(customer.Customer, month, token);
            if (report.Summary.Trips == 0) continue;
            if (await TakeAsync(customer.Customer, month, takenBy, token)) taken++;
        }

        return taken;
    }

    /// <summary>The month before the one the given instant falls in, as "MM/yyyy".</summary>
    public static string PreviousMonth(DateTimeOffset at)
    {
        var previous = new DateTime(at.Year, at.Month, 1).AddMonths(-1);
        return previous.ToString("MM/yyyy");
    }
}
