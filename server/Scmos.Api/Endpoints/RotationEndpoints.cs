using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The rotation: which customer belongs to whom.
///
/// Readable by anyone who may see the team's jobs, because "whose customer is
/// this" is a question the whole operation asks all day. Replacing it wants the
/// capability that assigns work — a rotation is a decision about who does what,
/// and it arrives as one document that replaces the last one.
/// </summary>
public static class RotationEndpoints
{
    public record RotationRow(
        string? Customer, string? Sheet,
        bool? Import, bool? Export, bool? Fcl, bool? Lcl, bool? Domestic,
        string? PrimaryContact, string? PrimaryEmail,
        string? BackupContact, string? BackupEmail,
        string? Backup2Contact, string? Backup2Email,
        string? SubFcl, string? SubLcl, string? CsLcb);

    public record ReplaceBody(List<RotationRow>? Rows);
    public record EditBody(
        string? Customer,
        bool? Import, bool? Export, bool? Fcl, bool? Lcl, bool? Domestic,
        string? PrimaryId, string? BackupId, string? Backup2Id,
        List<int>? SubFclSupplierIds, List<int>? SubLclSupplierIds,
        string? CsLcb);

    public static void MapRotation(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/rotation").WithTags("Rotation");

        group.MapGet("", async (string? ownerId, string? customer, HttpContext context,
            IUserAccessor users, RotationService rotation, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewTeam))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูตารางความรับผิดชอบ", StatusCodes.Status403Forbidden);

            return Results.Json(new { rows = await rotation.ListAsync(ownerId, customer, token) });
        });

        group.MapGet("/owners", async (HttpContext context, IUserAccessor users,
            RotationService rotation, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewTeam))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูตารางความรับผิดชอบ", StatusCodes.Status403Forbidden);

            return Results.Json(new { owners = await rotation.OwnersAsync(token) });
        });

        group.MapGet("/options", async (HttpContext context, IUserAccessor users,
            RotationService rotation, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewTeam))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูตัวเลือก Job Rotation",
                    StatusCodes.Status403Forbidden);

            return Results.Json(await rotation.OptionsAsync(token));
        });

        group.MapPost("", async ([FromBody] EditBody body, HttpContext context,
            IUserAccessor users, RotationService rotation, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!CanManage(user))
                return ApiResults.Error("เฉพาะ Supervisor ขึ้นไปเท่านั้นที่เพิ่ม Job Rotation ได้",
                    StatusCodes.Status403Forbidden);

            var result = await rotation.CreateAsync(ToEdit(body), user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Register, "rotation", result.Id.ToString(),
                body.Customer ?? "", "รายการ", "", result.Message, "", token);
            return Results.Json(new { ok = true, result.Id, result.Message });
        });

        group.MapPut("/{id:long}", async (long id, [FromBody] EditBody body, HttpContext context,
            IUserAccessor users, RotationService rotation, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!CanManage(user))
                return ApiResults.Error("เฉพาะ Supervisor ขึ้นไปเท่านั้นที่แก้ไข Job Rotation ได้",
                    StatusCodes.Status403Forbidden);

            var result = await rotation.UpdateAsync(id, ToEdit(body), user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "rotation", id.ToString(),
                body.Customer ?? "", "รายการ", "", result.Message, "", token);
            return Results.Json(new { ok = true, result.Id, result.Message });
        });

        group.MapDelete("/{id:long}", async (long id, HttpContext context,
            IUserAccessor users, RotationService rotation, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!CanManage(user))
                return ApiResults.Error("เฉพาะ Supervisor ขึ้นไปเท่านั้นที่ลบ Job Rotation ได้",
                    StatusCodes.Status403Forbidden);

            var result = await rotation.DeleteAsync(id, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status404NotFound);

            await audit.RecordAsync(user, AuditActions.Update, "rotation", id.ToString(),
                "Job Rotation", "รายการ", result.Message, "ลบแล้ว", "", token);
            return Results.Json(new { ok = true, result.Message });
        });

        group.MapPost("/replace", async ([FromBody] ReplaceBody? body, HttpContext context,
            IUserAccessor users, RotationService rotation, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!CanManage(user))
                return ApiResults.Error("เฉพาะ Supervisor ขึ้นไปเท่านั้นที่นำเข้าตารางความรับผิดชอบได้",
                    StatusCodes.Status403Forbidden);

            var incoming = body?.Rows ?? [];
            if (incoming.Count == 0) return ApiResults.Error("ไม่พบข้อมูลในไฟล์", StatusCodes.Status400BadRequest);
            if (incoming.Count > 5000)
                return ApiResults.Error("นำเข้าได้ไม่เกิน 5,000 แถวต่อครั้ง", StatusCodes.Status413PayloadTooLarge);

            var result = await rotation.ReplaceAsync(incoming.Select(Build).ToList(), user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            // Worth an audit row even though the table carries updated_by: this
            // replaces every assignment at once, and "who changed the rotation
            // and when" is asked months later by somebody looking at one job.
            await audit.RecordAsync(user, AuditActions.BulkReplace, "rotation", "", "ตารางความรับผิดชอบ",
                $"{result.Replaced} แถว", $"{result.Added} แถว", "", "", token);

            return Results.Json(new { ok = true, added = result.Added, replaced = result.Replaced });
        });
    }

    private static RotationAssignment Build(RotationRow row) => new()
    {
        Customer = Clean(row.Customer, 200),
        Sheet = Clean(row.Sheet, 120),
        Import = row.Import ?? false,
        Export = row.Export ?? false,
        Fcl = row.Fcl ?? false,
        Lcl = row.Lcl ?? false,
        Domestic = row.Domestic ?? false,
        PrimaryContact = Clean(row.PrimaryContact, 300),
        PrimaryEmail = Clean(row.PrimaryEmail, 160).ToLowerInvariant(),
        BackupContact = Clean(row.BackupContact, 300),
        BackupEmail = Clean(row.BackupEmail, 160).ToLowerInvariant(),
        Backup2Contact = Clean(row.Backup2Contact, 300),
        Backup2Email = Clean(row.Backup2Email, 160).ToLowerInvariant(),
        SubFcl = Clean(row.SubFcl, 300),
        SubLcl = Clean(row.SubLcl, 300),
        CsLcb = Clean(row.CsLcb, 400),
    };

    private static RotationEdit ToEdit(EditBody body) => new(
        Clean(body.Customer, 200),
        body.Import ?? false, body.Export ?? false,
        body.Fcl ?? false, body.Lcl ?? false, body.Domestic ?? false,
        Clean(body.PrimaryId, 20), Clean(body.BackupId, 20), Clean(body.Backup2Id, 20),
        body.SubFclSupplierIds ?? [], body.SubLclSupplierIds ?? [],
        Clean(body.CsLcb, 400));

    private static bool CanManage(AppUser user) => user.Can(Capability.AssignJobs);

    private static string Clean(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
