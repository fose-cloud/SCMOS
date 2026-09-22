using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The proposed corrections, for the job's owner: what waits on which job,
/// and the yes or no — the same gate a LINE approval passes through.
/// Nothing here proposes; <see cref="Data.CorrectionProposer"/> does, from
/// the workflow, and nothing here writes a cell but an owner's click.
/// </summary>
public static class CorrectionEndpoints
{
    public record DecideBody(string? Note);

    public static void MapCorrections(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/corrections").WithTags("Corrections");

        // What is waiting on which job — for the workspace, which marks the
        // row and lets the owner decide from the drawer. Everybody signed in
        // sees the list; only the owner (or a cover, or an account that may
        // edit any job) can act on a row, judged when they do.
        group.MapGet("/pending", async (HttpContext context, IUserAccessor users, CorrectionService corrections, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var items = await corrections.PendingAsync(token);
            return Results.Json(new { items, count = items.Count, mine = await corrections.MineAsync(user, token) });
        });

        group.MapPost("/{id:long}/apply", async (long id, HttpContext context, IUserAccessor users, CorrectionService corrections, CancellationToken token) =>
        {
            if (Gate(context, users, out var user) is { } stop) return stop;
            return Answer(await corrections.ApplyAsync(id, user!, token));
        });

        group.MapPost("/{id:long}/reject", async (long id, [FromBody] DecideBody? body, HttpContext context, IUserAccessor users, CorrectionService corrections, CancellationToken token) =>
        {
            if (Gate(context, users, out var user) is { } stop) return stop;
            return Answer(await corrections.RejectAsync(id, user!, body?.Note, token));
        });

        // Every proposal on one job, approved together — the drawer's button.
        group.MapPost("/job/{key}/apply", async (string key, HttpContext context, IUserAccessor users, CorrectionService corrections, CancellationToken token) =>
        {
            if (Gate(context, users, out var user) is { } stop) return stop;
            return Answer(await corrections.ApplyJobAsync(key, user!, token));
        });

        // Every proposal on the jobs this person owns — the toolbar's button.
        group.MapPost("/apply-mine", async (HttpContext context, IUserAccessor users, CorrectionService corrections, CancellationToken token) =>
        {
            if (Gate(context, users, out var user) is { } stop) return stop;
            return Answer(await corrections.ApplyMineAsync(user!, token));
        });
    }

    /// <summary>Signed in, allowed to edit jobs at all, and past the second factor the edit would need — the LINE approval's own gate.</summary>
    private static IResult? Gate(HttpContext context, IUserAccessor users, out AppUser? user)
    {
        user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (!user.Can(Capability.EditAnyJob) && !user.Can(Capability.EditOwnJobs))
            return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขข้อมูลงาน", StatusCodes.Status403Forbidden);
        return ApiResults.NeedsSecondFactor(users, user, user.Can(Capability.EditAnyJob) ? Capability.EditAnyJob : Capability.EditOwnJobs);
    }

    private static IResult Answer(CorrectionOutcome outcome) => outcome.Ok
        ? Results.Json(new { message = outcome.Message, applied = outcome.Applied, stale = outcome.Stale, refused = outcome.Refused })
        : ApiResults.Error(outcome.Message, outcome.Status);
}
