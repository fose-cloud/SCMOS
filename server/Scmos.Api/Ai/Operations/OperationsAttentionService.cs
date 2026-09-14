using Scmos.Api.Auth;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Operations;

public sealed record OperationsAttentionRow(string Key, string JobCode, string Customer, string Date,
    string Owner, string? Risk, bool MissingDriver, bool MissingPlate);
public sealed record OperationsAttentionResult(string AsOfDate, int Total, int Returned, int Undated,
    int InvalidRows, IReadOnlyList<OperationsAttentionRow> Rows);

/// <summary>Read-only attention list. Missing one dispatch detail is not a new Monitor risk score.</summary>
public sealed class OperationsAttentionService(IOperationsSource source, TimeProvider clock)
{
    public async Task<OperationsAttentionResult> ReadAsync(AppUser user, CancellationToken token)
    {
        var scope = AiPermissionPolicy.Scope(user);
        if (scope is null || !user.Can(Capability.ViewDashboard)) throw new UnauthorizedAccessException();
        var today = OperationsReadService.Today(clock.GetUtcNow());
        var total = 0; var undated = 0; var invalid = 0;
        var result = new List<OperationsAttentionRow>();
        await foreach (var row in source.ReadAsync(new(scope.Team, scope.OperatorId), token).WithCancellation(token))
        {
            if (!scope.Team && row.OwnerId != scope.OperatorId) continue;
            if (row.Job is not { } job) { invalid++; continue; }
            if (!scope.Team && job.OwnerId != scope.OperatorId) continue;
            if (!WorkspaceTabs.CountedInWorkspace(job.Cat) || JobRules.IsDone(job.Status) || WorkspaceTabs.IsCancelled(job)) continue;
            var date = Formats.ParseDay(job.Date);
            if (date is null) { undated++; continue; }
            if (date > today.AddDays(MonitorRules.SoonDays) || Formats.Clean(job.ArrDate).Length > 0 || Formats.Clean(job.ArrTime).Length > 0) continue;
            var missingDriver = Formats.Clean(job.Driver).Length == 0;
            var missingPlate = Formats.Clean(job.Licence).Length == 0;
            var risk = MonitorRules.Judge(job, today);
            if (!missingDriver && !missingPlate && risk is null) continue;
            total++;
            result.Add(new(job.Key, job.JobCode, job.Customer, job.Date, job.Owner, risk?.Why.ToString(), missingDriver, missingPlate));
            result.Sort((a, b) =>
            {
                var dateOrder = Nullable.Compare(Formats.ParseDay(a.Date), Formats.ParseDay(b.Date));
                return dateOrder != 0 ? dateOrder : StringComparer.Ordinal.Compare(a.Key, b.Key);
            });
            if (result.Count > 50) result.RemoveAt(result.Count - 1);
        }
        return new(Formats.PlanDate(today), total, result.Count, undated, invalid, result);
    }
}
