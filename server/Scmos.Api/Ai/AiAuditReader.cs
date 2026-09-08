using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using System.Text.Json;

namespace Scmos.Api.Ai;

public sealed record AiAuditEventView(string Event, string Status, DateTimeOffset At, int? Total, int? Returned);
public sealed record AiAuditRunView(string RunId, string UserId, string Role, string AgentId, string Model,
    AiReadScope Scope, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string? ToolCallId, string? Tool, string? ToolStatus, string? View, int? Limit,
    string Risk, string ApprovalStatus, string Source, string[] SourceKeys,
    int? Total, int? Returned, AiUsage? Usage, AiAuditEventView[] Events);
public sealed record AiAuditPage(AiAuditRunView[] Runs, long? NextBeforeId);

/// <summary>Read-only projections: an unfinished run is never inferred to have succeeded.</summary>
public sealed class AiAuditReader(SqlAiExecutionAudit audit, TimeProvider clock)
{
    public static bool Allowed(AppUser? user) => user is not null && AiPermissionPolicy.InternalUser(user)
        && user.Can(Capability.ViewAudit);
    public static AiAuditRunView Project(IReadOnlyList<AiAuditLog> events, DateTimeOffset now)
    {
        var start = events[0];
        var end = events.LastOrDefault(e => e.Sequence == 4);
        var tool = events.LastOrDefault(e => e.Sequence is 2 or 3);
        var last = events[^1];
        var status = end?.Status ?? (now - start.At > TimeSpan.FromMinutes(2) ? "incomplete" : "running");
        return new(start.RunId, start.UserId, start.Role, start.AgentId, start.Model,
            new(start.TeamScope, start.OperatorId), status, start.At, end?.At,
            tool?.ToolCallId, tool?.Tool, tool?.Status, tool?.View, tool?.Limit,
            start.Risk, start.ApprovalStatus, start.Source,
            tool is { Sequence: 3, Status: "succeeded" } ? JsonSerializer.Deserialize<string[]>(tool.SourceKeys)! : [],
            tool?.Total, tool?.Returned,
            last.InputTokens is { } input && last.OutputTokens is { } output ? new(input, output) : null,
            events.Select(e => new AiAuditEventView(e.Event, e.Status, e.At, e.Total, e.Returned)).ToArray());
    }

    public async Task<AiAuditPage> PageAsync(AppUser user, long? beforeId, int take, CancellationToken token)
    {
        if (!Allowed(user)) throw new UnauthorizedAccessException();
        if (take is < 1 or > 100 || beforeId is <= 0) throw new ArgumentException("Invalid audit page.");
        await using var db = audit.Open();
        var query = db.AiAuditLogs.AsNoTracking().Where(e => e.Sequence == 1);
        if (beforeId.HasValue) query = query.Where(e => e.Id < beforeId.Value);
        var starts = await query.OrderByDescending(e => e.Id).Take(take + 1).ToListAsync(token);
        var ids = starts.Take(take).Select(e => e.RunId).ToArray();
        var all = ids.Length == 0 ? [] : await db.AiAuditLogs.AsNoTracking().Where(e => ids.Contains(e.RunId))
            .OrderBy(e => e.Sequence).ToListAsync(token);
        var byRun = all.ToLookup(e => e.RunId);
        return new(ids.Select(id => Project(byRun[id].ToArray(), clock.GetUtcNow())).ToArray(),
            starts.Count > take ? starts[take - 1].Id : null);
    }

    public async Task<AiAuditRunView?> RunAsync(AppUser user, string runId, CancellationToken token)
    {
        if (!Allowed(user)) throw new UnauthorizedAccessException();
        if (!AiAuditRules.Id(runId)) throw new ArgumentException("Invalid audit run.");
        await using var db = audit.Open();
        var events = await db.AiAuditLogs.AsNoTracking().Where(e => e.RunId == runId)
            .OrderBy(e => e.Sequence).ToListAsync(token);
        return events.Count == 0 ? null : Project(events, clock.GetUtcNow());
    }
}
