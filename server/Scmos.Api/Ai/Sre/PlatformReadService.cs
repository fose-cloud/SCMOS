using System.Text.Json;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Sre;

/// <summary>
/// One signal of the platform's condition: a measurement, a count or a
/// deployment, with a safe identifier and no secret, address, message text or
/// person's data — the line the SRE read draws.
/// </summary>
/// <param name="Id">A stable, safe identifier: health:database, run:123456, errors:ai:timeout, webhook:5.</param>
/// <param name="Kind">health · deployment · error.</param>
/// <param name="State">ok · warn · bad · unknown.</param>
public sealed record PlatformSignal(string Id, string Kind, string Label, string Value, string Detail, DateTimeOffset? At, string State, string Source);

public sealed record PlatformAnswer(string View, string Window, int Total, int Returned, bool Truncated,
    DateTimeOffset RetrievedAt, string Basis, IReadOnlyList<PlatformSignal> Rows);

/// <summary>What the platform can say about itself without a telemetry connector — the API's own process, its database, its caches, its workers' last signs of life, its own error ledgers.</summary>
public interface IPlatformSource
{
    /// <summary>The database answered a trivial query in this many milliseconds, or null when it did not answer within the cap.</summary>
    Task<double?> PingDatabaseAsync(CancellationToken token);
    /// <summary>The register snapshot as cached — rows and the newest change — or null when nothing is cached (the next reader pays the read).</summary>
    (int Rows, DateTimeOffset UpdatedAt)? CachedRegister();
    /// <summary>The newest LINE event, mail, TMS event, business audit row and AI audit row — each null when the table is empty.</summary>
    Task<PlatformActivity> ActivityAsync(CancellationToken token);
    /// <summary>Failures since a moment, by kind — counts and the newest one's safe identifier.</summary>
    Task<IReadOnlyList<PlatformFailure>> FailuresAsync(DateTimeOffset since, CancellationToken token);
    /// <summary>The process: when it started, the runtime, the environment, the instance's safe name; whether storage and the AI provider are configured.</summary>
    PlatformProcess Process();
    /// <summary>The API requests the process remembers since a moment — route patterns, statuses, durations, exception types; nothing else.</summary>
    IReadOnlyList<RequestSample> Requests(DateTimeOffset since);
    /// <summary>When the request ring started remembering — the process's start.</summary>
    DateTimeOffset TelemetrySince { get; }
}

public sealed record PlatformActivity(DateTimeOffset? LastLine, DateTimeOffset? LastMail, DateTimeOffset? LastTms, DateTimeOffset? LastEdit, DateTimeOffset? LastAiRun);
public sealed record PlatformFailure(string Kind, string Label, int Count, DateTimeOffset? Newest, string NewestId);
public sealed record PlatformProcess(DateTimeOffset StartedAt, string Runtime, string Environment, string Instance, bool StorageConfigured, bool AiProviderConfigured, bool AiEnabled);

/// <summary>A workflow run as GitHub reports it — metadata only, the link constructed, the title untrusted display.</summary>
public sealed record DeploymentRun(long Id, string Workflow, string Status, string Conclusion, string Branch, string Sha, string Title, string Actor, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IDeploymentSource
{
    Task<IReadOnlyList<DeploymentRun>> RunsAsync(int limit, CancellationToken token);
}

/// <summary>
/// The SRE Agent's one read — Phase 7 (22 Sep 2026). Three views over what
/// the platform already knows about itself: <c>health</c> (the process, the
/// database's answer time, the register cache, storage and provider, the
/// workers' last signs of life), <c>deployments</c> (the repository's
/// workflow runs, as GitHub reports them), <c>errors</c> (this platform's
/// own failure ledgers, counted by kind with a safe identifier for the
/// newest). No telemetry connector exists yet — Application Insights is an
/// infrastructure decision the department has not taken — so nothing here
/// claims a metric it cannot measure. Nothing restarts, rolls back, or
/// touches a secret, a firewall or a resource: no such tool exists to be
/// offered.
/// </summary>
public sealed class PlatformReadService(IPlatformSource? platform, IDeploymentSource? deployments, TimeProvider clock)
{
    public const string Tool = "query_platform";
    public static readonly string[] Views = ["health", "deployments", "errors", "requests"];
    /// <summary>The requests view's window: the last hour, as the process remembers it.</summary>
    public const int RequestMinutes = 60;
    public const int EvidenceLimit = 50;
    public const int MaxDays = 30;
    public const int DefaultDays = 1;
    /// <summary>A database that takes longer than this to answer a trivial query is waking or saturated — the finding of 21 Sep 2026.</summary>
    public const int SlowDatabaseMs = 5000;
    /// <summary>A worker that has not signed in this long, during working hours, is worth a look; outside them it is the weekend.</summary>
    public const int QuietHours = 24;

    /// <summary>Whether the platform stands behind this read; deployments may be absent (no GitHub) without disconnecting the health views.</summary>
    public bool Connected => platform is not null;

    public async Task<PlatformAnswer> ReadAsync(JsonElement arguments, CancellationToken token)
    {
        if (platform is null) throw new InvalidOperationException("No platform source is connected.");
        var view = arguments.GetProperty("view").GetString() ?? "";
        var limit = arguments.GetProperty("limit").GetInt32();
        var days = arguments.TryGetProperty("days", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetInt32() : DefaultDays;
        if (!Views.Contains(view, StringComparer.Ordinal) || limit is < 1 or > EvidenceLimit || days is < 1 or > MaxDays)
            throw new InvalidOperationException("Invalid platform read arguments.");
        var now = clock.GetUtcNow();
        var rows = view switch
        {
            "health" => await HealthAsync(now, token),
            "deployments" => await DeploymentsAsync(limit, token),
            "requests" => Requests(now),
            _ => await ErrorsAsync(now.AddDays(-days), now, token),
        };
        var shown = rows.Take(limit).ToList();
        var window = view switch { "health" => "now", "deployments" => "latest_workflow_runs", "requests" => $"last_{RequestMinutes}_minutes", _ => $"last_{days}_days" };
        return new PlatformAnswer(view, window, rows.Count, shown.Count, rows.Count > shown.Count, now, Basis, shown);
    }

    private async Task<List<PlatformSignal>> HealthAsync(DateTimeOffset now, CancellationToken token)
    {
        var process = platform!.Process();
        var rows = new List<PlatformSignal>
        {
            new("health:process", "health", "API process", Uptime(now - process.StartedAt),
                $"เริ่มเมื่อ {Stamp(process.StartedAt)} · {process.Runtime} · {process.Environment} · instance {process.Instance}", process.StartedAt, "ok", "process"),
        };
        var ping = await platform.PingDatabaseAsync(token);
        rows.Add(ping is null
            ? new("health:database", "health", "Database", "ไม่ตอบใน 10 วินาที", "SQL serverless อาจกำลังตื่น (ประมาณ 110 วินาที) หรืออิ่มตัวจากการอ่านทะเบียนทั้งก้อน", now, "bad", "database")
            : new("health:database", "health", "Database", $"{ping.Value:0} ms", ping.Value > SlowDatabaseMs ? "ช้ากว่าปกติ — กำลังตื่นหรือมีการอ่านทะเบียนทั้งก้อนค้างอยู่" : "ตอบเร็ว", now, ping.Value > SlowDatabaseMs ? "warn" : "ok", "database"));
        var cached = platform.CachedRegister();
        rows.Add(cached is { } snapshot
            ? new("health:register", "health", "Register snapshot", $"{snapshot.Rows} งาน (cached)", $"แก้ไขล่าสุด {Stamp(snapshot.UpdatedAt)} · ผู้อ่านคนถัดไปไม่ต้องรออ่านทั้งก้อน", snapshot.UpdatedAt, "ok", "cache")
            : new("health:register", "health", "Register snapshot", "ไม่อยู่ใน cache", "ผู้อ่านคนถัดไป (Dashboard, KPI, AI) จะรออ่านทะเบียนทั้งก้อน — 24–62 วินาทีในเวลางาน", null, "warn", "cache"));
        rows.Add(new("health:storage", "health", "Blob storage", process.StorageConfigured ? "configured" : "not configured", "ที่เก็บเอกสาร", null, process.StorageConfigured ? "ok" : "bad", "config"));
        rows.Add(new("health:ai", "health", "AI platform", process.AiEnabled ? "enabled" : "disabled", process.AiProviderConfigured ? "provider configured" : "no provider key", null,
            process.AiEnabled && process.AiProviderConfigured ? "ok" : process.AiEnabled ? "bad" : "warn", "config"));
        var activity = await platform.ActivityAsync(token);
        foreach (var (id, label, at) in new[]
        {
            ("health:line", "LINE inbound", activity.LastLine), ("health:tms", "Carrier TMS inbound", activity.LastTms),
            ("health:mail", "Mailbox inbound", activity.LastMail), ("health:edits", "Register edits", activity.LastEdit), ("health:ai-runs", "AI runs", activity.LastAiRun),
        })
        {
            var age = at is null ? (TimeSpan?)null : now - at.Value;
            rows.Add(new(id, "health", label, at is null ? "ยังไม่มีเลย" : $"ล่าสุด {Stamp(at.Value)}",
                age is null ? "ไม่มีข้อมูลในตาราง" : age.Value.TotalHours > QuietHours ? $"เงียบมา {Uptime(age.Value)}" : $"เมื่อ {Uptime(age.Value)} ที่แล้ว",
                at, at is null ? "unknown" : age!.Value.TotalHours > QuietHours ? "warn" : "ok", "ledgers"));
        }
        return rows;
    }

    private async Task<List<PlatformSignal>> DeploymentsAsync(int limit, CancellationToken token)
    {
        if (deployments is null) return [new("deployments:none", "deployment", "Workflow runs", "ไม่มีแหล่งข้อมูล", "GitHub source not connected", null, "unknown", "github_public_repo")];
        var runs = await deployments.RunsAsync(limit, token);
        return runs.Select(run => new PlatformSignal($"run:{run.Id}", "deployment", run.Workflow,
                run.Status == "completed" ? run.Conclusion : run.Status,
                $"{run.Branch} @ {(run.Sha.Length >= 7 ? run.Sha[..7] : run.Sha)} · {Clean(run.Title, 120)} · by {Clean(run.Actor, 40)} · {Stamp(run.CreatedAt)}",
                run.UpdatedAt, run.Status != "completed" ? "warn" : run.Conclusion == "success" ? "ok" : run.Conclusion is "cancelled" or "skipped" ? "unknown" : "bad", "github_public_repo"))
            .ToList();
    }

    private async Task<List<PlatformSignal>> ErrorsAsync(DateTimeOffset since, DateTimeOffset now, CancellationToken token)
    {
        var failures = await platform!.FailuresAsync(since, token);
        var rows = failures.OrderByDescending(f => f.Count).ThenBy(f => f.Kind, StringComparer.Ordinal)
            .Select(f => new PlatformSignal($"errors:{f.Kind}", "error", f.Label, f.Count.ToString(),
                f.Count == 0 ? "ไม่มีในช่วงนี้" : $"ล่าสุด {(f.Newest is { } at ? Stamp(at) : "—")} · {f.NewestId}", f.Newest, f.Count == 0 ? "ok" : "warn", "ledgers"))
            .ToList();
        // What the API itself threw, by exception type and route pattern, as far back as the process remembers.
        var remembered = platform.Requests(since);
        var thrown = remembered.Where(r => r.Exception is not null)
            .GroupBy(r => (r.Exception!, r.Route)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Item1, StringComparer.Ordinal).Take(20).ToList();
        foreach (var group in thrown)
        {
            var newest = group.MaxBy(r => r.At)!;
            rows.Add(new($"errors:api:{Safe(group.Key.Item1)}:{Safe(group.Key.Item2)}", "error", $"API threw {group.Key.Item1}", group.Count().ToString(),
                $"{newest.Method} {group.Key.Item2} · ล่าสุด {Stamp(newest.At)}" + (newest.Correlation.Length > 0 ? $" · correlation {newest.Correlation}" : ""),
                newest.At, "bad", "process"));
        }
        var memorySince = platform.TelemetrySince > since ? platform.TelemetrySince : since;
        rows.Add(new("errors:api", "error", "API exceptions remembered", thrown.Sum(g => g.Count()).ToString(),
            $"จากคำขอที่โปรเซสจำได้ตั้งแต่ {Stamp(memorySince)} ({remembered.Count} คำขอ)" + (thrown.Count == 0 ? " · ไม่มี" : ""), null, thrown.Count == 0 ? "ok" : "warn", "process"));
        return rows;
    }

    /// <summary>The last hour's requests: the volume, how many failed or were slow, how long they took, and the routes that were slowest.</summary>
    private List<PlatformSignal> Requests(DateTimeOffset now)
    {
        var since = now.AddMinutes(-RequestMinutes);
        var samples = platform!.Requests(since);
        var memorySince = platform.TelemetrySince > since ? platform.TelemetrySince : since;
        if (samples.Count == 0)
            return [new("requests:volume", "health", "API requests", "0", $"ไม่มีคำขอที่จำได้ตั้งแต่ {Stamp(memorySince)}", null, "unknown", "process")];
        var durations = samples.Select(s => s.DurationMs).OrderBy(d => d).ToList();
        var failed = samples.Count(s => s.Status >= 500);
        var refused = samples.Count(s => s.Status is >= 400 and < 500);
        var slow = samples.Count(s => s.DurationMs > RequestTelemetry.SlowMs);
        var rows = new List<PlatformSignal>
        {
            new("requests:volume", "health", "API requests", samples.Count.ToString(), $"ตั้งแต่ {Stamp(memorySince)} · ล้มเหลว (5xx) {failed} · ปฏิเสธ (4xx) {refused} · ช้ากว่า {RequestTelemetry.SlowMs / 1000} วิ {slow}",
                samples[^1].At, failed > 0 ? "bad" : slow > 0 ? "warn" : "ok", "process"),
            new("requests:latency", "health", "Response time", $"p50 {Percentile(durations, 0.5):0} ms · p95 {Percentile(durations, 0.95):0} ms", $"ช้าสุด {durations[^1]:0} ms", null,
                Percentile(durations, 0.95) > RequestTelemetry.SlowMs ? "warn" : "ok", "process"),
        };
        // The routes that were slowest, by their p95 — each a pattern, never a path.
        foreach (var group in samples.GroupBy(s => (s.Method, s.Route)).Select(g => new
            {
                g.Key.Method, g.Key.Route, Count = g.Count(), Errors = g.Count(s => s.Status >= 500), Slow = g.Count(s => s.DurationMs > RequestTelemetry.SlowMs),
                P95 = Percentile(g.Select(s => s.DurationMs).OrderBy(d => d).ToList(), 0.95),
                Newest = g.Max(s => s.At),
            }).OrderByDescending(r => r.Errors).ThenByDescending(r => r.P95).Take(EvidenceLimit - 2))
        {
            rows.Add(new($"route:{Safe(group.Method)}:{Safe(group.Route)}", "health", $"{group.Method} {group.Route}", $"{group.Count} · p95 {group.P95:0} ms",
                $"ล้มเหลว {group.Errors} · ช้ากว่า {RequestTelemetry.SlowMs / 1000} วิ {group.Slow}", group.Newest,
                group.Errors > 0 ? "bad" : group.P95 > RequestTelemetry.SlowMs ? "warn" : "ok", "process"));
        }
        return rows;
    }

    /// <summary>The nearest-rank percentile of sorted durations: the value below which that share of requests finished.</summary>
    private static double Percentile(List<double> sorted, double share) => sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Count) - 1, 0, sorted.Count - 1)];

    /// <summary>A route pattern or exception type as a piece of a row id: ASCII letters, digits and pattern punctuation only, short enough for the audit's key.</summary>
    private static string Safe(string text)
    {
        var safe = new string(text.Where(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '{' or '}' or '-' or '_' or '.').ToArray());
        return safe.Length > 40 ? safe[..40] : safe;
    }

    private const string Basis = "The platform's own signals — process, database answer time, register cache, configuration, the ledgers' newest rows and failure counts, "
        + "and the last few thousand API requests the process remembers (route patterns, statuses, durations, exception types; forgotten at restart) — "
        + "and GitHub's workflow-run metadata at the fixed repository. No Application Insights or Azure Monitor connector exists; no metric is claimed that was not measured. "
        + "Safe identifiers only: no path, query, body, secret, address, message text or person's data. Nothing restarted, rolled back or changed.";

    public static string Uptime(TimeSpan span) => span.TotalDays >= 1 ? $"{(int)span.TotalDays} วัน {span.Hours} ชม." : span.TotalHours >= 1 ? $"{(int)span.TotalHours} ชม. {span.Minutes} นาที" : $"{Math.Max((int)span.TotalMinutes, 0)} นาที";
    private static string Stamp(DateTimeOffset at) => at.ToOffset(Formats.Zone).ToString("dd/MM/yyyy HH:mm");
    private static string Clean(string? text, int max)
    {
        var value = new string((text ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return value.Length > max ? value[..max] : value;
    }
}

public sealed class PlatformReadHandler(PlatformReadService service) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(arguments, token));
}
