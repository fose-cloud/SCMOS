using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public record JourneyBody(string? From, string? To, int Km);

/// <summary>
/// How far a journey is, and what it has cost before.
///
/// Reading needs a signed-in account: anybody quoting has to see both. Recording
/// a distance needs the permission that changes a rate, because that is what it
/// does — the distance is one of the two numbers every quotation is built from,
/// and a wrong one prices every future journey on that road.
/// </summary>
public static class JourneyEndpoints
{
    public static void MapJourneys(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/journeys").WithTags("Journeys");

        group.MapGet("", async (HttpContext context, IUserAccessor users,
            JourneyService journeys, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await journeys.ListAsync(token));
        });

        // One journey: its remembered distance and its price history together,
        // because a screen that showed one without the other would be asking
        // somebody to quote a number with nothing to check it against.
        group.MapGet("/look", async (string? from, string? to, string? vehicle,
            HttpContext context, IUserAccessor users, JourneyService journeys,
            CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await journeys.LookAsync(from ?? "", to ?? "", vehicle ?? "", token));
        });

        group.MapPost("", async ([FromBody] JourneyBody body, HttpContext context,
            IUserAccessor users, JourneyService journeys, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.EditRates))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกระยะทาง", StatusCodes.Status403Forbidden);

            var result = await journeys.SaveAsync(body.From ?? "", body.To ?? "", body.Km,
                user.Signature, token);
            if (!result.Ok) return ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);

            await audit.RecordAsync(user, AuditActions.Update, "journey",
                JourneyKey.Of(body.From ?? "", body.To ?? ""), result.Message,
                "ระยะทาง", "", $"{body.Km} กม.", "", token);
            return Results.Json(new { message = result.Message, journey = result.Journey });
        });
    }
}
