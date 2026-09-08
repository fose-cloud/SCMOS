using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai;

public sealed record OperationsControlState(bool Available, bool Enabled, int Revision, bool EmergencyDisabled);
public sealed record OperationsControlView(bool Available, bool Enabled, int Revision,
    bool CanManage, bool CanEnable, bool EmergencyDisabled, string BlockReason);

public interface IOperationsControl
{
    Task<OperationsControlState> ReadAsync(CancellationToken token);
}

public sealed class OperationsControlService(ScmosDbContext db, AuditService audit,
    IOptions<AiOptions> options, ILogger<OperationsControlService> log) : IOperationsControl
{
    public static bool CanManage(AppUser? user) => AiPermissionPolicy.Authenticated(user)
        && AiPermissionPolicy.InternalUser(user!) && user!.Role == Roles.Admin;

    public async Task<OperationsControlState> ReadAsync(CancellationToken token)
    {
        try
        {
            // No process cache: every API instance observes the same durable state.
            var row = await db.AiOperationsControls.AsNoTracking().SingleOrDefaultAsync(row => row.Id == 1, token);
            return new(row is not null, row?.Enabled == true, row?.Revision ?? 0, options.Value.OperationsEmergencyDisabled);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            log.LogWarning("Operations AI control unavailable; refusing new runs");
            return new(false, false, 0, options.Value.OperationsEmergencyDisabled);
        }
    }

    public static bool Effective(OperationsControlState state) => state.Available && state.Enabled && !state.EmergencyDisabled;

    public async Task<string> SetAsync(AppUser user, bool enabled, int revision, CancellationToken token)
    {
        if (!CanManage(user)) return "forbidden";
        if (enabled && options.Value.OperationsEmergencyDisabled) return "emergency_disabled";
        try
        {
            var row = await db.AiOperationsControls.SingleOrDefaultAsync(row => row.Id == 1, token);
            if (row is null) return "control_unavailable";
            if (row.Revision != revision) return "control_conflict";
            if (row.Enabled == enabled) return "ok";
            var previous = row.Enabled;
            row.Enabled = enabled;
            row.Revision = checked(row.Revision + 1);
            audit.Stage(user, "status", "ai-control", "operations-agent", "Operations AI", "enabled",
                previous ? "true" : "false", enabled ? "true" : "false", "Administrator confirmed read-only Operations AI switch");
            // EF's single SaveChanges transaction commits both state and audit or neither.
            await db.SaveChangesAsync(token);
            return "ok";
        }
        catch (DbUpdateConcurrencyException) { return "control_conflict"; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            log.LogWarning("Operations AI switch was not saved");
            return "control_unavailable";
        }
    }
}
