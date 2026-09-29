using Scmos.Api.Ai;
using Scmos.Api.Auth;

namespace Scmos.Api.Endpoints;

public static class AiAuditEndpoints
{
    public static void MapAiAudit(this IEndpointRouteBuilder routes)
    {
        // Searchable since 29 Sep 2026 (§47): agent, tool, result, user, from/to (Bangkok days, yyyy-MM-dd), key.
        routes.MapGet("/api/ai/audit", async (long? beforeId, int? take, string? agent, string? tool, string? result,
            string? user, string? from, string? to, string? key, HttpContext context,
            IUserAccessor users, AiAuditReader reader, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var current = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(current)) return ApiResults.SignInRequired;
            if (!AiAuditReader.Allowed(current)) return ApiResults.Error("Audit access is unavailable", 403);
            if (beforeId is <= 0 || take is < 1 or > 100) return ApiResults.Error("Invalid audit pagination", 400);
            DateOnly? Day(string? value) => DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", out var day) ? day : null;
            if ((from is { Length: > 0 } && Day(from) is null) || (to is { Length: > 0 } && Day(to) is null))
                return ApiResults.Error("Dates are yyyy-MM-dd", 400);
            var search = new AuditSearch(agent, tool, result, user, Day(from), Day(to), key);
            if (search.Problem() is { } problem) return ApiResults.Error("Invalid audit search: " + problem, 400);
            try { return Results.Json(await reader.PageAsync(current!, beforeId, take ?? 25, token, search)); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return ApiResults.Error("AI audit is unavailable", 503); }
        }).WithTags("AI Audit");
        routes.MapGet("/api/ai/audit/{runId}", async (string runId, HttpContext context,
            IUserAccessor users, AiAuditReader reader, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiAuditReader.Allowed(user)) return ApiResults.Error("Audit access is unavailable", 403);
            if (!AiAuditRules.Id(runId)) return ApiResults.Error("Invalid audit run", 400);
            try
            {
                var run = await reader.RunAsync(user!, runId, token);
                return run is null ? Results.NotFound() : Results.Json(run);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception) { return ApiResults.Error("AI audit is unavailable", 503); }
        }).WithTags("AI Audit");
    }
}
