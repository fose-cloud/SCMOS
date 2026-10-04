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

/// <summary>
/// The AI history's search (AI Agent Platform specification §47): by day (Bangkok), what kind of
/// decision, words in its summary or its job key, the customer or carrier of the job it is about, and who
/// answered it. Every text is matched as it is written, case aside; a blank is no filter.
/// </summary>
public sealed record DecisionSearch(DateOnly? From = null, DateOnly? To = null, string? Type = null, string? Text = null,
    string? Customer = null, string? Carrier = null, string? DecidedBy = null)
{
    public const int MaxText = 80;

    /// <summary>Why the search cannot be run as asked, or null.</summary>
    public string? Problem() =>
        From is { } from && To is { } to && to < from ? "the range ends before it starts"
        : From is { } start && To is { } end && end.DayNumber - start.DayNumber > 366 ? "the range is longer than a year"
        : new[] { Type, Text, Customer, Carrier, DecidedBy }.Any(one => one is { } value && (value.Length > MaxText || value.Any(char.IsControl)))
            ? "a filter is too long or holds control characters"
        : null;
}

public sealed record AiDecisionView(long Id, string RunId, string AgentId, string DecisionType, string EntityType,
    string EntityId, string Summary, string ResultStatus, string Status, string RiskLevel, decimal? Confidence,
    bool RequiresApproval, bool Shadow, int Autonomy, string PromptVersion, AiDecisionFindings Findings,
    IReadOnlyList<string> RuleReferences, IReadOnlyList<string> EvidenceReferences, string HumanChoice,
    bool? HumanMatches, string OverrideReason, string DecidedBy, DateTimeOffset? DecidedAt, DateTimeOffset CreatedAt,
    // Whether the person reading the list may answer it (CanAnswer) — so the screen offers no button the server refuses.
    bool CanAnswer = false);

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

    /// <summary>
    /// A finding about a message from somebody's own mailbox (4 Oct 2026) — the Booking Agent's draft — carries that
    /// person as its owner, and is seen by them and by Supervisor and above, never by the rest of the team: the same
    /// line the Communication Center draws (<c>MailVisibility</c>). Every other finding is listed as before.
    /// </summary>
    public static IQueryable<AiDecision> WithoutOthersMail(IQueryable<AiDecision> rows, AppUser user)
    {
        if (ApprovalPolicy.IsApprover(user)) return rows;
        var me = user.OperatorId;
        return rows.Where(row => row.EntityType != "email" || row.OwnerId == "" || row.OwnerId == me);
    }

    public async Task<(IReadOnlyList<AiDecisionView> Items, int Total)> ListAsync(AppUser user, string? status,
        string? agentId, string? entityType, string? entityId, int page, int size, CancellationToken token,
        DecisionSearch? search = null)
    {
        var query = WithoutOthersMail(db.AiDecisions.AsNoTracking(), user);
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
        if (search is not null)
        {
            if (search.From is { } from)
            {
                var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), Formats.Zone);
                query = query.Where(row => row.CreatedAt >= since);
            }
            if (search.To is { } to)
            {
                var until = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue), Formats.Zone);
                query = query.Where(row => row.CreatedAt < until);
            }
            if (Clean(search.Type) is { } type) query = query.Where(row => row.DecisionType == type);
            if (Clean(search.Text) is { } text) query = query.Where(row => row.Summary.Contains(text) || row.EntityId.Contains(text));
            if (Clean(search.DecidedBy) is { } by) query = query.Where(row => row.DecidedBy.Contains(by) || row.HumanChoice.Contains(by));
            // The customer and the carrier are the job's, read from the register row the decision is about.
            if (Clean(search.Customer) is { } customer)
                query = query.Where(row => row.EntityType == "job"
                    && db.OperationJobs.Any(job => job.Key == row.EntityId && job.Customer.Contains(customer)));
            if (Clean(search.Carrier) is { } carrier)
                query = query.Where(row => row.EntityType == "job"
                    && db.OperationJobs.Any(job => job.Key == row.EntityId && job.Trucker.Contains(carrier)));
        }
        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(row => row.CreatedAt).ThenByDescending(row => row.Id)
            .Skip((page - 1) * size).Take(size).ToListAsync(token);
        return (rows.Select(row => View(row) with { CanAnswer = CanAnswer(user, row) }).ToList(), total);
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

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
