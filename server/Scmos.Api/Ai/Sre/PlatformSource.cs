using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Sre;

/// <summary>
/// What the API knows about itself, read where it already is: the process,
/// a timed trivial query, the register cache as it stands (never loaded for
/// this), the ledgers' newest rows, and its own failure counts. No table is
/// read wider than a count or a maximum, and no row's content leaves.
/// </summary>
public sealed class PlatformSource(ScmosDbContext db, JobRegisterCache register, IFileStore files, IOptions<OpenAiOptions> provider,
    IOptions<AiOptions> ai, IHostEnvironment environment) : IPlatformSource
{
    public const int PingTimeoutMs = 10000;

    public async Task<double?> PingDatabaseAsync(CancellationToken token)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(token);
        cap.CancelAfter(PingTimeoutMs);
        var watch = Stopwatch.StartNew();
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT 1", cap.Token);
            return watch.Elapsed.TotalMilliseconds;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception) { return null; }
    }

    public (int Rows, DateTimeOffset UpdatedAt)? CachedRegister()
        => register.Peek() is { } snapshot ? (snapshot.Count, snapshot.UpdatedAt)
            // A stale copy young enough to hand a summarising reader counts as "in hand": that reader will not wait.
            : JobRegisterCache.Stale() is { } stale ? (stale.Count, stale.UpdatedAt) : null;

    public async Task<PlatformActivity> ActivityAsync(CancellationToken token)
    {
        var lastLine = await db.LineEvents.AsNoTracking().Where(e => e.MessageType != CarrierEvent.MessageType).MaxAsync(e => (DateTimeOffset?)e.ReceivedAt, token);
        var lastTms = await db.LineEvents.AsNoTracking().Where(e => e.MessageType == CarrierEvent.MessageType).MaxAsync(e => (DateTimeOffset?)e.ReceivedAt, token);
        var lastMail = await db.Emails.AsNoTracking().MaxAsync(e => (DateTimeOffset?)e.ReceivedAt, token);
        var lastEdit = await db.AuditEvents.AsNoTracking().MaxAsync(e => (DateTimeOffset?)e.At, token);
        var lastAi = await db.AiAuditLogs.AsNoTracking().MaxAsync(e => (DateTimeOffset?)e.At, token);
        return new PlatformActivity(lastLine, lastMail, lastTms, lastEdit, lastAi);
    }

    public async Task<IReadOnlyList<PlatformFailure>> FailuresAsync(DateTimeOffset since, CancellationToken token)
    {
        var failures = new List<PlatformFailure>();
        // AI runs that did not succeed, by the audit's own status word; a run id's first eight characters are its safe name.
        var aiEnded = await db.AiAuditLogs.AsNoTracking()
            .Where(e => e.Event == "run_completed" && e.At >= since && e.Status != "succeeded" && e.Status != "clarification_required")
            .GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Newest = g.Max(e => e.At) })
            .ToListAsync(token);
        foreach (var group in aiEnded)
        {
            var newest = await db.AiAuditLogs.AsNoTracking().Where(e => e.Event == "run_completed" && e.Status == group.Status && e.At == group.Newest)
                .Select(e => e.RunId).FirstOrDefaultAsync(token) ?? "";
            failures.Add(new($"ai:{group.Status}", $"AI runs ended {group.Status}", group.Count, group.Newest, "run " + newest[..Math.Min(8, newest.Length)]));
        }
        if (aiEnded.Count == 0) failures.Add(new("ai", "AI runs that did not succeed", 0, null, ""));

        var lineFailed = await db.LineEvents.AsNoTracking().Where(e => e.ProcessingStatus == LineProcessing.Failed && e.ReceivedAt >= since)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Newest = g.Max(e => e.ReceivedAt), NewestId = g.Max(e => e.Id) }).FirstOrDefaultAsync(token);
        failures.Add(new("line:failed", "LINE messages the worker could not process", lineFailed?.Count ?? 0, lineFailed?.Newest, lineFailed is null ? "" : $"line:{lineFailed.NewestId}"));

        var mailFailed = await db.Emails.AsNoTracking().Where(e => e.ProcessingStatus == MailProcessing.Failed && e.ReceivedAt >= since)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Newest = g.Max(e => e.ReceivedAt), NewestId = g.Max(e => e.Id) }).FirstOrDefaultAsync(token);
        failures.Add(new("mail:failed", "Mails the worker could not process", mailFailed?.Count ?? 0, mailFailed?.Newest, mailFailed is null ? "" : $"mail:{mailFailed.NewestId}"));

        var webhooks = await db.CarrierWebhooks.AsNoTracking().Where(w => w.FailedInARow > 0 || (w.Status != CarrierWebhooks.Active && w.DisabledAt >= since))
            .Select(w => new { w.Id, w.FailedInARow, w.Status, At = w.LastDeliveryAt ?? w.DisabledAt }).ToListAsync(token);
        var troubled = webhooks.OrderByDescending(w => w.At).FirstOrDefault();
        failures.Add(new("webhooks:failing", "Carrier webhooks failing or retired", webhooks.Count, troubled?.At,
            troubled is null ? "" : $"webhook:{troubled.Id} ({troubled.Status}, {troubled.FailedInARow} failed in a row)"));
        return failures;
    }

    public PlatformProcess Process()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        // The App Service instance id, when there is one, is a safe name for which worker answered; the machine name is not shown.
        var instance = Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID") is { Length: > 0 } id ? id[..Math.Min(8, id.Length)] : "local";
        return new PlatformProcess(new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero), $".NET {Environment.Version.Major}",
            environment.EnvironmentName, instance, files.Configured, provider.Value.ApiKey.Length > 0, ai.Value.Enabled && ai.Value.ChatEnabled);
    }
}

/// <summary>Fixed-host, fixed-repository, GET-only reader of the repository's workflow runs. No caller value reaches a URL.</summary>
public sealed class GitHubDeploymentSource(HttpClient client) : IDeploymentSource
{
    public const string Repository = Engineering.GitHubEngineeringSource.Repository;

    public async Task<IReadOnlyList<DeploymentRun>> RunsAsync(int limit, CancellationToken token)
    {
        if (limit is < 1 or > PlatformReadService.EvidenceLimit) throw new InvalidOperationException("Invalid deployment read.");
        using var response = await client.GetAsync($"repos/{Repository}/actions/runs?per_page={limit}", token);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Workflow runs unavailable.");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        if (!json.RootElement.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Invalid workflow runs.");
        var rows = new List<DeploymentRun>();
        foreach (var run in runs.EnumerateArray())
        {
            var id = run.GetProperty("id").GetInt64();
            if (id <= 0) continue;
            var sha = Text(run, "head_sha", 40);
            if (sha.Length != 40 || sha.Any(c => !Uri.IsHexDigit(c))) continue;
            var actor = run.TryGetProperty("actor", out var a) && a.ValueKind == JsonValueKind.Object && a.TryGetProperty("login", out var login) ? login.GetString() ?? "" : "";
            rows.Add(new DeploymentRun(id, Text(run, "name", 60), Text(run, "status", 20), Text(run, "conclusion", 20), Text(run, "head_branch", 80), sha,
                Text(run, "display_title", 160), new string(actor.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '[' or ']').ToArray()),
                run.GetProperty("created_at").GetDateTimeOffset(), run.GetProperty("updated_at").GetDateTimeOffset()));
            if (rows.Count == limit) break;
        }
        return rows;
    }

    private static string Text(JsonElement run, string name, int max)
    {
        var value = run.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() ?? "" : "";
        value = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return value.Length > max ? value[..max] : value;
    }
}
