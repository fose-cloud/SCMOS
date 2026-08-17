using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Data;

namespace Scmos.Api.Endpoints;

public static class JobsEndpoints
{
    public record SaveRequest(List<JsonElement>? Jobs, string? By);
    public record DeleteRequest(List<string>? Keys, bool? All, string? By);

    public static void MapJobs(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("", async (HttpContext context, IUserAccessor users, JobsRepository jobs, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var (json, _) = await jobs.LoadAsync(token);
            // Written verbatim: the rows are already JSON and were checked on the way out.
            return Results.Text(json, "application/json");
        });

        group.MapPut("", async (SaveRequest body, HttpContext context, IUserAccessor users, JobsRepository jobs, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var incoming = body.Jobs ?? [];
            if (incoming.Count == 0) return Results.Json(new { saved = 0 });
            if (incoming.Count > JobsRepository.Limit)
                return ApiResults.Error($"Too many jobs in one save (max {JobsRepository.Limit})", StatusCodes.Status413PayloadTooLarge);

            var (saved, at) = await jobs.SaveAsync(incoming, user.Signature, token);
            return Results.Json(new { saved, updatedAt = at.ToUniversalTime().ToString("O") });
        });

        // DELETE never infers a body, so it has to be asked for by name. The
        // workspace sends one: either the keys to remove or `all` to wipe.
        group.MapDelete("", async ([FromBody] DeleteRequest? body, HttpContext context, IUserAccessor users, JobsRepository jobs, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            if (body?.All == true)
            {
                // Wiping the register is how a corrected plan file gets in. Only a
                // supervisor may do it — the workspace hides the button from
                // everyone else, and this is the half that cannot be clicked past.
                if (!user.IsSupervisor)
                    return ApiResults.Error("Only a supervisor may clear the register", StatusCodes.Status403Forbidden);
                await jobs.ClearAsync(token);
                return Results.Json(new { cleared = true });
            }

            var deleted = await jobs.DeleteAsync(body?.Keys ?? [], token);
            return Results.Json(new { deleted });
        });
    }
}
