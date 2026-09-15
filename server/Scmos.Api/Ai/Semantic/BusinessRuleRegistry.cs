using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Semantic;

public sealed record BusinessRuleDescriptor(string Id, string Version, string SourceMember,
    string Meaning, string MissingData, int? ThresholdMinutes = null);

/// <summary>Code provenance, not editable policy or customer-contract authority. No duplicated calculations.</summary>
public static class BusinessRuleRegistry
{
    public static IReadOnlyList<BusinessRuleDescriptor> All { get; } = Array.AsReadOnly(new[]
    {
        new BusinessRuleDescriptor("arrival.on_time", "2", "Rules/JobRules.cs:JobRules.IsOnTime",
            "Measurable arrival on or before planned date/time; zero grace. KPI base must contain only measurable jobs.",
            "Both dates must exist on the calendar; invalid or missing dates are excluded and an empty base is unknown. Times retain the existing parser.", 0),
        new BusinessRuleDescriptor("arrival.late_beyond", "1", "Rules/JobRules.cs:JobRules.LateBeyond",
            "Measured arrival minus planned moment is strictly greater than the supplied tolerance; default threshold is shown separately.",
            "False for unmeasurable records does not mean on-time.", JobRules.LateMinutes),
        new BusinessRuleDescriptor("workspace.delay", "1", "Rules/WorkspaceTabs.cs:WorkspaceTabs.Matches",
            "DELAY: delayed status OR nonblank reason, excluding cancelled jobs. This is not a measured arrival-time KPI.",
            "Blank reason alone does not establish no delay; category/date/active scopes belong to the caller."),
        new BusinessRuleDescriptor("monitor.risk", "1", "Rules/MonitorRules.cs:MonitorRules.Judge",
            $"One prioritized flag: overdue plan date, unassigned, no carrier, or both driver and plate blank. Carrier/truck checks use {MonitorRules.SoonDays} days; unassigned is checked before that window.",
            "Completed/cancelled, unparseable plan date, or any recorded arrival date/time produce no flag; this does not certify complete data.")
    });

    // No verified customer contract has been registered. Never fall back to a global rule for a customer SLA.
    public static BusinessRuleDescriptor? Resolve(string id, string? customerContractId = null)
        => customerContractId is null ? All.FirstOrDefault(rule => rule.Id == id) : null;
}
