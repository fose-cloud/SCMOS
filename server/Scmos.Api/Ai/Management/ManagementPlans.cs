using System.Text.Json;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Documents;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai.Management;

/// <summary>
/// One step of a plan: which specialist's read it is, and how its arguments
/// are built — from the plan's own arguments, or from what the step before
/// it found. The model never writes a step's arguments; the server does.
/// </summary>
public sealed record PlanStep(string Tool, string View, string Purpose);

/// <summary>
/// A plan the Management Agent may run: a fixed sequence of specialists'
/// reads, each authorised on its own, composed by server code. The model
/// selects a plan the way another agent selects a tool; it cannot compose
/// one, reorder one, or pass a step an argument of its own.
/// </summary>
public sealed record ManagementPlan(string Name, string Title, string Description, AiInputSchema Schema, IReadOnlyList<PlanStep> Steps);

/// <summary>
/// The accepted plans — Phase 8's first increment (22 Sep 2026): bounded
/// multi-step orchestration over adapters that already exist, never over a
/// tool that does not. Two plans, three specialists, at most three steps.
/// Adding a plan is adding a row here and a permission in the catalogue;
/// nothing in the runtime is generic over "any tool in any order".
/// </summary>
public static class ManagementPlans
{
    public const string JobPlan = "summarise_job";
    public const string LatePaperworkPlan = "summarise_late_paperwork";
    /// <summary>The most reads a plan may take — a bound on the plans, well under the audit's eight.</summary>
    public const int MaxSteps = 3;
    /// <summary>How many jobs a search may match before the question is sent back for a clearer name.</summary>
    public const int SearchLimit = 5;
    /// <summary>The late-paperwork plan reads the paperwork this many days back by plan date — the documents tool's own default.</summary>
    public const int PaperworkDays = DocumentsReadService.DefaultDays;
    public const int EvidenceLimit = 50;

    public static readonly ManagementPlan[] All =
    [
        new(JobPlan, "สรุปงานหนึ่งงาน",
            "Summarise one job across specialists: the job's standing in the register (Operations), the paperwork filed and still owed for it (Documents), and what the carrier said about it in LINE, from their TMS and in linked mail (Communication). "
            + "query = the job number, container or customer name the user gives. Three read-only steps, each under its own authorisation; nothing is written, sent or approved.",
            new AiInputSchema(new AiArgument("query", false, Max: 120)),
            [
                new("search_shipment", "search", "หางานจากเลขงาน ตู้ หรือลูกค้า"),
                new(DocumentsReadService.Tool, "job", "เอกสารที่มีและที่ยังขาดของงานนั้น"),
                new(MessagesReadService.Tool, "job", "ข้อความจากผู้ขนส่งเกี่ยวกับงานนั้น"),
            ]),
        new(LatePaperworkPlan, "งานล่าช้าที่เอกสารยังไม่ครบ",
            "Cross two specialists' reads: the active jobs in the My Job DELAY bucket (Operations) and the jobs short of required paperwork by plan date (Documents), and name the jobs in both. "
            + "limit = how many delayed jobs to read (50 unless the user asks for fewer). The overlap is a fact about two lists, not a cause: the plan never says one explains the other.",
            new AiInputSchema(new AiArgument("limit", true, Max: EvidenceLimit)),
            [
                new("query_delays", "delays", "งานที่อยู่ในกล่อง DELAY ของ My Job"),
                new(DocumentsReadService.Tool, "missing", "งานที่เอกสารยังไม่ครบตามวันแผน"),
            ]),
    ];

    public static ManagementPlan? Find(string name) => All.FirstOrDefault(plan => plan.Name == name);

    /// <summary>A plan as the model is offered it: a function with the plan's own schema, owned by the Management Agent, with no handler of its own.</summary>
    public static AiToolDefinition Offer(ManagementPlan plan) =>
        new(plan.Name, plan.Description, ManagementAgent.Id, Rules.Capability.ViewDashboard, AiRisk.Low, plan.Schema, null);

    /// <summary>
    /// The arguments of a step, built by the server from the plan's arguments
    /// and the key the first step found — the shapes the specialists' own
    /// schemas accept, and nothing the model wrote.
    /// </summary>
    public static JsonElement Arguments(ManagementPlan plan, int step, JsonElement planArguments, string? foundKey)
    {
        var definition = plan.Steps[step - 1];
        object arguments = (plan.Name, definition.Tool) switch
        {
            (JobPlan, "search_shipment") => new { query = planArguments.GetProperty("query").GetString()!.Trim(), limit = SearchLimit },
            (JobPlan, DocumentsReadService.Tool) => new { view = "job", query = Key(foundKey), days = (int?)null, limit = EvidenceLimit },
            (JobPlan, MessagesReadService.Tool) => new { view = "job", query = Key(foundKey), days = (int?)null, limit = EvidenceLimit },
            (LatePaperworkPlan, "query_delays") => new { limit = planArguments.GetProperty("limit").GetInt32() },
            (LatePaperworkPlan, DocumentsReadService.Tool) => new { view = "missing", query = (string?)null, days = PaperworkDays, limit = EvidenceLimit },
            _ => throw new InvalidOperationException("Unknown plan step."),
        };
        return JsonSerializer.SerializeToElement(arguments);
    }

    private static string Key(string? found) => string.IsNullOrWhiteSpace(found) ? throw new InvalidOperationException("A keyed step needs the job the first step found.") : found;
}

/// <summary>One step as it was run: whose read it was, what it read, and what it returned — the audit's own facts, repeated on the answer.</summary>
public sealed record CollaborationStep(int Step, string AgentId, string Tool, string View, string Purpose, int Total, int Returned, bool Truncated, string Status);

/// <summary>A fact the server composed from the steps — a count or a job — never a conclusion about why.</summary>
public sealed record CollaborationFinding(string Id, string Label, string Value, string Detail, IReadOnlyList<string> JobKeys);

/// <summary>
/// The Management Agent's answer (Phase 8): the plan that ran, every step
/// with its outcome, and the findings the server composed from them. The
/// specialists' own answers travel beside it in the response, each in the
/// slot its own agent would have used, so the screen shows the same cards.
/// </summary>
public sealed record CollaborationAnswer(string Plan, string Title, int Steps, IReadOnlyList<CollaborationStep> Trail,
    IReadOnlyList<CollaborationFinding> Findings, DateTimeOffset RetrievedAt, string Basis);
