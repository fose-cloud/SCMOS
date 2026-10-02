using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

/// <summary>
/// The annual evaluation's writes that must land whole (2 Oct 2026, Phase 13). A snapshot or a calculation used to save
/// carrier by carrier, and a submitted sheet saved its response before its answers: a failure part-way left some carriers on
/// new evidence and the rest on old, or a link marked submitted with no answers that its evaluator could never send again.
/// </summary>
internal static class EvaluationWrites
{
    /// <summary>
    /// The work in one transaction under the context's execution strategy (the API retries transient SQL errors, and a
    /// user-opened transaction must sit inside that). A retry starts from a clean change tracker, so the work must read what
    /// it changes; on failure nothing it added is left tracked for the next save — the audit row's — to write by accident.
    /// </summary>
    public static async Task InOneAsync(ScmosDbContext db, Func<Task> work, CancellationToken token)
    {
        var attempt = 0;
        try
        {
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (attempt++ > 0) db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                await work();
                await transaction.CommitAsync(token);
            });
        }
        catch
        {
            db.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary>A row a unique index allows once, written twice — two requests at the same moment.</summary>
    public static bool Duplicate(DbUpdateException problem) => problem.InnerException is SqlException { Number: 2601 or 2627 };
}
