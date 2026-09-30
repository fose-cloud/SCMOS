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
    public record CapacityBody(string? Date, string? VehicleType, int Available, int Committed);

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

        // The carrier's My job (30 Sep 2026): the department's Operation Workspace over its own jobs — these
        // answer in /api/jobs's own shapes (the whole register, the changes since a stamp, the save), cut to
        // the carrier's rows and, on the save, to the cells a carrier may change. See CarrierRegisterService.
        group.MapGet("/jobs", async (HttpContext context, IUserAccessor users, CarrierRegisterService register,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var read = await register.ReadAsync(user, token);
            return read is null ? NotACarrier() : Results.Json(read);
        });

        group.MapGet("/jobs/since", async (string? after, HttpContext context, IUserAccessor users,
            CarrierRegisterService register, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!DateTimeOffset.TryParse(after, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var stamp))
                return ApiResults.Error("after ต้องเป็นเวลาแบบ ISO-8601", StatusCodes.Status400BadRequest);
            var delta = await register.ChangedAsync(user, stamp, token);
            return delta is null ? NotACarrier() : Results.Json(delta);
        });

        group.MapPut("/jobs", async (HttpContext context, IUserAccessor users, CarrierRegisterService register,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (context.Request.ContentLength > 4 * 1024 * 1024) return ApiResults.Error("คำขอใหญ่เกินไป", StatusCodes.Status413PayloadTooLarge);
            System.Text.Json.JsonDocument body;
            try { body = await System.Text.Json.JsonDocument.ParseAsync(context.Request.Body, cancellationToken: token); }
            catch (System.Text.Json.JsonException) { return ApiResults.Error("รูปแบบข้อมูลไม่ถูกต้อง", StatusCodes.Status400BadRequest); }
            using (body)
            {
                if (!body.RootElement.TryGetProperty("jobs", out var sent) || sent.ValueKind != System.Text.Json.JsonValueKind.Array
                    || sent.GetArrayLength() > 500)
                    return ApiResults.Error("ต้องส่ง jobs ไม่เกิน 500 งาน", StatusCodes.Status400BadRequest);
                var result = await register.SaveAsync(user, sent.EnumerateArray().Select(job => job.Clone()).ToList(), token);
                return result.Ok ? Results.Json(new { saved = result.Saved }) : ApiResults.Error(result.Message, result.Status);
            }
        });

        // The carrier's own Dashboard (30 Sep 2026): the department's dashboard, fed this company's jobs and
        // this company's measures — the same figures, for one carrier.
        group.MapGet("/dashboard/jobs", async (HttpContext context, IUserAccessor users, CarrierPortalReads reads,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var jobs = await reads.DashboardJobsAsync(user, token);
            return jobs is null ? NotACarrier() : Results.Json(jobs);
        });

        group.MapGet("/dashboard/measures", async (string? year, string? month, string? day, bool? trend, HttpContext context,
            IUserAccessor users, CarrierPortalReads reads, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var report = await reads.DashboardMeasuresAsync(user, new Period(Part(year, 4), Part(month, 2), Part(day, 2)), trend == true, token);
            return report is null ? NotACarrier() : Results.Json(report);
        });

        // The carrier's own Capacity (30 Sep 2026): what it says it has free per day and vehicle, beside its own
        // Leschaco jobs. The supplier is the account's — the department's /api/capacity took it from the body,
        // so a carrier could have written another's; carriers are refused there now (CarrierBoundary).
        group.MapGet("/capacity", async (string? from, int? days, HttpContext context, IUserAccessor users,
            CarrierService carriers, CapacityService capacity, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            var names = await carriers.NamesOfAsync(company, token);
            return Results.Json(await capacity.ReadForSupplierAsync(company.Id, company.Name, names, from, days ?? 14, token));
        });

        group.MapPost("/capacity", async ([FromBody] CapacityBody body, HttpContext context, IUserAccessor users,
            CarrierService carriers, CapacityService capacity, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var company = await carriers.CompanyOfAsync(user, token);
            if (company is null) return NotACarrier();
            var result = await capacity.ReportAsync(company.Id, (body.Date ?? "").Trim(), body.VehicleType ?? "",
                body.Available, body.Committed, user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
            await audit.RecordAsync(user, AuditActions.Update, "capacity",
                $"{company.Id} · {body.Date} · {body.VehicleType}", result.Message,
                body.VehicleType ?? "", "", $"ว่าง {body.Available} · รับไว้ {body.Committed}", "ผู้ขนส่งแจ้งกำลังรถเอง", token);
            return Results.Json(new { message = result.Message });
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

    /// <summary>A period part as the dashboard sends it: digits of the right length, or nothing ("ALL" is nothing).</summary>
    private static string Part(string? value, int length)
    {
        var text = (value ?? "").Trim();
        return text.Length == length && text.All(char.IsAsciiDigit) ? text : "";
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
