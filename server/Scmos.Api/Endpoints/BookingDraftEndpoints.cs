using System.Text.Json;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Booking;
using Scmos.Api.Auth;

namespace Scmos.Api.Endpoints;

/// <summary>The Booking Agent's pasted-text read, for the add-job form (Agent Platform, 28 Sep 2026).</summary>
public static class BookingDraftEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { MaxDepth = 4 };
    /// <summary>Room for the longest text read (8,000 characters of Thai, three bytes each) and the envelope.</summary>
    private const int MaxBody = 32 * 1024;

    public sealed record DraftRequest(string? Category, string? Text);

    public static void MapBookingDraft(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/ai/booking-draft", async (HttpContext context, IUserAccessor users, BookingDraftService drafts,
            CancellationToken token) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var user = users.Current(context);
            if (!AiPermissionPolicy.Authenticated(user)) return ApiResults.SignInRequired;
            if (!context.Request.HasJsonContentType()) return ApiResults.Error("Expected application/json", 415);
            // A cross-site form cannot send the header, as for every AI control route.
            if (context.Request.Headers["X-SCMOS-AI-Control"] != "1") return ApiResults.Error("Invalid control request", 400);
            var bytes = new byte[MaxBody + 1];
            var count = 0;
            while (count < bytes.Length)
            {
                var read = await context.Request.Body.ReadAsync(bytes.AsMemory(count), token);
                if (read == 0) break;
                count += read;
            }
            if (count > MaxBody) return ApiResults.Error("Request too large", 413);
            DraftRequest? body;
            try { body = JsonSerializer.Deserialize<DraftRequest>(bytes.AsSpan(0, count), JsonOptions); }
            catch (JsonException) { return ApiResults.Error("Invalid request", 400); }
            if (body is null) return ApiResults.Error("Invalid request", 400);

            var correlationId = AiAuditRules.CorrelationOf(context.Request.Headers["X-Correlation-Id"], context.TraceIdentifier);
            context.Response.Headers["X-Correlation-Id"] = correlationId;
            var result = await drafts.DraftAsync(user!, body.Category, body.Text, correlationId, token);
            if (result.Draft is not { } draft) return ApiResults.Error(result.Error ?? "Could not read the text.", result.Status);
            return Results.Json(new
            {
                category = draft.Category,
                status = draft.Status,
                fields = draft.Verified,
                readings = draft.Fields.Where(one => one.Proposed.Length > 0 || one.Quote.Length > 0)
                    .Select(one => new { field = one.Field, proposed = one.Proposed, quote = one.Quote, verified = one.Verified, value = one.Value, reason = one.Reason }),
                missing = draft.Missing,
            });
        }).WithTags("AI");
    }
}
