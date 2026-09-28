using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai;

public sealed record AiDecisionFindings(IReadOnlyList<AgentFinding> Facts, IReadOnlyList<AgentFinding> RuleResults,
    IReadOnlyList<AgentFinding> Observations, IReadOnlyList<AgentFinding> Inferences,
    IReadOnlyList<AgentFinding> Recommendations, IReadOnlyList<AgentFinding> BlockingIssues);

public sealed record AiDecisionView(long Id, string RunId, string AgentId, string DecisionType, string EntityType,
    string EntityId, string Summary, string ResultStatus, string Status, string RiskLevel, decimal? Confidence,
    bool RequiresApproval, bool Shadow, int Autonomy, string PromptVersion, AiDecisionFindings Findings,
    IReadOnlyList<string> RuleReferences, IReadOnlyList<string> EvidenceReferences, string HumanChoice,
    bool? HumanMatches, string OverrideReason, string DecidedBy, DateTimeOffset? DecidedAt, DateTimeOffset CreatedAt);

/// <summary>
/// Where agents write what they concluded and people write what they did about
/// it. Nothing reaches the table that fails <see cref="AgentResultRules"/>; a
/// decision is answered once; the list a person sees is the jobs they may see —
/// their own, or the team's when their role sees the team — the same scope every
/// AI read follows.
/// </summary>
public sealed class AiDecisionLog(ScmosDbContext db, AgentRegistry agents, AuditService audit, TimeProvider clock)
{
    public const string Open = "OPEN";
    public const string Accepted = "ACCEPTED";
    public const string Overridden = "OVERRIDDEN";
    public const string Dismissed = "DISMISSED";
    /// <summary>The agent's next pass concluded something else about the same thing.</summary>
    public const string Superseded = "SUPERSEDED";
    /// <summary>The agent's next pass found nothing left to conclude.</summary>
    public const string Resolved = "RESOLVED";
    public static readonly string[] Outcomes = [Accepted, Overridden, Dismissed];

    /// <summary>
    /// The finding without its moving parts: kind, outcome, risk and the rules
    /// that fired — never the minutes or the clock, which change every pass
    /// and would make one finding look like a new one each time.
    /// </summary>
    public static string FingerprintOf(AgentResult result) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join("|", result.AgentId, result.DecisionType, result.Status, result.RiskLevel ?? "",
            string.Join(",", result.RuleReferences.Order(StringComparer.Ordinal)))))).ToLowerInvariant();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Stores one result, or refuses it whole with the reasons. The run id, when given, is the audited run's.</summary>
    public async Task<(long? Id, IReadOnlyList<string> Problems)> RecordAsync(AgentResult result, string runId,
        string ownerId, bool shadow, AiAutonomy autonomy, CancellationToken token)
    {
        var (row, problems) = Stage(result, runId, ownerId, shadow, autonomy);
        if (row is null) return (null, problems);
        await db.SaveChangesAsync(token);
        return (row.Id, []);
    }

    /// <summary>
    /// Checks one result and adds it to the context without saving — for a pass
    /// that writes many and saves once. A result that fails is not added.
    /// </summary>
    public (AiDecision? Row, IReadOnlyList<string> Problems) Stage(AgentResult result, string runId, string ownerId,
        bool shadow, AiAutonomy autonomy)
    {
        var problems = AgentResultRules.Problems(result, agents.All.Select(agent => agent.Id)).ToList();
        if (runId.Length > 0 && !AiAuditRules.Id(runId)) problems.Add("runId is not a run id");
        if (ownerId.Length > 20) problems.Add("ownerId is longer than 20 characters");
        if (problems.Count > 0) return (null, problems);

        var findings = new AiDecisionFindings(result.Facts, result.RuleResults, result.Observations,
            result.Inferences, result.Recommendations, result.BlockingIssues);
        var row = new AiDecision
        {
            RunId = runId, AgentId = result.AgentId, DecisionType = result.DecisionType, EntityType = result.EntityType,
            EntityId = result.EntityId.Trim(), OwnerId = ownerId, Summary = result.Summary.Trim(), ResultStatus = result.Status,
            Status = Open, RiskLevel = result.RiskLevel ?? "", Confidence = result.Confidence, RequiresApproval = result.RequiresApproval,
            Shadow = shadow, Autonomy = (int)autonomy, PromptVersion = AiBuild.PromptVersion,
            Payload = JsonSerializer.Serialize(findings, Json),
            RuleReferences = JsonSerializer.Serialize(result.RuleReferences, Json),
            EvidenceReferences = JsonSerializer.Serialize(result.EvidenceReferences, Json),
            Fingerprint = FingerprintOf(result),
            CreatedAt = clock.GetUtcNow(),
        };
        if (row.Payload.Length > 60_000 || row.RuleReferences.Length > 4000 || row.EvidenceReferences.Length > 4000)
            return (null, ["the result is larger than the log keeps"]);
        db.AiDecisions.Add(row);
        return (row, []);
    }

    public static bool CanList(AppUser? user) => AiPermissionPolicy.Authenticated(user) && AiPermissionPolicy.InternalUser(user!)
        && user!.Can(Capability.ViewDashboard);

    public async Task<(IReadOnlyList<AiDecisionView> Items, int Total)> ListAsync(AppUser user, string? status,
        string? agentId, string? entityType, string? entityId, int page, int size, CancellationToken token)
    {
        var query = db.AiDecisions.AsNoTracking();
        // The team's decisions for a role that sees the team; otherwise the person's own jobs, and no one's without an id.
        if (!user.Can(Capability.ViewTeam))
        {
            if (user.OperatorId.Length == 0) return ([], 0);
            query = query.Where(row => row.OwnerId == user.OperatorId);
        }
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(row => row.Status == status.Trim().ToUpperInvariant());
        if (!string.IsNullOrWhiteSpace(agentId)) query = query.Where(row => row.AgentId == agentId.Trim());
        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(row => row.EntityType == entityType.Trim());
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(row => row.EntityId == entityId.Trim());
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(token);
        return (rows.Select(View).ToList(), total);
    }

    /// <summary>Who may answer a decision: a supervisor or above, or the owner of the job it is about.</summary>
    public static bool CanAnswer(AppUser? user, AiDecision row) => user is not null && AiPermissionPolicy.InternalUser(user)
        && (ApprovalPolicy.IsApprover(user) || (row.OwnerId.Length > 0 && row.OwnerId == user.OperatorId));

    /// <summary>
    /// What a person did about a decision: agreed, chose otherwise (and why), or
    /// set it aside. For a shadow decision this is the comparison autonomy is
    /// raised on, so an override needs its reason and the choice made instead.
    /// </summary>
    public async Task<string> AnswerAsync(long id, AppUser user, string? outcome, string? choice, string? reason, CancellationToken token)
    {
        var wanted = (outcome ?? "").Trim().ToUpperInvariant();
        if (!Outcomes.Contains(wanted, StringComparer.Ordinal)) return "invalid_outcome";
        var chose = (choice ?? "").Trim();
        var why = (reason ?? "").Trim();
        if (chose.Length > 400 || why.Length > 400 || chose.Any(char.IsControl) || why.Any(char.IsControl)) return "invalid_text";
        if (wanted == Overridden && (chose.Length == 0 || why.Length == 0)) return "override_needs_choice_and_reason";
        var row = await db.AiDecisions.SingleOrDefaultAsync(one => one.Id == id, token);
        if (row is null) return "not_found";
        if (!CanAnswer(user, row)) return user.Can(Capability.ViewTeam) || row.OwnerId == user.OperatorId ? "forbidden" : "not_found";
        if (row.Status != Open) return "already_answered";

        var now = clock.GetUtcNow();
        row.Status = wanted;
        row.HumanChoice = chose;
        row.HumanMatches = wanted switch { Accepted => true, Overridden => false, _ => null };
        row.OverrideReason = why;
        row.DecidedById = user.UserId;
        row.DecidedBy = user.Signature;
        row.DecidedAt = now;
        audit.Stage(user, AuditActions.Update, "ai-decision", row.Id.ToString(), $"{row.AgentId} · {row.EntityId}",
            "status", Open, wanted, why.Length > 0 ? why : chose);
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException) { return "already_answered"; }
        return "ok";
    }

    public static AiDecisionView View(AiDecision row)
    {
        AiDecisionFindings findings;
        try { findings = JsonSerializer.Deserialize<AiDecisionFindings>(row.Payload, Json) ?? Empty; }
        catch (JsonException) { findings = Empty; }
        return new(row.Id, row.RunId, row.AgentId, row.DecisionType, row.EntityType, row.EntityId, row.Summary,
            row.ResultStatus, row.Status, row.RiskLevel, row.Confidence, row.RequiresApproval, row.Shadow, row.Autonomy,
            row.PromptVersion, findings, List(row.RuleReferences), List(row.EvidenceReferences), row.HumanChoice,
            row.HumanMatches, row.OverrideReason, row.DecidedBy, row.DecidedAt, row.CreatedAt);
    }

    private static readonly AiDecisionFindings Empty = new([], [], [], [], [], []);

    private static IReadOnlyList<string> List(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }
}
