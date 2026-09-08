using System.Text.Json;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Ai.Operations;

public interface IOperationsSource
{
    IAsyncEnumerable<OperationAnalysisRow> ReadAsync(OperationReadScope scope, CancellationToken token);
}
public sealed class OperationsSource(JobsRepository jobs) : IOperationsSource
{
    public IAsyncEnumerable<OperationAnalysisRow> ReadAsync(OperationReadScope scope, CancellationToken token)
        => jobs.ReadAnalysisAsync(scope, token);
}

public sealed record OperationEvidence(string Key, string Category, string JobCode, string Container,
    string Customer, string Trucker, string Date, string PlanTime, string Status,
    bool HasOwner, bool HasDriver, bool HasPlate, bool ArrivalRecorded,
    string? Risk, string Explanation, string SuggestedAction, string Source = "operation_jobs");
public sealed record OperationsAnswer(string View, string AsOfDate, string TimeZone, string Window,
    int Total, int Returned, bool Truncated, int UndatedActive, int InvalidRows,
    DateTimeOffset RetrievedAt, DateTimeOffset? SourceUpdatedAt, string Basis,
    IReadOnlyList<OperationEvidence> Rows);

/// <summary>Reuses SCMOS rules. No LLM calculates counts, invents severity, or sees raw job JSON.</summary>
public sealed class OperationsReadService(IOperationsSource source, TimeProvider clock)
{
    public static DateOnly Today(DateTimeOffset utc) => DateOnly.FromDateTime(utc.ToOffset(Formats.Zone).DateTime);

    public async Task<OperationsAnswer> ReadAsync(string tool, JsonElement arguments, AiToolContext context,
        CancellationToken token)
    {
        if (!context.Scope.Team && string.IsNullOrWhiteSpace(context.Scope.OperatorId))
            throw new UnauthorizedAccessException("An explicit operator scope is required.");
        var now = context.AsOf ?? clock.GetUtcNow();
        var today = Today(now);
        var limit = arguments.GetProperty("limit").GetInt32();
        var view = tool switch
        {
            "query_shipments" => arguments.GetProperty("view").GetString()!,
            "search_shipment" => "search",
            "query_delays" => "delays",
            _ => throw new InvalidOperationException("Unknown read tool."),
        };
        if (limit is < 1 or > 50 || view is not ("today" or "risk_today" or "search" or "delays"))
            throw new InvalidOperationException("Invalid read arguments.");
        var search = view == "search" ? arguments.GetProperty("query").GetString()!.Trim() : "";
        if (view == "search" && (string.IsNullOrWhiteSpace(search) || search.Length > 120))
            throw new InvalidOperationException("Invalid search.");
        var matched = new List<(OperationEvidence Row, int Priority, int Day)>();
        var total = 0;
        var undated = 0;
        var invalid = 0;
        DateTimeOffset? updated = null;
        var scope = new OperationReadScope(context.Scope.Team, context.Scope.OperatorId);
        await foreach (var row in source.ReadAsync(scope, token).WithCancellation(token))
        {
            token.ThrowIfCancellationRequested();
            if (!scope.Team && !string.Equals(row.OwnerId, scope.OwnerId, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.Job is not { } job) { invalid++; continue; }
            // Defense in depth even for a substituted/cached source implementation.
            if (!scope.Team && !string.Equals(job.OwnerId, scope.OwnerId, StringComparison.OrdinalIgnoreCase)) continue;
            if (row.UpdatedAt != default && (updated is null || row.UpdatedAt > updated)) updated = row.UpdatedAt;
            if (!WorkspaceTabs.CountedInWorkspace(job.Cat) || JobRules.IsDone(job.Status)
                || WorkspaceTabs.IsCancelled(job) || string.IsNullOrWhiteSpace(job.Key)) continue;
            var day = Formats.ParseDay(job.Date);
            if (day is null) undated++;
            var flag = MonitorRules.Judge(job, today);
            var include = view switch
            {
                "today" => day == today,
                // A date window, not a new risk formula: backlog plus the existing near-term horizon.
                "risk_today" => day is not null && day <= today.AddDays(MonitorRules.SoonDays) && flag is not null,
                "delays" => WorkspaceTabs.Matches(WorkspaceTabs.Delay, job, "", today),
                "search" => new[] { job.Key, job.JobCode, job.Container, job.Customer }
                    .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase)),
                _ => false,
            };
            if (!include) continue;
            total++;
            var explanation = view == "delays" ? "สถานะหรือช่อง REASON/DELAY ระบุความล่าช้าตามกฎ My Job"
                : flag is { } risk ? Explain(risk.Why) : "";
            var evidence = new OperationEvidence(job.Key, Text(job.Cat), Text(job.JobCode), Text(job.Container),
                Text(job.Customer), Text(job.Trucker), Text(job.Date), row.PlanTime, Text(job.Status),
                job.Owner.Length > 0, job.Driver.Length > 0, job.Licence.Length > 0,
                job.ArrDate.Trim().Length > 0 || job.ArrTime.Trim().Length > 0,
                flag?.Why.ToString(), explanation, flag is { } action ? Action(action.Why) : "");
            matched.Add((evidence, view == "risk_today" ? (int)flag!.Value.Why : 0, day?.DayNumber ?? int.MaxValue));
            // Count the whole scoped result but retain at most the requested evidence rows.
            matched.Sort((a, b) =>
            {
                var order = a.Priority.CompareTo(b.Priority);
                if (order == 0) order = a.Day.CompareTo(b.Day);
                return order == 0 ? StringComparer.Ordinal.Compare(a.Row.Key, b.Row.Key) : order;
            });
            if (matched.Count > limit) matched.RemoveAt(matched.Count - 1);
        }
        return new(view, Formats.PlanDate(today), "Asia/Bangkok",
            view == "today" ? "scheduled_today_active" : view == "risk_today" ? "overdue_through_next_2_days" : "all_active_dates",
            total, matched.Count, total > matched.Count, undated, invalid, now, updated,
            "SCMOS MonitorRules.Judge / WorkspaceTabs.Delay; calculated from scoped records, not model-generated",
            matched.Select(pair => pair.Row).ToArray());
    }

    private static string Text(string text) => new(text.Take(160).Where(c => !char.IsControl(c)).ToArray());
    private static string Explain(MonitorRules.Risk risk) => risk switch
    {
        MonitorRules.Risk.Overdue => "เลยวันที่แผน และยังไม่มีข้อมูล ARRIVAL",
        MonitorRules.Risk.Unassigned => "ยังไม่มีผู้รับผิดชอบ และยังไม่มีข้อมูล ARRIVAL",
        MonitorRules.Risk.NoCarrier => "ใกล้วันงานภายใน 2 วัน แต่ยังไม่มีผู้ขนส่ง",
        MonitorRules.Risk.NoTruck => "มีผู้ขนส่งแล้ว แต่ยังไม่มีทั้งทะเบียนรถและชื่อคนขับ",
        _ => "",
    };
    private static string Action(MonitorRules.Risk risk) => risk switch
    {
        MonitorRules.Risk.Overdue => "ตรวจสอบการมาถึงและติดตามผู้ขนส่ง",
        MonitorRules.Risk.Unassigned => "ให้หัวหน้างานตรวจสอบการมอบหมาย",
        MonitorRules.Risk.NoCarrier => "ติดตามการยืนยันจากผู้ขนส่ง",
        MonitorRules.Risk.NoTruck => "ขอข้อมูลทะเบียนรถและคนขับ",
        _ => "",
    };
}

public sealed class OperationsReadHandler(string tool, OperationsReadService service) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(tool, arguments, context, token));
}
