using System.Security.Cryptography;
using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai;

/// <summary>Application-owned audit vocabulary, independent of untrusted provider strings.</summary>
public static class AiAuditRules
{
    public static bool Id(string? value) => value is { Length: 32 } && Guid.TryParseExact(value, "N", out _);
    private static bool Text(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(char.IsControl);
    public static AiAuditLog From(AiExecutionEvent e)
    {
        var sequence = e.Event switch { "run_started" => 1, "tool_started" => 2, "tool_completed" => 3, "run_completed" => 4, _ => 0 };
        var start = sequence is 1 or 2;
        var hasTool = e.Tool is not null;
        if (!Id(e.RunId) || !Text(e.UserId, 160) || !Roles.All.Any(r => r.Name == e.Role)
            || e.Role == Roles.Subcontractor || e.AgentId != "operations-agent" || sequence == 0
            || e.Scope is null || (!e.Scope.Team && !Text(e.Scope.OperatorId, 20))
            || (e.Scope.OperatorId is not null && !Text(e.Scope.OperatorId, 20))
            || !Text(e.Model, 100) || e.Model!.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.' and not '/')
            || e.At == default || e.Usage is { InputTokens: < 0 } or { OutputTokens: < 0 })
            throw new ArgumentException("Invalid audit metadata.");
        if (start ? e.Status != "running" : e.Status is not ("succeeded" or "failed" or "cancelled" or "timeout"
            or "provider_unavailable" or "provider_busy" or "source_unavailable" or "clarification_required"
            or "invalid_tool" or "not_connected" or "audit_failed"))
            throw new ArgumentException("Invalid audit status.");
        if ((sequence == 1 && hasTool) || (sequence is 2 or 3 && !hasTool)
            || hasTool != Id(e.ToolCallId) || (!hasTool && e.ToolCallId is not null)
            || (hasTool && e.Tool is not ("query_shipments" or "search_shipment" or "query_delays")))
            throw new ArgumentException("Invalid audit tool.");
        if (hasTool ? (e.Limit is null or < 1 or > 50 || e.View is not ("today" or "risk_today" or "search" or "delays")
                || (e.Tool == "query_shipments" && e.View is not ("today" or "risk_today"))
                || (e.Tool == "search_shipment" && e.View != "search") || (e.Tool == "query_delays" && e.View != "delays"))
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
        };
        row.Fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(row)));
        return row;
    }

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
        if (first.Sequence != 1 || last.Sequence == 4 || next.Sequence <= last.Sequence
            || first.RunId != next.RunId || first.UserId != next.UserId || first.Role != next.Role
            || first.AgentId != next.AgentId || first.Model != next.Model
            || first.TeamScope != next.TeamScope || first.OperatorId != next.OperatorId || next.At < last.At)
            throw new InvalidOperationException("Invalid audit transition.");
        if (next.Sequence == 2 && last.Sequence != 1 || next.Sequence == 3 && last.Sequence != 2
            || next.Sequence == 4 && last.Sequence == 2)
            throw new InvalidOperationException("Incomplete audit tool.");
        if (next.Sequence > 2 && (last.Tool != next.Tool || last.ToolCallId != next.ToolCallId
            || last.View != next.View || last.Limit != next.Limit))
            throw new InvalidOperationException("Audit tool changed.");
        if (next.Sequence == 4 && next.Status == "succeeded"
            && (last.Sequence != 3 || last.Status != "succeeded" || last.Total != next.Total
                || last.Returned != next.Returned || last.SourceKeys != next.SourceKeys))
            throw new InvalidOperationException("Audit completion lacks evidence.");
        return true;
    }
}
