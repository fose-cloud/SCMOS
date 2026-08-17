using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record AuditView(
    long Id, DateTimeOffset At, string Who, string WhoId, string Role,
    string Action, string Entity, string EntityId, string EntityLabel,
    string Field, string OldValue, string NewValue, string Reason,
    string IpAddress, string SessionId, string Source);

public record AuditPage(IReadOnlyList<AuditView> Entries, int Total);

/// <summary>
/// The only thing that writes the audit trail.
///
/// Callers hand it what changed; it works out who, when and from where. That
/// split is deliberate: an endpoint that had to remember to capture the caller's
/// address would eventually forget, and the row it wrote would look complete.
///
/// Recording never fails a change. If the trail cannot be written the change
/// still happens and the failure is logged loudly — losing an audit row is bad,
/// but silently refusing an operator's edit because a log table is full is
/// worse, and the loud log is what gets it fixed.
/// </summary>
public class AuditService(ScmosDbContext db, IHttpContextAccessor context, ILogger<AuditService> log)
{
    /// <summary>Records one change. Returns false when it could not be written.</summary>
    public async Task<bool> RecordAsync(AppUser user, string action, string entity, string entityId,
        string entityLabel, string field, string oldValue, string newValue, string reason,
        CancellationToken token, string source = "web")
    {
        try
        {
            db.AuditEvents.Add(Build(user, action, entity, entityId, entityLabel,
                field, oldValue, newValue, reason, source));
            await db.SaveChangesAsync(token);
            return true;
        }
        catch (Exception error)
        {
            log.LogError(error, "Audit row lost: {Action} {Entity} {EntityId} by {Who}",
                action, entity, entityId, user.Signature);
            return false;
        }
    }

    /// <summary>
    /// Records a batch in one save — a bulk status change is one action a person
    /// took, and writing it row by row would take a second per hundred jobs.
    /// </summary>
    public async Task<bool> RecordManyAsync(AppUser user, IEnumerable<(string Action, string Entity,
        string EntityId, string EntityLabel, string Field, string OldValue, string NewValue)> changes,
        string reason, CancellationToken token, string source = "web")
    {
        var rows = changes.Select(change => Build(user, change.Action, change.Entity, change.EntityId,
            change.EntityLabel, change.Field, change.OldValue, change.NewValue, reason, source)).ToList();
        if (rows.Count == 0) return true;

        try
        {
            db.AuditEvents.AddRange(rows);
            await db.SaveChangesAsync(token);
            return true;
        }
        catch (Exception error)
        {
            log.LogError(error, "Audit batch lost: {Count} rows by {Who}", rows.Count, user.Signature);
            return false;
        }
    }

    private AuditEvent Build(AppUser user, string action, string entity, string entityId,
        string entityLabel, string field, string oldValue, string newValue, string reason, string source)
    {
        var request = context.HttpContext?.Request;
        return new AuditEvent
        {
            At = DateTimeOffset.UtcNow,
            Who = user.Signature,
            WhoId = user.OperatorId,
            Role = user.Role,
            Action = action,
            Entity = entity,
            EntityId = Trim(entityId, 120),
            EntityLabel = Trim(entityLabel, 200),
            Field = Trim(field, 60),
            // Values are truncated rather than refused. A 4,000-character remark
            // is still worth an audit row saying it changed.
            OldValue = Trim(oldValue, 400),
            NewValue = Trim(newValue, 400),
            Reason = Trim(reason, 400),
            IpAddress = Address(),
            SessionId = Session(request),
            Source = source,
        };
    }

    /// <summary>
    /// The caller's address.
    ///
    /// Behind App Service the connection comes from the platform, so the real
    /// client is in X-Forwarded-For — first entry, since the rest are proxies.
    /// Recording the load balancer's address in every row would make the field
    /// worthless.
    /// </summary>
    private string Address()
    {
        var request = context.HttpContext?.Request;
        var forwarded = request?.Headers["X-Forwarded-For"].ToString() ?? "";
        if (forwarded.Length > 0)
        {
            var first = forwarded.Split(',')[0].Trim();
            // App Service appends :port to the forwarded address.
            var colon = first.LastIndexOf(':');
            if (colon > 0 && first.Count(c => c == ':') == 1) first = first[..colon];
            if (first.Length > 0) return Trim(first, 60);
        }
        return Trim(context.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "", 60);
    }

    private static string Session(HttpRequest? request)
    {
        var platform = request?.Headers["x-ms-client-principal-id"].ToString() ?? "";
        if (platform.Length > 0) return Trim(platform, 120);
        // No platform session locally; the request id still ties the rows written
        // by one call together, which is what the field is for.
        return Trim(request?.HttpContext.TraceIdentifier ?? "", 120);
    }

    private static string Trim(string? value, int max)
    {
        var text = (value ?? "").Trim();
        return text.Length <= max ? text : text[..max];
    }

    /* ----------------------------------------------------------- reading */

    public async Task<AuditPage> ReadAsync(string? entity, string? entityId, string? who,
        string? action, int skip, int take, CancellationToken token)
    {
        var query = db.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entity) && entity != "All") query = query.Where(e => e.Entity == entity);
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(e => e.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(who)) query = query.Where(e => e.Who.Contains(who) || e.WhoId == who);
        if (!string.IsNullOrWhiteSpace(action) && action != "All") query = query.Where(e => e.Action == action);

        var total = await query.CountAsync(token);
        var rows = await query.OrderByDescending(e => e.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(take, 1, 500))
            .Select(e => new AuditView(e.Id, e.At, e.Who, e.WhoId, e.Role, e.Action, e.Entity,
                e.EntityId, e.EntityLabel, e.Field, e.OldValue, e.NewValue, e.Reason,
                e.IpAddress, e.SessionId, e.Source))
            .ToListAsync(token);

        return new AuditPage(rows, total);
    }
}
