using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Who may sign in, and as what.
///
/// Administrator only — `AdministerData` is the capability that separates
/// "runs the operation" from "decides who runs the operation", and it is the one
/// grant a supervisor deliberately does not have.
/// </summary>
public static class StaffEndpoints
{
    public record CreateBody(string? Email, string? Name, string? Role, string? Note);
    public record UpdateBody(string? Email, string? Name, string? Role, bool? Active, string? Note);

    public static void MapStaff(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/staff").WithTags("Administration");

        // Reading the directory needs ViewAudit rather than AdministerData: a
        // supervisor may need to see who holds what, and the list carries no
        // secret. Changing it is the administrator's alone.
        group.MapGet("", async (HttpContext context, IUserAccessor users, StaffService staff,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewAudit))
                return ApiResults.Error("ดูทะเบียนผู้ใช้ได้เฉพาะระดับหัวหน้างานขึ้นไป",
                    StatusCodes.Status403Forbidden);

            return Results.Json(new
            {
                people = await staff.ListAsync(token),
                roles = Roles.All.Select(role => new
                {
                    name = role.Name,
                    scopeEn = role.ScopeEn,
                    scopeTh = role.ScopeTh,
                    grants = Enum.GetValues<Capability>()
                        .Where(capability => capability != Capability.None && Roles.Can(role.Name, capability))
                        .Select(capability => capability.ToString()).ToArray(),
                }),
                // Whether this caller may change anything, so the screen offers
                // controls that will work rather than ones that 403.
                canManage = user.Can(Capability.AdministerData),
                you = user.OperatorId,
            });
        });

        group.MapPost("", async ([FromBody] CreateBody body, HttpContext context, IUserAccessor users,
            StaffService staff, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("เพิ่มผู้ใช้ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            var result = await staff.CreateAsync(body.Email ?? "", body.Name ?? "", body.Role ?? "",
                body.Note ?? "", user, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "staff", result.Id ?? "",
                body.Name ?? "", "role", "", body.Role ?? "", body.Note ?? "", token);

            return Results.Json(new { message = result.Message, id = result.Id });
        });

        group.MapPost("/{id}", async (string id, [FromBody] UpdateBody body, HttpContext context,
            IUserAccessor users, StaffService staff, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("แก้ไขผู้ใช้ได้เฉพาะผู้ดูแลระบบ", StatusCodes.Status403Forbidden);

            // Read the row before the change so the trail can say what it was.
            var before = (await staff.ListAsync(token)).FirstOrDefault(p => p.Id == id);

            var result = await staff.UpdateAsync(id, body.Email, body.Name, body.Role, body.Active,
                body.Note, user, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            var field = body.Role is not null ? "role" : body.Active is not null ? "active" : "details";
            var was = field switch
            {
                "role" => before?.Role ?? "",
                "active" => (before?.Active ?? true).ToString(),
                _ => before?.Email ?? "",
            };
            var now = field switch
            {
                "role" => body.Role ?? "",
                "active" => (body.Active ?? true).ToString(),
                _ => body.Email ?? "",
            };

            await audit.RecordAsync(user, AuditActions.Update, "staff", id,
                before?.Name ?? id, field, was, now, body.Note ?? "", token);

            return Results.Json(new { message = result.Message });
        });
    }
}
