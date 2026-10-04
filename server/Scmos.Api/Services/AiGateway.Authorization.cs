using System.Diagnostics;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

public partial class AiGateway
{
    public async Task<object> PoliciesAsync(CancellationToken token)
    {
        var catalog = policies ?? AiPolicyCatalog.Current;
        var auditAvailable = true;
        var security = new Dictionary<string, DateTimeOffset>();
        var execution = new Dictionary<string, DateTimeOffset>();
        var reserved = new Dictionary<string, decimal>();
        decimal? fleetReserved = null;
        var readiness = new AgentRegistry().All.ToDictionary(a => a.Id, a => catalog.Readiness(a.Id));
        foreach (var agent in readiness.Where(entry => entry.Value is null).Select(entry => entry.Key).ToArray())
        {
            try
            {
                var manifest = catalog.Find(agent)!;
                readiness[agent] = await (owners ?? new AiOwnerDirectory(db))
                    .ValidateAsync(manifest.HumanOwner, manifest.FallbackOwner, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { readiness[agent] = "owner_directory_unavailable"; }
        }
        try
        {
            security = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                db.AiAuthorizationLogs.Where(x => x.SecurityEvent).GroupBy(x => x.AgentId)
                    .Select(x => new { Id = x.Key, At = x.Max(e => e.At) }), x => x.Id, x => x.At, token);
            execution = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                db.AiAuditLogs.Where(x => x.Event == "run_completed").GroupBy(x => x.AgentId)
                    .Select(x => new { Id = x.Key, At = x.Max(e => e.At) }), x => x.Id, x => x.At, token);
            var at = (clock ?? TimeProvider.System).GetUtcNow();
            var month = new DateTimeOffset(at.Year, at.Month, 1, 0, 0, 0, TimeSpan.Zero);
            reserved = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToDictionaryAsync(
                db.AiAuthorizationLogs.Where(x => x.At >= month && x.At < month.AddMonths(1)).GroupBy(x => x.AgentId)
                    .Select(x => new { Id = x.Key, Cost = x.Sum(e => e.ReservedCost) }), x => x.Id, x => x.Cost, token);
            fleetReserved = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SumAsync(
                db.AiAuthorizationLogs.Where(x => x.At >= month && x.At < month.AddMonths(1)), x => (decimal?)x.ReservedCost, token) ?? 0;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { auditAvailable = false; }
        return new
        {
            policyVersion = catalog.Version, valid = catalog.Valid, auditAvailable,
            catalog.ChangedBy, catalog.ChangedAt, catalog.Reason, catalog.PreviousVersion, catalog.ApprovalReference,
            readOnly = true,
            sharedBudget = catalog.SharedBudget is not { } shared ? null : new
            {
                shared.Scope, shared.Currency, shared.MonthlyCostLimit, shared.DailyCostLimit,
                reservedCostMonth = auditAvailable ? fleetReserved : null,
                remainingCostMonth = auditAvailable && fleetReserved is { } used
                    ? (decimal?)Math.Max(0m, shared.MonthlyCostLimit - used) : null,
                periodTimeZone = "UTC"
            },
            agents = new AgentRegistry().All.Select(a =>
            {
                var manifest = catalog.Find(a.Id);
                string[] Actions(AiPermissionLevel level) => Enum.GetValues<AiAction>().Where(action => catalog.Permission(a.Id, action) == level)
                    .Select(action => action.ToString()).ToArray();
                return new
                {
                    a.Id, a.Name, status = readiness[a.Id] is null ? "POLICY_READY" : "CONFIGURATION_REQUIRED",
                    reasonCode = readiness[a.Id] ?? "ready", agentVersion = manifest?.AgentVersion,
                    policyVersion = catalog.Version, allowedTools = manifest?.AllowedTools.Order(StringComparer.Ordinal).ToArray() ?? [],
                    read = Actions(AiPermissionLevel.Read), analyze = Actions(AiPermissionLevel.Analyze), draft = Actions(AiPermissionLevel.Draft),
                    execute = Actions(AiPermissionLevel.Execute), humanApproval = Actions(AiPermissionLevel.HumanApprovalRequired),
                    forbidden = Actions(AiPermissionLevel.Forbidden), budget = manifest?.Budget,
                    humanOwner = manifest?.HumanOwner, fallbackOwner = manifest?.FallbackOwner,
                    reservedCostMonth = auditAvailable ? reserved.GetValueOrDefault(a.Id) : (decimal?)null,
                    lastExecution = execution.TryGetValue(a.Id, out var ran) ? ran : (DateTimeOffset?)null,
                    lastSecurityEvent = security.TryGetValue(a.Id, out var denied) ? denied : (DateTimeOffset?)null
                };
            }).ToArray()
        };
    }

    public Task<AiAuthorizationDecision> AuthorizeAsync(AiAuthorizationRequest request, CancellationToken token)
        => AuthorizeCoreAsync(request, token);

    // Only the typed operations adapter below may supply a verified approval. Never expose a bypass flag to agents/routes.
    private async Task<AiAuthorizationDecision> AuthorizeCoreAsync(AiAuthorizationRequest request, CancellationToken token,
        bool reviewedOperations = false, string? rejection = null)
    {
        var watch = Stopwatch.StartNew();
        var catalog = policies ?? AiPolicyCatalog.Current;
        var permission = catalog.Permission(request.AgentId, request.Action);
        AiAuthorizationDecision Deny(string reason) => new(AiAuthorizationVerdict.Deny, reason, catalog.Version,
            request.RiskLevel, permission, request.CorrelationId, false);
        AiAuthorizationDecision decision;
        try
        {
            decision = Evaluate(reviewedOperations ? request with { ApprovalId = null } : request, catalog);
            permission = decision.Permission;
            if (rejection is not null) decision = Deny(rejection);
            if (decision.Allowed || decision.Decision == AiAuthorizationVerdict.HumanApprovalRequired)
            {
                try
                {
                    var manifest = catalog.Find(request.AgentId)!;
                    var directory = owners ?? new AiOwnerDirectory(db);
                    var problem = await directory.ValidateAsync(manifest.HumanOwner, manifest.FallbackOwner, token);
                    var origin = request.OriginAgentId is { } source && source != request.AgentId ? catalog.Find(source) : null;
                    if (problem is null && origin is not null)
                        problem = await directory.ValidateAsync(origin.HumanOwner, origin.FallbackOwner, token);
                    if (problem is not null) decision = Deny(problem);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception) { decision = Deny("owner_directory_unavailable"); }
            }
            if (decision.Allowed || decision.Decision == AiAuthorizationVerdict.HumanApprovalRequired)
            {
                if (aiOptions is null || governance is null) decision = Deny("governance_unavailable");
                else
                {
                    var settings = aiOptions.Value;
                    var definition = new AgentRegistry().Find(request.AgentId)!;
                    var need = permission switch
                    {
                        AiPermissionLevel.Read => AgentNeed.Run,
                        AiPermissionLevel.Analyze or AiPermissionLevel.Draft => AgentNeed.Recommend,
                        AiPermissionLevel.HumanApprovalRequired => AgentNeed.ExecuteWithApproval,
                        _ => AgentNeed.ExecuteAutonomously
                    };
                    var snapshot = await governance.SnapshotAsync(token);
                    var gate = snapshot.Gate(definition, settings.Enabled && AgentRegistry.Enabled(definition, settings), need);
                    var originId = request.OriginAgentId ?? request.AgentId;
                    if (originId != definition.Id && new AgentRegistry().Find(originId) is { } origin)
                    {
                        var originGate = snapshot.Gate(origin, settings.Enabled && AgentRegistry.Enabled(origin, settings), need);
                        if (!originGate.Allowed) gate = originGate;
                    }
                    if (!settings.Enabled || !settings.Valid) decision = Deny("ai_stopped");
                    else if (!gate.Allowed) decision = Deny(gate.Code);
                    else if (settings.DisabledTools.Contains(request.ToolId, StringComparer.Ordinal)
                        || settings.DisabledAgentGroups.Contains(AiPolicyEntry.Group(request.AgentId), StringComparer.Ordinal)
                        || settings.DisabledAgentGroups.Contains(AiPolicyEntry.Group(originId), StringComparer.Ordinal)) decision = Deny("kill_switch");
                    else if (settings.DisableExternalCommunication && AiPolicyCatalog.ExternalCommunication(request.Action)) decision = Deny("external_communication_stopped");
                    else if (settings.DisableWriteActions && permission is AiPermissionLevel.Execute or AiPermissionLevel.HumanApprovalRequired) decision = Deny("writes_stopped");
                    else if (catalog.Find(request.AgentId)?.Budget is not { } budget || settings.MaxOutputTokens > budget.MaxOutputTokens
                        || settings.TimeoutSeconds > budget.MaxRuntimeSeconds) decision = Deny("budget_configuration_invalid");
                }
            }
            if (reviewedOperations && decision.Decision == AiAuthorizationVerdict.HumanApprovalRequired)
                decision = decision with { Decision = AiAuthorizationVerdict.Allow, ReasonCode = "reviewed_operations_action" };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { decision = Deny("policy_unavailable"); }
        if (policyAudit is null) return Deny("audit_unavailable");
        try
        {
            var id = Guid.NewGuid().ToString("N");
            var security = decision.ReasonCode is "unknown_agent" or "unknown_action" or "unknown_tool" or "privilege_chaining"
                or "absolute_forbidden" or "network_forbidden" or "agent_permission_forbidden" or "invalid_identity"
                or "invalid_tool_input" or "tool_forbidden";
            security |= decision.ReasonCode is "invalid_approval" or "approval_identity_revoked"
                or "approval_transaction_required" or "self_approval_forbidden" or "approval_mfa_required";
            var entry = new AiPolicyAuditEvent(id, (clock ?? TimeProvider.System).GetUtcNow(), request, decision, security, watch.ElapsedMilliseconds);
            var refused = await policyAudit.RecordAuthorizationAsync(entry, catalog.Find(request.AgentId)?.Budget, token);
            return refused is null ? decision with { AuditId = id } : Deny(refused) with { AuditId = id };
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Deny("audit_unavailable"); }
    }

    // Pure grant checks. Runtime switches and durable audit/reservations are mandatory in AuthorizeAsync.
    public static AiAuthorizationDecision Evaluate(AiAuthorizationRequest request, AiPolicyCatalog catalog)
    {
        var permission = catalog.Permission(request.AgentId, request.Action);
        AiAuthorizationDecision Deny(string reason) => new(AiAuthorizationVerdict.Deny, reason, catalog.Version,
            request.RiskLevel, permission, request.CorrelationId, false);
        var definition = new AgentRegistry().Find(request.AgentId);
        if (definition is null) return Deny("unknown_agent");
        if (!Enum.IsDefined(request.Action)) return Deny("unknown_action");
        if (AiPolicyCatalog.AbsoluteDeny(request.Action)) return Deny("absolute_forbidden");
        if (!catalog.Valid || catalog.Find(request.AgentId) is not { } manifest) return Deny("manifest_invalid");
        if (request.AgentVersion != manifest.AgentVersion) return Deny("agent_version_mismatch");
        if (permission == AiPermissionLevel.Forbidden) return Deny("agent_permission_forbidden");
        if (!catalog.Tools.TryGetValue(request.ToolId, out var action)) return Deny("unknown_tool");
        if (action != request.Action || !manifest.AllowedTools.Contains(request.ToolId)) return Deny("tool_forbidden");
        if (!request.InputValid) return Deny("invalid_tool_input");
        if (!manifest.AllowedApiScopes.Contains(request.ApiScope)) return Deny("api_scope_forbidden");
        if (!AiAuditRules.IsCorrelation(request.CorrelationId) || request.CorrelationId.Length == 0
            || !Bounded(request.ResourceType, 60) || !Bounded(request.ResourceId, 160)) return Deny("invalid_request");
        if (request.RequestedDataScope is not { } scope) return Deny("missing_data_scope");
        if (scope.Team ? !manifest.DataScope.Team || scope.OperatorId is not null
            : !manifest.DataScope.Operator || string.IsNullOrWhiteSpace(scope.OperatorId)) return Deny("data_scope_forbidden");
        if (request.SystemPass)
        {
            if (request.User is not null || !manifest.DataScope.SystemPass || !request.ToolId.StartsWith("scan_", StringComparison.Ordinal)) return Deny("invalid_identity");
        }
        else
        {
            if (request.User is not { } user || !AiPermissionPolicy.CanUse(user, definition)) return Deny("user_forbidden");
            var userScope = AiPermissionPolicy.Scope(user);
            if (userScope is null || (scope.Team && !userScope.Team)
                || (!scope.Team && !userScope.Team && scope.OperatorId != userScope.OperatorId)) return Deny("data_scope_forbidden");
        }
        if (!Enum.IsDefined(request.RiskLevel) || request.RiskLevel == AiRisk.Restricted
            || (manifest.MaximumRiskLevel == AiRisk.Low && request.RiskLevel != AiRisk.Low)
            || (manifest.MaximumRiskLevel == AiRisk.Medium && request.RiskLevel == AiRisk.High)) return Deny("risk_forbidden");
        var origin = request.OriginAgentId ?? request.AgentId;
        if (origin != request.AgentId)
        {
            var chain = request.DelegationChain;
            var original = catalog.Find(origin);
            if (original is null || catalog.Readiness(origin) is not null || request.OriginalAction is null
                || catalog.Permission(origin, request.OriginalAction.Value) == AiPermissionLevel.Forbidden
                || catalog.Permission(origin, request.Action) == AiPermissionLevel.Forbidden
                || !original.AllowedTools.Contains(request.ToolId) || !original.AllowedApiScopes.Contains(request.ApiScope)
                || (scope.Team ? !original.DataScope.Team : !original.DataScope.Operator)
                || chain is null || chain.Count is < 2 or > 8 || chain[0] != origin || chain[^1] != request.AgentId
                || chain.Distinct(StringComparer.Ordinal).Count() != chain.Count || chain.Any(id => catalog.Find(id) is null))
                return Deny("privilege_chaining");
            // Multi-hop delegation is not implemented; intermediate agents cannot launder a permission.
            if (chain.Count != 2) return Deny("privilege_chaining");
            if (request.NetworkDestination is { } destination && !original.NetworkAllowList.Contains(destination))
                return Deny("privilege_chaining");
            permission = AiPermissionPrecedence.Restrict(permission, catalog.Permission(origin, request.Action));
        }
        else if (request.DelegationChain is { Count: > 0 } chain && (chain.Count != 1 || chain[0] != origin)) return Deny("privilege_chaining");
        if (AiPolicyCatalog.ExternalCommunication(request.Action) && request.AgentId != AgentIds.Communication) return Deny("agent_permission_forbidden");
        if (request.AgentId == AgentIds.Engineering && request.Action is AiAction.ProductionDeploy or AiAction.InfrastructureModify) return Deny("absolute_forbidden");
        if (request.NetworkDestination is { } host && !manifest.NetworkAllowList.Contains(host)) return Deny("network_forbidden");
        // The approval queue can prepare a proposal but this gateway never accepts an arbitrary approval id as execution authority.
        if (request.ApprovalId is not null) return Deny("approval_not_consumable");
        if (catalog.Readiness(request.AgentId) is { } readiness) return Deny(readiness);
        if (permission == AiPermissionLevel.HumanApprovalRequired || AiPolicyCatalog.HumanOnly(request.Action) || request.RiskLevel == AiRisk.High)
            return new(AiAuthorizationVerdict.HumanApprovalRequired, "human_approval_required", catalog.Version,
                request.RiskLevel, AiPermissionLevel.HumanApprovalRequired, request.CorrelationId, true);
        // No new execution adapter has been approved by this implementation.
        if (permission == AiPermissionLevel.Execute) return Deny("execution_adapter_not_approved");
        return new(AiAuthorizationVerdict.Allow, "allowed", catalog.Version, request.RiskLevel, permission, request.CorrelationId, false);
    }
    private static bool Bounded(string? text, int max) => !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl);
}
