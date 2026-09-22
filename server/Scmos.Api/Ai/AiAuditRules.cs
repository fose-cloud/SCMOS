using System.Security.Cryptography;
using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai;

/// <summary>
/// Application-owned audit vocabulary, independent of untrusted provider strings.
///
/// <para>
/// Since Phase 1D (20 Sep 2026) a run may hold more than one tool step:
/// <c>run_started</c> at sequence 1, then for step <i>k</i> a
/// <c>tool_started</c> at 2<i>k</i> and a <c>tool_completed</c> at 2<i>k</i>+1,
/// and <c>run_completed</c> at 2·max(<i>N</i>,1)+2 where <i>N</i> is the
/// number of steps taken — so a run with one step or none still ends at 4,
/// exactly as every run before 1D did, and the history reads unchanged.
/// Every event of a run carries the same correlation id, the one the API
/// request came in with. The dispatch budget still allows one step; the
/// topology is ready for more before anything is allowed to take more.
/// </para>
/// </summary>
public static class AiAuditRules
{
    /// <summary>The agents whose runs may be written to the audit — a connected agent, not a registry descriptor. The Data Agent since Phase 2.</summary>
    public static readonly string[] KnownAgents = ["operations-agent", "data-agent", "communication-agent", "document-agent", "engineering-agent", "sre-agent"];

    /// <summary>The read tools a step may name, with the views each may use.</summary>
    public static readonly string[] KnownTools = ["query_shipments", "search_shipment", "query_delays", "query_followup", "query_kpi", "query_messages", "query_documents", "extract_document", "query_repository", "read_source", "query_platform"];

    /// <summary>The platform tool's views (Phase 7).</summary>
    public static readonly string[] PlatformViews = ["health", "deployments", "errors"];

    /// <summary>The source read's modes (Phase 6, second increment) — a step lists a directory or reads a file window.</summary>
    public static readonly string[] SourceViews = ["list", "file"];

    /// <summary>The documents tool's views (Phase 5), and the extractor's — a job category, as the Workspace's document reader takes it.</summary>
    public static readonly string[] DocumentViews = ["job", "missing", "invoice", "expiring"];
    public static readonly string[] ExtractViews = ["import", "export", "delivery"];

    /// <summary>The messages tool's views (Phase 4).</summary>
    public static readonly string[] MessageViews = ["job", "waiting", "unmatched", "today"];

    /// <summary>The follow-up tool's views (Phase 3).</summary>
    public static readonly string[] FollowUpViews = ["missing_truck", "no_carrier", "unreported", "container_mismatch"];

    /// <summary>The most tool steps one run may hold — a bound on the audit, not a licence for the dispatcher.</summary>
    public const int MaxSteps = 8;

    public static bool Id(string? value) => value is { Length: 32 } && Guid.TryParseExact(value, "N", out _);
    private static bool Text(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);

    /// <summary>A correlation id as the API accepts one: 1–64 ASCII letters, digits, '.', '-', '_' or ':' — the shape of a trace identifier. Empty is allowed for rows from before 1D.</summary>
    public static bool IsCorrelation(string? value) =>
        value is not null && value.Length <= 64
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' or ':');

    /// <summary>The correlation id for a request: the header when it is well-formed, otherwise the server's own trace identifier (trimmed to shape), otherwise a fresh id.</summary>
    public static string CorrelationOf(string? header, string? traceIdentifier)
    {
        var text = (header ?? "").Trim();
        if (text.Length > 0 && IsCorrelation(text)) return text;
        var trace = (traceIdentifier ?? "").Trim();
        if (trace.Length > 0 && IsCorrelation(trace)) return trace;
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>The sequence an event takes in its run — see the class remarks. A tool event with no step named is step 1, as every event before 1D was.</summary>
    public static int SequenceOf(string @event, int? step) => @event switch
    {
        "run_started" => 1,
        "tool_started" when (step ?? 1) is >= 1 and <= MaxSteps => 2 * (step ?? 1),
        "tool_completed" when (step ?? 1) is >= 1 and <= MaxSteps => 2 * (step ?? 1) + 1,
        "run_completed" when step is null or (>= 0 and <= MaxSteps) => 2 * Math.Max(step ?? 0, 1) + 2,
        _ => 0,
    };

    public static AiAuditLog From(AiExecutionEvent e)
    {
        var sequence = SequenceOf(e.Event, e.Step);
        var start = e.Event is "run_started" or "tool_started";
        var hasTool = e.Tool is not null;
        if (!Id(e.RunId) || !Text(e.UserId, 160) || !Roles.All.Any(r => r.Name == e.Role)
            || e.Role == Roles.Subcontractor || !KnownAgents.Contains(e.AgentId, StringComparer.Ordinal) || sequence == 0
            || (e.Event == "run_started" && e.Step is not (null or 0))
            || e.Scope is null || (!e.Scope.Team && !Text(e.Scope.OperatorId, 20))
            || (e.Scope.OperatorId is not null && !Text(e.Scope.OperatorId, 20))
            || !Text(e.Model, 100) || e.Model!.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.' and not '/')
            || e.At == default || e.Usage is { InputTokens: < 0 } or { OutputTokens: < 0 }
            || !IsCorrelation(e.CorrelationId))
            throw new ArgumentException("Invalid audit metadata.");
        if (start ? e.Status != "running" : e.Status is not ("succeeded" or "failed" or "cancelled" or "timeout"
            or "provider_unavailable" or "provider_busy" or "source_unavailable" or "clarification_required"
            or "invalid_tool" or "not_connected" or "audit_failed"))
            throw new ArgumentException("Invalid audit status.");
        var toolEvent = e.Event is "tool_started" or "tool_completed";
        if ((e.Event == "run_started" && hasTool) || (toolEvent && !hasTool)
            || hasTool != Id(e.ToolCallId) || (!hasTool && e.ToolCallId is not null)
            || (hasTool && !KnownTools.Contains(e.Tool, StringComparer.Ordinal)))
            throw new ArgumentException("Invalid audit tool.");
        var followUp = e.View is not null && FollowUpViews.Contains(e.View, StringComparer.Ordinal);
        // "today" is a view of two tools and "job" of two more; the tool says which vocabulary it speaks.
        var messages = e.Tool == "query_messages";
        var engineering = e.Tool == "query_repository";
        var engineeringView = e.View is "open_issues" or "open_prs" or "recent_commits";
        var sourceRead = e.Tool == "read_source";
        var sourceView = e.View is not null && SourceViews.Contains(e.View, StringComparer.Ordinal);
        var platform = e.Tool == "query_platform";
        var platformView = e.View is not null && PlatformViews.Contains(e.View, StringComparer.Ordinal);
        var messageView = e.View is not null && MessageViews.Contains(e.View, StringComparer.Ordinal);
        var documents = e.Tool == "query_documents";
        var documentView = e.View is not null && DocumentViews.Contains(e.View, StringComparer.Ordinal);
        var extract = e.Tool == "extract_document";
        var extractView = e.View is not null && ExtractViews.Contains(e.View, StringComparer.Ordinal);
        if (hasTool ? (e.Limit is null or < 1 or > 50
                || (messages ? !messageView : documents ? !documentView : extract ? !extractView
                    : engineering ? !engineeringView || e.Limit > 20
                    : sourceRead ? !sourceView
                    : platform ? !platformView
                    : (e.View is not ("today" or "risk_today" or "search" or "delays" or "kpi") && !followUp))
                || (e.Tool == "query_shipments" && e.View is not ("today" or "risk_today"))
                || (e.Tool == "search_shipment" && e.View != "search") || (e.Tool == "query_delays" && e.View != "delays")
                || (e.Tool == "query_followup" != followUp)
                || (e.Tool == "query_kpi" && e.View != "kpi") || (e.View == "kpi" && e.Tool != "query_kpi")
                || (engineeringView && !engineering))
            : e.View is not null || e.Limit is not null)
            throw new ArgumentException("Invalid audit summary.");
        var keys = e.SourceKeys ?? [];
        if (e.Status == "succeeded")
        {
            if (!hasTool || e.Total is null or < 0 || e.Returned is null or < 0 or > 50
                || e.Total < e.Returned || e.Returned > e.Limit || keys.Length != e.Returned
                || keys.Distinct(StringComparer.Ordinal).Count() != keys.Length || keys.Any(k => !Text(k, 80)))
                throw new ArgumentException("Invalid audit evidence.");
        }
        else if (e.Total is not null || e.Returned is not null || keys.Length != 0)
            throw new ArgumentException("Unexpected audit evidence.");
        // JSON escaping can expand identifiers: fail closed rather than truncate source references.
        var sourceKeys = JsonSerializer.Serialize(keys);
        if (sourceKeys.Length > 6000) throw new ArgumentException("Audit references too large.");
        var row = new AiAuditLog
        {
            RunId = e.RunId, Sequence = sequence, UserId = e.UserId, Role = e.Role, AgentId = e.AgentId,
            Event = e.Event, Status = e.Status, At = e.At.ToUniversalTime(), TeamScope = e.Scope.Team,
            OperatorId = e.Scope.Team ? null : e.Scope.OperatorId, Tool = e.Tool, ToolCallId = e.ToolCallId,
            Model = e.Model!, View = e.View, Limit = e.Limit, Total = e.Total, Returned = e.Returned,
            InputTokens = e.Usage?.InputTokens, OutputTokens = e.Usage?.OutputTokens, SourceKeys = sourceKeys,
            Source = e.AgentId switch
            {
                "engineering-agent" => "github_public_repo",
                "sre-agent" => "platform",
                "document-agent" => "documents",
                "communication-agent" => "line_events+emails",
                _ => "operation_jobs",
            },
            CorrelationId = e.CorrelationId, Step = toolEvent ? e.Step ?? 1 : e.Event == "run_completed" ? e.Step : null,
        };
        row.Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row)));
        return row;
    }

    /// <summary>How many tool steps a run has completed, from its rows so far.</summary>
    public static int StepsCompleted(IReadOnlyList<AiAuditLog> rows) => rows.Count(r => r.Event == "tool_completed");

    /// <returns>False only for an exact idempotent replay; conflicting replays are rejected.</returns>
    public static bool MayAppend(IReadOnlyList<AiAuditLog> prior, AiAuditLog next)
    {
        var repeated = prior.SingleOrDefault(e => e.Sequence == next.Sequence);
        if (repeated is not null)
        {
            if (repeated.Fingerprint != next.Fingerprint) throw new InvalidOperationException("Conflicting audit replay.");
            return false;
        }
        if (prior.Count == 0)
        {
            if (next.Sequence != 1) throw new InvalidOperationException("Audit run has no start.");
            return true;
        }
        var first = prior[0];
        var last = prior[^1];
        if (first.Sequence != 1 || last.Event == "run_completed" || next.Sequence <= last.Sequence
            || first.RunId != next.RunId || first.UserId != next.UserId || first.Role != next.Role
            || first.AgentId != next.AgentId || first.Model != next.Model
            || first.TeamScope != next.TeamScope || first.OperatorId != next.OperatorId || next.At < last.At
            || first.CorrelationId != next.CorrelationId)
            throw new InvalidOperationException("Invalid audit transition.");
        var steps = StepsCompleted(prior);
        switch (next.Event)
        {
            case "tool_started":
                // A step starts after the run started or after the previous step completed, never over a started one.
                if (last.Event is not ("run_started" or "tool_completed") || next.Step != steps + 1)
                    throw new InvalidOperationException("Incomplete audit tool.");
                break;
            case "tool_completed":
                if (last.Event != "tool_started" || last.Step != next.Step)
                    throw new InvalidOperationException("Incomplete audit tool.");
                if (last.Tool != next.Tool || last.ToolCallId != next.ToolCallId
                    || last.View != next.View || last.Limit != next.Limit)
                    throw new InvalidOperationException("Audit tool changed.");
                break;
            case "run_completed":
                if (last.Event == "tool_started") throw new InvalidOperationException("Incomplete audit tool.");
                // A completion that names its step count must name the right one; one that does not (pre-1D) is a one-step run.
                if (next.Step is { } named ? named != steps : steps > 1) throw new InvalidOperationException("Audit step count mismatch.");
                if (next.Status == "succeeded"
                    && (last.Event != "tool_completed" || last.Status != "succeeded" || last.Total != next.Total
                        || last.Returned != next.Returned || last.SourceKeys != next.SourceKeys))
                    throw new InvalidOperationException("Audit completion lacks evidence.");
                break;
            default:
                throw new InvalidOperationException("Invalid audit transition.");
        }
        return true;
    }
}
