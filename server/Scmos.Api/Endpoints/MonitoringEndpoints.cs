using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class MonitoringEndpoints
{
    public record MilestoneBody(string? Stage, DateTimeOffset? ActualAt, string? Status, string? TruckNo,
        string? Driver, string? Remark, string? DelayReason, string? PhotoKey);

    public record DelayBody(string? Stage, string? Category, string? Detail, int? ImpactMinutes);

    public record DelayUpdateBody(long Id, string? NotifiedTeam, string? RecoveryAction, bool? Resolved);

    public static void MapMonitoring(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/shipment").WithTags("Monitoring");

        // The delay taxonomy, so the screen offers the eight and not free text.
        group.MapGet("/delay-reasons", () => Results.Json(
            Enum.GetValues<DelayCategory>().Select(category => new
            {
                id = category.ToString(),
                thai = DelayReasons.Thai(category),
                responsible = DelayReasons.ResponsibleFor(category).ToString(),
                responsibleThai = DelayReasons.Thai(DelayReasons.ResponsibleFor(category)),
                againstCarrier = DelayReasons.CountsAgainstCarrier(category),
            })));

        // What the classifier makes of a free-text reason, without saving anything.
        group.MapGet("/classify-delay", (string? text) =>
        {
            var suggestion = MonitoringService.Suggest(text ?? "");
            return Results.Json(new
            {
                category = suggestion.Category.ToString(),
                categoryThai = DelayReasons.Thai(suggestion.Category),
                responsible = suggestion.Responsible.ToString(),
                responsibleThai = DelayReasons.Thai(suggestion.Responsible),
                basis = suggestion.Basis,
                confidence = suggestion.Confidence,
            });
        });

        group.MapGet("/{jobKey}", async (string jobKey, HttpContext context, IUserAccessor users,
            MonitoringService monitoring, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var track = await monitoring.ReadAsync(jobKey, token);
            return track is null
                ? ApiResults.Error("ไม่พบงานนี้", StatusCodes.Status404NotFound)
                : Results.Json(track);
        });

        group.MapPost("/{jobKey}/milestone", async (string jobKey, [FromBody] MilestoneBody body,
            HttpContext context, IUserAccessor users, MonitoringService monitoring, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await monitoring.UpdateAsync(jobKey, body.Stage ?? "", body.ActualAt,
                body.Status ?? "", body.TruckNo ?? "", body.Driver ?? "", body.Remark ?? "",
                body.DelayReason ?? "", body.PhotoKey ?? "", user.Signature, token);

            return result.Ok
                ? Results.Json(new { message = result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });

        group.MapPost("/{jobKey}/delay", async (string jobKey, [FromBody] DelayBody body,
            HttpContext context, IUserAccessor users, MonitoringService monitoring, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var result = await monitoring.RecordDelayAsync(jobKey, body.Stage ?? "", body.Category,
                body.Detail ?? "", body.ImpactMinutes, user.Signature, token);

            return result.Ok
                ? Results.Json(new { message = result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });

        group.MapPost("/delay/update", async ([FromBody] DelayUpdateBody body, HttpContext context,
            IUserAccessor users, MonitoringService monitoring, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var result = await monitoring.UpdateDelayAsync(body.Id, body.NotifiedTeam,
                body.RecoveryAction, body.Resolved, token);
            return result.Ok
                ? Results.Json(new { message = result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });
    }
}
