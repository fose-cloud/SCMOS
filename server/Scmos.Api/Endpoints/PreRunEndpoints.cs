using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class PreRunEndpoints
{
    public record SendBody(string? JobKey);
    public record RespondBody(long Id, string? ConfirmedBy, string? TruckNo, string? Driver,
        string? DriverContact, string? Correction, string? Remark);
    public record ChaseBody(long Id, string? Note);

    public static void MapPreRun(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/pre-run").WithTags("Pre-run");

        // Defaults to tomorrow, which is what the process is for, but takes any
        // date — the July plan is already in the past and still has to be worked.
        group.MapGet("", async (string? date, HttpContext context, IUserAccessor users,
            PreRunService preRun, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var wanted = Clean(date) ?? DateTimeOffset.Now.AddDays(1).ToString("dd/MM/yyyy");
            return Results.Json(await preRun.BuildAsync(wanted, token));
        });

        group.MapPost("/send", async ([FromBody] SendBody body, HttpContext context, IUserAccessor users,
            PreRunService preRun, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await preRun.SendAsync(body.JobKey ?? "", user.Signature, token);
            return result.Ok
                ? Results.Json(new { message = result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });

        group.MapPost("/respond", async ([FromBody] RespondBody body, HttpContext context, IUserAccessor users,
            PreRunService preRun, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await preRun.RespondAsync(body.Id, body.ConfirmedBy ?? "", body.TruckNo ?? "",
                body.Driver ?? "", body.DriverContact ?? "", body.Correction ?? "", body.Remark ?? "",
                user.Signature, token);
            return result.Ok
                ? Results.Json(new { message = result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });

        group.MapPost("/chase", async ([FromBody] ChaseBody body, HttpContext context, IUserAccessor users,
            PreRunService preRun, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var result = await preRun.ChaseAsync(body.Id, body.Note ?? "", user.Signature, token);
            return result.Ok
                ? Results.Json(new { message = result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });
    }

    /// <summary>A plan date or nothing. Anything else is not a date this system uses.</summary>
    private static string? Clean(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Length == 10 && text[2] == '/' && text[5] == '/' ? text : null;
    }
}
