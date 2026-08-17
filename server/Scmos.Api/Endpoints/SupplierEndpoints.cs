using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// Suppliers, rates, CAR/PAR and the AI gateway.
///
/// The write routes are supervisor-only wherever the action commits the company
/// to something: approving a vendor, changing a rate, deciding on an AI
/// proposal. Reading is open to anyone signed in.
/// </summary>
public static class SupplierEndpoints
{
    public record RegisterBody(string? Name, string? Code, string? ServiceType, string? ServiceArea);
    public record StatusBody(string? Status);
    public record AliasBody(string? Alias);
    public record EvaluateBody(string? Period, int? Safety, int? Documents, string? Note);
    public record RaiseBody(string? JobKey, string? Kind, string? Category, string? Title);
    public record EvidenceBody(string? Kind, string? FileName, string? ObjectKey, string? Note);
    public record InvokeBody(string? Tool, string? Summary, JsonElementPayload? Payload);
    public record DecideBody(bool Approved, string? Note);
    public record AppliedBody(string? Result);

    /// <summary>The tool arguments, kept opaque — the gateway stores them verbatim.</summary>
    public record JsonElementPayload(Dictionary<string, string>? Fields);

    public static void MapSuppliers(this IEndpointRouteBuilder routes)
    {
        /* ------------------------------------------------------- suppliers */
        var suppliers = routes.MapGroup("/api/suppliers").WithTags("Suppliers");

        suppliers.MapGet("", async (string? status, string? q, HttpContext context, IUserAccessor users,
            SupplierService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service.ListAsync(status, q, token));
        });

        suppliers.MapGet("/{id:int}", async (int id, HttpContext context, IUserAccessor users,
            SupplierService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var profile = await service.ProfileAsync(id, token);
            return profile is null
                ? ApiResults.Error("ไม่พบผู้ขนส่งรายนี้", StatusCodes.Status404NotFound)
                : Results.Json(profile);
        });

        suppliers.MapPost("", async ([FromBody] RegisterBody body, HttpContext context, IUserAccessor users,
            SupplierService service, CancellationToken token) =>
            await Guarded(context, users, true, user => service.RegisterAsync(
                body.Name ?? "", body.Code ?? "", body.ServiceType ?? "", body.ServiceArea ?? "",
                user.Signature, token)));

        suppliers.MapPost("/{id:int}/status", async (int id, [FromBody] StatusBody body, HttpContext context,
            IUserAccessor users, SupplierService service, CancellationToken token) =>
            await Guarded(context, users, true, user => service.SetStatusAsync(
                id, body.Status ?? "", user.Signature, token)));

        suppliers.MapPost("/{id:int}/alias", async (int id, [FromBody] AliasBody body, HttpContext context,
            IUserAccessor users, SupplierService service, CancellationToken token) =>
            await Guarded(context, users, true, user => service.LinkAliasAsync(
                id, body.Alias ?? "", user.Signature, token)));

        suppliers.MapPost("/{id:int}/evaluate", async (int id, [FromBody] EvaluateBody body, HttpContext context,
            IUserAccessor users, SupplierService service, CancellationToken token) =>
            await Guarded(context, users, true, user => service.EvaluateAsync(
                id, body.Period ?? "", body.Safety, body.Documents, body.Note ?? "", user.Signature, token)));

        /* ----------------------------------------------------------- rates */
        var rates = routes.MapGroup("/api/rates").WithTags("Rates");

        rates.MapGet("", async (string? carrier, string? service, HttpContext context, IUserAccessor users,
            RateService service_, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service_.ReadAsync(carrier, service, token));
        });

        rates.MapGet("/quotes", async (string? customer, string? destination, string? vehicle, decimal? diesel,
            HttpContext context, IUserAccessor users, RateService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var quotes = await service.QuotesForAsync(customer ?? "", destination ?? "",
                vehicle ?? "", diesel ?? 32.94m, token);
            return Results.Json(quotes);
        });

        /* ------------------------------------------------------- incidents */
        var incidents = routes.MapGroup("/api/incidents").WithTags("Incidents");

        incidents.MapGet("", async (string? stage, string? kind, HttpContext context, IUserAccessor users,
            IncidentService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await service.ListAsync(stage, kind, token));
        });

        incidents.MapGet("/{id:long}", async (long id, HttpContext context, IUserAccessor users,
            IncidentService service, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var view = await service.ReadAsync(id, token);
            return view is null ? ApiResults.Error("ไม่พบเคสนี้", StatusCodes.Status404NotFound) : Results.Json(view);
        });

        incidents.MapPost("", async ([FromBody] RaiseBody body, HttpContext context, IUserAccessor users,
            IncidentService service, CancellationToken token) =>
            await GuardedIncident(context, users, false, (user, _) => service.RaiseAsync(
                body.JobKey ?? "", body.Kind ?? "CAR", body.Category ?? "other", body.Title ?? "",
                user.Signature, token)));

        incidents.MapPost("/{id:long}", async (long id, [FromBody] Dictionary<string, string> body,
            HttpContext context, IUserAccessor users, IncidentService service, CancellationToken token) =>
            await GuardedIncident(context, users, false, (user, _) => service.UpdateAsync(
                id, body ?? [], user.Signature, token)));

        incidents.MapPost("/{id:long}/advance", async (long id, HttpContext context, IUserAccessor users,
            IncidentService service, CancellationToken token) =>
            await GuardedIncident(context, users, false, (user, role) => service.AdvanceAsync(
                id, user.Signature, role, token)));

        incidents.MapPost("/{id:long}/evidence", async (long id, [FromBody] EvidenceBody body,
            HttpContext context, IUserAccessor users, IncidentService service, CancellationToken token) =>
            await GuardedIncident(context, users, false, (user, _) => service.AddEvidenceAsync(
                id, body.Kind ?? "", body.FileName ?? "", body.ObjectKey ?? "", body.Note ?? "",
                user.Signature, token)));

        /* --------------------------------------------------------------- AI */
        var ai = routes.MapGroup("/api/ai").WithTags("AI");

        // The permission matrix, readable so the screen can show what the
        // assistant may and may not do rather than asserting it in prose.
        ai.MapGet("/tools", async (HttpContext context, IUserAccessor users, AiGateway gateway,
            CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(new
            {
                tools = await gateway.ToolsAsync(token),
                forbidden = AiPermissions.Forbidden,
            });
        });

        ai.MapPost("/invoke", async ([FromBody] InvokeBody body, HttpContext context, IUserAccessor users,
            AiGateway gateway, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // No executor is passed: nothing in the catalogue is wired to a real
            // action yet. An Allow tool therefore reports that plainly instead of
            // pretending to have done something.
            var outcome = await gateway.InvokeAsync(
                body.Tool ?? "", body.Summary ?? "", body.Payload?.Fields ?? new Dictionary<string, string>(),
                user, null, token);

            return outcome.Ok
                ? Results.Json(new { message = outcome.Message, kind = outcome.Kind, approvalId = outcome.ApprovalId })
                : ApiResults.Error(outcome.Message, StatusCodes.Status403Forbidden);
        });

        ai.MapGet("/approvals", async (string? state, HttpContext context, IUserAccessor users,
            AiGateway gateway, CancellationToken token) =>
        {
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            return Results.Json(await gateway.ApprovalsAsync(state, token));
        });

        ai.MapPost("/approvals/{id:long}", async (long id, [FromBody] DecideBody body, HttpContext context,
            IUserAccessor users, AiGateway gateway, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            var outcome = await gateway.DecideAsync(id, body.Approved, body.Note ?? "", user, token);
            return outcome.Ok
                ? Results.Json(new { message = outcome.Message })
                : ApiResults.Error(outcome.Message, StatusCodes.Status403Forbidden);
        });

        ai.MapPost("/approvals/{id:long}/applied", async (long id, [FromBody] AppliedBody body,
            HttpContext context, IUserAccessor users, AiGateway gateway, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!AiPermissions.CanApprove(user.Role))
                return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);
            var outcome = await gateway.MarkAppliedAsync(id, body.Result ?? "", token);
            return outcome.Ok
                ? Results.Json(new { message = outcome.Message })
                : ApiResults.Error(outcome.Message, StatusCodes.Status400BadRequest);
        });
    }

    private static async Task<IResult> Guarded(HttpContext context, IUserAccessor users,
        bool supervisorOnly, Func<AppUser, Task<SupplierResult>> action)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (supervisorOnly && !user.IsSupervisor)
            return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);

        var result = await action(user);
        return result.Ok
            ? Results.Json(new { message = result.Message, id = result.Id })
            : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> GuardedIncident(HttpContext context, IUserAccessor users,
        bool supervisorOnly, Func<AppUser, string, Task<IncidentResult>> action)
    {
        var user = users.Current(context);
        if (user is null) return ApiResults.SignInRequired;
        if (supervisorOnly && !user.IsSupervisor)
            return ApiResults.Error("ทำได้เฉพาะระดับหัวหน้างานขึ้นไป", StatusCodes.Status403Forbidden);

        var result = await action(user, user.Role);
        return result.Ok
            ? Results.Json(new { message = result.Message, id = result.Id })
            : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
    }
}
