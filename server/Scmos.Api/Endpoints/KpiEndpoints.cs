using Scmos.Api.Auth;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The KPI screen's only source. Everything it shows is computed here, so the
/// figures the team reports upward do not depend on which build of the front end
/// a viewer has loaded.
/// </summary>
public static class KpiEndpoints
{
    public static void MapKpi(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/kpi", async (string? year, string? month, string? day,
            HttpContext context, IUserAccessor users, KpiService kpi, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var period = new Period(Clean(year, 4), Clean(month, 2), Clean(day, 2));
            var report = await kpi.BuildAsync(period, token);
            return Results.Json(report);
        }).WithTags("KPI");

        // The eight measures the business reports on, each with the base it was
        // measured over and whether it can be measured at all.
        routes.MapGet("/api/kpi/measures", async (string? year, string? month, string? day, bool? trend, string? customer, string? trucker,
            HttpContext context, IUserAccessor users, KpiEngine engine, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var period = new Period(Clean(year, 4), Clean(month, 2), Clean(day, 2));
            // The dashboard's CUSTOMER / TRUCKER pickers (22 Sep 2026): pipe-separated any-of values, "ALL" for none.
            var scope = KpiScope.Parse(customer, trucker);
            // The trend runs the engine once per month, so it is asked for
            // rather than always computed — the Excel export and the dashboard
            // want a single period and should not pay for six.
            return Results.Json(trend == true
                ? await engine.BuildWithTrendAsync(period, scope, token)
                : await engine.BuildAsync(period, scope, token));
        }).WithTags("KPI");

        // The issues behind one count of the carrier scorecard (1 Oct 2026), so a number can be opened, each issue
        // given its column, and linked to the CAR/PAR that answers it. Read by the same rule that counted them.
        routes.MapGet("/api/kpi/scorecard/issues", async (string? year, string? month, string? day, string? customer, string? trucker,
            string? carrier, string? column, HttpContext context, IUserAccessor users, KpiEngine engine, OperationalIssueService issues,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (CarrierTenantContext.IsCarrier(user))
                return ApiResults.Error("บัญชีผู้ขนส่งเปิดรายการของทุกบริษัทไม่ได้", StatusCodes.Status403Forbidden);
            var wanted = (column ?? "").Trim();
            if ((carrier ?? "").Trim().Length == 0
                || !(Rules.ScorecardColumn.All.Contains(wanted) || wanted == CarrierScorecard.Ungraded))
                return ApiResults.Error("ระบุผู้ขนส่งและคอลัมน์", StatusCodes.Status400BadRequest);
            var period = new Period(Clean(year, 4), Clean(month, 2), Clean(day, 2));
            var found = await engine.ScorecardIssuesAsync(period, KpiScope.Parse(customer, trucker), carrier!.Trim(), wanted, token);
            var cases = await issues.CasesOfAsync(found, token);
            return Results.Json(found.OrderBy(issue => Rules.Formats.DateNumber(issue.FoundOn)).ThenBy(issue => issue.Id).Select(issue => new
            {
                issue.Id, issue.Code, issue.FoundOn, issue.FoundAt, issue.Source, issue.JobRef, issue.JobKey, issue.Detail, issue.Category,
                issue.Severity, issue.Status, issue.AccidentGrade,
                chosenColumn = issue.ScorecardColumn,
                column = Rules.ScorecardColumn.Of(issue),
                issue.CaseId,
                caseReference = issue.CaseId is { } id && cases.TryGetValue(id, out var linked) ? linked.Reference : "",
                caseStage = issue.CaseId is { } stageOf && cases.TryGetValue(stageOf, out var staged) ? staged.Stage : "",
            }));
        }).WithTags("KPI");

        routes.MapGet("/api/kpi/excel", async (string? year, string? month, string? day,
            HttpContext context, IUserAccessor users, KpiEngine engine, KpiService kpi,
            CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;

            var period = new Period(Clean(year, 4), Clean(month, 2), Clean(day, 2));
            var measures = await engine.BuildAsync(period, token);
            var operational = await kpi.BuildAsync(period, token);
            var bytes = KpiWorkbook.Build(measures, operational);

            var stamp = DateTimeOffset.Now.ToString("yyyy-MM-dd");
            var scope = period.IsAll ? "all" : $"{period.Year}{period.Month}{period.Day}";
            return Results.File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"SCMOS_KPI_{scope}_{stamp}.xlsx");
        }).WithTags("KPI");
    }

    /// <summary>
    /// Period parts are digits or they are nothing. "ALL" is what the screen
    /// sends for an unset filter, and it must not be read as a year.
    /// </summary>
    private static string Clean(string? value, int length)
    {
        var text = (value ?? "").Trim();
        if (text.Length != length) return "";
        return text.All(char.IsAsciiDigit) ? text : "";
    }
}
