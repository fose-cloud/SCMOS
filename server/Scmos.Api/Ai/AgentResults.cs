namespace Scmos.Api.Ai;

/// <summary>One line of an agent's result, and where it came from.</summary>
/// <param name="Source">The evidence it rests on — a job key, a rule id, a document id. Required on a fact and on a rule result.</param>
public sealed record AgentFinding(string Text, string? Source = null);

/// <summary>
/// What an agent concluded about one thing (AI Agent Platform specification
/// §23–§25). The five kinds of statement are separate lists on purpose: a
/// fact ("the truck left at 09:23") is not a rule result ("pick-up is planned
/// for 10:00"), and neither is an inference ("this may be late") or a
/// recommendation ("check the ETA with the carrier"). A screen that shows them
/// shows them apart; a result that mixes them cannot be written.
/// </summary>
public sealed record AgentResult(
    string AgentId,
    string DecisionType,
    string EntityType,
    string EntityId,
    string Status,
    string Summary,
    IReadOnlyList<AgentFinding> Facts,
    IReadOnlyList<AgentFinding> RuleResults,
    IReadOnlyList<AgentFinding> Observations,
    IReadOnlyList<AgentFinding> Inferences,
    IReadOnlyList<AgentFinding> Recommendations,
    IReadOnlyList<AgentFinding> BlockingIssues,
    IReadOnlyList<string> RuleReferences,
    IReadOnlyList<string> EvidenceReferences,
    string RiskLevel = "",
    decimal? Confidence = null,
    bool RequiresApproval = false);

/// <summary>
/// The schema check a result passes before anything is stored or shown (§56):
/// a result that fails is refused whole — never trimmed into shape, never
/// half-written. Pure; the Phase 0.1 checks prove it.
/// </summary>
public static class AgentResultRules
{
    public const string Completed = "COMPLETED";
    public const string Unknown = "UNKNOWN";
    public const string InsufficientInformation = "INSUFFICIENT_INFORMATION";
    public const string RequiresHumanReview = "REQUIRES_HUMAN_REVIEW";
    public const string Blocked = "BLOCKED";

    /// <summary>§25: an agent may say it does not know. These are answers, not failures.</summary>
    public static readonly string[] Statuses = [Completed, Unknown, InsufficientInformation, RequiresHumanReview, Blocked];

    /// <summary>The risk words the specification uses — the Requirement agent's and the OTD agent's (§26, §31); empty means none given.</summary>
    public static readonly string[] RiskLevels = ["", "LOW", "MEDIUM", "HIGH", "CRITICAL", "NORMAL", "WATCH"];

    /// <summary>The things a decision may be about.</summary>
    public static readonly string[] EntityTypes = ["job", "carrier", "billing-invoice", "document", "rfq", "email"];

    public const int MaxItems = 20;
    public const int MaxText = 400;
    public const int MaxReferences = 50;
    public const int MaxReference = 120;

    public static IReadOnlyList<string> Problems(AgentResult? result, IEnumerable<string> knownAgents)
    {
        var problems = new List<string>();
        if (result is null) return ["result is missing"];
        if (!knownAgents.Contains(result.AgentId, StringComparer.Ordinal)) problems.Add("agent is not registered");
        if (!Word(result.DecisionType, 40)) problems.Add("decisionType must be 1–40 lower-case letters, digits, '_' or '-'");
        if (!EntityTypes.Contains(result.EntityType, StringComparer.Ordinal)) problems.Add("entityType is not one the platform knows");
        if (!Line(result.EntityId, 80)) problems.Add("entityId must be 1–80 characters");
        if (!Statuses.Contains(result.Status, StringComparer.Ordinal)) problems.Add("status is not in the vocabulary");
        if (!Line(result.Summary, MaxText)) problems.Add("summary must be 1–400 characters");
        if (!RiskLevels.Contains(result.RiskLevel ?? "", StringComparer.Ordinal)) problems.Add("riskLevel is not in the vocabulary");
        if (result.Confidence is < 0m or > 1m) problems.Add("confidence must be between 0 and 1");

        void List(IReadOnlyList<AgentFinding>? items, string name, bool sourced)
        {
            if (items is null) { problems.Add($"{name} is missing"); return; }
            if (items.Count > MaxItems) problems.Add($"{name} holds more than {MaxItems} items");
            foreach (var item in items)
            {
                if (item is null || !Line(item.Text, MaxText)) { problems.Add($"{name}: every item is 1–400 characters"); break; }
                if (item.Source is not null && !Line(item.Source, MaxReference)) { problems.Add($"{name}: a source is 1–120 characters"); break; }
                // Evidence before inference: a fact or a rule result that cannot say where it came from is not one.
                if (sourced && item.Source is null) { problems.Add($"{name}: every item names its source"); break; }
            }
        }
        List(result.Facts, "facts", sourced: true);
        List(result.RuleResults, "ruleResults", sourced: true);
        List(result.Observations, "observations", sourced: false);
        List(result.Inferences, "inferences", sourced: false);
        List(result.Recommendations, "recommendations", sourced: false);
        List(result.BlockingIssues, "blockingIssues", sourced: false);
        References(result.RuleReferences, "ruleReferences", problems);
        References(result.EvidenceReferences, "evidenceReferences", problems);
        if (problems.Count > 0) return problems;

        // A conclusion has to rest on something. An agent that knows nothing says so — UNKNOWN or
        // INSUFFICIENT_INFORMATION — rather than recommending from nothing.
        var grounded = result.Facts.Count > 0 || result.RuleResults.Count > 0;
        if ((result.Recommendations.Count > 0 || result.Inferences.Count > 0) && !grounded)
            problems.Add("an inference or a recommendation needs at least one fact or rule result");
        if (result.Status == Completed && !grounded) problems.Add("a COMPLETED result needs at least one fact or rule result");
        if (result.Status == Blocked && result.BlockingIssues.Count == 0) problems.Add("a BLOCKED result names what blocks it");
        if (result.BlockingIssues.Count > 0 && result.Status != Blocked) problems.Add("blocking issues make the result BLOCKED");
        if (result.RequiresApproval && result.Recommendations.Count == 0) problems.Add("approval is asked for a recommendation; there is none");
        if (grounded && result.EvidenceReferences.Count == 0) problems.Add("a grounded result lists its evidence references");
        return problems;
    }

    private static void References(IReadOnlyList<string>? items, string name, List<string> problems)
    {
        if (items is null) { problems.Add($"{name} is missing"); return; }
        if (items.Count > MaxReferences) problems.Add($"{name} holds more than {MaxReferences}");
        if (items.Any(item => !Line(item, MaxReference))) problems.Add($"{name}: every reference is 1–120 characters");
    }

    private static bool Line(string? text, int max) =>
        text is not null && text.Trim().Length > 0 && text.Length <= max && !text.Any(c => char.IsControl(c) && c != '\n');

    private static bool Word(string? text, int max) =>
        text is { Length: > 0 } && text.Length <= max && text.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
}
