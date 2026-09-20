using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The assistant's permission matrix and its approval queue: what the
/// assistant may do, what it has proposed, and a person's word on each
/// proposal. Lived inside the supplier routes from the first AI release;
/// its own module since Phase 1E (20 Sep 2026), when the queue got rules
/// worth reading on their own — see <see cref="ApprovalPolicy"/>.
/// </summary>
public static class AiApprovalEndpoints
{
    public record InvokeBody(string? Tool, string? Summary, JsonElementPayload? Payload);
    public record DecideBody(bool Approved, string? Note);
    /// <summary>The fingerprint the person read (1E) must come back with the result, or nothing is recorded.</summary>
    public record AppliedBody(string? Result, string? Hash);
    public record CancelBody(string? Note);

    /// <summary>The tool arguments, kept opaque — the gateway stores them verbatim.</summary>
    public record JsonElementPayload(Dictionary<string, string>? Fields);

    public static void MapAiApprovals(this IEndpointRouteBuilder routes)
    {
        var ai = routes.MapGroup("/api/ai").WithTags("AI");

        // The permission matrix, readable so the screen can show what the
        // assistant may and may not do rather than asserting it in prose.
        ai.MapGet("/tools", async (HttpContext context, IUserAccessor users, AiGateway gateway,
            CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(new
            {
                tools = await gateway.ToolsAsync(token),
                forbidden = AiPermissions.Forbidden,
            });
        });

        ai.MapPost("/invoke", async ([FromBody] InvokeBody body, HttpContext context, IUserAccessor users,
            AiGateway gateway, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            // A carrier's account has no business in the assistant's queue —
            // neither proposing nor reading (1E).
            if (!AiPermissionPolicy.InternalUser(user))
                return ApiResults.Error("ใช้ได้เฉพาะบัญชีภายใน", StatusCodes.Status403Forbidden);

            // No executor is passed: nothing in the catalogue is wired to a real
            // action yet. An Allow tool therefore reports that plainly instead of
            // pretending to have done something.
            var outcome = await gateway.InvokeAsync(
                body.Tool ?? "", body.Summary ?? "", body.Payload?.Fields ?? new Dictionary<string, string>(),
                user, null, token, correlationId: AiAuditRules.CorrelationOf(context.Request.Headers["X-Correlation-Id"], context.TraceIdentifier));

            return outcome.Ok
                ? Results.Json(new { message = outcome.Message, kind = outcome.Kind, approvalId = outcome.ApprovalId })
                : ApiResults.Error(outcome.Message, StatusCodes.Status403Forbidden);
        });

        // The queue as this person may see it: all of it for an approver,
        // their own proposals for anybody else, nothing for a carrier (1E).
        ai.MapGet("/approvals", async (string? state, HttpContext context, IUserAccessor users,
            AiGateway gateway, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!AiPermissionPolicy.InternalUser(user))
                return ApiResults.Error("ใช้ได้เฉพาะบัญชีภายใน", StatusCodes.Status403Forbidden);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Json(await gateway.ApprovalsAsync(user, state, token));
        });

        // A person agreeing to a machine's proposal is the entry an audit will
        // care about most, so it is recorded with the decision note as the
        // reason — and with the assistant named as the source of the change.
        // The second factor is asked for first, as for every approval act.
        ai.MapPost("/approvals/{id:long}", async (long id, [FromBody] DecideBody body, HttpContext context,
            IUserAccessor users, AiGateway gateway, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!ApprovalPolicy.IsApprover(user))
                return ApiResults.Error("อนุมัติได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ApproveAi) is { } refusal) return refusal;
            var outcome = await gateway.DecideAsync(id, body.Approved, body.Note ?? "", user, token);
            if (!outcome.Ok) return ApiResults.Error(outcome.Message, StatusCodes.Status409Conflict);

            await audit.RecordAsync(user,
                body.Approved ? AuditActions.Approve : AuditActions.Reject,
                "approval", id.ToString(), outcome.Message, "", "pending",
                body.Approved ? "approved" : "rejected", body.Note ?? "", token, source: "ai");

            return Results.Json(new { message = outcome.Message });
        });

        // The requester withdraws what they proposed (or an approver does);
        // a row that is not theirs to see is not there (1E).
        ai.MapPost("/approvals/{id:long}/cancel", async (long id, [FromBody] CancelBody? body, HttpContext context,
            IUserAccessor users, AiGateway gateway, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!AiPermissionPolicy.InternalUser(user))
                return ApiResults.Error("ใช้ได้เฉพาะบัญชีภายใน", StatusCodes.Status403Forbidden);
            if (await gateway.FindAsync(id, user, token) is null)
                return ApiResults.Error("ไม่พบรายการนี้", StatusCodes.Status404NotFound);
            var outcome = await gateway.CancelAsync(id, body?.Note ?? "", user, token);
            if (!outcome.Ok) return ApiResults.Error(outcome.Message, StatusCodes.Status409Conflict);

            await audit.RecordAsync(user, AuditActions.Reject, "approval", id.ToString(), outcome.Message,
                "", "pending", "cancelled", body?.Note ?? "", token, source: "ai");

            return Results.Json(new { message = outcome.Message });
        });

        // A person records that they made the approved change — by hand, in
        // the screen the change belongs to. Nothing is executed here (1E: the
        // fingerprint they read must come back, and the second factor first).
        ai.MapPost("/approvals/{id:long}/applied", async (long id, [FromBody] AppliedBody body,
            HttpContext context, IUserAccessor users, AiGateway gateway, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!ApprovalPolicy.IsApprover(user))
                return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);
            if (ApiResults.NeedsSecondFactor(users, user, Capability.ApproveAi) is { } refusal) return refusal;
            var outcome = await gateway.MarkAppliedAsync(id, body.Result ?? "", body.Hash ?? "", user, token);
            if (!outcome.Ok) return ApiResults.Error(outcome.Message, StatusCodes.Status409Conflict);

            await audit.RecordAsync(user, AuditActions.Apply, "approval", id.ToString(), outcome.Message,
                "", "approved", "applied", body.Result ?? "", token, source: "ai");

            return Results.Json(new { message = outcome.Message });
        });
    }
}
