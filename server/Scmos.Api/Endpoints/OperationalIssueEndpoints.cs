using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

/// <summary>
/// The daily issue log.
///
/// Reading is open to anyone who may see the dashboard, because what went wrong
/// yesterday is not confidential to the person it went wrong for — the log only
/// works if the next shift can read it. Writing wants the same capability as
/// editing a job: whoever is running the work is who notices the problem.
///
/// Every row carries its own <c>created_by</c> and <c>updated_by</c>, so this
/// does not also write to the audit trail. The audit exists because the
/// register keeps no history of its own; this table keeps its own.
/// </summary>
public static class OperationalIssueEndpoints
{
    /// <summary>What a caller may send when raising or importing an issue.</summary>
    public record IssueBody(
        string? Code, string? FoundOn, string? FoundAt, string? Source, string? Reporter,
        string? JobRef, string? JobKey, string? Detail, string? Category, string? Severity,
        string? Impact, string? Channel, string? Owner, string? OwnerId, string? DueOn,
        string? Status, string? RootCause,
        // Optional so the Excel import, which has no such columns, still binds.
        string? Driver = null, string? ContainerNo = null, string? Licence = null);

    public record ImportBody(List<IssueBody>? Issues);

    public static void MapOperationalIssues(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/issues").WithTags("OperationalIssues");

        group.MapGet("/form", async (HttpContext context, IUserAccessor users,
            OperationalIssueService issues, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            return Results.Json(await issues.FormAsync(token));
        });

        group.MapGet("", async (string? status, string? severity, string? jobKey, string? owner,
            HttpContext context, IUserAccessor users, OperationalIssueService issues,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงานปัญหา", StatusCodes.Status403Forbidden);

            return Results.Json(new { issues = await issues.ListAsync(status, severity, jobKey, owner, token) });
        });

        group.MapGet("/summary", async (HttpContext context, IUserAccessor users,
            OperationalIssueService issues, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!user.Can(Capability.ViewDashboard))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ดูรายงานปัญหา", StatusCodes.Status403Forbidden);

            return Results.Json(await issues.SummaryAsync(token));
        });

        group.MapPost("", async ([FromBody] IssueBody? body, HttpContext context,
            IUserAccessor users, OperationalIssueService issues, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!Writes(user))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์บันทึกปัญหา", StatusCodes.Status403Forbidden);
            if (body is null) return ApiResults.Error("ไม่มีข้อมูล", StatusCodes.Status400BadRequest);

            var result = await issues.RaiseAsync(Build(body, user), user.Signature, token);
            return result.Ok
                ? Results.Json(new { ok = true, result.Message, result.Id, result.Code })
                : ApiResults.Error(result.Message, StatusCodes.Status400BadRequest);
        });

        group.MapPatch("/{id:long}", async (long id, [FromBody] Dictionary<string, string>? fields,
            HttpContext context, IUserAccessor users, OperationalIssueService issues,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!Writes(user))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขปัญหา", StatusCodes.Status403Forbidden);
            if (fields is null || fields.Count == 0)
                return ApiResults.Error("ไม่มีข้อมูลให้แก้ไข", StatusCodes.Status400BadRequest);

            var result = await issues.UpdateAsync(id, fields, user.Signature, token);
            return result.Ok
                ? Results.Json(new { ok = true, result.Message })
                : ApiResults.Error(result.Message, StatusCodes.Status404NotFound);
        });

        // The team's existing log, brought in whole. Codes already in the table
        // are skipped rather than replaced, so importing the same sheet twice
        // adds what is new and leaves worked-on rows alone.
        group.MapPost("/import", async ([FromBody] ImportBody? body, HttpContext context,
            IUserAccessor users, OperationalIssueService issues, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (!Writes(user))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์นำเข้าปัญหา", StatusCodes.Status403Forbidden);

            var incoming = body?.Issues ?? [];
            if (incoming.Count == 0) return Results.Json(new { added = 0, skipped = 0 });
            if (incoming.Count > 2000)
                return ApiResults.Error("นำเข้าได้ไม่เกิน 2,000 รายการต่อครั้ง",
                    StatusCodes.Status413PayloadTooLarge);

            var (added, skipped) = await issues.ImportAsync(
                incoming.Select(row => Build(row, user)).ToList(), user.Signature, token);
            return Results.Json(new { added, skipped });
        });
    }

    private static bool Writes(AppUser user) =>
        user.Can(Capability.EditOwnJobs) || user.Can(Capability.EditAnyJob);

    /// <summary>
    /// The entity a request describes.
    ///
    /// An issue with no owner named takes the person recording it, because an
    /// issue nobody holds is the one that sits in the log until somebody reads
    /// it in a meeting.
    /// </summary>
    private static OperationalIssue Build(IssueBody body, AppUser user) => new()
    {
        Code = Clean(body.Code, 20),
        FoundOn = Clean(body.FoundOn, 20),
        FoundAt = Clean(body.FoundAt, 10),
        Source = Clean(body.Source, 60),
        Reporter = Clean(body.Reporter, 160),
        JobRef = Clean(body.JobRef, 200),
        JobKey = Clean(body.JobKey, 80),
        Detail = (body.Detail ?? "").Trim(),
        Category = Clean(body.Category, 80),
        Severity = Clean(body.Severity, 20),
        Impact = (body.Impact ?? "").Trim(),
        Channel = Clean(body.Channel, 80),
        Owner = Clean(body.Owner, 160).Length > 0 ? Clean(body.Owner, 160) : user.DisplayName,
        OwnerId = Clean(body.OwnerId, 20).Length > 0 ? Clean(body.OwnerId, 20) : user.OperatorId,
        DueOn = Clean(body.DueOn, 20),
        Status = Clean(body.Status, 30),
        RootCause = Clean(body.RootCause, 120),
        Driver = Clean(body.Driver, 160),
        ContainerNo = Clean(body.ContainerNo, 80),
        Licence = Clean(body.Licence, 60),
    };

    private static string Clean(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
