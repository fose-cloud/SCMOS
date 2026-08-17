using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>Document verification: what each job still owes, and what cannot be read.</summary>
public static class VerificationEndpoints
{
    public record UnclearBody(string? Detail);

    public static void MapVerification(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/verification").WithTags("Verification");

        group.MapGet("", async (string? scope, bool? mine, HttpContext context, IUserAccessor users,
            VerificationService verification, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var owner = mine == true || !user.Can(Capability.ViewTeam) ? user.OperatorId : null;
            return Results.Json(await verification.ReadAsync(scope, owner, token));
        });

        // The checklist itself, so the screen shows the agreed list rather than
        // its own copy of it.
        group.MapGet("/checklist", (string? category, HttpContext context, IUserAccessor users) =>
            users.Current(context) is null
                ? ApiResults.SignInRequired
                : Results.Json(DocumentChecklist.For(category ?? "IMPORT")));

        // Marking a file unreadable is what raises the document-unclear alert.
        // It needs UploadDocuments rather than a supervisor: the person who
        // opened the scan and could not read it is the one who should say so.
        group.MapPost("/documents/{id:long}/unclear", async (long id, [FromBody] UnclearBody body,
            HttpContext context, IUserAccessor users, VerificationService verification,
            AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.UploadDocuments))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แจ้งสถานะเอกสาร", StatusCodes.Status403Forbidden);

            var result = await verification.MarkUnclearAsync(id, body.Detail ?? "", token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "document", id.ToString(), result.Message,
                "note", "", VerificationService.UnclearMark, body.Detail ?? "", token);

            return Results.Json(new { message = result.Message });
        });

        group.MapPost("/documents/{id:long}/clear", async (long id, HttpContext context,
            IUserAccessor users, VerificationService verification, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.UploadDocuments))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แจ้งสถานะเอกสาร", StatusCodes.Status403Forbidden);

            var result = await verification.ClearUnclearAsync(id, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "document", id.ToString(), result.Message,
                "note", VerificationService.UnclearMark, "", "ได้รับไฟล์ที่อ่านได้แล้ว", token);

            return Results.Json(new { message = result.Message });
        });
    }
}
