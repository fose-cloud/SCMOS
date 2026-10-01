using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Annual Carrier Evaluation, the department's side (1 Oct 2026). Reading takes
/// <see cref="Capability.ViewAnnualEvaluation"/>, setting up <see cref="Capability.ManageAnnualEvaluation"/>, approving and
/// finalizing <see cref="Capability.DecideAnnualEvaluation"/> — all asked in <see cref="AnnualEvaluationService"/>. A carrier
/// reaches none of it (<see cref="CarrierBoundary"/>); evaluators outside SCMOS get their own routes, not these.
/// </summary>
public static class AnnualEvaluationEndpoints
{
    public record MoveBody(string? Status, string? Reason);

    public static void MapAnnualEvaluations(this IEndpointRouteBuilder routes)
    {
        var campaigns = routes.MapGroup("/api/annual-evaluations").WithTags("Annual Evaluation");

        campaigns.MapGet("", async (HttpContext context, IUserAccessor users, AnnualEvaluationService service, CancellationToken token) =>
            await ReadAsync(context, users, async user => await service.ListAsync(user, token)));

        campaigns.MapGet("/departments", async (HttpContext context, IUserAccessor users, AnnualEvaluationService service, CancellationToken token) =>
            await ReadAsync(context, users, async user => await service.DepartmentsAsync(user, token)));

        campaigns.MapPost("/departments", async ([FromBody] DepartmentInput body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.SaveDepartmentAsync(user, body, token)));

        campaigns.MapPost("", async ([FromBody] CreateCampaignInput body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.CreateAsync(user, body, token)));

        campaigns.MapGet("/{id:int}", async (int id, HttpContext context, IUserAccessor users, AnnualEvaluationService service,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await service.ReadAsync(user, id, token)));

        campaigns.MapPut("/{id:int}", async (int id, [FromBody] CampaignInput body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.UpdateAsync(user, id, body, token)));

        campaigns.MapPut("/{id:int}/kpis", async (int id, [FromBody] List<KpiInput> body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.SaveKpisAsync(user, id, body, token)));

        campaigns.MapPut("/{id:int}/questions", async (int id, [FromBody] List<QuestionInput> body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.SaveQuestionsAsync(user, id, body, token)));

        campaigns.MapPut("/{id:int}/departments", async (int id, [FromBody] List<CampaignDepartmentInput> body, HttpContext context,
            IUserAccessor users, AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.SaveDepartmentsAsync(user, id, body, token)));

        campaigns.MapPut("/{id:int}/score-bands", async (int id, [FromBody] List<ScoreBandInput> body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.SaveScoreBandsAsync(user, id, body, token)));

        campaigns.MapGet("/{id:int}/carriers", async (int id, HttpContext context, IUserAccessor users, AnnualEvaluationService service,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await service.CarriersAsync(user, id, token)));

        campaigns.MapPost("/{id:int}/carriers", async (int id, [FromBody] CarrierChangeInput body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.ChangeCarriersAsync(user, id, body, token)));

        campaigns.MapPost("/{id:int}/carriers/count", async (int id, HttpContext context, IUserAccessor users, AnnualEvaluationService service,
            CancellationToken token) =>
            await WriteAsync(context, users, user => service.RecountAsync(user, id, token)));

        campaigns.MapPost("/{id:int}/status", async (int id, [FromBody] MoveBody body, HttpContext context, IUserAccessor users,
            AnnualEvaluationService service, CancellationToken token) =>
            await WriteAsync(context, users, user => service.MoveAsync(user, id, body.Status, body.Reason, token)));
    }

    /// <summary>A read the service answers with null when this person may not see it — or it does not exist.</summary>
    private static async Task<IResult> ReadAsync<T>(HttpContext context, IUserAccessor users, Func<AppUser, Task<T?>> read) where T : class
    {
        context.Response.Headers.CacheControl = "no-store";
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (!user.Can(Capability.ViewAnnualEvaluation))
            return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดู Annual Evaluation", StatusCodes.Status403Forbidden);
        return await read(user) is { } found ? Results.Json(found) : ApiResults.Error("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> WriteAsync(HttpContext context, IUserAccessor users, Func<AppUser, Task<AnnualEvaluationResult>> work)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        var result = await work(user);
        return result.Ok ? Results.Json(new { message = result.Message, id = result.Id }) : ApiResults.Error(result.Message, result.Status);
    }
}
