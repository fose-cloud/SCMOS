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

    /// <summary>Phase 3 — what to chase, by the department's own rules: the bell's and the LINE chase's, on the Monitor's horizon.</summary>
    public const string FollowUpTool = "query_followup";
    public static readonly string[] FollowUpViews = ["missing_truck", "no_carrier", "unreported", "container_mismatch"];

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
            FollowUpTool => arguments.GetProperty("view").GetString()!,
            _ => throw new InvalidOperationException("Unknown read tool."),
        };
        if (limit is < 1 or > ToolRegistry.OperationsEvidenceLimit
            || (tool == FollowUpTool ? !FollowUpViews.Contains(view, StringComparer.Ordinal) : view is not ("today" or "risk_today" or "search" or "delays")))
            throw new InvalidOperationException("Invalid read arguments.");
        // The follow-up views judge the plan moment against the clock: the read's
        // own instant, in the department's time zone.
        var bangkok = now.ToOffset(Formats.Zone);
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
            // Done jobs are out of every view but a search the server has asked to reach them (a summary of a finished job).
            if (!WorkspaceTabs.CountedInWorkspace(job.Cat) || (JobRules.IsDone(job.Status) && !(view == "search" && context.IncludeDone))
                || WorkspaceTabs.IsCancelled(job) || string.IsNullOrWhiteSpace(job.Key)) continue;
            var day = Formats.ParseDay(job.Date);
            if (day is null) undated++;
            var flag = MonitorRules.Judge(job, today);
            var arrived = job.ArrDate.Trim().Length > 0 || job.ArrTime.Trim().Length > 0;
            var near = day is not null && day <= today.AddDays(MonitorRules.SoonDays);
            // "unreported": today's job, its plan moment passed, and the register
            // not yet saying the truck has left or arrived (the LINE chase rule).
            var planAt = day == today ? Formats.Moment(job.Date, row.PlanTime) : null;
            var minutesPast = planAt is { } at ? (int)Math.Floor((bangkok.DateTime - at).TotalMinutes) : (int?)null;
            var include = view switch
            {
                "today" => day == today,
                // A date window, not a new risk formula: backlog plus the existing near-term horizon.
                "risk_today" => near && flag is not null,
                "delays" => WorkspaceTabs.Matches(WorkspaceTabs.Delay, job, "", today),
                "search" => new[] { job.Key, job.JobCode, job.Container, job.Customer }
                    .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase)),
                // Phase 3 — the bell's own rules, on the Monitor's horizon, for what to chase.
                "missing_truck" => near && !arrived && Notifications.MissingBookingData(job.Status, job.Trucker, job.Licence, job.Driver),
                "no_carrier" => near && !arrived && Notifications.NeedsCarrier(job.Status, job.Trucker),
                "unreported" => minutesPast is >= 0 && !LineChase.Reported(job.Cat, job.Status, job.ArrDate, job.ArrTime),
                "container_mismatch" => Notifications.ContainerWillNotMatch(job.Container),
                _ => false,
            };
            if (!include) continue;
            total++;
            var explanation = view switch
            {
                "delays" => "สถานะหรือช่อง REASON/DELAY ระบุความล่าช้าตามกฎ My Job",
                "missing_truck" => job.Licence.Trim().Length == 0 && job.Driver.Trim().Length == 0 ? "มีผู้ขนส่งแล้ว แต่ยังไม่มีทั้งทะเบียนรถและชื่อคนขับ"
                    : job.Licence.Trim().Length == 0 ? "มีผู้ขนส่งแล้ว แต่ยังไม่มีทะเบียนรถ" : "มีผู้ขนส่งแล้ว แต่ยังไม่มีชื่อคนขับ",
                "no_carrier" => day!.Value.DayNumber - today.DayNumber is var away && away < 0 ? $"ยังไม่มีผู้ขนส่ง และเลยกำหนดมาแล้ว {-away} วัน"
                    : away == 0 ? "ยังไม่มีผู้ขนส่ง และกำหนดวันนี้" : $"ยังไม่มีผู้ขนส่ง อีก {away} วันถึงกำหนด",
                "unreported" => $"เลยเวลาแผน {row.PlanTime} มา {minutesPast} นาที ยังไม่มีรายงานสถานะ (ยังไม่ถึง DISPATCHED และไม่มีเวลาถึง)",
                "container_mismatch" => "เลขตู้ไม่ตรงมาตรฐาน (4 ตัวอักษร + 7 หลัก) E-Card จะไม่ตรงกับ booking",
                _ => flag is { } risk ? Explain(risk.Why) : "",
            };
            var suggested = view switch
            {
                "missing_truck" => "ขอทะเบียนรถและชื่อคนขับจากผู้ขนส่ง",
                "no_carrier" => "ติดต่อผู้ขนส่ง หรือส่งต่อรายถัดไปตามลำดับ",
                "unreported" => "ติดตามผู้ขนส่ง — ถามสถานะรถและเวลาถึง",
                "container_mismatch" => "ตรวจเลขตู้กับ booking ก่อนรถถึงหน้าท่า",
                _ => flag is { } action ? Action(action.Why) : "",
            };
            var evidence = new OperationEvidence(job.Key, Text(job.Cat), Text(job.JobCode), Text(job.Container),
                Text(job.Customer), Text(job.Trucker), Text(job.Date), row.PlanTime, Text(job.Status),
                job.Owner.Length > 0, job.Driver.Length > 0, job.Licence.Length > 0, arrived,
                flag?.Why.ToString(), explanation, suggested);
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
            view switch
            {
                "today" => "scheduled_today_active",
                "risk_today" or "missing_truck" or "no_carrier" => "overdue_through_next_2_days",
                "unreported" => "scheduled_today_plan_time_passed",
                _ => "all_active_dates",
            },
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
