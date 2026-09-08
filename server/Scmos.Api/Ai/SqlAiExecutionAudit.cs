using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Data;

namespace Scmos.Api.Ai;

/// <summary>Strict durable writer, separate from SCMOS's best-effort business audit.</summary>
public sealed class SqlAiExecutionAudit(DbContextOptions<ScmosDbContext> options) : IAiExecutionAudit
{
    public bool Ready { get; private set; }

    internal ScmosDbContext Open()
    {
        // Never share tracked entities/transactions with a business repository, or log parameter values.
        var db = new ScmosDbContext(new DbContextOptionsBuilder<ScmosDbContext>(options)
            .UseLoggerFactory(NullLoggerFactory.Instance).EnableSensitiveDataLogging(false).Options);
        db.Database.SetCommandTimeout(5);
        return db;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken token)
    {
        if (Ready) return true; // Only scoped to this request; every write still has to commit.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await using var db = Open();
            // Select the actual mapped shape, not CanConnect: a missing migration must refuse readiness.
            await db.AiAuditLogs.AsNoTracking().Take(1).ToListAsync(timeout.Token);
            return Ready = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { return Ready = false; }
    }

    public async Task RecordAsync(AiExecutionEvent entry, CancellationToken token)
    {
        var row = AiAuditRules.From(entry);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await using var strategyContext = Open();
            await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async ct =>
            {
                // A fresh context on each retry handles ambiguous commits via the unique event/fingerprint.
                await using var db = Open();
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var prior = await db.AiAuditLogs.AsNoTracking().Where(e => e.RunId == row.RunId)
                    .OrderBy(e => e.Sequence).ToListAsync(ct);
                if (AiAuditRules.MayAppend(prior, row))
                {
                    row.Id = 0;
                    db.AiAuditLogs.Add(row);
                    await db.SaveChangesAsync(ct);
                }
                await transaction.CommitAsync(ct);
            }, timeout.Token);
            Ready = true;
        }
        catch { Ready = false; throw; }
    }
}
