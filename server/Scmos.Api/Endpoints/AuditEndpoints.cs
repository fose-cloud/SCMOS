using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Reading the audit trail.
///
/// Read-only by design. There is no route that edits or deletes an entry, and
/// there is not going to be one — a trail somebody can tidy up is not a trail.
/// </summary>
public static class AuditEndpoints
{
    public static void MapAudit(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/audit").WithTags("Audit");

        group.MapGet("", async (string? entity, string? entityId, string? who, string? action,
            int? skip, int? take, HttpContext context, IUserAccessor users, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // Reading who changed what is a supervisory act. An operator seeing
            // the whole team's edit history is a different system from one where
            // they see their own work.
            if (!user.Can(Capability.ViewAudit))
                return ApiResults.Error("ดูประวัติการใช้งานได้เฉพาะระดับหัวหน้างานขึ้นไป",
                    StatusCodes.Status403Forbidden);

            var page = await audit.ReadAsync(entity, entityId, who, action, skip ?? 0, take ?? 100, token);
            return Results.Json(page);
        });

        // What the screen offers as filters, from the rules rather than a list
        // the screen keeps its own copy of.
        group.MapGet("/kinds", (HttpContext context, IUserAccessor users) =>
            users.Current(context) is null
                ? ApiResults.SignInRequired
                : Results.Json(new
                {
                    entities = new[] { "job", "supplier", "rate", "incident", "document", "approval", "register" },
                    actions = new[]
                    {
                        AuditActions.Update, AuditActions.Assign, AuditActions.StatusChange,
                        AuditActions.CarrierChange, AuditActions.RateChange, AuditActions.Approve,
                        AuditActions.Reject, AuditActions.Apply, AuditActions.Close, AuditActions.Upload,
                        AuditActions.Register, AuditActions.RetentionReview, AuditActions.BulkReplace,
                        "create", "delete",
                    },
                    needsReason = AuditActions.NeedsReason,
                }));
    }
}
