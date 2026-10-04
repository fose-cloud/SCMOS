using Scmos.Api.Auth;

namespace Scmos.Api.Ai;

/// <summary>Request construction only; all authorization decisions stay in the existing gateway.</summary>
public static class AiPolicyEntry
{
    // Binding metadata, not a permission grant: policy still decides Agent x Action x Tool.
    public static readonly System.Collections.Frozen.FrozenDictionary<string, AiAction> ToolContracts =
        System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(new Dictionary<string, AiAction>(StringComparer.Ordinal)
        {
            ["query_shipments"] = AiAction.BookingRead, ["search_shipment"] = AiAction.BookingRead,
            ["query_delays"] = AiAction.DelayDetect, ["query_followup"] = AiAction.DelayDetect,
            ["query_kpi"] = AiAction.KpiGenerate, ["query_messages"] = AiAction.CommunicationRead,
            ["query_documents"] = AiAction.DocumentRead, ["extract_document"] = AiAction.DocumentExtract,
            ["analyze_billing"] = AiAction.BillingAnalyze, ["query_repository"] = AiAction.CodeRead,
            ["read_source"] = AiAction.CodeRead, ["query_platform"] = AiAction.SystemHealthRead,
            ["draft_booking"] = AiAction.BookingCreateDraft, ["scan_otd"] = AiAction.OtdCalculate,
            ["scan_validation"] = AiAction.ValidationAnalyze, ["scan_carrier"] = AiAction.CarrierRecommend,
            ["scan_communication"] = AiAction.CommunicationDraft, ["scan_booking"] = AiAction.BookingCreateDraft,
            ["summarise_job"] = AiAction.ManagementAnalyze, ["summarise_late_paperwork"] = AiAction.ManagementAnalyze,
            ["update_shipment"] = AiAction.BookingUpdateCriticalField,
            ["request_communication_draft"] = AiAction.CommunicationDraft,
            ["summarize_evaluation"] = AiAction.ManagementAnalyze
        }, StringComparer.Ordinal);

    public static (AiAction Action, string Tool) RunContract(string agent) => agent switch
    {
        AgentIds.Operations => (AiAction.BookingRead, "query_shipments"),
        AgentIds.Data => (AiAction.KpiGenerate, "query_kpi"),
        AgentIds.Communication => (AiAction.CommunicationRead, "query_messages"),
        AgentIds.DocumentInvoice => (AiAction.DocumentRead, "query_documents"),
        AgentIds.Engineering => (AiAction.CodeRead, "query_repository"),
        AgentIds.Sre => (AiAction.SystemHealthRead, "query_platform"),
        AgentIds.Management => (AiAction.ManagementAnalyze, "summarise_job"),
        _ => ((AiAction)(-1), "unregistered_run")
    };
    public static string Group(string agent) => agent switch
    {
        AgentIds.Engineering or AgentIds.Sre => "platform",
        AgentIds.Communication => "communication",
        AgentIds.DocumentInvoice or AgentIds.Booking => "documents",
        AgentIds.Data or AgentIds.Management => "analysis",
        _ => "operations"
    };
    public static (AiAction Action, string Tool) PassContract(string agent) => agent switch
    {
        AgentIds.Otd => (AiAction.OtdCalculate, "scan_otd"),
        AgentIds.Validation => (AiAction.ValidationAnalyze, "scan_validation"),
        AgentIds.Carrier => (AiAction.CarrierRecommend, "scan_carrier"),
        AgentIds.Communication => (AiAction.CommunicationDraft, "scan_communication"),
        AgentIds.Booking => (AiAction.BookingCreateDraft, "scan_booking"),
        _ => ((AiAction)(-1), "unregistered_pass")
    };
    public static async Task<AiAuthorizationDecision> AuthorizeAsync(IAiPolicyGateway? gateway,
        AiAuthorizationRequest request, CancellationToken token)
    {
        AiAuthorizationDecision Refuse(string reason) => new(AiAuthorizationVerdict.Deny, reason, "unavailable",
            request.RiskLevel, AiPermissionLevel.Forbidden, request.CorrelationId, false);
        if (gateway is null) return Refuse("policy_gateway_unavailable");
        try { return await gateway.AuthorizeAsync(request, token); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Refuse("policy_unavailable"); }
    }
}
