using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Subcontract Management's Action Plan (1 Oct 2026). Every route asks <see cref="ActionPlanService"/> whether
/// this person may read the plan, so a people development plan cannot be reached around the screen; a carrier
/// reaches none of it (<see cref="CarrierBoundary"/>). Evidence goes through <c>POST /api/documents</c> with
/// <c>actionPlanId</c>.
/// </summary>
public static class ActionPlanEndpoints
{
    public record MoveBody(string? Status, string? Reason);
    public record ReviewBody(string? Result, string? Comment);
    public record UpdateBody(string? Comment, long? ItemId, int? Progress);
    public record ScoreBody(string? Dimension, int? Previous, int? Current, int? Target);
    public record ReferenceBody(string? Kind, string? RefId, string? Label);
    public record TypeBody(string? DevelopmentType, string? Name, bool Active = true);

    public static void MapActionPlans(this IEndpointRouteBuilder routes)
    {
        var plans = routes.MapGroup("/api/action-plans").WithTags("Action Plan");

        plans.MapGet("", async ([AsParameters] ActionPlanFilter filter, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await ReadAsync(context, users, async user => Results.Json(await service.ListAsync(user, filter, token))));

        plans.MapGet("/dashboard", async (int? year, HttpContext context, IUserAccessor users, ActionPlanService service,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => Results.Json(await service.DashboardAsync(user, year, token))));

        plans.MapGet("/meta", async (HttpContext context, IUserAccessor users, ActionPlanService service, CancellationToken token) =>
            await ReadAsync(context, users, async user => Results.Json(new
            {
                types = await service.TypesAsync(token),
                people = await service.PeopleAsync(token),
                targetTypes = ActionPlanRules.TargetTypes,
                methods = ActionPlanRules.Methods,
                trainingTypes = ActionPlanRules.TrainingTypes,
                dimensions = ActionPlanRules.ScoreDimensions,
                canEdit = user.Can(Capability.EditActionPlans),
                canReview = user.Can(Capability.ReviewActionPlans),
                canConfigure = user.Can(Capability.AdministerData),
                me = user.OperatorId,
            })));

        plans.MapPost("/types", async ([FromBody] TypeBody body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.AdministerData))
                return ApiResults.Error("เฉพาะผู้ดูแลระบบที่แก้รายการประเภทแผนได้", StatusCodes.Status403Forbidden);
            return Answer(await service.SaveTypeAsync(user, body.DevelopmentType, body.Name, body.Active, token));
        });

        plans.MapGet("/{id:long}", async (long id, HttpContext context, IUserAccessor users, ActionPlanService service,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await service.ReadAsync(user, id, token) is { } detail
                ? Results.Json(detail) : ApiResults.Error("ไม่พบแผนนี้", StatusCodes.Status404NotFound)));

        plans.MapGet("/{id:long}/history", async (long id, HttpContext context, IUserAccessor users, ActionPlanService service,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await service.HistoryAsync(user, id, token) is { } rows
                ? Results.Json(rows.Select(row => new { row.Id, row.At, row.Who, row.Action, row.Field, row.OldValue, row.NewValue, row.Reason }))
                : ApiResults.Error("ไม่พบแผนนี้", StatusCodes.Status404NotFound)));

        plans.MapPost("", async ([FromBody] ActionPlanInput body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.CreateAsync(user, body, token)));

        plans.MapPut("/{id:long}", async (long id, [FromBody] ActionPlanInput body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.UpdateAsync(user, id, body, token)));

        plans.MapPost("/{id:long}/status", async (long id, [FromBody] MoveBody body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.MoveAsync(user, id, body.Status, body.Reason, token)));

        plans.MapPost("/{id:long}/review", async (long id, [FromBody] ReviewBody body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.ReviewAsync(user, id, body.Result, body.Comment, token)));

        plans.MapPost("/{id:long}/items", async (long id, [FromBody] ActionItemInput body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.AddItemAsync(user, id, body, token)));

        plans.MapPut("/{id:long}/items/{itemId:long}", async (long id, long itemId, [FromBody] ActionItemInput body,
            HttpContext context, IUserAccessor users, ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.UpdateItemAsync(user, id, itemId, body, token)));

        plans.MapDelete("/{id:long}/items/{itemId:long}", async (long id, long itemId, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.RemoveItemAsync(user, id, itemId, token)));

        plans.MapPost("/{id:long}/progress", async (long id, [FromBody] UpdateBody body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.AddUpdateAsync(user, id, body.Comment, body.ItemId, body.Progress, token)));

        plans.MapPost("/{id:long}/scores", async (long id, [FromBody] ScoreBody body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.ScoreAsync(user, id, body.Dimension, body.Previous, body.Current, body.Target, token)));

        plans.MapPost("/{id:long}/references", async (long id, [FromBody] ReferenceBody body, HttpContext context, IUserAccessor users,
            ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.AddReferenceAsync(user, id, body.Kind, body.RefId, body.Label, token)));

        plans.MapDelete("/{id:long}/references/{referenceId:long}", async (long id, long referenceId, HttpContext context,
            IUserAccessor users, ActionPlanService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.RemoveReferenceAsync(user, id, referenceId, token)));
    }

    private static async Task<IResult> ReadAsync(HttpContext context, IUserAccessor users, Func<AppUser, Task<IResult>> read)
    {
        context.Response.Headers.CacheControl = "no-store";
        var user = users.Current(context);
        return user is null ? ApiResults.SignInRequired : await read(user);
    }

    private static async Task<IResult> WriteAsync(HttpContext context, IUserAccessor users, Func<AppUser, Task<ActionPlanResult>> work)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        return Answer(await work(user));
    }

    private static IResult Answer(ActionPlanResult result) =>
        result.Ok ? Results.Json(new { message = result.Message, id = result.Id }) : ApiResults.Error(result.Message, result.Status);
}
