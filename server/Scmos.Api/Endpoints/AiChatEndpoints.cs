using System.Text.Json;
using System.Text.Json.Serialization;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Services;
using Scmos.Api.Rules;

namespace Scmos.Api.Endpoints;

public static class AiChatEndpoints
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SwitchRequest([property: JsonRequired] bool Enabled, [property: JsonRequired] int Revision);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 4 };

    public static void MapAiFoundation(this IEndpointRouteBuilder routes)
    {
        routes.MapAiAudit();
        routes.MapPost("/api/ai/operations-control", async (HttpContext context, IUserAccessor users,
            OperationsControlService control, AgentOrchestrator runtime, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!OperationsControlService.CanManage(user)) return ApiResults.Error("Administrator only", 403);
            if (users.Refuses(user!, Capability.AdministerData) is not null)
                return Results.Json(new { code = "second_factor_required", error = "Second-factor sign-in required" }, statusCode: 403);
            if (!context.Request.HasJsonContentType()) return ApiResults.Error("Expected application/json", 415);
            // Required custom header cannot be sent by a cross-site HTML form.
            if (context.Request.Headers["X-SCMOS-AI-Control"] != "1") return ApiResults.Error("Invalid control request", 400);
            var bytes = new byte[1025];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            if (count > 1024) return ApiResults.Error("Request too large", 413);
            SwitchRequest? request;
            try { request = JsonSerializer.Deserialize<SwitchRequest>(bytes.AsSpan(0, count), JsonOptions); }
            catch (JsonException) { return ApiResults.Error("Invalid switch request", 400); }
            if (request is null || request.Revision < 0) return ApiResults.Error("Invalid switch request", 400);
            if (request.Enabled)
            {
                var status = await runtime.StatusAsync(user!, token);
                if (status.OperationsControl?.CanEnable != true)
                    return Results.Json(new { code = status.OperationsControl?.BlockReason ?? "control_not_ready", error = "Operations AI is not ready" }, statusCode: 503);
            }
            var code = await control.SetAsync(user!, request.Enabled, request.Revision, token);
            return code == "ok" ? Results.Json(new { saved = true })
                : Results.Json(new { code, error = "Operations AI switch was not saved" }, statusCode: code == "control_conflict" ? 409 : code == "forbidden" ? 403 : 503);
        }).WithTags("AI");
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
