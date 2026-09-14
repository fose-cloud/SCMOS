using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Operations;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record OperationsChangeRequest(string Key, string Version,
    Dictionary<string, string> Changes, string Reason);
public sealed record OperationsChangePayload(int Version, string Key, string Fingerprint,
    Dictionary<string, string> Before, Dictionary<string, string> Changes, string Reason,
    string RequesterId, string RequesterOperatorId, DateTimeOffset ExpiresAt);

/// <summary>Reviewed edits only. No whole-job replacement, raw SQL or permission changes.</summary>
public static class OperationsChangePolicy
{
    public const string Agent = "operations-write-v1";
    public static bool CanRequest(AppUser? user) => user is not null && AiPermissionPolicy.InternalUser(user)
        && user.Can(Capability.EditOwnJobs);
    public static bool CanApprove(AppUser? user) => CanRequest(user)
        && user!.Can(Capability.ApproveAi | Capability.EditAnyJob | Capability.AssignJobs);
    public static bool Owns(AppUser user, OperationJob job) => user.Can(Capability.EditAnyJob)
        || (!string.IsNullOrWhiteSpace(user.OperatorId) && user.OperatorId == job.OwnerId);
    public static bool Assignable(StaffMember? person) => person is { Active: true }
        && Roles.Find(person.Role) is { } role && role.Name != Roles.Subcontractor
        && Roles.Can(person.Role, Capability.EditOwnJobs);
    public static string Fingerprint(OperationJob job) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { job.Key, job.Cat, job.OwnerId, job.Owner,
            job.WorkDate, job.Status, job.Data, job.UpdatedAt }))));

    public static Dictionary<string, string> Values(OperationJob job)
    {
        var data = JsonNode.Parse(job.Data) as JsonObject ?? throw new JsonException();
        return new() { ["date"] = job.WorkDate, ["planTime"] = data["planTime"]?.GetValue<string>() ?? "",
            ["status"] = job.Status, ["opId"] = job.OwnerId };
    }
    public static string Validate(OperationsChangeRequest? request, OperationJob job, StaffMember? assignee)
    {
        if (request is null || request.Key != job.Key || request.Changes is null
            || request.Changes.Count is < 1 or > 4 || string.IsNullOrWhiteSpace(request.Reason)
            || request.Reason.Length > 400 || request.Version != Fingerprint(job)) return "invalid_or_stale";
        if (!WorkspaceTabs.CountedInWorkspace(job.Cat) || JobRules.IsDone(job.Status)
            || job.Status.Equals(JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)) return "closed_or_unsupported";
        var before = Values(job);
        foreach (var (field, value) in request.Changes)
        {
            if (value is null || !before.ContainsKey(field)) return "invalid_field";
            if (field == "date" && (!DateOnly.TryParseExact(value, "dd/MM/yyyy", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var day) || day.Year is < 1900 or > 2100)) return "invalid_date";
            if (field == "planTime" && !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)) return "invalid_time";
            if (field == "status" && (!JobStatus.IsValid(job.Cat, value) || value != value.ToUpperInvariant())) return "invalid_status";
            // Closure and cancellation continue through the existing reviewed workflow.
            if (field == "status" && value is JobStatus.Completed or JobStatus.Cancelled or JobStatus.BillingPending)
                return "workflow_required";
            if (field == "opId" && (!Assignable(assignee) || assignee!.Id != value)) return "invalid_assignee";
        }
        return request.Changes.All(p => before[p.Key] == p.Value) ? "no_change" : "ok";
    }

    /// <summary>Stage only. Caller commits the row, approval and audit atomically.</summary>
    public static void Stage(OperationJob job, IReadOnlyDictionary<string, string> changes,
        StaffMember? assignee, string by, DateTimeOffset now)
    {
        var data = JsonNode.Parse(job.Data) as JsonObject ?? throw new JsonException();
        if (changes.TryGetValue("date", out var date) && date != job.WorkDate)
        {
            if (string.IsNullOrWhiteSpace(data["origDate"]?.GetValue<string>())) data["origDate"] = job.WorkDate;
            job.WorkDate = date;
        }
        if (changes.TryGetValue("status", out var status)) job.Status = status;
        if (changes.ContainsKey("opId")) { job.OwnerId = assignee!.Id; job.Owner = assignee.Name; }
        foreach (var (key, value) in changes) data[key] = value;
        data["key"] = job.Key;
        data["op"] = job.Owner;
        data["opId"] = job.OwnerId;
        data["date"] = job.WorkDate;
        data["status"] = job.Status;
        job.Data = data.ToJsonString();
        job.UpdatedBy = by;
        job.UpdatedAt = now;
    }
}
