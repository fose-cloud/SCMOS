using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The list of things the team dispatches, read by everybody and changed by
/// Admin.
/// </summary>
public static class VehicleTypeEndpoints
{
    public record AddBody(string? Code, string? Label);

    public static void MapVehicleTypes(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/vehicle-types").WithTags("Vehicle types");

        // Anybody signed in reads it: it fills the type dropdown on every job.
        group.MapGet("", async (HttpContext context, IUserAccessor users,
            VehicleTypeService types, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Results.Json(await types.ReadAsync(token));
        });

        group.MapPost("", async ([FromBody] AddBody body, HttpContext context, IUserAccessor users,
            VehicleTypeService types, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("เพิ่มประเภทรถได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            var result = await types.AddAsync(body.Code ?? "", body.Label ?? "", user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "vehicle-type",
                body.Code ?? "", result.Message, "", "", body.Code ?? "", "", token);
            return Results.Json(new { message = result.Message });
        });

        // Retire, not delete — the service says why. Kept as DELETE because
        // that is what the caller means: take it off the list.
        group.MapDelete("/{id:int}", async (int id, HttpContext context, IUserAccessor users,
            VehicleTypeService types, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("นำประเภทรถออกได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            var result = await types.RetireAsync(id, user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "vehicle-type",
                id.ToString(), result.Message, "", "", "retired", "", token);
            return Results.Json(new { message = result.Message });
        });
    }
}
