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
    /// <param name="Carriers">Which of the campaign's carriers (their campaign row ids); empty means every included one.</param>
    public record SnapshotBody(List<int>? Carriers, string? Reason);
    public record ManualScoreBody(string? KpiCode, decimal Score, string? Note);
    public record ReasonBody(string? Reason);
    public record ExtendBody(string? ExpiresOn);
    public record DecisionBody(string? Decision, string? Note);

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

        // Phase 3: the evidence each carrier is scored on, taken as a new version each time (1 Oct 2026).
        campaigns.MapPost("/{id:int}/generate-snapshot", async (int id, [FromBody] SnapshotBody? body, HttpContext context, IUserAccessor users,
            EvaluationSnapshotService snapshots, CancellationToken token) =>
            await WriteAsync(context, users, user => snapshots.GenerateAsync(user, id, body?.Carriers, body?.Reason, token)));

        campaigns.MapGet("/{id:int}/carriers/{carrier:int}/snapshot", async (int id, int carrier, int? version, HttpContext context,
            IUserAccessor users, EvaluationSnapshotService snapshots, CancellationToken token) =>
            await ReadAsync(context, users, async user => await snapshots.ReadAsync(user, id, carrier, version, token)));

        // Phase 4: scoring. Each calculation is a new version of each carrier's result (1 Oct 2026).
        campaigns.MapPost("/{id:int}/calculate", async (int id, [FromBody] SnapshotBody? body, HttpContext context, IUserAccessor users,
            EvaluationScoringService scoring, CancellationToken token) =>
            await WriteAsync(context, users, user => scoring.CalculateAsync(user, id, body?.Carriers, body?.Reason, token)));

        campaigns.MapGet("/{id:int}/results", async (int id, HttpContext context, IUserAccessor users, EvaluationScoringService scoring,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await scoring.ResultsAsync(user, id, token)));

        campaigns.MapGet("/{id:int}/carriers/{carrier:int}/result", async (int id, int carrier, int? version, HttpContext context,
            IUserAccessor users, EvaluationScoringService scoring, CancellationToken token) =>
            await ReadAsync(context, users, async user => await scoring.ResultAsync(user, id, carrier, version, token)));

        campaigns.MapPut("/{id:int}/carriers/{carrier:int}/manual-score", async (int id, int carrier, [FromBody] ManualScoreBody body,
            HttpContext context, IUserAccessor users, EvaluationScoringService scoring, CancellationToken token) =>
            await WriteAsync(context, users, user => scoring.SetManualScoreAsync(user, id, carrier, body.KpiCode, body.Score, body.Note, token)));

        // Phase 7: evaluators and their links. A link's token is in the answer once, never again (1 Oct 2026).
        campaigns.MapGet("/{id:int}/evaluators", async (int id, HttpContext context, IUserAccessor users, EvaluationInvitationService invitations,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await invitations.EvaluatorsAsync(user, id, token)));

        campaigns.MapPost("/{id:int}/evaluators", async (int id, [FromBody] EvaluatorInput body, HttpContext context, IUserAccessor users,
            EvaluationInvitationService invitations, CancellationToken token) =>
            await LinkAsync(context, users, user => invitations.AddEvaluatorAsync(user, id, body, token)));

        campaigns.MapPost("/{id:int}/invitations", async (int id, [FromBody] InvitationRequest body, HttpContext context, IUserAccessor users,
            EvaluationInvitationService invitations, CancellationToken token) =>
            await LinkAsync(context, users, user => invitations.GenerateAsync(user, id, body, token)));

        campaigns.MapPost("/{id:int}/invitations/{invitation:long}/revoke", async (int id, long invitation, [FromBody] ReasonBody body,
            HttpContext context, IUserAccessor users, EvaluationInvitationService invitations, CancellationToken token) =>
            await LinkAsync(context, users, user => invitations.RevokeAsync(user, id, invitation, body.Reason, token)));

        campaigns.MapPost("/{id:int}/invitations/{invitation:long}/renew", async (int id, long invitation, HttpContext context, IUserAccessor users,
            EvaluationInvitationService invitations, CancellationToken token) =>
            await LinkAsync(context, users, user => invitations.RenewAsync(user, id, invitation, token)));

        campaigns.MapPost("/{id:int}/invitations/{invitation:long}/extend", async (int id, long invitation, [FromBody] ExtendBody body,
            HttpContext context, IUserAccessor users, EvaluationInvitationService invitations, CancellationToken token) =>
            await LinkAsync(context, users, user => invitations.ExtendAsync(user, id, invitation, body.ExpiresOn, token)));

        campaigns.MapPost("/{id:int}/invitations/{invitation:long}/sent", async (int id, long invitation, HttpContext context, IUserAccessor users,
            EvaluationInvitationService invitations, CancellationToken token) =>
            await LinkAsync(context, users, user => invitations.MarkSentAsync(user, id, invitation, token)));

        // Phases 8–9: the campaign at a glance, and management's decision on each carrier (2 Oct 2026).
        campaigns.MapGet("/{id:int}/summary", async (int id, HttpContext context, IUserAccessor users, EvaluationReviewService review,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await review.SummaryAsync(user, id, token)));

        campaigns.MapPut("/{id:int}/carriers/{carrier:int}/decision", async (int id, int carrier, [FromBody] DecisionBody body,
            HttpContext context, IUserAccessor users, EvaluationReviewService review, CancellationToken token) =>
            await WriteAsync(context, users, user => review.DecideAsync(user, id, carrier, body.Decision, body.Note, token)));

        // Phase 10: a carrier's improvement plan is an Action Plan, drafted from its findings; the campaign lists them (2 Oct 2026).
        campaigns.MapGet("/{id:int}/carriers/{carrier:int}/plan-draft", async (int id, int carrier, HttpContext context, IUserAccessor users,
            EvaluationPlanService plans, CancellationToken token) =>
            await ReadAsync(context, users, async user => await plans.DraftAsync(user, id, carrier, token)));

        campaigns.MapGet("/{id:int}/plans", async (int id, HttpContext context, IUserAccessor users, EvaluationPlanService plans,
            CancellationToken token) =>
            await ReadAsync(context, users, async user => await plans.PlansAsync(user, id, token)));

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

    /// <summary>An invitation change; a new link's token is in <c>links</c> and nowhere else, and the answer is not cached.</summary>
    private static async Task<IResult> LinkAsync(HttpContext context, IUserAccessor users, Func<AppUser, Task<InvitationOutcome>> work)
    {
        context.Response.Headers.CacheControl = "no-store";
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        var result = await work(user);
        return result.Ok ? Results.Json(new { message = result.Message, links = result.Links ?? [] }) : ApiResults.Error(result.Message, result.Status);
    }

    private static async Task<IResult> WriteAsync(HttpContext context, IUserAccessor users, Func<AppUser, Task<AnnualEvaluationResult>> work)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        var result = await work(user);
        return result.Ok ? Results.Json(new { message = result.Message, id = result.Id }) : ApiResults.Error(result.Message, result.Status);
    }
}
