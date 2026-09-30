using Microsoft.AspNetCore.Http;

namespace Scmos.Api.Rules;

/// <summary>
/// The department's own figures, which a carrier's account is refused (29 Sep 2026).
///
/// A Subcontractor holds <see cref="Capability.ViewDashboard"/> for its own
/// screens, and that let it through every route guarded by that grant alone, or
/// by sign-in alone: the KPI with every carrier's scorecard, the Supplier
/// Register with every carrier's score, tax id and files, the dashboard and the
/// risk board. None of those is a carrier's screen — its own figures are under
/// <c>/api/carrier</c>, cut to its company — so these answer it 403. Found by
/// signing in as one and asking, the day the department said a carrier's KPI
/// is its own and nobody else's. The bell (<c>/api/notifications</c>) stays
/// open, because every screen asks it, and answers a carrier with nothing.
/// </summary>
public static class CarrierBoundary
{
    public static readonly string[] DepartmentOnly = ["/api/kpi", "/api/suppliers", "/api/dashboard", "/api/risk"];

    public static bool Refuses(PathString path) =>
        DepartmentOnly.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));
}
