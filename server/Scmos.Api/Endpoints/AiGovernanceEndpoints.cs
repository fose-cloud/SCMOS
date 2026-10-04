using System.Text.Json;
using System.Text.Json.Serialization;
using Scmos.Api.Ai;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The AI platform's governance — the Agent Platform foundation (27 Sep 2026):
/// every agent's autonomy, shadow mode, status, health and cost, the platform's
/// execution switch, and the decision log. Reading is for whoever may read the
/// audit; changing is the Administrator's, with the second factor, a reason and
/// the revision read — the same protections the Operations switch has.
/// </summary>
public static class AiGovernanceEndpoints
{
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record SettingsRequest([property: JsonRequired] int Autonomy, bool ShadowMode, string? Status,
        [property: JsonRequired] string Reason, [property: JsonRequired] int Revision);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record AgentSwitchRequest([property: JsonRequired] string Target, [property: JsonRequired] bool On,
        [property: JsonRequired] int Revision);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record OutcomeRequest([property: JsonRequired] string Outcome, string? Choice, string? Reason);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 4 };
    private const int MaxBody = 2048;

    public static void MapAiGovernance(this IEndpointRouteBuilder routes)
    {
        var ai = routes.MapGroup("/api/ai").WithTags("AI");

        // Grants have no API mutation route. Normal administrators may inspect/revoke runtime switches, not expand policy.
        ai.MapGet("/policies", async (HttpContext context, IUserAccessor users, AiGateway gateway, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiGovernanceService.CanView(user)) return ApiResults.Error("AI policy is for administrators and auditors", 403);
            return Results.Json(await gateway.PoliciesAsync(token));
        });

        ai.MapGet("/agents", async (HttpContext context, IUserAccessor users, AiGovernanceService governance, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiGovernanceService.CanView(user)) return ApiResults.Error("AI governance is for administrators and auditors", 403);
            return Results.Json(await governance.ReportAsync(user!, token));
        });

        // One agent's settings, or the platform's ceiling under the id "platform".
        ai.MapPut("/agents/{agentId}/settings", async (string agentId, HttpContext context, IUserAccessor users,
            AiGovernanceService governance, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiGovernanceService.CanManage(user)) return ApiResults.Error("Administrator only", 403);
            if (users.Refuses(user!, Capability.AdministerData) is not null)
                return Results.Json(new { code = "second_factor_required", error = "Second-factor sign-in required" }, statusCode: 403);
            var body = await ReadAsync<SettingsRequest>(context, token);
            if (body.Problem is { } problem) return problem;
            var request = body.Value!;
            var code = await governance.SetAsync(user!, agentId, request.Autonomy,
                request.ShadowMode, request.Status, request.Reason, request.Revision, token);
            return code == "ok" ? Results.Json(new { saved = true })
                : Results.Json(new { code, error = "The AI setting was not saved" }, statusCode: code switch
                {
                    "forbidden" => 403,
                    "unknown_agent" => 404,
                    "conflict" => 409,
                    "unavailable" => 503,
                    _ => 400,
                });
        });

        // The Control Tower's on/off switch (29 Sep 2026): { target: "agent" | "pass", on, revision }. It holds over
        // the agent's flag in configuration until switched again — no Portal change, no restart; AI__Enabled off
        // still stops every agent. Operations keeps its own switch (POST /api/ai/operations-control).
        ai.MapPut("/agents/{agentId}/switch", async (string agentId, HttpContext context, IUserAccessor users,
            AiGovernanceService governance, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiGovernanceService.CanManage(user)) return ApiResults.Error("Administrator only", 403);
            if (users.Refuses(user!, Capability.AdministerData) is not null)
                return Results.Json(new { code = "second_factor_required", error = "Second-factor sign-in required" }, statusCode: 403);
            var body = await ReadAsync<AgentSwitchRequest>(context, token);
            if (body.Problem is { } problem) return problem;
            var request = body.Value!;
            var code = await governance.SwitchAsync(user!, agentId, request.Target, request.On, request.Revision, token);
            return code == "ok" ? Results.Json(new { saved = true })
                : Results.Json(new { code, error = "The AI switch was not saved" }, statusCode: code switch
                {
                    "forbidden" => 403,
                    "unknown_agent" => 404,
                    "conflict" => 409,
                    "unavailable" => 503,
                    _ => 400,
                });
        });

        // A rule-first agent's pass, now — what the scheduler does every AI__AgentScanMinutes. Governance still
        // decides whether it writes anything; the answer says what the pass found and changed.
        ai.MapPost("/agents/{agentId}/scan", async (string agentId, HttpContext context, IUserAccessor users,
            AgentScanner scanner, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiGovernanceService.CanManage(user)) return ApiResults.Error("Administrator only", 403);
            if (context.Request.Headers["X-SCMOS-AI-Control"] != "1") return ApiResults.Error("Invalid control request", 400);
            if (!AgentScanner.Agents.Contains(agentId, StringComparer.Ordinal))
                return Results.Json(new { code = "not_scannable", error = "This agent does not run a pass" }, statusCode: 404);
            var summary = (await scanner.ScanAsync(token, agentId)).Single();
            return Results.Json(summary, statusCode: summary.Code is "ok" or "answered_meanwhile" ? 200 : 503);
        });

        // The Control Tower's cards and My AI Tasks (§43, §44): counts in the decision list's own scope.
        ai.MapGet("/tasks", async (HttpContext context, IUserAccessor users, AiTasksService tasks, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiDecisionLog.CanList(user)) return ApiResults.Error("AI tasks are for the department's own accounts", 403);
            return Results.Json(await tasks.CountAsync(user!, token));
        });

        // The history's search too (§47): from/to are Bangkok days (yyyy-MM-dd); type, q, customer, carrier and
        // decidedBy are matched as written. Nothing here widens who sees what — the list's own scope still applies.
        ai.MapGet("/decisions", async (string? status, string? agent, string? entityType, string? entityId, int? page, int? pageSize,
            string? from, string? to, string? type, string? q, string? customer, string? carrier, string? decidedBy,
            HttpContext context, IUserAccessor users, AiDecisionLog decisions, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiDecisionLog.CanList(user)) return ApiResults.Error("AI decisions are for the department's own accounts", 403);
            DateOnly? Day(string? value) => DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", out var day) ? day : null;
            if ((from is { Length: > 0 } && Day(from) is null) || (to is { Length: > 0 } && Day(to) is null))
                return ApiResults.Error("Dates are yyyy-MM-dd", 400);
            var search = new DecisionSearch(Day(from), Day(to), type, q, customer, carrier, decidedBy);
            if (search.Problem() is { } problem) return ApiResults.Error("Invalid search: " + problem, 400);
            var pageNo = Math.Max(1, page ?? 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 200);
            var (items, total) = await decisions.ListAsync(user!, status, agent, entityType, entityId, pageNo, size, token, search);
            return Results.Json(new { items, total, page = pageNo, pageSize = size });
        });

        // What a person did about a decision — agreed, chose otherwise and why, or set it aside.
        ai.MapPost("/decisions/{id:long}/outcome", async (long id, HttpContext context, IUserAccessor users,
            AiDecisionLog decisions, CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!AiDecisionLog.CanList(user)) return ApiResults.Error("AI decisions are for the department's own accounts", 403);
            var body = await ReadAsync<OutcomeRequest>(context, token);
            if (body.Problem is { } problem) return problem;
            var code = await decisions.AnswerAsync(id, user!, body.Value!.Outcome, body.Value.Choice, body.Value.Reason, token);
            return code == "ok" ? Results.Json(new { saved = true })
                : Results.Json(new { code, error = "The answer was not saved" }, statusCode: code switch
                {
                    "forbidden" => 403,
                    "not_found" => 404,
                    "already_answered" => 409,
                    _ => 400,
                });
        });
    }

    /// <summary>
    /// A small JSON body with the control header — a cross-site form cannot send
    /// the header, so a signed-in administrator's browser cannot be made to
    /// change a setting by another page.
    /// </summary>
    private static async Task<(T? Value, IResult? Problem)> ReadAsync<T>(HttpContext context, CancellationToken token) where T : class
    {
        if (!context.Request.HasJsonContentType()) return (null, ApiResults.Error("Expected application/json", 415));
        if (context.Request.Headers["X-SCMOS-AI-Control"] != "1") return (null, ApiResults.Error("Invalid control request", 400));
        var bytes = new byte[MaxBody + 1];
        var count = 0;
        while (count < bytes.Length)
        {
            var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), token);
            if (read == 0) break;
            count += read;
        }
        if (count > MaxBody) return (null, ApiResults.Error("Request too large", 413));
        try
        {
            var value = JsonSerializer.Deserialize<T>(bytes.AsSpan(0, count), JsonOptions);
            return value is null ? (null, ApiResults.Error("Invalid request", 400)) : (value, null);
        }
        catch (JsonException) { return (null, ApiResults.Error("Invalid request", 400)); }
    }
}
