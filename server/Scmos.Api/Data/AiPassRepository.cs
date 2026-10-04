using Microsoft.EntityFrameworkCore;
using Scmos.Api.Ai;
using Scmos.Api.Ai.Carrier;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

public sealed record PassCarrierAttempt(string JobKey, string Carrier, string Outcome, int Rank);
public sealed record PassCarrierChoice(string JobKey, string Carrier, string By, DateTimeOffset At, long Id);
public sealed record PassMail(long Id, string Subject, string FromAddress, string FromName, string BodyText, DateTimeOffset ReceivedAt);

/// <summary>
/// Finite, policy-checked data operations for existing scheduled passes. Never exposes
/// DbContext, IQueryable, SQL, connections or credentials to an agent. Business
/// assessment/draft rules remain in their existing classes.
/// </summary>
public sealed class AiPassRepository(ScmosDbContext db, IAiPolicyGateway gateway)
{
    private async Task Guard(string agent, CancellationToken token)
    {
        var (action, tool) = AiPolicyEntry.PassContract(agent);
        var decision = await AiPolicyEntry.AuthorizeAsync(gateway, AiAuthorizationRequest.Pass(agent, action, tool), token);
        if (!decision.Allowed) throw new UnauthorizedAccessException("AI pass denied: " + decision.ReasonCode);
    }

    public async Task<IReadOnlyList<PassCarrierAttempt>> CarrierAttemptsAsync(string[] keys, CancellationToken token)
    {
        await Guard(AgentIds.Carrier, token);
        if (keys.Length > 500) throw new ArgumentException("Pass batch exceeds 500 keys.");
        return await db.SupplierRequests.AsNoTracking().Where(x => keys.Contains(x.JobKey)).OrderBy(x => x.Rank).ThenBy(x => x.Id)
            .Select(x => new PassCarrierAttempt(x.JobKey, x.Carrier, x.Outcome, x.Rank)).ToListAsync(token);
    }

    public async Task<IReadOnlyDictionary<string, PendingRequest>> PendingRequestsAsync(CancellationToken token)
    {
        await Guard(AgentIds.Communication, token);
        var rows = await db.SupplierRequests.AsNoTracking().Where(x => x.Outcome == CarrierAssignment.Pending)
            .Select(x => new { x.Id, x.JobKey, x.Carrier, x.RequestedAt }).ToListAsync(token);
        var result = new Dictionary<string, PendingRequest>(StringComparer.Ordinal);
        foreach (var row in rows) result[row.JobKey] = new(row.Id, row.Carrier, row.RequestedAt);
        return result;
    }

    public async Task<IReadOnlyList<string>> PodKeysAsync(string[] keys, CancellationToken token)
    {
        await Guard(AgentIds.Communication, token);
        if (keys.Length > 500) throw new ArgumentException("Pass batch exceeds 500 keys.");
        return await db.Documents.AsNoTracking().Where(x => x.Folder == "POD" && keys.Contains(x.JobKey))
            .Select(x => x.JobKey).Distinct().ToListAsync(token);
    }

    public async Task<IReadOnlyList<string>> ReviewMessageKeysAsync(CancellationToken token)
    {
        await Guard(AgentIds.Communication, token);
        return await db.LineEvents.AsNoTracking().Where(x => x.ProcessingStatus == LineProcessing.NeedReview && x.JobKey != "")
            .Select(x => x.JobKey).Distinct().ToListAsync(token);
    }

    public async Task<List<AiDecision>> OpenDecisionsAsync(string agent, CancellationToken token)
    {
        await Guard(agent, token);
        return await db.AiDecisions.Where(x => x.AgentId == agent && x.Status == AiDecisionLog.Open).ToListAsync(token);
    }

    public async Task<IReadOnlyDictionary<string, string>> AnsweredAsync(string agent, string[] keys, CancellationToken token)
    {
        await Guard(agent, token);
        if (keys.Length > 500) throw new ArgumentException("Pass batch exceeds 500 keys.");
        var rows = await db.AiDecisions.AsNoTracking().Where(x => x.AgentId == agent && keys.Contains(x.EntityId)
            && (x.Status == AiDecisionLog.Accepted || x.Status == AiDecisionLog.Overridden || x.Status == AiDecisionLog.Dismissed))
            .Select(x => new { x.Id, x.EntityId, x.Fingerprint }).ToListAsync(token);
        return rows.GroupBy(x => x.EntityId).Select(g => g.MaxBy(x => x.Id)!).ToDictionary(x => x.EntityId, x => x.Fingerprint);
    }

    public async Task<IReadOnlyList<PassCarrierChoice>> CarrierChoicesAsync(string[] keys, CancellationToken token)
    {
        await Guard(AgentIds.Carrier, token);
        if (keys.Length > 500) throw new ArgumentException("Pass batch exceeds 500 keys.");
        return await db.SupplierRequests.AsNoTracking().Where(x => keys.Contains(x.JobKey))
            .Select(x => new PassCarrierChoice(x.JobKey, x.Carrier, x.RequestedBy, x.RequestedAt, x.Id)).ToListAsync(token);
    }

    public async Task<int> ResolveLinkedMailAsync(DateTimeOffset now, CancellationToken token)
    {
        await Guard(AgentIds.Booking, token);
        var open = await db.AiDecisions.Where(x => x.AgentId == AgentIds.Booking && x.EntityType == "email" && x.Status == AiDecisionLog.Open).ToListAsync(token);
        if (open.Count == 0) return 0;
        var ids = open.Select(x => long.TryParse(x.EntityId, out var id) ? id : 0).Where(id => id > 0).ToList();
        var linked = await db.EmailJobLinks.AsNoTracking().Where(x => ids.Contains(x.EmailId) && x.Status == MailLink.Confirmed)
            .Select(x => new { x.EmailId, x.JobKey, x.ConfirmedBy }).ToListAsync(token);
        var resolved = 0;
        foreach (var row in open)
            if (linked.FirstOrDefault(x => x.EmailId.ToString() == row.EntityId) is { } link)
            {
                row.Status = AiDecisionLog.Resolved; row.DecidedAt = now; row.DecidedBy = "system";
                row.HumanChoice = link.JobKey.Length <= 400 ? link.JobKey : link.JobKey[..400];
                row.OverrideReason = "อีเมลนี้ถูกจับคู่กับงานแล้ว" + (link.ConfirmedBy.Length > 0 ? $" ({link.ConfirmedBy})" : "");
                resolved++;
            }
        if (resolved > 0 && !await SaveAsync(AgentIds.Booking, token)) throw new InvalidOperationException("Mail decision changed meanwhile.");
        return resolved;
    }

    public async Task<IReadOnlyList<PassMail>> UnplacedMailAsync(DateTimeOffset since, int limit, CancellationToken token)
    {
        await Guard(AgentIds.Booking, token);
        if (limit is < 1 or > 50) throw new ArgumentException("Invalid mail pass limit.");
        var candidates = await db.Emails.AsNoTracking().Where(x => x.ReceivedAt >= since
            && (x.ProcessingStatus == MailProcessing.Processed || x.ProcessingStatus == MailProcessing.NeedReview)
            && !db.EmailJobLinks.Any(link => link.EmailId == x.Id && (link.Status == MailLink.Confirmed || link.Status == MailLink.Suggested)))
            .OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id)
            .Select(x => new PassMail(x.Id, x.Subject, x.FromAddress, x.FromName, x.BodyText, x.ReceivedAt)).Take(200).ToListAsync(token);
        var ids = candidates.Select(x => x.Id.ToString()).ToList();
        var read = (await db.AiDecisions.AsNoTracking().Where(x => x.AgentId == AgentIds.Booking && x.EntityType == "email" && ids.Contains(x.EntityId))
            .Select(x => x.EntityId).ToListAsync(token)).ToHashSet(StringComparer.Ordinal);
        return candidates.Where(x => !read.Contains(x.Id.ToString())).Take(limit).ToList();
    }

    public async Task<bool> SaveAsync(string agent, CancellationToken token)
    {
        await Guard(agent, token); // Revoke/kill changes also win immediately before a pass writes its decision log.
        // This finite adapter writes AI decisions only, never shipment/rate/billing/audit state.
        if (db.ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && x.Entity is not AiDecision)) throw new InvalidOperationException("Pass cannot save other entities.");
        if (db.ChangeTracker.Entries<AiDecision>().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            && (x.Entity.AgentId != agent || x.State == EntityState.Deleted))) throw new InvalidOperationException("Pass decision scope mismatch.");
        try { await db.SaveChangesAsync(token); return true; }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }
    }
}
