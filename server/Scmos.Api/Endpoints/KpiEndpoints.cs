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
        routes.MapGet("/api/kpi/measures", async (string? year, string? month, string? day,
            HttpContext context, IUserAccessor users, KpiEngine engine, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var period = new Period(Clean(year, 4), Clean(month, 2), Clean(day, 2));
            return Results.Json(await engine.BuildAsync(period, token));
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
