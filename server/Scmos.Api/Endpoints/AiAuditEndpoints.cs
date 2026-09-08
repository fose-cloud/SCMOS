using Scmos.Api.Ai;
using Scmos.Api.Auth;

namespace Scmos.Api.Endpoints;

public static class AiAuditEndpoints
{
    public static void MapAiAudit(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/ai/audit", async (long? beforeId, int? take, HttpContext context,
            IUserAccessor users, AiAuditReader reader, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiAuditReader.Allowed(user)) return ApiResults.Error("Audit access is unavailable", 403);
            if (beforeId is <= 0 || take is < 1 or > 100) return ApiResults.Error("Invalid audit pagination", 400);
            try { return Results.Json(await reader.PageAsync(user!, beforeId, take ?? 25, token)); }
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
