using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Scmos.Api.Data;

/// <summary>
/// Moves a register into Azure SQL.
///
/// The 2,102 July jobs already exist as worked jobs, so the migration carries
/// those across rather than re-deriving them from the plan workbooks — the
/// cleanup, the reassignments and the history in each job are the whole point.
///
/// Three input shapes are accepted, because all three are things somebody
/// actually has to hand:
///   { "jobs": [ … ] }              the old app's own GET /api/jobs response
///   [ … ]                          a bare array of jobs
///   [ { "results": [ { "data": "…" } ] } ]   a `wrangler d1 execute --json` dump
///
/// Writing is an upsert on the job key, so a half-finished run is resumed by
/// running it again.
/// </summary>
public static class PlanSeeder
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, "--seed");
        var path = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        if (path is null)
        {
            app.Logger.LogError("Usage: dotnet run -- --seed <path-to-export.json> [--as <email>]");
            return 2;
        }

        if (!File.Exists(path))
        {
            app.Logger.LogError("No such file: {Path}", path);
            return 2;
        }

        var asIndex = Array.IndexOf(args, "--as");
        var author = asIndex >= 0 && asIndex + 1 < args.Length ? args[asIndex + 1] : "migration";

        List<JsonElement> jobs;
        try
        {
            jobs = Extract(await File.ReadAllTextAsync(path));
        }
        catch (JsonException error)
        {
            app.Logger.LogError("{Path} is not valid JSON: {Message}", path, error.Message);
            return 2;
        }

        if (jobs.Count == 0)
        {
            app.Logger.LogWarning("{Path} holds no jobs — nothing to move.", path);
            return 1;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        await db.Database.MigrateAsync();

        var repository = scope.ServiceProvider.GetRequiredService<JobsRepository>();
        var total = 0;
        // Well under the per-save ceiling, so progress is visible on a slow link.
        foreach (var batch in jobs.Chunk(500))
        {
            var (saved, _) = await repository.SaveAsync(batch, author, CancellationToken.None);
            total += saved;
            app.Logger.LogInformation("Moved {Total} of {Count} jobs.", total, jobs.Count);
        }

        app.Logger.LogInformation("Done. {Total} jobs are in Azure SQL.", total);
        return 0;
    }

    private static List<JsonElement> Extract(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.Clone();
        var jobs = new List<JsonElement>();

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("jobs", out var wrapped))
        {
            Collect(wrapped, jobs);
            return jobs;
        }

        if (root.ValueKind == JsonValueKind.Array)
        {
            // A wrangler dump is an array of result sets, each holding rows whose
            // `data` column is the job as a JSON string.
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("results", out var results))
                {
                    foreach (var row in results.EnumerateArray())
                    {
                        if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("data", out var data)
                            && data.ValueKind == JsonValueKind.String)
                        {
                            try
                            {
                                using var inner = JsonDocument.Parse(data.GetString() ?? "");
                                jobs.Add(inner.RootElement.Clone());
                            }
                            catch (JsonException)
                            {
                                // A row that will not parse is skipped rather than
                                // stopping the move, the same as on load.
                            }
                        }
                        else if (row.ValueKind == JsonValueKind.Object)
                        {
                            jobs.Add(row);
                        }
                    }
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Object) jobs.Add(element);
            }
        }

        return jobs;
    }

    private static void Collect(JsonElement array, List<JsonElement> into)
    {
        if (array.ValueKind != JsonValueKind.Array) return;
        foreach (var element in array.EnumerateArray())
            if (element.ValueKind == JsonValueKind.Object)
                into.Add(element);
    }
}
