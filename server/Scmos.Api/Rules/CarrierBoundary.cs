using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Scmos.Api.Rules;

/// <summary>
/// The routes a carrier's account may call — every other <c>/api</c> route
/// answers it 403 (30 Sep 2026).
///
/// A Subcontractor holds <see cref="Capability.ViewDashboard"/>,
/// <see cref="Capability.EditOwnJobs"/> and <see cref="Capability.UploadDocuments"/>
/// for its own screens, and those grants let it through department routes
/// guarded by them alone or by sign-in alone. Signing in as one and asking found
/// the KPI with every carrier's scorecard, the Supplier Register with every
/// carrier's score and tax id, the dashboard, the risk board and the capacity
/// board — whose POST took the supplier from the request, so a carrier could
/// write another's fleet. Those were closed one by one (v2.8.0–v2.8.1), which
/// only covers what somebody thought to probe; this turns it round. A carrier's
/// own work lives under <c>/api/carrier…</c>, cut to its company, plus the
/// handful of shared routes its screens need, each already guarded per carrier:
/// who am I, the bell (empty for a carrier), the vehicle vocabulary, its jobs'
/// documents (<c>CarrierDocumentAccess</c>) and its billing (tenant-checked).
///
/// A new carrier screen that needs another route adds it here, with the reason;
/// a department route needs nothing — it is closed to carriers by default.
/// </summary>
public static class CarrierBoundary
{
    private static Regex Path(string pattern) =>
        new("^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>(method or "*", path) — the path whole, digits standing for an id.</summary>
    public static readonly IReadOnlyList<(string Method, Regex Path)> Allowed =
    [
        // Its own: the portal, NEW job, My job, Capacity, Rate, KPI, Dashboard, job requests — and the TMS API (v1).
        ("*", Path(@"/api/carrier(/.*)?")),
        // Who is signed in; the bell (answers a carrier with nothing) and its vocabulary; the vehicle list.
        ("GET", Path(@"/api/me")),
        ("GET", Path(@"/api/notifications(/kinds)?")),
        ("GET", Path(@"/api/vehicle-types")),
        // Its jobs' documents: POD upload and opening a file — CarrierDocumentAccess checks the job is its.
        ("GET", Path(@"/api/documents")),
        ("POST", Path(@"/api/documents")),
        ("GET", Path(@"/api/documents/\d+/content")),
        // Its billing, as the Billing screen uses it — the service resolves the carrier's own supplier.
        ("GET", Path(@"/api/carrier-billing/(cases|control-tower)")),
        ("POST", Path(@"/api/carrier-billing/cases/\d+/draft")),
        ("PUT", Path(@"/api/carrier-billing/invoices/\d+")),
        ("POST", Path(@"/api/carrier-billing/invoices/\d+/(documents|submit|charges)")),
        ("PUT", Path(@"/api/carrier-billing/invoices/\d+/original-package")),
    ];

    /// <summary>Whether a carrier's account may make this request. Anything outside <c>/api</c> is not this rule's.</summary>
    public static bool Allows(string method, PathString path)
    {
        if (!path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) return true;
        var value = (path.Value ?? "").TrimEnd('/');
        var verb = HttpMethods.IsHead(method) ? HttpMethods.Get : method;
        return Allowed.Any(rule => (rule.Method == "*" || string.Equals(rule.Method, verb, StringComparison.OrdinalIgnoreCase))
            && rule.Path.IsMatch(value));
    }
}
