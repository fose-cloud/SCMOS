using Scmos.Api.Ai;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

public partial class AiGateway
{
    /// <summary>
    /// Typed communication entry for server-sourced jobs/context. The initiating
    /// request is built by the caller's server adapter, never from model identity
    /// fields. Both authorizations use the same identity, scope, object and correlation.
    /// No destination/body/credentials/transport callback is accepted; draft only.
    /// </summary>
    public static async Task<AgentResult?> DraftCommunicationAsync(IAiPolicyGateway? gateway,
        AiAuthorizationRequest initiating, CachedJobRow row, CommunicationContext context,
        DateTimeOffset now, CancellationToken token)
    {
        if (row.Record is null || string.IsNullOrWhiteSpace(row.Key)) return null;
        if (initiating.ResourceType != "shipment" || initiating.ResourceId != row.Key
            || (initiating.RequestedDataScope is { Team: false } scope && scope.OperatorId != row.Record.OpId)
            || initiating.ApprovalId is not null || initiating.NetworkDestination is not null
            || initiating.OriginAgentId is not null || initiating.DelegationChain is { Count: > 1 })
        {
            await AiPolicyEntry.AuthorizeAsync(gateway, initiating with { InputValid = false }, token);
            throw new UnauthorizedAccessException("Invalid communication origin/object binding.");
        }
        var origin = await AiPolicyEntry.AuthorizeAsync(gateway, initiating, token);
        if (!origin.Allowed) throw new UnauthorizedAccessException("Communication origin denied: " + origin.ReasonCode);
        var request = initiating with
        {
            AgentId = AgentIds.Communication, AgentVersion = AiPolicyCatalog.AgentVersion,
            Action = AiAction.CommunicationDraft, ToolId = initiating.SystemPass ? "scan_communication" : "request_communication_draft",
            OriginAgentId = initiating.AgentId, OriginalAction = initiating.Action,
            DelegationChain = initiating.AgentId == AgentIds.Communication
                ? [AgentIds.Communication] : [initiating.AgentId, AgentIds.Communication],
            RiskLevel = AiRisk.Low
        };
        var target = await AiPolicyEntry.AuthorizeAsync(gateway, request, token);
        if (!target.Allowed) throw new UnauthorizedAccessException("Communication draft denied: " + target.ReasonCode);
        // Same deterministic rules/templates already used by the scheduled pass.
        return CommunicationDrafts.Assess(row, now, context);
    }
}
