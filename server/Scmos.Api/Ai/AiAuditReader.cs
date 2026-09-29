using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using System.Text.Json;

namespace Scmos.Api.Ai;

public sealed record AiAuditEventView(string Event, string Status, DateTimeOffset At, int? Total, int? Returned,
    int? Step = null, string? Tool = null);
public sealed record AiAuditRunView(string RunId, string UserId, string Role, string AgentId, string Model,
    AiReadScope Scope, string Status, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt,
    string? ToolCallId, string? Tool, string? ToolStatus, string? View, int? Limit,
    string Risk, string ApprovalStatus, string Source, string[] SourceKeys,
    int? Total, int? Returned, AiUsage? Usage, AiAuditEventView[] Events,
    /// <summary>The API request the run belongs to (1D); empty for older runs.</summary>
    string CorrelationId = "",
    /// <summary>Tool steps completed.</summary>
    int Steps = 0);
public sealed record AiAuditPage(AiAuditRunView[] Runs, long? NextBeforeId);

/// <summary>
/// The AI Activity search (AI Agent Platform specification §47): which agent, which tool, how the run ended
/// (<c>incomplete</c> is a run that never recorded its end), who ran it (id or part of it — the mail pass is
/// <c>system:agent-pass</c>), which Bangkok days, and an evidence key its last step returned (a job key,
/// <c>mail:41</c>, <c>paste:1</c>). Only the audit's own vocabulary is accepted for agent, tool and result.
/// </summary>
public sealed record AuditSearch(string? Agent = null, string? Tool = null, string? Result = null, string? User = null,
    DateOnly? From = null, DateOnly? To = null, string? Key = null)
{
    public static readonly string[] Results = ["succeeded", "failed", "cancelled", "timeout", "provider_unavailable", "provider_busy",
        "source_unavailable", "clarification_required", "invalid_tool", "not_connected", "audit_failed", "incomplete"];

    public string? Problem() =>
        Agent is { Length: > 0 } agent && !AiAuditRules.KnownAgents.Contains(agent, StringComparer.Ordinal) ? "unknown agent"
        : Tool is { Length: > 0 } tool && !AiAuditRules.KnownTools.Contains(tool, StringComparer.Ordinal) ? "unknown tool"
        : Result is { Length: > 0 } result && !Results.Contains(result, StringComparer.Ordinal) ? "unknown result"
        : User is { } user && (user.Length > 160 || user.Any(char.IsControl)) ? "user is too long"
        : Key is { } key && (key.Length > 80 || key.Any(ch => char.IsControl(ch) || ch == '"')) ? "key is not an evidence key"
        : From is { } from && To is { } to && to < from ? "the range ends before it starts"
        : null;
}

/// <summary>Read-only projections: an unfinished run is never inferred to have succeeded.</summary>
public sealed class AiAuditReader(SqlAiExecutionAudit audit, TimeProvider clock)
{
    public static bool Allowed(AppUser? user) => user is not null && AiPermissionPolicy.InternalUser(user)
        && user.Can(Capability.ViewAudit);
    public static AiAuditRunView Project(IReadOnlyList<AiAuditLog> events, DateTimeOffset now)
    {
        var start = events[0];
        var end = events.LastOrDefault(e => e.Event == "run_completed");
        // The last tool step is the one whose evidence the answer was made of.
        var tool = events.LastOrDefault(e => e.Event is "tool_started" or "tool_completed");
        var last = events[^1];
        var status = end?.Status ?? (now - start.At > TimeSpan.FromMinutes(2) ? "incomplete" : "running");
        return new(start.RunId, start.UserId, start.Role, start.AgentId, start.Model,
            new(start.TeamScope, start.OperatorId), status, start.At, end?.At,
            tool?.ToolCallId, tool?.Tool, tool?.Status, tool?.View, tool?.Limit,
            start.Risk, start.ApprovalStatus, start.Source,
            tool is { Event: "tool_completed", Status: "succeeded" } ? JsonSerializer.Deserialize<string[]>(tool.SourceKeys)! : [],
            tool?.Total, tool?.Returned,
            last.InputTokens is { } input && last.OutputTokens is { } output ? new(input, output) : null,
            events.Select(e => new AiAuditEventView(e.Event, e.Status, e.At, e.Total, e.Returned, e.Step, e.Tool)).ToArray(),
            start.CorrelationId, AiAuditRules.StepsCompleted(events));
    }

    public async Task<AiAuditPage> PageAsync(AppUser user, long? beforeId, int take, CancellationToken token, AuditSearch? search = null)
    {
        if (!Allowed(user)) throw new UnauthorizedAccessException();
        if (take is < 1 or > 100 || beforeId is <= 0) throw new ArgumentException("Invalid audit page.");
        if (search?.Problem() is { } problem) throw new ArgumentException("Invalid audit search: " + problem);
        await using var db = audit.Open();
        var query = db.AiAuditLogs.AsNoTracking().Where(e => e.Sequence == 1);
        if (beforeId.HasValue) query = query.Where(e => e.Id < beforeId.Value);
        if (search is not null)
        {
            if (search.Agent is { Length: > 0 } agent) query = query.Where(e => e.AgentId == agent);
            if (search.User is { Length: > 0 } who) query = query.Where(e => e.UserId.Contains(who.Trim()));
            if (search.From is { } from)
            {
                var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Formats.Zone);
                query = query.Where(e => e.At >= since);
            }
            if (search.To is { } to)
            {
                var until = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Formats.Zone);
                query = query.Where(e => e.At < until);
            }
            // The rest live on the run's later rows, not its start.
            if (search.Tool is { Length: > 0 } tool)
                query = query.Where(e => db.AiAuditLogs.Any(step => step.RunId == e.RunId && step.Tool == tool));
            if (search.Result == "incomplete")
                query = query.Where(e => !db.AiAuditLogs.Any(end => end.RunId == e.RunId && end.Event == "run_completed"));
            else if (search.Result is { Length: > 0 } result)
                query = query.Where(e => db.AiAuditLogs.Any(end => end.RunId == e.RunId && end.Event == "run_completed" && end.Status == result));
            if (search.Key is { Length: > 0 } key)
            {
                var quoted = "\"" + key.Trim() + "\"";
                query = query.Where(e => db.AiAuditLogs.Any(step => step.RunId == e.RunId && step.Event == "tool_completed"
                    && step.SourceKeys.Contains(quoted)));
            }
        }
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
