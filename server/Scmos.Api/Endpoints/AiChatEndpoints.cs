using System.Text.Json;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class AiChatEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 4 };

    public static void MapAiFoundation(this IEndpointRouteBuilder routes)
    {
        routes.MapAiAudit();
        routes.MapGet("/api/ai/status", async (HttpContext context, IUserAccessor users, AiGateway gateway,
            AgentOrchestrator runtime, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiPermissionPolicy.InternalUser(user!)) return ApiResults.Error("AI scope is unavailable", 403);
            return Results.Json(await runtime.StatusAsync(user!, token));
        }).WithTags("AI");

        routes.MapPost("/api/ai/chat", async (HttpContext context, IUserAccessor users,
            AiGateway gateway, AgentOrchestrator runtime, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiPermissionPolicy.InternalUser(user!)) return ApiResults.Error("AI scope is unavailable", 403);
            if (!context.Request.HasJsonContentType()) return ApiResults.Error("Expected application/json", 415);
            if (context.Request.ContentLength > AiRequestValidator.MaxBodyBytes)
                return ApiResults.Error("AI request is too large", 413);

            // Enforce the same bound for chunked requests, before deserialization.
            var buffer = new byte[AiRequestValidator.MaxBodyBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            if (count > AiRequestValidator.MaxBodyBytes) return ApiResults.Error("AI request is too large", 413);
            AiChatRequest? request;
            try { request = JsonSerializer.Deserialize<AiChatRequest>(buffer.AsSpan(0, count), JsonOptions); }
            catch (JsonException) { return ApiResults.Error("Invalid AI request; only message, agentId and context.page are accepted", 400); }
            var result = await gateway.ChatAsync(request, user, runtime, token);
            if (result.Status == 429) context.Response.Headers.RetryAfter = "60";
            return Results.Json(new
            {
                result.Response.RunId, result.Response.Code, result.Response.Summary, result.Response.AgentId,
                result.Response.Mock, result.Response.Usage, result.Response.Evidence,
                Error = result.Status >= 400 ? result.Response.Summary : null,
            }, statusCode: result.Status);
        }).WithTags("AI");
    }
}
