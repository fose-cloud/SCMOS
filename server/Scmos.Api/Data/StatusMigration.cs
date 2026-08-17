using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Moves the register onto the controlled status set.
///
///   dotnet run -- --migrate-status --dry-run   see what would change
///   dotnet run -- --migrate-status             write it
///
/// The status is written to the column and to the stored JSON together, because
/// the workspace reads the JSON and every summary reads the column, and a job
/// that disagrees with itself is worse than one that is out of date.
///
/// A status that does not map becomes DRAFT and is listed by name at the end —
/// a job at an unknown stage is a job somebody has to look at, not one to guess
/// about.
/// </summary>
public static class StatusMigration
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var dryRun = args.Contains("--dry-run");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();

        var jobs = await db.OperationJobs.ToListAsync();
        app.Logger.LogInformation("Read {Count} jobs.", jobs.Count);

        var moved = new Dictionary<string, int>(StringComparer.Ordinal);
        var unmapped = new Dictionary<string, int>(StringComparer.Ordinal);
        var offLadder = new List<string>();
        var changed = 0;

        foreach (var job in jobs)
        {
            var before = Formats.Clean(job.Status);

            // Already migrated: leave it exactly as it is, so the command can be
            // run twice without churning rows or double-counting the report.
            if (JobStatus.IsValid(job.Cat, before)) continue;

            var after = JobStatus.FromLegacy(before);
            if (after == JobStatus.Draft && before.Length > 0 && !before.Equals("draft", StringComparison.OrdinalIgnoreCase))
            {
                unmapped[before] = unmapped.GetValueOrDefault(before) + 1;
            }

            // The mapped code has to exist on this category's ladder. An export
            // job that mapped to a status only imports carry would be invalid
            // the moment it was written.
            if (!JobStatus.IsValid(job.Cat, after))
            {
                offLadder.Add($"{job.Key} ({job.Cat}): {before} → {after}");
                continue;
            }

            moved[$"{before} → {after}"] = moved.GetValueOrDefault($"{before} → {after}") + 1;
            changed++;

            if (dryRun) continue;

            var node = JsonNode.Parse(job.Data)?.AsObject();
            if (node is null) continue;
            node["status"] = after;

            job.Status = after;
            job.Data = node.ToJsonString();
            job.UpdatedBy = "status-migration";
            job.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (!dryRun) await db.SaveChangesAsync();

        app.Logger.LogInformation("{Verb} {Changed} of {Total} jobs.",
            dryRun ? "Would change" : "Changed", changed, jobs.Count);

        foreach (var (mapping, count) in moved.OrderByDescending(entry => entry.Value))
            app.Logger.LogInformation("   {Count,5}  {Mapping}", count, mapping);

        if (unmapped.Count > 0)
        {
            app.Logger.LogWarning("Statuses with no mapping — these became DRAFT and need a person:");
            foreach (var (status, count) in unmapped.OrderByDescending(entry => entry.Value))
                app.Logger.LogWarning("   {Count,5}  {Status}", count, status);
        }

        if (offLadder.Count > 0)
        {
            app.Logger.LogWarning("Left unchanged — the mapped status is not on that category's ladder:");
            foreach (var line in offLadder.Take(20)) app.Logger.LogWarning("   {Line}", line);
        }

        return 0;
    }
}
