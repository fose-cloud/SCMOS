using Scmos.Api.Rules;

namespace Scmos.Api.Ai;

public sealed record AgentDefinition(string Id, string Name, string Description, AiRisk Risk,
    Capability RequiredCapability, IReadOnlyList<string> AllowedTools, IReadOnlyList<string> Pages);

/// <summary>Deterministic master routing. A page hint never supplies authorization.</summary>
public sealed class AgentRegistry
{
    private static AgentDefinition Define(string id, string name, string description, Capability capability,
        string[] tools, string[] pages) => new(id, name, description, AiRisk.Low, capability,
            Array.AsReadOnly(tools), Array.AsReadOnly(pages));

    public IReadOnlyList<AgentDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        Define("operations-agent", "Operations Agent", "Operational shipment evidence and risk", Capability.ViewDashboard,
            ["query_shipments", "search_shipment", "query_delays"], ["operations", "workspace"]),
        Define("vendor-agent", "Vendor Agent", "Approved vendor evidence", Capability.ManageSuppliers,
            ["search_supplier", "recommend_supplier"], ["vendors", "suppliers"]),
        Define("rate-agent", "Rate Agent", "Approved rates only; never invent a price", Capability.ViewRates,
            ["query_rates", "recommend_rate"], ["rates", "quotation"]),
        Define("kpi-agent", "KPI Agent", "Existing SCMOS KPI definitions", Capability.ViewDashboard,
            ["calculate_kpi", "analyze_kpi"], ["kpi"]),
        Define("incident-agent", "Incident Agent", "Incident and CAR/PAR evidence", Capability.ViewDashboard,
            ["query_incidents"], ["incidents"]),
        Define("billing-agent", "Billing Agent", "Unavailable until an authoritative invoice source exists", Capability.ViewRates,
            [], ["billing"]),
        Define("compliance-agent", "Compliance Agent", "Training and qualification evidence", Capability.ManageTraining,
            [], ["compliance", "training"]),
        Define("management-agent", "Management Agent", "Source-linked specialist summaries", Capability.ViewDashboard,
            ["generate_report"], ["management", "dashboard"]),
    });

    public AgentDefinition? Find(string id) => All.FirstOrDefault(a => a.Id == id);
    public AgentDefinition? Resolve(AiChatRequest request)
    {
        if (request.AgentId is not null) return Find(request.AgentId);
        if (request.Context is null) return Find("operations-agent");
        return All.FirstOrDefault(a => a.Pages.Contains(request.Context.Page, StringComparer.Ordinal));
    }

    public static bool Enabled(AgentDefinition agent, AiOptions options) => agent.Id switch
    {
        "operations-agent" => options.OperationsAgentEnabled,
        "vendor-agent" => options.VendorAgentEnabled,
        "rate-agent" => options.RateAgentEnabled,
        "kpi-agent" => options.KpiAgentEnabled,
        "incident-agent" => options.IncidentAgentEnabled,
        "billing-agent" => options.BillingAgentEnabled,
        "compliance-agent" => options.ComplianceAgentEnabled,
        "management-agent" => options.ManagementAgentEnabled,
        _ => false,
    };
}
