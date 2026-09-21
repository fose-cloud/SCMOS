using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scmos.Api.Data;

namespace Scmos.Api.Ai;

/// <summary>Strict durable writer, separate from SCMOS's best-effort business audit.</summary>
public sealed class SqlAiExecutionAudit(DbContextOptions<ScmosDbContext> options, IOptions<AiOptions>? ai = null) : IAiExecutionAudit
{
    public bool Ready { get; private set; }

    /// <summary>
    /// How long one audit write, or the readiness probe, may take.
    ///
    /// Five seconds flat until 21 September 2026, when the production database,
    /// busy with a whole-register read in working hours, could not commit a
    /// run's first event inside it: from 14:37 every agent answered
    /// audit_not_ready with no row written — the Data Agent that had answered
    /// at noon among them. Half the run's own budget, never under five seconds
    /// nor over thirty: a write still fails rather than waits for ever, and the
    /// run's token bounds it besides, but it waits as long as the run it is
    /// recording would. A sink built without options keeps the five.
    /// </summary>
    public static int WriteSeconds(AiOptions? options) => options is null ? 5 : Math.Clamp(options.TimeoutSeconds / 2, 5, 30);

    private int Budget => WriteSeconds(ai?.Value);

    internal ScmosDbContext Open()
    {
        // Never share tracked entities/transactions with a business repository, or log parameter values.
        var db = new ScmosDbContext(new DbContextOptionsBuilder<ScmosDbContext>(options)
            .UseLoggerFactory(NullLoggerFactory.Instance).EnableSensitiveDataLogging(false).Options);
        db.Database.SetCommandTimeout(Budget);
        return db;
    }

    public async Task<bool> CheckReadyAsync(CancellationToken token)
    {
        if (Ready) return true; // Only scoped to this request; every write still has to commit.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(Budget));
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
        timeout.CancelAfter(TimeSpan.FromSeconds(Budget));
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
