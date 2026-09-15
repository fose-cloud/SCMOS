namespace Scmos.Api.Ai.Semantic;

public sealed record SourceScopeDescriptor(string Id, string Version, string Source,
    string Grain, string Authorization, string Population, string TimeWindow, string Completeness);

/// <summary>Describes existing adapters, never a SQL query or permission grant.</summary>
public static class SourceScopeRegistry
{
    public static IReadOnlyList<SourceScopeDescriptor> All { get; } = Array.AsReadOnly(new[]
    {
        Describe("operations.today", "Valid plan date equals the request's frozen Asia/Bangkok day."),
        Describe("operations.risk_today", "Parseable plan date from backlog through today + 2 inclusive AND MonitorRules flag; no lower date bound."),
        Describe("operations.delays", "All active dates, including undated jobs when the DELAY predicate matches."),
        Describe("operations.search", "All active dates; case-insensitive substring in key, job code, container or customer only.")
    });
    private static SourceScopeDescriptor Describe(string id, string window) => new(id, "1",
        "Data/JobsRepository.ReadAnalysisAsync -> Ai/Operations/OperationsReadService.ReadAsync",
        "One operation_jobs record by authoritative key; repeated job codes are not deduplicated.",
        "Server-derived team or operator scope, rechecked by the read service; no browser-supplied owner.",
        "Workspace category predicate (excludes DELIVERY), not done/cancelled, nonblank authoritative key. Malformed rows reported separately.",
        window,
        "Scan complete authorized source for totals; keep only requested evidence rows (maximum 50). Undated active and malformed counts describe the scoped scan, not just matching evidence.");
    public static SourceScopeDescriptor? Resolve(string id) => All.FirstOrDefault(s => s.Id == id);
}
