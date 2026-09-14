using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Operations;

public sealed record OperationsChangeResult(string Code, long? Id = null);
public sealed record OperationsAssignee(string Id, string Name);
public sealed record OperationsChangePreview(string Key, string Version, Dictionary<string, string> Values,
    bool Enabled, bool CanApprove, IReadOnlyList<OperationsAssignee> Assignees, IReadOnlyList<string> Statuses);

/// <summary>
/// Reuses approvals and operation_jobs. A proposal never edits a job. Confirmation
/// commits the exact reviewed payload and audit in one serializable transaction.
/// No model call occurs here and legacy approval routes cannot execute this contract.
/// </summary>
public sealed class OperationsChangeService(ScmosDbContext db, AuditService audit,
    JobRegisterCache cache, TimeProvider clock, IOptions<AiOptions> options)
{
    public bool Configured => options.Value.OperationsWritesEnabled && !options.Value.OperationsEmergencyDisabled;
    private async Task<bool> Enabled(CancellationToken token) => Configured
        && await db.AiOperationsControls.AnyAsync(c => c.Id == 1 && c.Enabled, token);
    private async Task<bool> CurrentStaff(AppUser user, Capability capability, CancellationToken token)
    {
        var person = await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == user.OperatorId, token);
        return OperationsChangePolicy.Assignable(person) && Roles.Can(person!.Role, capability);
    }
    public async Task<OperationsChangePreview?> PreviewAsync(string key, AppUser user, CancellationToken token)
    {
        if (!OperationsChangePolicy.CanRequest(user) || key.Length is < 1 or > 80) return null;
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Key == key, token);
        if (job is null || !OperationsChangePolicy.Owns(user, job)) return null;
        var staff = await db.Staff.AsNoTracking().Where(s => s.Active).ToListAsync(token);
        return new(key, OperationsChangePolicy.Fingerprint(job), OperationsChangePolicy.Values(job),
            await Enabled(token), OperationsChangePolicy.CanApprove(user),
            staff.Where(OperationsChangePolicy.Assignable).Select(s => new OperationsAssignee(s.Id, s.Name)).ToArray(),
            JobStatus.For(job.Cat).Where(s => s is not (JobStatus.Completed or JobStatus.Cancelled or JobStatus.BillingPending)).ToArray());
    }
    public async Task<object> InterpretAsync(string? message, AppUser user, CancellationToken token)
    {
        if (!OperationsChangePolicy.CanRequest(user)) return new { code = "forbidden" };
        var command = OperationsChangeCommand.Parse(message);
        if (command is null) return new { code = "clarification_required",
            questions = OperationsChangeCommand.MissingDetails(message) };
        var preview = await PreviewAsync(command.Key, user, token);
        if (preview is null) return new { code = "forbidden" };
        // Read the same snapshot again before validation; a concurrent edit requires a fresh draft.
        var job = await db.OperationJobs.AsNoTracking().SingleOrDefaultAsync(j => j.Key == command.Key, token);
        if (job is null) return new { code = "invalid_or_stale" };
        var assignee = await Assignee(command.Changes, token);
        var code = OperationsChangePolicy.Validate(new(command.Key, preview.Version, command.Changes, command.Reason), job, assignee);
        if (code is "invalid_date" or "invalid_time" or "invalid_assignee" or "invalid_status")
            return new { code = "clarification_required", questions = new[] { code } };
        if (code != "ok") return new { code };
        if (command.Changes.ContainsKey("status") && await db.WorkflowEvents.AnyAsync(e => e.JobKey == job.Key, token))
            return new { code = "workflow_required" };
        // No SaveChanges, no proposal creation and no model invocation in this read-only step.
        return new { code = "draft", preview, changes = command.Changes, reason = command.Reason };
    }
    public async Task<IReadOnlyList<object>> ListAsync(AppUser user, CancellationToken token)
    {
        if (!OperationsChangePolicy.CanRequest(user)) return [];
        var query = db.Approvals.AsNoTracking().Where(a => a.Agent == OperationsChangePolicy.Agent);
        if (!OperationsChangePolicy.CanApprove(user)) query = query.Where(a => a.RequestedBy == user.Signature);
        var rows = await query.OrderByDescending(a => a.Id).Take(100).ToListAsync(token);
        var result = new List<object>();
        foreach (var row in rows)
        {
            var payload = ReadPayload(row);
            if (payload is null || (!OperationsChangePolicy.CanApprove(user) && payload.RequesterId != user.UserId)) continue;
            result.Add(new { row.Id, row.State, row.RequestedBy, row.RequestedAt, row.DecidedBy,
                row.DecidedAt, row.Result, payload, canApprove = OperationsChangePolicy.CanApprove(user) });
        }
        return result;
    }
    public Task<OperationsChangeResult> ProposeAsync(OperationsChangeRequest request, AppUser user, CancellationToken token)
        => Atomic(async () =>
        {
            if (!OperationsChangePolicy.CanRequest(user) || !await CurrentStaff(user, Capability.EditOwnJobs, token)) return new("forbidden");
            if (!await Enabled(token)) return new("write_disabled");
            if (request.Key is null || request.Key.Length is < 1 or > 80) return new("invalid_request");
            var job = await db.OperationJobs.SingleOrDefaultAsync(j => j.Key == request.Key, token);
            if (job is null || !OperationsChangePolicy.Owns(user, job)) return new("forbidden");
            var assignee = await Assignee(request.Changes, token);
            var code = OperationsChangePolicy.Validate(request, job, assignee);
            if (code != "ok") return new(code);
            if (request.Changes.ContainsKey("status") && await db.WorkflowEvents.AnyAsync(e => e.JobKey == job.Key, token))
                return new("workflow_required");
            var payload = new OperationsChangePayload(1, job.Key, request.Version,
                OperationsChangePolicy.Values(job), new(request.Changes), request.Reason,
                user.UserId, user.OperatorId, clock.GetUtcNow().AddMinutes(30));
            var row = new Approval { Agent = OperationsChangePolicy.Agent, Tool = "update_shipment",
                Summary = "Operations change: " + job.Key, Payload = JsonSerializer.Serialize(payload),
                State = "pending", RequestedBy = user.Signature, RequestedAt = clock.GetUtcNow() };
            db.Approvals.Add(row);
            audit.Stage(user, "propose", "job", job.Key, row.Summary, "", "", "pending", request.Reason, "ai");
            await db.SaveChangesAsync(token);
            return new("pending", row.Id);
        }, token);

    public Task<OperationsChangeResult> ConfirmAsync(long id, string fingerprint, bool approve, string note,
        AppUser user, CancellationToken token) => Atomic(async () =>
    {
        if (!OperationsChangePolicy.CanApprove(user)
            || !await CurrentStaff(user, Capability.ApproveAi | Capability.EditAnyJob | Capability.AssignJobs, token)) return new("forbidden");
        if (string.IsNullOrWhiteSpace(note) || note.Length > 400) return new("reason_required");
        var row = await db.Approvals.SingleOrDefaultAsync(a => a.Id == id && a.Agent == OperationsChangePolicy.Agent, token);
        var payload = row is null ? null : ReadPayload(row);
        if (row is null || payload is null || payload.Fingerprint != fingerprint) return new("invalid_proposal");
        // Same id has at most one effect, including a retry after a lost response.
        if (row.State == "applied") return new("already_applied", row.Id);
        if (row.State != "pending") return new("already_decided", row.Id);
        if (approve && !await Enabled(token)) return new("write_disabled");
        var state = approve ? "applied" : "rejected";
        if (approve && payload.ExpiresAt <= clock.GetUtcNow()) state = "expired";
        if (state == "applied")
        {
            var job = await db.OperationJobs.SingleOrDefaultAsync(j => j.Key == payload.Key, token);
            var requester = await db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == payload.RequesterOperatorId, token);
            if (!OperationsChangePolicy.Assignable(requester) || (job is not null
                && !Roles.Can(requester!.Role, Capability.EditAnyJob) && job.OwnerId != requester.Id)) return new("requester_forbidden");
            if (job is null || OperationsChangePolicy.Fingerprint(job) != payload.Fingerprint) state = "stale";
            else
            {
                var assignee = await Assignee(payload.Changes, token);
                var code = OperationsChangePolicy.Validate(new(payload.Key, payload.Fingerprint, payload.Changes, payload.Reason), job, assignee);
                if (code != "ok") return new(code);
                if (payload.Changes.ContainsKey("status") && await db.WorkflowEvents.AnyAsync(e => e.JobKey == job.Key, token)) return new("workflow_required");
                OperationsChangePolicy.Stage(job, payload.Changes, assignee, user.Signature, clock.GetUtcNow());
                foreach (var (field, value) in payload.Changes)
                    audit.Stage(user, "edit", "job", job.Key, job.JobCode, field, payload.Before[field], value,
                        $"Approval #{row.Id}: {note}", "ai");
            }
        }
        row.State = state;
        row.DecidedBy = user.Signature;
        row.DecidedAt = clock.GetUtcNow();
        row.DecisionNote = note;
        row.Result = state; // Computed here, never a client claim that a change ran.
        audit.Stage(user, state == "applied" ? "apply" : state, "approval", row.Id.ToString(), row.Summary,
            "state", "pending", state, note, "ai");
        await db.SaveChangesAsync(token);
        return new(state, row.Id);
    }, token);

    private static OperationsChangePayload? ReadPayload(Approval row)
    {
        try { return JsonSerializer.Deserialize<OperationsChangePayload>(row.Payload) is { Version: 1 } p ? p : null; }
        catch (JsonException) { return null; }
    }
    private Task<StaffMember?> Assignee(Dictionary<string, string>? changes, CancellationToken token)
        => changes is not null && changes.TryGetValue("opId", out var id)
            ? db.Staff.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id, token) : Task.FromResult<StaffMember?>(null);
    private async Task<OperationsChangeResult> Atomic(Func<Task<OperationsChangeResult>> action, CancellationToken token)
    {
        try
        {
            var result = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
                var outcome = await action();
                await transaction.CommitAsync(token);
                return outcome;
            });
            if (result.Code is "applied" or "already_applied") cache.Invalidate();
            return result;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { db.ChangeTracker.Clear(); return new("write_unavailable"); }
    }
}
