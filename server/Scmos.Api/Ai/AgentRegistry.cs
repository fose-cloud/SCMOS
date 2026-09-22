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
            ["query_shipments", "search_shipment", "query_delays", "query_followup"], ["operations", "workspace"]),
        Define("vendor-agent", "Vendor Agent", "Approved vendor evidence", Capability.ManageSuppliers,
            ["search_supplier", "recommend_supplier"], ["vendors", "suppliers"]),
        Define("rate-agent", "Rate Agent", "Approved rates only; never invent a price", Capability.ViewRates,
            ["query_rates", "recommend_rate"], ["rates", "quotation"]),
        // The specification's "SCMOS Data Agent": the department's own figures
        // for a period, with the rule and its version on the answer (Phase 2).
        Define("data-agent", "Data Agent", "Volumes and the on-time KPI for a period, by SCMOS's own rules", Capability.ViewDashboard,
            ["query_kpi"], ["kpi", "reports"]),
        Define("incident-agent", "Incident Agent", "Incident and CAR/PAR evidence", Capability.ViewDashboard,
            ["query_incidents"], ["incidents"]),
        // The specification's Document & Invoice Agent (Phase 5): the paperwork
        // the department already keeps, by the checklist, billing and compliance
        // rules the screens already apply. The former billing-agent descriptor,
        // never connected under that name — an invoice ledger still does not
        // exist, and the read says so where it would matter.
        Define("document-agent", "Document & Invoice Agent", "Paperwork held and owed per job, carrier invoices against the billing rule, compliance files near expiry — read, never approved", Capability.UploadDocuments,
            ["query_documents"], ["documents", "verification", "compliance", "billing"]),
        Define("compliance-agent", "Compliance Agent", "Training and qualification evidence", Capability.ManageTraining,
            [], ["compliance", "training"]),
        // The specification's Management Agent (Phase 8): source-linked specialist
        // summaries — a fixed plan of other specialists' reads, each authorised on
        // its own, composed by the server. Its "tools" are the plans it may select.
        Define("management-agent", "Management Agent", "Summaries across specialists by fixed plans — one job's standing, paperwork and messages; delayed jobs short of paperwork — each step read under its own specialist's authorisation, nothing concluded about cause", Capability.ViewDashboard,
            [Management.ManagementPlans.JobPlan, Management.ManagementPlans.LatePaperworkPlan], ["management", "dashboard"]),
        // The specification's Communication Agent (Phase 4): what the carriers
        // said, as the LINE parser and the mail links already read it. Reading
        // the Communication Center is what it needs — never a carrier's account.
        Define("communication-agent", "Communication Agent", "What carriers said in LINE, from their TMS and in linked mail — read, never sent", Capability.ViewMailbox,
            ["query_messages"], ["line", "mail", "communications"]),
        Define("engineering-agent", "Engineering Agent", "Read-only SCMOS GitHub issue, PR and commit metadata, and a bounded read of the repository's own source", Capability.AdministerData,
            ["query_repository", "read_source"], ["engineering"]),
        // The specification's SRE Agent (Phase 7): what the platform knows about itself — read, measured, never acted on.
        Define("sre-agent", "SRE Agent", "The platform's own health, deployments and failure counts — read and measured, nothing restarted or changed", Capability.AdministerData,
            ["query_platform"], ["sre", "health", "system"]),
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
        "data-agent" => options.DataAgentEnabled,
        "communication-agent" => options.CommunicationAgentEnabled,
        "engineering-agent" => options.EngineeringAgentEnabled,
        "sre-agent" => options.SreAgentEnabled,
        "incident-agent" => options.IncidentAgentEnabled,
        "document-agent" => options.DocumentAgentEnabled,
        "compliance-agent" => options.ComplianceAgentEnabled,
        "management-agent" => options.ManagementAgentEnabled,
        _ => false,
    };
}
