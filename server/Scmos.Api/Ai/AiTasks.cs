using Microsoft.EntityFrameworkCore;
using Scmos.Api.Ai.Carrier;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai;

/// <param name="JobsMonitored">Open jobs in the agents' window — planned yesterday to a week ahead — that this person may see.</param>
/// <param name="AiHandling">Open decisions this person may see.</param>
/// <param name="NeedsMyDecision">Of those, the ones this person may answer.</param>
/// <param name="PendingApproval">AI proposals waiting for an approver — for an approver; nobody else decides them.</param>
/// <param name="CarrierEscalation">A job nobody in the carrier order can take, or a request unanswered past the reminder window.</param>
/// <param name="AgentFailure">Agents whose circuit breaker is not closed.</param>
public sealed record AiTaskCounts(int JobsMonitored, int AiHandling, int NeedsMyDecision, int HighRisk, int Blocked,
    int InformationRequired, int CarrierEscalation, int PendingApproval, int AgentFailure);

/// <summary>
/// The AI Control Tower's summary cards and "My AI Tasks" (AI Agent Platform
/// specification §43, §44): what the agents hold for this person, counted by
/// the same scope the decision list uses — the team's for a role that sees the
/// team, otherwise the person's own jobs — so a card never counts what its list
/// would not show. Read only; every figure is a count of rows SCMOS already keeps.
/// </summary>
public sealed class AiTasksService(ScmosDbContext db, JobRegisterCache register, IAiGovernance governance, AgentRegistry agents,
    TimeProvider clock)
{
    public const string HighRisk = "HIGH";
    public const string Critical = "CRITICAL";

    /// <summary>The section of My AI Tasks a decision belongs to — the first that fits, most urgent first.</summary>
    public static string SectionOf(AiDecisionView decision) =>
        decision.ResultStatus == AgentResultRules.Blocked ? "blocked"
        : decision.RiskLevel is HighRisk or Critical ? "high_risk"
        : IsCarrierEscalation(decision.AgentId, decision.RuleReferences) ? "carrier"
        : decision.ResultStatus == AgentResultRules.InsufficientInformation ? "information"
        : "decision";

    public static bool IsCarrierEscalation(string agentId, IEnumerable<string> ruleReferences) =>
        (agentId == CarrierAgent.Id && ruleReferences.Contains("Carrier.NoEligible"))
        || (agentId == CommunicationAgent.Id && ruleReferences.Contains("Template:" + CommunicationTemplates.ConfirmationReminder));

    public async Task<AiTaskCounts> CountAsync(AppUser user, CancellationToken token)
    {
        var team = user.Can(Capability.ViewTeam);
        var approver = ApprovalPolicy.IsApprover(user);
        var mine = user.OperatorId;
        var open = db.AiDecisions.AsNoTracking().Where(row => row.Status == AiDecisionLog.Open);
        if (!team) open = mine.Length == 0 ? open.Where(_ => false) : open.Where(row => row.OwnerId == mine);

        var handling = await open.CountAsync(token);
        var decide = approver ? handling : mine.Length == 0 ? 0 : await open.CountAsync(row => row.OwnerId == mine, token);
        var high = await open.CountAsync(row => row.RiskLevel == HighRisk || row.RiskLevel == Critical, token);
        var blocked = await open.CountAsync(row => row.ResultStatus == AgentResultRules.Blocked, token);
        var information = await open.CountAsync(row => row.ResultStatus == AgentResultRules.InsufficientInformation, token);
        var noEligible = "\"Carrier.NoEligible\"";
        var reminder = $"\"Template:{CommunicationTemplates.ConfirmationReminder}\"";
        var carrier = await open.CountAsync(row =>
            (row.AgentId == CarrierAgent.Id && row.RuleReferences.Contains(noEligible))
            || (row.AgentId == CommunicationAgent.Id && row.RuleReferences.Contains(reminder)), token);

        var now = clock.GetUtcNow();
        var approvals = approver
            ? await db.Approvals.AsNoTracking().CountAsync(row => row.State == ApprovalPolicy.Pending
                && (row.ExpiresAt == null || row.ExpiresAt > now), token)
            : 0;

        var today = DateOnly.FromDateTime(now.ToOffset(Formats.Zone).DateTime);
        var monitored = (await register.ReadAsync(token, staleOk: true)).Rows.Count(row => row.Record is { } job
            && (team || (mine.Length > 0 && job.OpId == mine))
            && !JobRules.IsDone(job.Status) && !WorkspaceTabs.IsCancelled(job.Status)
            && Formats.ParseDay(job.Date) is { } day && day >= today.AddDays(-1) && day <= today.AddDays(7));

        var snapshot = await governance.SnapshotAsync(token);
        var failing = agents.All.Count(agent => snapshot.HealthOf(agent.Id).Breaker != BreakerState.Closed);

        return new AiTaskCounts(monitored, handling, decide, high, blocked, information, carrier, approvals, failing);
    }
}
