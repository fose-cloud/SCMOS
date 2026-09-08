using System.Text.Json;
using Scmos.Api.Rules;
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
    IAiReadToolHandler? Handler, string AuditPolicy = "required-before-and-after");

/// <summary>Only reviewed contracts enter the new runtime; no reflection or SQL tool.</summary>
public sealed class ToolRegistry
{
    public ToolRegistry(OperationsReadService? operations = null)
    {
        AiToolDefinition Read(string name, string description, AiInputSchema schema) => new(name,
            description, "operations-agent", Capability.ViewDashboard, AiRisk.Low, schema,
            operations is null ? null : new OperationsReadHandler(name, operations));
        All = Array.AsReadOnly(new[]
        {
            Read("query_shipments", "Read active Import/Export jobs. view=today lists jobs scheduled today in Thailand; view=risk_today lists today's high-risk/attention queue (overdue through the next 2 days) using SCMOS MonitorRules. Never calculates new risk weights.",
                new(new("view", false, Choices: ["today", "risk_today"]), new("limit", true, Max: 50))),
            Read("search_shipment", "Search active Import/Export jobs by key, job code, container or customer, across all dates. No driver, private note or other personal-data search.",
                new(new("query", false), new("limit", true, Max: 50))),
            Read("query_delays", "Read active jobs in the existing My Job DELAY bucket, across all dates; this is not a KPI calculation or the risk queue.",
                new(new AiArgument("limit", true, Max: 50))),
        });
    }

    public IReadOnlyList<AiToolDefinition> All { get; }

    public AiToolDefinition? Find(string name) => All.FirstOrDefault(t => t.Name == name);
}
