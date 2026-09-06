using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The Report Centre's data, as one document per report.
///
/// <para>
/// A report is assembled here and not in the browser. That is the same rule the
/// KPI screen follows and it matters more here: this document is what leaves
/// the building as a PDF, and a figure computed in the browser would depend on
/// which build of the front end a viewer happened to have loaded. What the
/// screen receives is finished — every count, every rate, and every base.
/// </para>
///
/// <para>
/// Reading needs <see cref="Capability.ViewDashboard"/> and nothing more. A
/// performance report is the operation's own record of itself; it is not the
/// rate book, and it names no price.
/// </para>
/// </summary>
public static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/reports").WithTags("Reports");

        // What the pickers may offer, with the coverage attached to each name so
        // the screen can warn before a report is generated rather than after.
        group.MapGet("/choices", async (HttpContext context, IUserAccessor users,
            MonthlyReportService reports, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            return Results.Json(await reports.ChoicesAsync(token));
        });

        group.MapGet("/monthly", async (string? customer, string? month,
            HttpContext context, IUserAccessor users, MonthlyReportService reports,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงาน", StatusCodes.Status403Forbidden);

            var wanted = (customer ?? "").Trim();
            if (wanted.Length == 0) return ApiResults.Error("ต้องเลือกลูกค้า", StatusCodes.Status400BadRequest);
            if (wanted.Length > 200)
                return ApiResults.Error("ชื่อลูกค้ายาวเกินไป", StatusCodes.Status400BadRequest);

            // MM/yyyy, as the month is written everywhere else in this system.
            var period = (month ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(period, @"^(0[1-9]|1[0-2])/[0-9]{4}$"))
                return ApiResults.Error("เดือนต้องอยู่ในรูปแบบ MM/yyyy", StatusCodes.Status400BadRequest);

            var report = await reports.BuildAsync(wanted, period, token);

            return Results.Json(new
            {
                report.Customer,
                report.Month,
                report.Summary,
                report.Target,
                report.Vendors,
                report.DelayReasons,
                report.Trend,
                // Said by the rule rather than worked out by the screen, so the
                // PDF and the preview carry the same warning in the same words.
                meetsTarget = MonthlyReport.MeetsTarget(report.Summary, report.Target),
                confidence = MonthlyReport.Confidence(report.Summary),
                minimumSample = MonthlyReport.MinimumSample,
                // Who ran it and when, because a report that has been forwarded
                // twice should still say where it came from.
                generatedBy = user.DisplayName,
                generatedAt = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy HH:mm"),
            });
        });
    }
}
