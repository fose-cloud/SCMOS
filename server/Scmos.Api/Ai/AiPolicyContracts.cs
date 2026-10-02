using Scmos.Api.Auth;

namespace Scmos.Api.Ai;

// Separate from persisted AiAutonomy/AiActionLevel; these values are never compared numerically.
public enum AiPermissionLevel { Forbidden = 0, Read = 10, Analyze = 20, Draft = 30, Execute = 40, HumanApprovalRequired = 50 }
public enum AiAuthorizationVerdict { Allow, Deny, HumanApprovalRequired }
public enum AiAction
{
    BookingRead, BookingAnalyze, BookingCreateDraft, BookingCreate, BookingUpdateNormalStatus, BookingUpdateCriticalField, BookingCancel,
    CarrierRead, CarrierAnalyze, CarrierRecommend, CarrierRequestConfirmation, CarrierAssign, CarrierReassign, CarrierOverrideSequence, CarrierSuspend, CarrierBlacklist,
    OtdCalculate, SlaCalculate, DelayDetect, DelayClassify, KpiGenerate,
    CommunicationDraft, CommunicationRead, CommunicationSendInternal, CommunicationSendCarrierReminder, CommunicationSendExternal, CommunicationSendRateCommitment,
    DocumentRead, DocumentExtract, DocumentVerify, DocumentOverrideVerification,
    BillingRead, BillingAnalyze, BillingValidate, BillingApprove, BillingApproveException, CreditNoteApprove,
    RateRead, RateAnalyze, RateRecommend, RateApprove, RateMasterModify,
    IncidentRead, IncidentCreate, IncidentAnalyze, IncidentClose, CarParClose,
    EvaluationAnalyze, EvaluationRecommend, EvaluationFinalize, MasterDataRead, MasterDataModify,
    CodeRead, CodeAnalyze, CodePatch, PullRequestCreate, ProtectedBranchMerge,
    SystemHealthRead, SystemAnalyze, RunbookExecute, ProductionDeploy, InfrastructureModify,
    UserPermissionModify, AiPolicyModify, AuditModify, AuditDelete, DirectProductionSql,
    ValidationAnalyze, ManagementAnalyze
}

public static class AgentIds
{
    public const string Operations = "operations-agent", Carrier = "vendor-agent", Rate = "rate-agent", Data = "data-agent",
        Incident = "incident-agent", DocumentInvoice = "document-agent", Compliance = "compliance-agent",
        Management = "management-agent", Communication = "communication-agent", Engineering = "engineering-agent",
        Otd = "otd-agent", Validation = "validation-agent", Booking = "booking-agent", Sre = "sre-agent";
}

public sealed record AgentBudgetPolicy(int MaxOutputTokens, int MaxToolCalls, int MaxApiCalls, int MaxRetry,
    int MaxRuntimeSeconds, int MaxConcurrentTasks, int DailyRequests, decimal DailyCostLimit, decimal MonthlyCostLimit,
    decimal MaxReservationCost);
public sealed record AgentDataScope(bool Team, bool Operator, bool SystemPass);
public sealed record AgentPolicyManifest(string AgentId, string AgentVersion, string Purpose, string? HumanOwner,
    string? FallbackOwner, IReadOnlyDictionary<AiAction, AiPermissionLevel> Permissions,
    IReadOnlySet<string> AllowedTools, IReadOnlySet<string> AllowedApiScopes, IReadOnlySet<string> NetworkAllowList,
    AgentDataScope DataScope, AiRisk MaximumRiskLevel, AgentBudgetPolicy? Budget, bool AuditRequired, bool FailClosed,
    string? RuntimeIsolationApproval = null);

public sealed record AiAuthorizationRequest(string AgentId, string AgentVersion, AppUser? User, AiAction Action,
    string ToolId, string ApiScope, string ResourceType, string ResourceId, AiReadScope? RequestedDataScope,
    AiRisk RiskLevel, string CorrelationId, string? CustomerId = null, string? CarrierId = null, string? ShipmentId = null,
    string? OriginAgentId = null, AiAction? OriginalAction = null, IReadOnlyList<string>? DelegationChain = null,
    string? NetworkDestination = null, string? ApprovalId = null, bool SystemPass = false, bool InputValid = true)
{
    public string UserId => User?.UserId ?? (SystemPass ? AiAuditRules.SystemUser : "");
    public static AiAuthorizationRequest For(string agent, AiAction action, string tool, AppUser? user,
        string correlation = "", string resourceType = "agent", string? resourceId = null) =>
        new(agent, AiPolicyCatalog.AgentVersion, user, action, tool, "scmos.ai", resourceType, resourceId ?? agent,
            user is null ? null : AiPermissionPolicy.Scope(user), AiRisk.Low,
            AiAuditRules.CorrelationOf(correlation, null));
    public static AiAuthorizationRequest Pass(string agent, AiAction action, string tool) =>
        For(agent, action, tool, null) with { SystemPass = true, RequestedDataScope = new(true, null) };
}
public sealed record AiAuthorizationDecision(AiAuthorizationVerdict Decision, string ReasonCode, string MatchedPolicy,
    AiRisk RiskLevel, AiPermissionLevel Permission, string CorrelationId, bool ApprovalRequirement,
    string AuditId = "")
{
    public bool Allowed => Decision == AiAuthorizationVerdict.Allow;
}

public interface IAiPolicyGateway
{
    Task<AiAuthorizationDecision> AuthorizeAsync(AiAuthorizationRequest request, CancellationToken token);
}
public sealed record AiPolicyAuditEvent(string Id, DateTimeOffset At, AiAuthorizationRequest Request,
    AiAuthorizationDecision Decision, bool SecurityEvent, long LatencyMs);
public interface IAiPolicyAudit
{
    // A committed authorization is a conservative cost reservation. No best-effort fallback.
    Task<string?> RecordAuthorizationAsync(AiPolicyAuditEvent entry, AgentBudgetPolicy? budget, CancellationToken token);
}

public static class AiPermissionPrecedence
{
    public static AiPermissionLevel Restrict(AiPermissionLevel first, AiPermissionLevel second)
    {
        if (!Enum.IsDefined(first) || !Enum.IsDefined(second)) return AiPermissionLevel.Forbidden;
        if (first == AiPermissionLevel.Forbidden || second == AiPermissionLevel.Forbidden) return AiPermissionLevel.Forbidden;
        if (first == AiPermissionLevel.HumanApprovalRequired || second == AiPermissionLevel.HumanApprovalRequired)
            return AiPermissionLevel.HumanApprovalRequired;
        // Merging ceilings must never turn a read grant into a write grant.
        if (first == AiPermissionLevel.Read || second == AiPermissionLevel.Read) return AiPermissionLevel.Read;
        if (first == AiPermissionLevel.Analyze || second == AiPermissionLevel.Analyze) return AiPermissionLevel.Analyze;
        if (first == AiPermissionLevel.Draft || second == AiPermissionLevel.Draft) return AiPermissionLevel.Draft;
        return AiPermissionLevel.Execute;
    }
}
