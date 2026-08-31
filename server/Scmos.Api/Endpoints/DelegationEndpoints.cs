using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Handing my work to a colleague while I am away.
///
/// The owner is always the signed-in person. There is no parameter for it, and
/// that is deliberate: a body that named an owner would let anybody grant
/// themselves access to anybody's jobs, which is the opposite of what this is
/// for.
/// </summary>
public static class DelegationEndpoints
{
    /// <param name="OwnerId">
    /// Whose jobs to hand over. Omitted means the caller's own; naming somebody
    /// else needs the authority to assign work, and the service refuses rather
    /// than falling back to the caller.
    /// </param>
    public record GrantBody(string? DelegateId, string? FromDate, string? ToDate, string? Reason,
        string? OwnerId);

    public static void MapDelegations(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/delegations").WithTags("Delegation");

        group.MapGet("", async (bool? all, HttpContext context, IUserAccessor users,
            DelegationService delegations, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // Everybody's grants are an administrator's business — it is the
            // list that answers "who could have edited this". Everyone else sees
            // the ones they made and the ones they were given.
            var everything = all == true && user.Can(Capability.AdministerData);

            return Results.Json(new
            {
                grants = await delegations.ForPersonAsync(user.OperatorId, everything, token),
                you = user.OperatorId,
                canSeeAll = user.Can(Capability.AdministerData),
                actingFor = await delegations.ActingForAsync(user.OperatorId, token),
            });
        });

        // Who I may hand my jobs to. Signed in is enough: arranging cover for
        // your own leave is not an audit question, and the staff endpoint this
        // list used to come from needs ViewAudit — which the operators who
        // actually take leave do not have, so the dropdown was always empty.
        group.MapGet("/candidates", async (HttpContext context, IUserAccessor users,
            DelegationService delegations, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Results.Json(await delegations.CandidatesAsync(user.OperatorId, token));
        });

        // Whose work this person may arrange cover for, themselves aside.
        // Empty for everybody without the authority to assign work, which is
        // what the form uses to decide whether to offer the choice at all.
        group.MapGet("/owners", async (HttpContext context, IUserAccessor users,
            DelegationService delegations, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!DelegationService.MayArrangeForOthers(user)) return Results.Json(Array.Empty<object>());
            return Results.Json(await delegations.OwnersAsync(user.OperatorId, token));
        });

        group.MapPost("", async ([FromBody] GrantBody body, HttpContext context, IUserAccessor users,
            DelegationService delegations, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // The same team that arranges carriers and keeps the training
            // register: Operation User and above. A read-only account has no
            // work of its own to hand over.
            if (!user.Can(Capability.EditOwnJobs))
                return ApiResults.Error("บัญชีนี้ไม่มีงานของตัวเองให้มอบหมาย",
                    StatusCodes.Status403Forbidden);

            var result = await delegations.GrantAsync(user, body.OwnerId ?? "", body.DelegateId ?? "",
                body.FromDate ?? "", body.ToDate ?? "", body.Reason ?? "", token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            // The owner is recorded even when it is the caller: the line that
            // answers "who could have edited this" should not need the reader
            // to know whether the field was blank that day.
            await audit.RecordAsync(user, AuditActions.Register, "delegation",
                result.Id.ToString(), user.DisplayName, "delegate", "",
                $"{body.OwnerId ?? user.OperatorId} → {body.DelegateId} · {body.FromDate}–{body.ToDate}",
                body.Reason ?? "", token);

            return Results.Json(new { message = result.Message, id = result.Id });
        });

        group.MapPost("/{id:long}/revoke", async (long id, HttpContext context, IUserAccessor users,
            DelegationService delegations, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await delegations.RevokeAsync(id, user, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status403Forbidden);

            await audit.RecordAsync(user, AuditActions.Update, "delegation", id.ToString(),
                "", "revoked", "false", "true", "ยกเลิกการมอบสิทธิ์ก่อนกำหนด", token);

            return Results.Json(new { message = result.Message });
        });
    }
}
