using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// The morning reminder, end to end: which of today's jobs each haulier's
/// room should be asked about, the message for each, and the sending — by
/// the scheduler at the hour, or by a person from the LINE screen.
///
/// <para>
/// A send is written to the audit trail (action <c>notify</c>, entity
/// <c>line-group</c>), and that row is the ledger: a room is not asked twice
/// on one day by the scheduler, whatever the container did in between. A
/// person may send again on purpose.
/// </para>
/// </summary>
public class LineReminderService(ScmosDbContext db, ILineNotifier notifier, AuditService audit,
    IConfiguration config, ILogger<LineReminderService> log)
{
    /// <summary>Where the hour lives in configuration. Empty switches the schedule off; the button stays.</summary>
    public const string TimeKey = "Line:RemindAt";

    public const string Action = "notify";
    public const string Entity = "line-group";

    /// <summary>The Bangkok clock time the scheduler sends at, or null when the schedule is off.</summary>
    public TimeOnly? RemindAt
    {
        get
        {
            var text = config[TimeKey];
            if (text is null) text = LineReminder.DefaultTime;
            text = text.Trim();
            if (text.Length == 0 || text.Equals("off", StringComparison.OrdinalIgnoreCase)) return null;
            return TimeOnly.TryParseExact(text.Replace('.', ':'), "H:mm", null, System.Globalization.DateTimeStyles.None, out var at)
                ? at : TimeOnly.Parse(LineReminder.DefaultTime);
        }
    }

    /// <param name="Jobs">Today's jobs of this haulier that are short of something, as the message names them.</param>
    /// <param name="Messages">The text(s) that would go, or empty when nothing is missing.</param>
    /// <param name="SentAt">When the room was last sent today's reminder, UTC, or null.</param>
    public record Room(
        string LineGroupId, string GroupName, long SupplierId, string Supplier,
        IReadOnlyList<LineReminder.JobLine> Jobs, IReadOnlyList<string> Messages, DateTimeOffset? SentAt, string SentBy);

    /// <summary>Every active vendor room, with what today's reminder would say to it.</summary>
    public async Task<IReadOnlyList<Room>> PreviewAsync(DateOnly day, CancellationToken token)
    {
        var groups = await db.LineGroups.AsNoTracking()
            .Where(one => one.IsActive && one.GroupType == LineGroupType.Vendor && one.SupplierId > 0)
            .OrderBy(one => one.GroupName)
            .ToListAsync(token);
        if (groups.Count == 0) return [];

        // LineGroup keys the supplier as a long; the supplier's own id is an int.
        var supplierIds = groups.Select(one => (int)one.SupplierId).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(one => supplierIds.Contains(one.Id))
            .ToDictionaryAsync(one => (long)one.Id, one => one.Name, token);

        var rows = await db.OperationJobs.AsNoTracking()
            .Where(one => one.WorkDate == Formats.PlanDate(day))
            .Select(one => new { one.Key, one.Cat, one.Status, one.Customer, one.Trucker, one.JobCode, one.Container, one.Data })
            .ToListAsync(token);
        var lines = rows.Select(one => ToLine(one.Key, one.Cat, one.Status, one.Customer, one.Trucker, one.JobCode, one.Container, one.Data)).ToList();

        // Today's sends, so a room already asked is shown as such.
        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var sent = await db.AuditEvents.AsNoTracking()
            .Where(one => one.Entity == Entity && one.Action == Action && one.At >= since)
            .OrderByDescending(one => one.At)
            .Select(one => new { one.EntityId, one.At, one.Who })
            .ToListAsync(token);

        var rooms = new List<Room>();
        foreach (var group in groups)
        {
            var supplier = suppliers.GetValueOrDefault(group.SupplierId, "");
            if (supplier.Length == 0) continue;
            var mine = lines.Where(one => LineAuthority.SameCarrier(supplier, one.Carrier))
                .Select(one => one.Line)
                .Where(LineReminder.Wanted)
                .ToList();
            var last = sent.FirstOrDefault(one => one.EntityId == group.LineGroupId);
            rooms.Add(new Room(group.LineGroupId, group.GroupName, group.SupplierId, supplier,
                mine, LineReminder.Compose(supplier, day, mine), last?.At, last?.Who ?? ""));
        }
        return rooms;
    }

    /// <summary>
    /// Sends one room its reminder and writes the ledger row. Returns the
    /// failure in words, or empty. A room with nothing missing is not sent
    /// and says so.
    /// </summary>
    public async Task<string> SendAsync(Room room, AppUser by, string source, CancellationToken token)
    {
        if (room.Messages.Count == 0) return "ไม่มีงานที่ขาดข้อมูลวันนี้ — ไม่ได้ส่ง";
        var failure = await notifier.PushAsync(room.LineGroupId, room.Messages, token);
        if (failure.Length > 0) return failure;

        await audit.RecordAsync(by, Action, Entity, room.LineGroupId, room.Supplier,
            "reminder", "", $"{room.Jobs.Count} งาน · {room.Messages.Count} ข้อความ",
            source, token, EventSource.Line);
        log.LogInformation("LINE reminder sent to {Group} ({Supplier}): {Jobs} job(s)", room.GroupName, room.Supplier, room.Jobs.Count);
        return "";
    }

    /// <summary>
    /// The scheduler's pass: every room not yet asked today, once. Returns
    /// how many were sent.
    /// </summary>
    public async Task<int> SendDueAsync(DateOnly day, CancellationToken token)
    {
        var by = new AppUser("scheduler", "", "SCMOS", "System", "", "system", Recognised: true);
        var sent = 0;
        foreach (var room in await PreviewAsync(day, token))
        {
            if (room.SentAt is not null || room.Messages.Count == 0) continue;
            var failure = await SendAsync(room, by, "กำหนดเวลา " + (RemindAt?.ToString("HH:mm") ?? ""), token);
            if (failure.Length == 0) sent++;
            else log.LogWarning("LINE reminder to {Group} failed: {Why}", room.GroupName, failure);
        }
        return sent;
    }

    /// <summary>The cells the message needs, out of the row and its JSON.</summary>
    private static (string Carrier, LineReminder.JobLine Line) ToLine(string key, string cat, string status,
        string customer, string trucker, string jobCode, string container, string data)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var json = JsonDocument.Parse(data);
            foreach (var name in new[] { "booking", "destination", "plant", "returnLoc", "emptyReturn", "planTime", "licence", "driver", "contact", "jobNo", "wh", "province", "zip" })
            {
                if (json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                    fields[name] = value.GetString() ?? "";
            }
        }
        catch (JsonException) { }
        string F(string name) => fields.GetValueOrDefault(name, "");

        var province = F("province");
        if (F("zip").Length > 0) province = (province + " " + F("zip")).Trim();
        return (trucker, new LineReminder.JobLine(
            key, cat, status, customer, jobCode, F("booking"), container,
            F("destination"), F("plant"), F("returnLoc").Length > 0 ? F("returnLoc") : F("emptyReturn"),
            F("planTime"), F("licence"), F("driver"), F("contact"),
            JobNo: F("jobNo"), Warehouse: F("wh"), Province: province));
    }
}

/// <summary>
/// Sends the reminder at the hour, Bangkok time — 08:00 unless
/// <c>Line__RemindAt</c> says otherwise, or says "off".
///
/// <para>
/// Looks every minute and asks the ledger, so a container that restarts at
/// 08:03 still sends and one that restarts at 08:30 does not send again.
/// Only when the integration is on; see LineEventWorker on why a hosted
/// service lives here rather than in a function app.
/// </para>
/// </summary>
public class LineReminderScheduler(IServiceProvider services, ILogger<LineReminderScheduler> log) : BackgroundService
{
    private static readonly TimeSpan Thailand = TimeSpan.FromHours(7);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(2), stopping); }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var reminders = scope.ServiceProvider.GetRequiredService<LineReminderService>();
                var now = DateTimeOffset.UtcNow.ToOffset(Thailand);
                var at = reminders.RemindAt;
                // Within a ten-minute window after the hour, so a pass that
                // waited on the database still counts as this morning's.
                if (at is not null && now.TimeOfDay >= at.Value.ToTimeSpan() && now.TimeOfDay < at.Value.ToTimeSpan().Add(TimeSpan.FromMinutes(10)))
                {
                    var sent = await reminders.SendDueAsync(DateOnly.FromDateTime(now.DateTime), stopping);
                    if (sent > 0) log.LogInformation("LINE reminder: {Count} room(s) sent at {At}", sent, at);
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception problem)
            {
                log.LogError(problem, "LINE reminder pass failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(1), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }
}
