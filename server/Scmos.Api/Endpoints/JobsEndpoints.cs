using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Endpoints;

public static class JobsEndpoints
{
    public record SaveRequest(List<JsonElement>? Jobs, string? By, string? Reason);
    public record DeleteRequest(List<string>? Keys, bool? All, string? By, string? Reason);

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
            if (users.Current(context) is null) return ApiResults.SignInRequired;
            var (json, _) = await jobs.LoadAsync(token);
            // Written verbatim: the rows are already JSON and were checked on the way out.
            return Results.Text(json, "application/json");
        });

        group.MapPut("", async (SaveRequest body, HttpContext context, IUserAccessor users,
            JobsRepository jobs, AuditService audit, CancellationToken token) =>
        {
            var user = users.Current(context);
            if (user is null) return ApiResults.SignInRequired;

            var incoming = body.Jobs ?? [];
            if (incoming.Count == 0) return Results.Json(new { saved = 0 });
            if (incoming.Count > JobsRepository.Limit)
                return ApiResults.Error($"Too many jobs in one save (max {JobsRepository.Limit})", StatusCodes.Status413PayloadTooLarge);

            // Read what the register says now, before the write replaces it.
            // Without this the trail could say what a field became but never what
            // it was, which is the half that makes an entry arguable.
            var keys = incoming.Select(job => Key(job)).Where(key => key.Length > 0).ToList();
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
            JobsRepository jobs, AuditService audit, CancellationToken token) =>
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

            var wanted = body?.Keys ?? [];
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
