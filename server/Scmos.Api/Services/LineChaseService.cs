using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// The status chase, end to end: which of today's jobs are due a question
/// right now, the message per room, the sending, and the ledger that keeps a
/// job from being asked twice at one stage.
///
/// <para>
/// Reads the day's rows through <see cref="LineReminderService"/>, so the
/// two messages name a job the same way. The audit row (action
/// <c>chase</c>, entity <c>job</c>, field the stage) is the ledger.
/// </para>
/// </summary>
public class LineChaseService(ScmosDbContext db, LineReminderService reminders, ILineNotifier notifier,
    AuditService audit, IConfiguration config, ILogger<LineChaseService> log)
{
    /// <summary>Where the margin after the plan time lives in configuration; "off" or 0 stops the chase.</summary>
    public const string MinutesKey = "Line:ChaseMinutes";

    /// <summary>Minutes before the plan time a job is chased; unset, "off" or 0 means never before.</summary>
    public const string BeforeKey = "Line:ChaseBeforeMinutes";

    /// <summary>How often an unreported job is asked again after the overdue ask; 0 stops the repeats.</summary>
    public const string RepeatKey = "Line:ChaseEveryHours";

    public const string Action = "chase";

    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    /// <summary>Minutes after the plan time a job is first chased; 0 when the chase is off.</summary>
    public int Minutes
    {
        get
        {
            var text = (config[MinutesKey] ?? "").Trim();
            if (text.Length == 0) return LineChase.DefaultMinutes;
            if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) return 0;
            return int.TryParse(text, out var minutes) && minutes >= 0 ? minutes : LineChase.DefaultMinutes;
        }
    }

    /// <summary>Minutes before the plan time a job is chased; 0, the default, when it is not.</summary>
    public int BeforeMinutes
    {
        get
        {
            var text = (config[BeforeKey] ?? "").Trim();
            if (text.Length == 0 || text.Equals("off", StringComparison.OrdinalIgnoreCase)) return LineChase.DefaultBeforeMinutes;
            return int.TryParse(text, out var minutes) && minutes >= 0 ? minutes : LineChase.DefaultBeforeMinutes;
        }
    }

    /// <summary>Hours between repeated asks; 0 when repeats are off.</summary>
    public int RepeatHours
    {
        get
        {
            var text = (config[RepeatKey] ?? "").Trim();
            if (text.Length == 0) return LineChase.DefaultRepeatHours;
            if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) return 0;
            return int.TryParse(text, out var hours) && hours >= 0 ? hours : LineChase.DefaultRepeatHours;
        }
    }

    /// <param name="Jobs">The jobs due now with their stage, in this room.</param>
    /// <param name="Message">The text that would go, or empty.</param>
    public record Room(string LineGroupId, string GroupName, string Supplier,
        IReadOnlyList<(LineReminder.JobLine Job, string Stage)> Jobs, string Message);

    /// <summary>Every room with a job due a question at this moment, not yet asked at that stage today.</summary>
    public async Task<IReadOnlyList<Room>> DueAsync(DateTimeOffset now, CancellationToken token)
    {
        var minutes = Minutes;
        if (minutes <= 0) return [];
        var here = now.ToOffset(Thailand);
        var day = DateOnly.FromDateTime(here.DateTime);

        // The chase ledger for today: which job was asked at which stage.
        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Thailand).ToUniversalTime();
        var asked = await db.AuditEvents.AsNoTracking()
            .Where(one => one.Entity == "job" && one.Action == Action && one.At >= since)
            .Select(one => new { one.EntityId, one.Field })
            .ToListAsync(token);
        var done = asked.Select(one => one.EntityId + "|" + one.Field).ToHashSet(StringComparer.Ordinal);

        // A job the room has already answered about today — a message on it
        // waiting for a person to approve — is not asked again. The haulier
        // said; the delay is on this side. Approving or dismissing the
        // message lets the chase resume if the register still shows nothing.
        var answered = (await db.LineEvents.AsNoTracking()
            .Where(one => one.ProcessingStatus == LineProcessing.NeedReview && one.JobKey != "" && one.ReceivedAt >= since)
            .Select(one => one.JobKey)
            .ToListAsync(token))
            .ToHashSet(StringComparer.Ordinal);

        var rooms = new List<Room>();
        foreach (var room in await reminders.RoomsAsync(day, token))
        {
            var due = room.AllJobs
                .Where(job => !answered.Contains(job.Key))
                .Select(job => (Job: job, Stage: LineChase.Stage(job, here, minutes, RepeatHours, BeforeMinutes)))
                .Where(one => one.Stage is not null && !done.Contains(one.Job.Key + "|" + one.Stage))
                .Select(one => (one.Job, one.Stage!))
                .ToList();
            if (due.Count == 0) continue;
            rooms.Add(new Room(room.LineGroupId, room.GroupName, room.Supplier, due,
                LineChase.Compose(room.Supplier, due, here)));
        }
        return rooms;
    }

    /// <summary>Sends every room its due question and writes the ledger. Returns how many jobs were asked.</summary>
    public async Task<int> SendDueAsync(DateTimeOffset now, CancellationToken token)
    {
        var by = new AppUser("scheduler", "", "SCMOS", "System", "", "system", Recognised: true);
        var asked = 0;
        foreach (var room in await DueAsync(now, token))
        {
            var failure = await notifier.PushAsync(room.LineGroupId, [room.Message], token);
            if (failure.Length > 0)
            {
                log.LogWarning("LINE chase to {Group} failed: {Why}", room.GroupName, failure);
                continue;
            }
            foreach (var (job, stage) in room.Jobs)
            {
                await audit.RecordAsync(by, Action, "job", job.Key, job.JobCode.Length > 0 ? job.JobCode : job.Booking,
                    stage, "", room.GroupName,
                    stage == LineChase.Before ? $"ติดตามสถานะรถ {LineChase.Elapsed(TimeSpan.FromMinutes(BeforeMinutes))}ก่อนเวลาแผน"
                    : stage == LineChase.Overdue ? $"ติดตามสถานะรถ {LineChase.Elapsed(TimeSpan.FromMinutes(Minutes))}หลังเวลาแผน"
                    : $"ติดตามสถานะรถซ้ำ ครั้งที่ {LineChase.AskNumber(stage)} — ยังไม่มีเวลาถึง",
                    token, EventSource.Line);
                asked++;
            }
            log.LogInformation("LINE chase sent to {Group} ({Supplier}): {Jobs} job(s)", room.GroupName, room.Supplier, room.Jobs.Count);
        }
        return asked;
    }
}

/// <summary>
/// Runs the chase every five minutes while the integration is on. Jobs
/// become due on their own plan times through the day, so this is a poll
/// rather than an hour; the ledger keeps each stage to one message.
/// </summary>
public class LineChaseScheduler(IServiceProvider services, ILogger<LineChaseScheduler> log) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(3), stopping); }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var chase = scope.ServiceProvider.GetRequiredService<LineChaseService>();
                var asked = await chase.SendDueAsync(DateTimeOffset.UtcNow, stopping);
                if (asked > 0) log.LogInformation("LINE chase: {Count} job(s) asked about", asked);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception problem)
            {
                log.LogError(problem, "LINE chase pass failed");
            }

            try { await Task.Delay(Tick, stopping); }
            catch (OperationCanceledException) { return; }
        }
    }
}
