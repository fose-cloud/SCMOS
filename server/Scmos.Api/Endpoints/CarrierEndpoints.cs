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
    public record AcceptBody(string? Licence, string? Driver, string? Contact);
    public record DeclineBody(string? Reason);

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

        group.MapPost("/{jobKey}/accept", async (string jobKey, [FromBody] AcceptBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await carriers.AcceptAsync(user, jobKey, body.Licence ?? "",
                body.Driver ?? "", body.Contact ?? "", token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "job", jobKey, jobKey,
                "trucker, licence, driver, contact", result.Before,
                $"{body.Licence} · {body.Driver} · {body.Contact}",
                "ผู้รับเหมายืนยันรับงานและแจ้งรถ", token);

            return Results.Json(new { message = result.Message });
        });

        group.MapPost("/{jobKey}/decline", async (string jobKey, [FromBody] DeclineBody body,
            HttpContext context, IUserAccessor users, CarrierService carriers, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await carriers.DeclineAsync(user, jobKey, body.Reason ?? "", token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "job", jobKey, jobKey,
                "supplier-response", "pending", "rejected", body.Reason ?? "", token);

            return Results.Json(new { message = result.Message });
        });
    }
}
