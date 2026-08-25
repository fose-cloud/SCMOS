using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class JobsEndpoints
{
    public record SaveRequest(List<JsonElement>? Jobs, string? By, string? Reason);
    /// <summary>
    /// What to remove. Exactly one of these decides it: <c>Keys</c> for named
    /// jobs, <c>All</c> for the whole register, <c>OwnerId</c> for everything
    /// one person holds — narrowed to a single month by <c>Month</c>, which is
    /// written the way work_date is, MM/yyyy.
    /// </summary>
    public record DeleteRequest(List<string>? Keys, bool? All, string? By, string? Reason,
        string? OwnerId, string? Month);

    /// <summary>
    /// Above this, a save is an import or a seed rather than somebody editing.
    /// Two thousand audit rows for one button press would bury the trail that
    /// matters, so a batch this size is recorded as the one action it was.
    /// </summary>
    private const int EditBatchLimit = 50;

    public static void MapJobs(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/jobs").WithTags("Jobs");

        group.MapGet("", async (HttpContext context, IUserAccessor users, JobsRepository jobs, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // A carrier does not get the register. This endpoint answers with
            // every job for every customer, including what each competitor was
            // assigned and at what rate; a subcontractor account has its own
            // scoped view at /api/carrier and no business here.
            if (string.Equals(user.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase))
                return ApiResults.Error("บัญชีผู้รับเหมาดูงานได้ที่หน้างานของบริษัทตัวเอง",
                    StatusCodes.Status403Forbidden);

            var (json, _) = await jobs.LoadAsync(token);
            // Written verbatim: the rows are already JSON and were checked on the way out.
            return Results.Text(json, "application/json");
        });

        // Cancelled or moved, straight from SQL. See JobsRepository.ChangedAsync
        // for why this does not go through the workspace's paging endpoint.
        group.MapGet("/changed", async (HttpContext context, IUserAccessor users,
            JobsRepository jobs, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (string.Equals(user.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase))
                return ApiResults.Error("บัญชีผู้รับเหมาดูงานได้ที่หน้างานของบริษัทตัวเอง",
                    StatusCodes.Status403Forbidden);

            var (json, _) = await jobs.ChangedAsync(token);
            return Results.Text(json, "application/json");
        });

        // One page of the register, filtered and counted here. See
        // WorkspaceService for why this exists alongside the full read.
        group.MapGet("/page", async (string? tab, string? cat, string? year, string? month,
            string? day, string? from, string? to,
            string? q, string? sort, string? dir, int? page, int? per,
            string? assignee, string? customer, string? trucker, string? type, string? status,
            string? kpi,
            HttpContext context, IUserAccessor users, WorkspaceService workspace,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;
            if (string.Equals(user.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase))
                return ApiResults.Error("บัญชีผู้รับเหมาดูงานได้ที่หน้างานของบริษัทตัวเอง",
                    StatusCodes.Status403Forbidden);

            var result = await workspace.ReadAsync(new WorkspaceService.Query(
                Tab: tab ?? WorkspaceTabs.MyJobs,
                Cat: cat ?? "ALL",
                Year: year ?? "ALL",
                Month: month ?? "ALL",
                Day: day ?? "ALL",
                From: from ?? "",
                To: to ?? "",
                Search: q ?? "",
                SortKey: sort ?? "",
                SortDir: dir ?? "asc",
                Page: page ?? 1,
                Per: per ?? 50,
                // Whose jobs count as "mine" is the API's answer, not a value the
                // caller may pass — otherwise any signed-in person could ask for
                // somebody else's workspace by naming their id.
                OpId: user.OperatorId,
                Assignee: assignee ?? "ALL",
                Owner: "",
                Customer: customer ?? "ALL",
                Trucker: trucker ?? "ALL",
                Type: type ?? "ALL",
                Status: status ?? "ALL",
                Kpi: kpi ?? "ALL"), token);

            return Results.Json(new
            {
                jobs = result.Rows,
                total = result.Total,
                pageCount = result.PageCount,
                page = result.CurrentPage,
                counts = result.Counts,
                dates = result.Dates,
                updatedAt = result.UpdatedAt,
            });
        });

        group.MapPut("", async (SaveRequest body, HttpContext context, IUserAccessor users,
            DelegationService delegations,
            JobsRepository jobs, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            // Signing in was the only thing this route ever checked, which meant
            // a Viewer, a Management account or anybody the directory has never
            // heard of could write to the register — the browser hid the
            // controls and that was the whole of the protection. Ownership is
            // enforced per job below; this is the gate for writing at all.
            if (!user.Can(Capability.EditOwnJobs) && !user.Can(Capability.EditAnyJob))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์แก้ไขข้อมูลงาน", StatusCodes.Status403Forbidden);

            var incoming = body.Jobs ?? [];
            if (incoming.Count == 0) return Results.Json(new { saved = 0 });
            if (incoming.Count > JobsRepository.MaxSaveBatch)
                return ApiResults.Error($"Too many jobs in one save (max {JobsRepository.MaxSaveBatch})", StatusCodes.Status413PayloadTooLarge);

            var keys = incoming.Select(job => Key(job)).Where(key => key.Length > 0).ToList();

            // Ownership, enforced here rather than only in the grid that draws
            // the rows. "An Operation User edits their own jobs" was a rule the
            // browser kept and the API took on trust, which made it a rule about
            // what the screen offers rather than about what can happen.
            if (!user.Can(Capability.EditAnyJob))
            {
                // Jobs belonging to somebody who asked this person to cover for
                // them are not "somebody else's" for as long as the grant runs.
                var acting = await delegations.ActingForAsync(user.OperatorId, token);
                var others = await jobs.OthersJobsAsync(keys, user.OperatorId, token, acting);
                if (others.Count > 0)
                    return ApiResults.Error(
                        $"แก้ไม่ได้ — {others.Count} งานในชุดนี้เป็นของผู้อื่น",
                        StatusCodes.Status403Forbidden);
            }

            // Read what the register says now, before the write replaces it.
            // Without this the trail could say what a field became but never what
            // it was, which is the half that makes an entry arguable.
            var before = incoming.Count <= EditBatchLimit
                ? await jobs.SnapshotAsync(keys, token)
                : [];

            var (saved, at) = await jobs.SaveAsync(incoming, user.Signature, token);

            var reason = (body.Reason ?? "").Trim();
            if (incoming.Count <= EditBatchLimit)
            {
                await audit.RecordManyAsync(user, Changes(incoming, before), reason, token);
            }
            else
            {
                await audit.RecordAsync(user, AuditActions.BulkReplace, "register", "", "ทะเบียนงาน",
                    "", "", $"{saved} งาน", reason.Length > 0 ? reason : "นำเข้าหรือโหลดแผนใหม่",
                    token, source: "import");
            }

            return Results.Json(new { saved, updatedAt = at.ToUniversalTime().ToString("O") });
        });

        // DELETE never infers a body, so it has to be asked for by name. The
        // workspace sends one: either the keys to remove or `all` to wipe.
        group.MapDelete("", async ([FromBody] DeleteRequest? body, HttpContext context, IUserAccessor users,
            JobsRepository jobs, DelegationService delegations, AuditService audit,
            CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            if (body?.All == true)
            {
                // Wiping the register is how a corrected plan file gets in. Only a
                // supervisor may do it — the workspace hides the button from
                // everyone else, and this is the half that cannot be clicked past.
                if (!user.Can(Capability.AdministerData) && !user.IsSupervisor)
                    return ApiResults.Error("Only a supervisor may clear the register", StatusCodes.Status403Forbidden);

                var (_, count) = await jobs.LoadAsync(token);
                await jobs.ClearAsync(token);
                await audit.RecordAsync(user, AuditActions.BulkReplace, "register", "", "ทะเบียนงาน",
                    "", $"{count} งาน", "0", (body.Reason ?? "").Trim(), token);
                return Results.Json(new { cleared = true });
            }

            var owner = (body?.OwnerId ?? "").Trim();
            if (owner.Length > 0)
            {
                // The same gate as clearing the whole register, for the same
                // reason: there is no history table behind the register, so a
                // person's month is gone the moment this returns. The screen
                // makes the administrator type the name back; this is the half
                // that cannot be clicked past.
                if (!user.Can(Capability.AdministerData) && !user.IsSupervisor)
                    return ApiResults.Error("Only a supervisor may clear another account's jobs",
                        StatusCodes.Status403Forbidden);

                var month = (body?.Month ?? "").Trim();
                // Refused here so the caller is told what is wrong. The
                // repository refuses it too — that one is the backstop, and it
                // would surface as a 500, which tells nobody anything.
                if (month.Length > 0 && !Regex.IsMatch(month, @"^(0[1-9]|1[0-2])/[0-9]{4}$"))
                    return ApiResults.Error("เดือนต้องอยู่ในรูปแบบ MM/yyyy", StatusCodes.Status400BadRequest);

                var removed = await jobs.ClearOwnerAsync(owner, month, token);
                await audit.RecordAsync(user, AuditActions.BulkReplace, "register", owner,
                    month.Length > 0 ? $"งานของ {owner} เดือน {month}" : $"งานของ {owner}",
                    "", $"{removed} งาน", "0", (body?.Reason ?? "").Trim(), token);
                return Results.Json(new { cleared = true, removed });
            }

            var wanted = body?.Keys ?? [];

            // Deleting had no check at all beyond being signed in: any account
            // could remove any job by sending its key. The grid refused to offer
            // the button, which is not the same thing.
            if (!user.Can(Capability.EditOwnJobs) && !user.Can(Capability.EditAnyJob))
                return ApiResults.Error("บัญชีนี้ไม่มีสิทธิ์ลบข้อมูลงาน", StatusCodes.Status403Forbidden);

            if (!user.Can(Capability.EditAnyJob))
            {
                var acting = await delegations.ActingForAsync(user.OperatorId, token);
                var others = await jobs.OthersJobsAsync(wanted, user.OperatorId, token, acting);
                if (others.Count > 0)
                    return ApiResults.Error(
                        $"ลบไม่ได้ — {others.Count} งานในชุดนี้เป็นของผู้อื่น",
                        StatusCodes.Status403Forbidden);
            }

            // Snapshotted first so the trail can say which job was removed rather
            // than only its key — a deleted row cannot be looked up afterwards.
            var labels = wanted.Count <= EditBatchLimit
                ? await jobs.SnapshotAsync(wanted, token)
                : [];

            var deleted = await jobs.DeleteAsync(wanted, token);

            if (wanted.Count <= EditBatchLimit)
            {
                await audit.RecordManyAsync(user, wanted.Select(key => (
                    Action: "delete",
                    Entity: "job",
                    EntityId: key,
                    EntityLabel: labels.TryGetValue(key, out var was)
                        ? $"{was.GetValueOrDefault("date")} · {was.GetValueOrDefault("trucker")}" : "",
                    Field: "",
                    OldValue: labels.TryGetValue(key, out var old) ? old.GetValueOrDefault("status", "") : "",
                    NewValue: "ลบแล้ว")), (body?.Reason ?? "").Trim(), token);
            }

            return Results.Json(new { deleted });
        });
    }

    /// <summary>
    /// The significant differences between what arrived and what was there.
    ///
    /// Only the fields <see cref="AuditActions"/> calls significant: a job has
    /// forty-odd, and a trail that records every keystroke is one nobody reads.
    /// A job with no previous row is a new job, and its creation is recorded
    /// once rather than as forty changes from nothing.
    /// </summary>
    private static IEnumerable<(string, string, string, string, string, string, string)> Changes(
        IReadOnlyList<JsonElement> incoming, Dictionary<string, Dictionary<string, string>> before)
    {
        foreach (var job in incoming)
        {
            var key = Key(job);
            if (key.Length == 0) continue;
            var label = Label(job);

            if (!before.TryGetValue(key, out var was))
            {
                yield return ("create", "job", key, label, "", "", Text(job, "status"));
                continue;
            }

            foreach (var (field, meaning) in Fields())
            {
                var now = Text(job, field);
                var then = was.GetValueOrDefault(field, "");
                if (now == then) continue;
                yield return (meaning.Action, "job", key, label, meaning.Label, then, now);
            }
        }
    }

    private static IEnumerable<(string Field, (string Action, string Label) Meaning)> Fields()
    {
        foreach (var field in new[] { "trucker", "status", "op", "date", "container", "licence", "driver", "planTime" })
        {
            var meaning = AuditActions.For(field);
            if (meaning is not null) yield return (field, meaning.Value);
        }
    }

    private static string Key(JsonElement job) => Text(job, "key");

    private static string Label(JsonElement job)
    {
        var code = Text(job, "jobCode");
        if (code.Length == 0) code = Text(job, "abs");
        if (code.Length == 0) code = Text(job, "jobNo");
        var customer = Text(job, "customer");
        return code.Length > 0 && customer.Length > 0 ? $"{code} · {customer}"
            : code.Length > 0 ? code : customer;
    }

    private static string Text(JsonElement job, string name) =>
        job.ValueKind == JsonValueKind.Object && job.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Number => value.ToString(),
                _ => "",
            }
            : "";
}
