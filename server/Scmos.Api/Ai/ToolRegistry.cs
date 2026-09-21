using System.Text.Json;
using Scmos.Api.Rules;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Data;
using Scmos.Api.Ai.Operations;

namespace Scmos.Api.Ai;

public sealed record AiArgument(string Name, bool IsInteger, bool Nullable = false, int Max = 120,
    string[]? Choices = null);

/// <summary>One schema source for both provider declaration and server-side validation.</summary>
public sealed class AiInputSchema(params AiArgument[] fields)
{
    private readonly AiArgument[] _fields = fields.ToArray();
    public string Json => JsonSerializer.Serialize(new
    {
        type = "object",
        additionalProperties = false,
        required = _fields.Select(f => f.Name),
        properties = _fields.ToDictionary(f => f.Name, f => Property(f)),
    });

    private static Dictionary<string, object> Property(AiArgument f)
    {
        var property = f.IsInteger
            ? new Dictionary<string, object> { ["type"] = f.Nullable ? new[] { "integer", "null" } : new[] { "integer" },
                ["minimum"] = 1, ["maximum"] = f.Max }
            : new Dictionary<string, object> { ["type"] = f.Nullable ? new[] { "string", "null" } : new[] { "string" },
                ["minLength"] = 1, ["maxLength"] = f.Max };
        if (f.Choices is not null) property["enum"] = f.Choices;
        return property;
    }

    public bool Valid(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > 4096) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                var field = _fields.FirstOrDefault(f => f.Name == property.Name);
                if (field is null || !names.Add(property.Name)) return false;
                var value = property.Value;
                if (field.Nullable && value.ValueKind == JsonValueKind.Null) continue;
                if (field.IsInteger)
                {
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)
                        || number < 1 || number > field.Max) return false;
                }
                else if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString())
                    || value.GetString()!.Length > field.Max) return false;
                if (field.Choices is not null && !field.Choices.Contains(value.GetString(), StringComparer.Ordinal)) return false;
            }
            return names.Count == _fields.Length;
        }
        catch (JsonException) { return false; }
    }
}

public sealed record AiReadScope(bool Team, string? OperatorId);
public sealed record AiToolContext(string RunId, string UserId, AiReadScope Scope, DateTimeOffset? AsOf = null);
public interface IAiReadToolHandler
{
    Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token);
}
public sealed record AiToolDefinition(string Name, string Description, string AgentId,
    Capability RequiredCapability, AiRisk Risk, AiInputSchema InputSchema,
    IAiReadToolHandler? Handler, string AuditPolicy = "required-before-and-after")
{
    // Internal metadata only; provider declarations keep their existing schema.
    [System.Text.Json.Serialization.JsonIgnore]
    public AiToolPolicy? Policy { get; init; }
}

/// <summary>Only reviewed contracts enter the new runtime; no reflection or SQL tool.</summary>
public sealed class ToolRegistry
{
    public const int OperationsEvidenceLimit = 50;
    public ToolRegistry(OperationsReadService? operations = null, DataReadService? data = null, MessagesReadService? messages = null)
    {
        AiToolDefinition Read(string name, string description, AiInputSchema schema) => new(name,
            description, "operations-agent", Capability.ViewDashboard, AiRisk.Low, schema,
            operations is null ? null : new OperationsReadHandler(name, operations))
        {
            Policy = new(AiActionLevel.Read, "1", "operation_jobs", typeof(OperationsAnswer), OperationsEvidenceLimit),
        };
        All = Array.AsReadOnly(new[]
        {
            Read("query_shipments", "Read active Import/Export jobs. view=today lists jobs scheduled today in Thailand; view=risk_today lists today's high-risk/attention queue (overdue through the next 2 days) using SCMOS MonitorRules. Never calculates new risk weights.",
                new(new("view", false, Choices: ["today", "risk_today"]), new("limit", true, Max: OperationsEvidenceLimit))),
            Read("search_shipment", "Search active Import/Export jobs by key, job code, container or customer, across all dates. No driver, private note or other personal-data search.",
                new(new("query", false), new("limit", true, Max: OperationsEvidenceLimit))),
            Read("query_delays", "Read active jobs in the existing My Job DELAY bucket, across all dates; this is not a KPI calculation or the risk queue.",
                new(new AiArgument("limit", true, Max: OperationsEvidenceLimit))),
            // Phase 3 — what to chase, by the bell's and the LINE chase's own rules.
            Read(OperationsReadService.FollowUpTool,
                "Read active Import/Export jobs that need a follow-up, by SCMOS's existing rules. view=missing_truck: due within 2 days (or overdue), carrier named but plate or driver missing, not yet arrived. view=no_carrier: due within 2 days (or overdue), no carrier yet. view=unreported: scheduled today, plan time already passed, and the register does not yet say the truck left (status before DISPATCHED) or arrived. view=container_mismatch: container number not in the 4-letter + 7-digit standard. Each row carries the reason and the suggested follow-up; no risk score is invented.",
                new(new("view", false, Choices: OperationsReadService.FollowUpViews), new("limit", true, Max: OperationsEvidenceLimit))),
            // Phase 2 — the Data Agent's one read: SCMOS's own KPI for a period.
            new(DataReadService.Tool,
                "Read the department's volumes and on-time KPI (SCMOS rule arrival.on_time, zero grace) for one period: a year YYYY, a month YYYY-MM or a day YYYY-MM-DD, optionally narrowed to one customer or one carrier by name. Returns totals, the measured base, on-time count and percent, unassessable/undated counts, and a per-carrier breakdown. Never calculates a figure outside SCMOS.",
                DataAgent.Id, Capability.ViewDashboard, AiRisk.Low,
                new(new("period", false, Max: 10), new("customer", false, Nullable: true), new("trucker", false, Nullable: true),
                    new("limit", true, Max: DataReadService.CarrierLimit)),
                data is { Connected: true } ? new DataReadHandler(data) : null)
            {
                Policy = new(AiActionLevel.Read, "1", "operation_jobs", typeof(DataAnswer), DataReadService.CarrierLimit),
            },
            // Phase 4 — the Communication Agent's one read: what the carriers said, as already read.
            new(MessagesReadService.Tool,
                "Read what carriers said, as SCMOS's own LINE parser and mail links already read it. view=job with query (a job number, container or customer): every LINE message, TMS event and linked mail about that job. view=waiting: messages waiting for a job owner's approval within the last days. view=unmatched: messages the system could not pin to any job. view=today: today's messages by carrier room. Each row carries the status, arrival, ETA, plate, container and seal the parser read, what the system did with it, and an excerpt with phone numbers masked. Sends nothing; changes nothing.",
                CommunicationAgent.Id, Capability.ViewMailbox, AiRisk.Low,
                new(new("view", false, Choices: MessagesReadService.Views), new("query", false, Nullable: true),
                    new("days", true, Nullable: true, Max: MessagesReadService.MaxDays), new("limit", true, Max: MessagesReadService.EvidenceLimit)),
                messages is { Connected: true } ? new MessagesReadHandler(messages) : null)
            {
                Policy = new(AiActionLevel.Read, "1", "operation_jobs", typeof(MessagesAnswer), MessagesReadService.EvidenceLimit),
            },
        });
    }

    public IReadOnlyList<AiToolDefinition> All { get; }

    public AiToolDefinition? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}
