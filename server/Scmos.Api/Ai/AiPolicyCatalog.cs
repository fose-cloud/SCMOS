using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Scmos.Api.Ai;

/// <summary>Reviewed, immutable, version-controlled policy. Unknown or malformed grants never start an agent.</summary>
public sealed class AiPolicyCatalog
{
    public const string AgentVersion = "governance-1";
    public static readonly AiPolicyCatalog Current = LoadEmbedded();
    public string Version { get; private init; } = "invalid";
    public string ChangedBy { get; private init; } = "";
    public string ChangedAt { get; private init; } = "";
    public string Reason { get; private init; } = "";
    public string? PreviousVersion { get; private init; }
    public string? ApprovalReference { get; private init; }
    public bool Valid { get; private init; }
    public IReadOnlyDictionary<string, AgentPolicyManifest> Manifests { get; private init; } = FrozenDictionary<string, AgentPolicyManifest>.Empty;
    public IReadOnlyDictionary<string, AiAction> Tools { get; private init; } = FrozenDictionary<string, AiAction>.Empty;
    public AgentPolicyManifest? Find(string id) => Manifests.GetValueOrDefault(id);
    public AiPermissionLevel Permission(string id, AiAction action) => Enum.IsDefined(action)
        ? Find(id)?.Permissions.GetValueOrDefault(action, AiPermissionLevel.Forbidden) ?? AiPermissionLevel.Forbidden
        : AiPermissionLevel.Forbidden;

    public string? Readiness(string agentId)
    {
        if (!Valid || Find(agentId) is not { } manifest) return "manifest_invalid";
        if (string.IsNullOrWhiteSpace(ApprovalReference)) return "policy_review_required";
        if (string.IsNullOrWhiteSpace(manifest.HumanOwner) || string.IsNullOrWhiteSpace(manifest.FallbackOwner)) return "owner_required";
        if (!ValidBudget(manifest.Budget))
            return "budget_required";
        // No deployment may silently enable a process without reviewed isolation evidence.
        return string.IsNullOrWhiteSpace(manifest.RuntimeIsolationApproval) ? "runtime_isolation_review_required" : null;
    }
    public static bool ValidBudget(AgentBudgetPolicy? budget) => budget is
        { MaxOutputTokens: >= 64 and <= 2000, MaxToolCalls: >= 1 and <= 8, MaxApiCalls: >= 1 and <= 8,
          MaxRetry: 0, MaxRuntimeSeconds: >= 1 and <= 60, MaxConcurrentTasks: >= 1 and <= 4, DailyRequests: > 0 }
        && Amount(budget.DailyCostLimit) && Amount(budget.MonthlyCostLimit) && Amount(budget.MaxReservationCost)
        && budget.MaxReservationCost <= budget.DailyCostLimit && budget.DailyCostLimit <= budget.MonthlyCostLimit;
    private static bool Amount(decimal value) => value is >= 0.000001m and <= 999999999999m && decimal.Round(value, 6) == value;

    public static bool AbsoluteDeny(AiAction action) => !Enum.IsDefined(action) || action is
        AiAction.UserPermissionModify or AiAction.AiPolicyModify or AiAction.AuditModify or AiAction.AuditDelete or AiAction.DirectProductionSql;
    public static bool ExternalCommunication(AiAction action) => action is AiAction.CommunicationSendCarrierReminder
        or AiAction.CommunicationSendExternal or AiAction.CommunicationSendRateCommitment;
    public static bool HumanOnly(AiAction action) => action is AiAction.BookingCreate or AiAction.BookingUpdateNormalStatus
        or AiAction.BookingUpdateCriticalField or AiAction.BookingCancel or AiAction.CarrierAssign or AiAction.CarrierReassign
        or AiAction.CarrierOverrideSequence or AiAction.CarrierSuspend or AiAction.CarrierBlacklist
        or AiAction.CommunicationSendExternal or AiAction.CommunicationSendRateCommitment
        or AiAction.DocumentOverrideVerification or AiAction.BillingApprove or AiAction.BillingApproveException
        or AiAction.CreditNoteApprove or AiAction.RateApprove or AiAction.RateMasterModify or AiAction.IncidentClose
        or AiAction.CarParClose or AiAction.EvaluationFinalize or AiAction.MasterDataModify or AiAction.ProtectedBranchMerge
        or AiAction.ProductionDeploy or AiAction.InfrastructureModify;

    private static AiPolicyCatalog LoadEmbedded()
    {
        using var stream = typeof(AiPolicyCatalog).Assembly.GetManifestResourceStream("Scmos.Api.Ai.Policy.permission-matrix.json");
        if (stream is null) return new();
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static AiPolicyCatalog Parse(string json)
    {
        try
        {
            // JsonSerializer otherwise accepts duplicate properties (last value wins).
            using var document = JsonDocument.Parse(json, new() { MaxDepth = 12 });
            if (!Unique(document.RootElement)) return new();
            // Enum dictionary keys must be canonical action names, not numeric aliases such as "0".
            if (!document.RootElement.TryGetProperty("agents", out var manifests) || manifests.ValueKind != JsonValueKind.Array)
                return new();
            foreach (var manifest in manifests.EnumerateArray())
            {
                if (!manifest.TryGetProperty("permissions", out var grants) || grants.ValueKind != JsonValueKind.Object
                    || grants.EnumerateObject().Any(p => !Enum.GetNames<AiAction>().Contains(p.Name, StringComparer.Ordinal)))
                    return new();
            }
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
            };
            var policy = JsonSerializer.Deserialize<PolicyFile>(json, options);
            if (policy is null || !AiAuditRules.IsPromptVersion(policy.PolicyVersion)
                || string.IsNullOrWhiteSpace(policy.ChangedBy) || string.IsNullOrWhiteSpace(policy.Reason)
                || !DateTimeOffset.TryParse(policy.ChangedAt, out _)
                || policy.Agents is null || policy.Tools is null) return new();
            var ids = new AgentRegistry().All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
            if (policy.Agents.Length != ids.Count || policy.Agents.Any(a => a is null || !ids.Remove(a.AgentId)) || ids.Count != 0) return new();
            if (policy.Tools.Any(t => !AiPolicyEntry.ToolContracts.TryGetValue(t.Key, out var registered) || registered != t.Value)) return new();
            foreach (var agent in policy.Agents)
            {
                if (agent.AgentVersion != AgentVersion || string.IsNullOrWhiteSpace(agent.Purpose)
                    || !agent.AuditRequired || !agent.FailClosed || !Enum.IsDefined(agent.MaximumRiskLevel)
                    || agent.Permissions is null || agent.Permissions.Any(p => !Enum.IsDefined(p.Key) || !Enum.IsDefined(p.Value))
                    || agent.AllowedTools is null || agent.AllowedApiScopes is null || agent.NetworkAllowList is null || agent.DataScope is null
                    || agent.AllowedApiScopes.Length == 0 || agent.AllowedApiScopes.Any(string.IsNullOrWhiteSpace)
                    || agent.Budget is not null && !ValidBudget(agent.Budget)
                    || agent.AllowedTools.Distinct(StringComparer.Ordinal).Count() != agent.AllowedTools.Length
                    || agent.AllowedTools.Any(t => !policy.Tools.ContainsKey(t))
                    || agent.NetworkAllowList.Any(h => !Uri.CheckHostName(h).Equals(UriHostNameType.Dns))
                    || agent.Permissions.Any(p => AbsoluteDeny(p.Key) && p.Value != AiPermissionLevel.Forbidden)
                    || agent.Permissions.Any(p => HumanOnly(p.Key) && p.Value is not (AiPermissionLevel.Forbidden or AiPermissionLevel.HumanApprovalRequired))
                    || (agent.AgentId != AgentIds.Communication && agent.Permissions.Any(p => ExternalCommunication(p.Key) && p.Value != AiPermissionLevel.Forbidden))
                    || (agent.AgentId == AgentIds.Engineering && agent.Permissions.GetValueOrDefault(AiAction.ProductionDeploy) != AiPermissionLevel.Forbidden)) return new();
            }
            return new()
            {
                Valid = true, Version = policy.PolicyVersion, ChangedBy = policy.ChangedBy, ChangedAt = policy.ChangedAt,
                Reason = policy.Reason, PreviousVersion = policy.PreviousVersion, ApprovalReference = policy.ApprovalReference,
                Tools = policy.Tools.ToFrozenDictionary(StringComparer.Ordinal),
                Manifests = policy.Agents.ToFrozenDictionary(a => a.AgentId, a => new AgentPolicyManifest(a.AgentId, a.AgentVersion,
                    a.Purpose, a.HumanOwner, a.FallbackOwner, a.Permissions.ToFrozenDictionary(), a.AllowedTools.ToFrozenSet(StringComparer.Ordinal),
                    a.AllowedApiScopes.ToFrozenSet(StringComparer.Ordinal), a.NetworkAllowList.ToFrozenSet(StringComparer.Ordinal),
                    a.DataScope, a.MaximumRiskLevel, a.Budget, a.AuditRequired, a.FailClosed, a.RuntimeIsolationApproval), StringComparer.Ordinal)
            };
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or NullReferenceException)
        { return new(); }
    }

    private static bool Unique(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => value.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == value.EnumerateObject().Count()
            && value.EnumerateObject().All(p => Unique(p.Value)),
        JsonValueKind.Array => value.EnumerateArray().All(Unique),
        _ => true
    };
    private sealed record PolicyFile(string PolicyVersion, string ChangedBy, string ChangedAt, string Reason,
        string? PreviousVersion, string? ApprovalReference, Dictionary<string, AiAction> Tools, ManifestFile[] Agents);
    private sealed record ManifestFile(string AgentId, string AgentVersion, string Purpose, string? HumanOwner,
        string? FallbackOwner, Dictionary<AiAction, AiPermissionLevel> Permissions, string[] AllowedTools,
        string[] AllowedApiScopes, string[] NetworkAllowList, AgentDataScope DataScope, AiRisk MaximumRiskLevel,
        AgentBudgetPolicy? Budget, bool AuditRequired, bool FailClosed, string? RuntimeIsolationApproval);
}
