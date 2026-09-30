using Scmos.Api.Auth;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Jobs a carrier keys in, for Leschaco to confirm (29 Sep 2026). Two doors:
/// <c>/api/carrier/job-requests</c> for the carrier, answering for its own
/// company only, and <c>/api/carrier-job-requests</c> for the department, where
/// a request is refused or marked approved with the job the add-job form saved.
/// </summary>
public static class CarrierJobRequestEndpoints
{
    public record CreateBody(string? Category, Dictionary<string, string>? Fields, string? Note);
    public record ApproveBody(string? JobKey, int Revision);
    public record RejectBody(string? Reason, int Revision);

    public static void MapCarrierJobRequests(this IEndpointRouteBuilder routes)
    {
        var carrier = routes.MapGroup("/api/carrier/job-requests").WithTags("Carrier");

        carrier.MapGet("", async (HttpContext context, IUserAccessor users, CarrierJobRequestService requests, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var mine = await requests.MineAsync(user, token);
            return mine is null ? NotACarrier() : Results.Json(new { items = mine, form = CarrierJobRequestService.Form });
        });

        carrier.MapPost("", async (CreateBody body, HttpContext context, IUserAccessor users, CarrierJobRequestService requests,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (context.Request.ContentLength > 16 * 1024) return ApiResults.Error("คำขอใหญ่เกินไป", StatusCodes.Status413PayloadTooLarge);
            return Answer(await requests.CreateAsync(user, body.Category, body.Fields, body.Note, token));
        });

        carrier.MapPost("/{id:long}/withdraw", async (long id, HttpContext context, IUserAccessor users,
            CarrierJobRequestService requests, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Answer(await requests.WithdrawAsync(user, id, token));
        });

        var department = routes.MapGroup("/api/carrier-job-requests").WithTags("Carrier");

        department.MapGet("", async (string? status, HttpContext context, IUserAccessor users, CarrierJobRequestService requests,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!CarrierJobRequestService.CanReview(user)) return ApiResults.Error("ต้องเป็นผู้ที่เพิ่มงานได้", StatusCodes.Status403Forbidden);
            return Results.Json(new { items = await requests.ListAsync(status, token) });
        });

        department.MapPost("/{id:long}/approve", async (long id, ApproveBody body, HttpContext context, IUserAccessor users,
            CarrierJobRequestService requests, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Answer(await requests.ApproveAsync(user, id, body.JobKey, body.Revision, token));
        });

        department.MapPost("/{id:long}/reject", async (long id, RejectBody body, HttpContext context, IUserAccessor users,
            CarrierJobRequestService requests, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Answer(await requests.RejectAsync(user, id, body.Reason, body.Revision, token));
        });
    }

    private static IResult Answer(CarrierJobRequestResult result) => result.Ok
        ? Results.Json(new { message = result.Message, id = result.Id })
        : Results.Json(new { code = result.Code, error = result.Message }, statusCode: result.Code switch
        {
            "no_company" or "forbidden" => StatusCodes.Status403Forbidden,
            "not_found" => StatusCodes.Status404NotFound,
            "closed" or "conflict" => StatusCodes.Status409Conflict,
            "too_many" => StatusCodes.Status429TooManyRequests,
            _ => StatusCodes.Status400BadRequest,
        });

    private static IResult NotACarrier() => ApiResults.Error(
        "บัญชีนี้ไม่ใช่บัญชีผู้รับเหมา หรือยังไม่ได้ผูกกับบริษัท — ให้ผู้ดูแลระบบตั้งค่าให้ก่อน",
        StatusCodes.Status403Forbidden);
}
