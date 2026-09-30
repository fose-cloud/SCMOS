using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The carrier's own way in.
///
/// Everything here answers for one supplier — the one the signed-in account is
/// tied to — and refuses outright when the account is tied to nobody. A carrier
/// never reaches <c>/api/jobs</c>; that endpoint hands back the whole register,
/// which is the plan for every customer and every competitor in it.
/// </summary>
public static class CarrierEndpoints
{
    public record AcceptBody(long? RequestId, string? Licence, string? Driver, string? Contact);
    public record DeclineBody(long? RequestId, string? ReasonCode, string? Remark, string? Reason = null);
    public record ResourcesBody(int TruckId, int? TrailerId, int DriverId);
    public record StatusBody(string? Type, DateTimeOffset? At, string? Remark);

    public static void MapCarrier(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/carrier").WithTags("Carrier");

        group.MapGet("", async (HttpContext context, IUserAccessor users, CarrierService carriers,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var portal = await carriers.ReadAsync(user, token);
            return portal is null
                ? ApiResults.Error(
                    "บัญชีนี้ไม่ใช่บัญชีผู้รับเหมา หรือยังไม่ได้ผูกกับบริษัท — ให้ผู้ดูแลระบบตั้งค่าให้ก่อน",
                    StatusCodes.Status403Forbidden)
                : Results.Json(portal);
        });

        // The carrier's own Rate and KPI screens (29 Sep 2026): its lanes of the rate book, read only, and
        // its line of the scorecard over its own jobs. Both are cut to the account's company on the server.
        group.MapGet("/rates", async (HttpContext context, IUserAccessor users, CarrierPortalReads reads,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var view = await reads.RatesAsync(user, token);
            return view is null ? NotACarrier() : Results.Json(view);
        });

        group.MapGet("/kpi", async (string? year, string? month, HttpContext context, IUserAccessor users,
            CarrierPortalReads reads, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!ValidPeriod(year, month, out var period))
                return ApiResults.Error("ระบุปี (yyyy) และเดือน (MM) ให้ถูกต้อง", StatusCodes.Status400BadRequest);
            var view = await reads.KpiAsync(user, period, token);
            return view is null ? NotACarrier() : Results.Json(view);
        });

        group.MapPost("/{jobKey}/accept", async (string jobKey, [FromBody] AcceptBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await carriers.AcceptAsync(user, jobKey, body.RequestId, body.Licence ?? "",
                body.Driver ?? "", body.Contact ?? "", token);
            if (!result.Ok) return ApiResults.Error(result.Message, Status(result.Code));

            if (!result.Replayed)
                await audit.RecordAsync(user, AuditActions.Update, "carrier-assignment",
                    (result.AssignmentId ?? body.RequestId)?.ToString() ?? jobKey, jobKey,
                    "outcome", CarrierAssignment.Pending, CarrierAssignment.Confirmed,
                    "ผู้รับเหมายืนยันรับงาน", token);

            return Results.Json(new { message = result.Message, replayed = result.Replayed, assignmentId = result.AssignmentId });
        });

        group.MapPost("/{jobKey}/decline", async (string jobKey, [FromBody] DeclineBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var remark = body.Remark ?? body.Reason ?? "";
            var result = await carriers.DeclineAsync(user, jobKey, body.RequestId,
                body.ReasonCode ?? "OTHER", remark, token);
            if (!result.Ok) return ApiResults.Error(result.Message, Status(result.Code));

            if (!result.Replayed)
                await audit.RecordAsync(user, AuditActions.Update, "carrier-assignment",
                    (result.AssignmentId ?? body.RequestId)?.ToString() ?? jobKey, jobKey,
                    "outcome", CarrierAssignment.Pending, CarrierAssignment.Rejected,
                    $"{body.ReasonCode ?? "OTHER"} · {remark}", token);

            return Results.Json(new { message = result.Message, replayed = result.Replayed, assignmentId = result.AssignmentId });
        });

        group.MapPut("/{jobKey}/resources", async (string jobKey, [FromBody] ResourcesBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await carriers.AssignResourcesAsync(user, jobKey,
                body.TruckId, body.TrailerId, body.DriverId, token);
            return result.Ok
                ? Results.Json(new { message = result.Message, replayed = result.Replayed })
                : ApiResults.Error(result.Message, Status(result.Code));
        });

        group.MapPost("/{jobKey}/status", async (string jobKey, [FromBody] StatusBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await carriers.AdvanceAsync(user, jobKey, body.Type ?? "",
                body.At, body.Remark ?? "", token);
            return result.Ok
                ? Results.Json(new { message = result.Message, replayed = result.Replayed,
                    status = result.Written?.GetValueOrDefault("status") })
                : ApiResults.Error(result.Message, Status(result.Code));
        });
    }

    private static IResult NotACarrier() => ApiResults.Error(
        "บัญชีนี้ไม่ใช่บัญชีผู้รับเหมา หรือยังไม่ได้ผูกกับบริษัท — ให้ผู้ดูแลระบบตั้งค่าให้ก่อน",
        StatusCodes.Status403Forbidden);

    /// <summary>A year, and a month or none for the whole year; nothing given is the current month in Bangkok.</summary>
    public static bool ValidPeriod(string? year, string? month, out Period period)
    {
        var now = DateTimeOffset.UtcNow.ToOffset(Formats.Zone);
        var y = string.IsNullOrWhiteSpace(year) ? now.Year.ToString("0000") : year.Trim();
        var m = string.IsNullOrWhiteSpace(year) && string.IsNullOrWhiteSpace(month) ? now.Month.ToString("00") : (month ?? "").Trim();
        if (m is "ALL") m = "";
        period = new Period(y, m, "");
        return y.Length == 4 && int.TryParse(y, out var yy) && yy is >= 2000 and <= 2100
            && (m.Length == 0 || (m.Length == 2 && int.TryParse(m, out var mm) && mm is >= 1 and <= 12));
    }

    private static int Status(string code) => code switch
    {
        CarrierService.ResultCode.NoCompany => StatusCodes.Status403Forbidden,
        CarrierService.ResultCode.NotOffered or CarrierService.ResultCode.NotHeld => StatusCodes.Status404NotFound,
        CarrierService.ResultCode.NotOwned => StatusCodes.Status404NotFound,
        CarrierService.ResultCode.Closed or CarrierService.ResultCode.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
}
