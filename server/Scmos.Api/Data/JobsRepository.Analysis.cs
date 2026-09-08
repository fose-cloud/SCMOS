using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>Read scope resolved by a trusted service, not model arguments.</summary>
public sealed record OperationReadScope(bool Team, string? OwnerId);
public sealed record OperationAnalysisRow(string OwnerId, WorkspaceTabs.JobView? Job, string PlanTime, DateTimeOffset UpdatedAt);

public partial class JobsRepository
{
    /// <summary>SQL ownership filtering happens before any data is materialized.</summary>
    public static IQueryable<OperationJob> AnalysisQuery(IQueryable<OperationJob> jobs, OperationReadScope scope)
    {
        if (!scope.Team && string.IsNullOrWhiteSpace(scope.OwnerId))
            throw new UnauthorizedAccessException("An explicit read scope is required.");
        var query = jobs.AsNoTracking();
        return scope.Team ? query : query.Where(job => job.OwnerId == scope.OwnerId);
    }

    public async IAsyncEnumerable<OperationAnalysisRow> ReadAnalysisAsync(OperationReadScope scope,
        [EnumeratorCancellation] CancellationToken token)
    {
        var query = AnalysisQuery(db.OperationJobs, scope)
            .Select(job => new { job.Key, job.OwnerId, job.Data, job.UpdatedAt });
        // Exactly one read per tool. Never LoadAsync's full JSON response or an N+1 lookup.
        await foreach (var row in query.AsAsyncEnumerable().WithCancellation(token))
            yield return AnalysisRow(row.Key, row.OwnerId, row.Data, row.UpdatedAt);
    }

    /// <summary>Project private/free-text fields away inside the repository boundary.</summary>
    public static OperationAnalysisRow AnalysisRow(string key, string ownerId, string json, DateTimeOffset updatedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return new(ownerId, null, "", updatedAt);
            var job = WorkspaceTabs.JobView.From(document.RootElement);
            var time = document.RootElement.TryGetProperty("planTime", out var value)
                && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
            // Keep presence semantics identical to MonitorRules (Trim, not a new blank-value policy).
            static string Present(string text) => text.Trim().Length > 0 ? "recorded" : "";
            return new(ownerId, job with
            {
                Key = key, OwnerId = ownerId, Raw = default,
                Owner = Present(job.Owner), Driver = Present(job.Driver), Licence = Present(job.Licence),
                Reason = Present(job.Reason), MoveReason = "", MoveBy = "", CancelReason = "",
                Abs = "", Booking = "", Destination = "", Sid = "", Seal = "",
            }, Formats.IsTime(time) ? time : "", updatedAt);
        }
        catch (JsonException) { return new(ownerId, null, "", updatedAt); }
    }
}
