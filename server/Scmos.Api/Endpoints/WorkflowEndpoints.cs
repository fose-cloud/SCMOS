using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The process, driven.
///
/// Every write here is a decision somebody made, so every write records who made
/// it. Ownership is checked the same way the register checks it: an Operation
/// User drives their own jobs, a supervisor drives anyone's.
/// </summary>
public static class WorkflowEndpoints
{
    public record AdvanceRequest(bool? Answer, string? Note);
    public record HoldRequest(string? Reason, string? Note);
    public record ReleaseRequest(string? Note);
    public record SupplierRequestBody(string? Carrier, int? QuotedPrice, string? SkipReason);
    public record SupplierResponseBody(long RequestId, string? Outcome, string? Reason);
    public record AssignCarrierBody(string? Carrier);

    public static void MapWorkflow(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/workflow").WithTags("Workflow");

        // The flow itself, so the screen can draw it without hard-coding it.
        group.MapGet("/definition", () => Results.Json(WorkflowService.Definition()));

        // Every workflow move, newest first. Append-only, so this is the record
        // rather than a copy of one.
        //
        // This used to answer /api/audit, which was the right name until there
        // was a general audit trail — a workflow move is one kind of change, not
        // the record of all of them. It lives under the workflow group now, and
        // /api/audit belongs to who-changed-what.
        group.MapGet("/events", async (int? limit, HttpContext context, IUserAccessor users,
            ScmosDbContext db, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var take = Math.Clamp(limit ?? 500, 1, 2000);
            var entries = await db.WorkflowEvents.AsNoTracking()
                .OrderByDescending(entry => entry.Id)
                .Take(take)
                .Select(entry => new
                {
                    id = entry.Id, jobKey = entry.JobKey, kind = entry.Kind,
                    from = entry.FromStage, to = entry.ToStage, hold = entry.Hold,
                    note = entry.Note, by = entry.By, at = entry.At,
                })
                .ToListAsync(token);
            return Results.Json(entries);
        });

        group.MapGet("/{jobKey}", async (string jobKey, HttpContext context, IUserAccessor users,
            WorkflowService workflow, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var state = await workflow.ReadAsync(jobKey, token);
            return state is null
                ? ApiResults.Error("ไม่พบงานนี้", StatusCodes.Status404NotFound)
                : Results.Json(state);
        });

        group.MapPost("/{jobKey}/advance", async (string jobKey, [FromBody] AdvanceRequest body,
            HttpContext context, IUserAccessor users, WorkflowService workflow, CancellationToken token) =>
            await Drive(context, users, jobKey, workflow,
                (user) => workflow.AdvanceAsync(jobKey, body.Answer, body.Note ?? "", user.Signature, token), token));

        group.MapPost("/{jobKey}/hold", async (string jobKey, [FromBody] HoldRequest body,
            HttpContext context, IUserAccessor users, WorkflowService workflow, CancellationToken token) =>
            await Drive(context, users, jobKey, workflow,
                (user) => workflow.HoldAsync(jobKey, body.Reason ?? "", body.Note ?? "", user.Signature, token), token));

        group.MapPost("/{jobKey}/release", async (string jobKey, [FromBody] ReleaseRequest body,
            HttpContext context, IUserAccessor users, WorkflowService workflow, CancellationToken token) =>
            await Drive(context, users, jobKey, workflow,
                (user) => workflow.ReleaseAsync(jobKey, body.Note ?? "", user.Signature, token), token));

        group.MapPost("/{jobKey}/supplier-request", async (string jobKey, [FromBody] SupplierRequestBody body,
            HttpContext context, IUserAccessor users, WorkflowService workflow, CancellationToken token) =>
            await Drive(context, users, jobKey, workflow,
                (user) => workflow.RequestSupplierAsync(jobKey, body.Carrier ?? "", body.QuotedPrice,
                    body.SkipReason, user.Signature, token), token));

        // The only route that puts a carrier on a job. Nothing else may.
        group.MapPost("/{jobKey}/assign-carrier", async (string jobKey, [FromBody] AssignCarrierBody body,
            HttpContext context, IUserAccessor users, WorkflowService workflow, CancellationToken token) =>
            await Drive(context, users, jobKey, workflow,
                (user) => workflow.AssignCarrierAsync(jobKey, body.Carrier ?? "", user.Signature, token), token));

        group.MapPost("/{jobKey}/supplier-response", async (string jobKey, [FromBody] SupplierResponseBody body,
            HttpContext context, IUserAccessor users, WorkflowService workflow, CancellationToken token) =>
            await Drive(context, users, jobKey, workflow,
                (user) => workflow.RespondSupplierAsync(jobKey, body.RequestId, body.Outcome ?? "", body.Reason ?? "", user.Signature, token), token));
    }

    /// <summary>
    /// One gate in front of every write: signed in, and allowed to touch this
    /// job. The ownership rule is the register's own — an Operation User drives
    /// their jobs, a supervisor drives all of them.
    /// </summary>
    private static async Task<IResult> Drive(HttpContext context, IUserAccessor users, string jobKey,
        WorkflowService workflow, Func<AppUser, Task<WorkflowOutcome>> action, CancellationToken token)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;

        if (!user.IsSupervisor)
        {
            var owner = await workflow.OwnerOfAsync(jobKey, token);
            if (owner is null) return ApiResults.Error("ไม่พบงานนี้", StatusCodes.Status404NotFound);
            if (user.OperatorId.Length == 0 || owner != user.OperatorId)
                return ApiResults.Error("แก้ไม่ได้ — งานนี้เป็นของคนอื่น", StatusCodes.Status403Forbidden);
        }

        var result = await action(user);
        return result.Ok
            ? Results.Json(new { message = result.Message, state = result.State })
            : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
    }
}
