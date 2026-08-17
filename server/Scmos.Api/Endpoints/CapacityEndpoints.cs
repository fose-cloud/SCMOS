using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Fleet availability, and the roles matrix the Administration screen reads.
/// </summary>
public static class CapacityEndpoints
{
    public record ReportBody(int SupplierId, string? Date, string? VehicleType,
        int Available, int Committed, string? Reason);

    public static void MapCapacity(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/capacity").WithTags("Capacity");

        group.MapGet("", async (string? from, int? days, HttpContext context, IUserAccessor users,
            CapacityService capacity, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Results.Json(await capacity.ReadAsync(from, days ?? 7, token));
        });

        // A carrier reports their own fleet; an operator records it on their
        // behalf after a phone call. Both are the same act, so both go here —
        // and both are audited, because a promise of six trailers that turns
        // into four is a conversation somebody will need the record of.
        group.MapPost("", async ([FromBody] ReportBody body, HttpContext context, IUserAccessor users,
            CapacityService capacity, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditOwnJobs))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกกำลังรถ", StatusCodes.Status403Forbidden);

            var result = await capacity.ReportAsync(body.SupplierId, body.Date ?? "",
                body.VehicleType ?? "", body.Available, body.Committed, user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            // Separated by a middle dot, not a slash: the date already contains
            // slashes, and "18/17/08/2026/20F" is not a thing anyone can read.
            await audit.RecordAsync(user, AuditActions.Update, "capacity",
                $"{body.SupplierId} · {body.Date} · {body.VehicleType}", result.Message,
                body.VehicleType ?? "", "", $"ว่าง {body.Available} · รับไว้ {body.Committed}",
                body.Reason ?? "", token);

            return Results.Json(new { message = result.Message });
        });

        // The roles matrix, from the same table the API enforces against, so the
        // Administration screen shows what is in force rather than a description
        // of it that somebody has to remember to update.
        routes.MapGet("/api/roles", (HttpContext context, IUserAccessor users) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var capabilities = Enum.GetValues<Capability>()
                .Where(capability => capability != Capability.None)
                .Select(capability => capability.ToString())
                .ToArray();

            return Results.Json(new
            {
                capabilities,
                roles = Roles.All.Select(role => new
                {
                    name = role.Name,
                    scopeEn = role.ScopeEn,
                    scopeTh = role.ScopeTh,
                    grants = capabilities.Where(name => Roles.Can(role.Name, Enum.Parse<Capability>(name))).ToArray(),
                }),
                people = StaffDirectory.All.Select(person => new
                {
                    id = person.Id, name = person.Name, account = person.Account, role = person.Role,
                }),
            });
        }).WithTags("Administration");
    }
}
