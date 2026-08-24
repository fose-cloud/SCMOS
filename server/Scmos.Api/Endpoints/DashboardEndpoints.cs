using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>The front page, the alert feed, and the risk answer behind them.</summary>
public static class DashboardEndpoints
{
    public static void MapDashboard(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/dashboard/today", async (string? date, HttpContext context,
            IUserAccessor users, DashboardService dashboard, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูแดชบอร์ด", StatusCodes.Status403Forbidden);

            return Results.Json(await dashboard.TodayAsync(date, token));
        });

        // The two whole-register rates, asked for once the board is on screen.
        routes.MapGet("/api/dashboard/today/rates", async (HttpContext context,
            IUserAccessor users, DashboardService dashboard, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูแดชบอร์ด", StatusCodes.Status403Forbidden);

            return Results.Json(new { figures = await dashboard.RatesAsync(token) });
        });

        var alerts = routes.MapGroup("/api/notifications").WithTags("Notifications");

        alerts.MapGet("", async (bool? mine, HttpContext context, IUserAccessor users,
            NotificationService notifications, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // Someone who cannot see the team's work should not be told about it
            // through the alert feed either — the feed is a view of the register,
            // and a view is still access.
            var scope = mine == true || !user.Can(Capability.ViewTeam) ? user.OperatorId : null;
            return Results.Json(await notifications.BuildAsync(scope, token));
        });

        alerts.MapGet("/kinds", (HttpContext context, IUserAccessor users) =>
            users.Current(context) is null
                ? ApiResults.SignInRequired
                : Results.Json(Notifications.All));

        routes.MapGet("/api/risk", async (HttpContext context, IUserAccessor users,
            RiskService risk, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูข้อมูลนี้", StatusCodes.Status403Forbidden);

            var scope = user.Can(Capability.ViewTeam) ? null : user.OperatorId;
            return Results.Json(await risk.TodayAsync(scope, token));
        });
    }
}
