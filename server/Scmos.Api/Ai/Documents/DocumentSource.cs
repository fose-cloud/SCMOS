using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Ai.Documents;

/// <summary>
/// The tables behind the Document &amp; Invoice Agent's read: the register
/// through the same analysis projection the Operations reads use (ownership
/// filtered in SQL, private fields projected away), and the documents table
/// as the verification and compliance screens read it — no wider.
/// </summary>
public sealed class DocumentSource(JobsRepository jobs, ScmosDbContext db) : IDocumentSource
{
    public IAsyncEnumerable<OperationAnalysisRow> JobsAsync(OperationReadScope scope, CancellationToken token)
        => jobs.ReadAnalysisAsync(scope, token);

    public async Task<IReadOnlyList<StoredDocument>> JobDocumentsAsync(IReadOnlyCollection<string> jobKeys, CancellationToken token)
    {
        if (jobKeys.Count == 0) return [];
        var wanted = jobKeys.ToList();
        return await db.Documents.AsNoTracking()
            .Where(file => file.JobKey != "" && wanted.Contains(file.JobKey))
            .OrderByDescending(file => file.Id)
            .Take(2000)
            .ToListAsync(token);
    }

    public async Task<IReadOnlyList<StoredDocument>> ComplianceAsync(CancellationToken token)
        => await db.Documents.AsNoTracking()
            .Where(file => file.ExpiryDate != "" && (file.SupplierId != null || file.DriverId != null))
            .OrderBy(file => file.Id)
            .Take(2000)
            .ToListAsync(token);
}
