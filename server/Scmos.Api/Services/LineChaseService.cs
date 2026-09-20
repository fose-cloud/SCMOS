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
    /// <summary>Minutes before the plan time a job is chased; nothing unless set (20 Sep 2026); "off" or 0 is nothing too.</summary>
    public const string BeforeKey = "Line:ChaseBeforeMinutes";

    /// <summary>The rounds a job still without its arrival is chased at — none unless set (20 Sep 2026); "off" is none too.</summary>
    public const string RoundsKey = "Line:ChaseAt";

    public const string Action = "chase";

    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    /// <summary>Minutes before the plan time a job is chased; 0 when that ask is off.</summary>
    public int BeforeMinutes
    {
        get
        {
            var text = (config[BeforeKey] ?? "").Trim();
            if (text.Length == 0) return LineChase.DefaultBeforeMinutes;
            if (text.Equals("off", StringComparison.OrdinalIgnoreCase)) return 0;
            return int.TryParse(text, out var minutes) && minutes >= 0 ? minutes : LineChase.DefaultBeforeMinutes;
        }
    }

    /// <summary>The Bangkok clock times of the day's rounds; empty when off.</summary>
    public IReadOnlyList<TimeOnly> Rounds => LineChase.Rounds(config[RoundsKey]);

    /// <summary>The rounds as the screen prints them: "10:00, 14:00", or empty when off.</summary>
    public string RoundsText => string.Join(", ", Rounds.Select(at => at.ToString("HH:mm")));

    /// <param name="Jobs">The jobs due now with their stage, in this room.</param>
    /// <param name="Message">The text that would go, or empty.</param>
    public record Room(string LineGroupId, string GroupName, string Supplier,
        IReadOnlyList<(LineReminder.JobLine Job, string Stage)> Jobs, string Message);

    /// <summary>Every room with a job due a question at this moment, not yet asked at that stage today.</summary>
    public async Task<IReadOnlyList<Room>> DueAsync(DateTimeOffset now, CancellationToken token)
    {
        var before = BeforeMinutes;
        var rounds = Rounds;
        if (before <= 0 && rounds.Count == 0) return [];
        var here = now.ToOffset(Thailand);
        var day = DateOnly.FromDateTime(here.DateTime);

        // The chase ledger for today: which job was asked at which stage.
        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Thailand).ToUniversalTime();
        var done = (await db.AuditEvents.AsNoTracking()
            .Where(one => one.Entity == "job" && one.Action == Action && one.At >= since)
            .Select(one => new { one.EntityId, one.Field })
            .ToListAsync(token))
            .Select(one => one.EntityId + "|" + one.Field)
            .ToHashSet(StringComparer.Ordinal);

        // A job the room has already answered about — a message on it waiting
        // for a person to approve — is not asked again. The haulier said; the
        // delay is on this side. Only a message with something to approve
        // counts.
        var answered = (await db.LineEvents.AsNoTracking()
            .Where(one => one.MessageType == "text" && one.JobKey != "" && one.ReceivedAt >= since
                && one.ProcessingStatus == LineProcessing.NeedReview && one.ErrorCode == "ready-to-apply")
            .Select(one => new { one.JobKey, one.ErrorMessage })
            .ToListAsync(token))
            .SelectMany(one => KeysOf(one.JobKey, one.ErrorMessage))
            .ToHashSet(StringComparer.Ordinal);

        var rooms = new List<Room>();
        foreach (var room in await reminders.RoomsAsync(day, token))
        {
            var due = new List<(LineReminder.JobLine Job, string Stage)>();
            foreach (var job in room.AllJobs)
            {
                if (answered.Contains(job.Key)) continue;
                // The one ask before the plan time.
                if (LineChase.BeforeDue(job, here, before) && !done.Contains(job.Key + "|" + LineChase.Before))
                {
                    due.Add((job, LineChase.Before));
                    continue;
                }
                // The day's rounds, for a job whose plan time has passed with no arrival written.
                foreach (var round in rounds)
                {
                    var stage = LineChase.RoundStage(round);
                    if (!LineChase.RoundDue(job, here, round) || done.Contains(job.Key + "|" + stage)) continue;
                    due.Add((job, stage));
                    break;
                }
            }
            if (due.Count == 0) continue;
            rooms.Add(new Room(room.LineGroupId, room.GroupName, room.Supplier, due,
                LineChase.Compose(room.Supplier, due, here)));
        }
        return rooms;
    }

    /// <summary>The jobs a row is about: its key, or every key of a message about every box of a number.</summary>
    private static IReadOnlyList<string> KeysOf(string jobKey, string note)
    {
        var many = LineAuthority.KeysIn(note);
        return many.Count > 1 && many.Contains(jobKey) ? many : [jobKey];
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
                    stage == LineChase.Before
                        ? $"ติดตามสถานะรถ {LineChase.Elapsed(TimeSpan.FromMinutes(BeforeMinutes))}ก่อนเวลาแผน"
                        : $"ติดตามสถานะรถ รอบ {stage[6..]} — ยังไม่มีเวลาถึง",
                    token, EventSource.Line);
                asked++;
            }
            log.LogInformation("LINE chase sent to {Group} ({Supplier}): {Jobs} job(s)", room.GroupName, room.Supplier, room.Jobs.Count);
        }
        return asked;
    }
}

/// <summary>
/// Runs the chase every five minutes while the integration is on. The
/// before-ask falls due on each job's own plan time through the day, so
/// this is a poll rather than an hour; the rounds have their ten-minute
/// windows; the ledger keeps each stage to one message.
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
