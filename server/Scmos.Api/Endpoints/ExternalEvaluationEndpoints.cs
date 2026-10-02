using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The evaluation page for people outside SCMOS (1 Oct 2026, Annual Evaluation Phase 6) — the only routes that answer
/// without a sign-in. The token in <see cref="TokenHeader"/> is the whole of the caller's identity and is never logged;
/// the requests are rate-limited per token, and every answer says not to cache, index or pass the page on.
/// </summary>
public static class ExternalEvaluationEndpoints
{
    public const string RateLimitPolicy = "external-evaluation";
    public const string TokenHeader = "X-Evaluation-Token";

    public static void MapExternalEvaluation(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/external/evaluation").WithTags("External Evaluation").RequireRateLimiting(RateLimitPolicy);

        group.MapGet("", async (HttpContext context, ExternalEvaluationService service, CancellationToken token) =>
        {
            Private(context);
            return Answer(await service.ReadAsync(TokenOf(context), token));
        });

        group.MapPost("/submit", async ([FromBody] ExternalSubmission? body, HttpContext context, ExternalEvaluationService service,
            CancellationToken token) =>
        {
            Private(context);
            return Answer(await service.SubmitAsync(TokenOf(context), body ?? new ExternalSubmission([], ""), token));
        });
    }

    public static string? TokenOf(HttpContext context) => context.Request.Headers[TokenHeader].FirstOrDefault()?.Trim();

    private static void Private(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    private static IResult Answer(ExternalOutcome outcome) =>
        outcome.Status == StatusCodes.Status200OK
            ? Results.Json(new { message = outcome.Message, view = outcome.View })
            : ApiResults.Error(outcome.Message, outcome.Status);
}
