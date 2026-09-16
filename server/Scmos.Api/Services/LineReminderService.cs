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

    /// <summary>The Bangkok clock times the scheduler sends at — 08:00 and 12:00 unless set; empty when off.</summary>
    public IReadOnlyList<TimeOnly> RemindTimes => LineReminder.Times(config[TimeKey]);

    /// <summary>The times as the screen prints them: "08:00, 12:00", or empty when off.</summary>
    public string RemindAtText => string.Join(", ", RemindTimes.Select(at => at.ToString("HH:mm")));

    /// <summary>What a manual send is ledgered as, beside the clock-time slots.</summary>
    public const string ManualSlot = "manual";

    /// <param name="Jobs">Today's jobs of this haulier that are short of something, as the message names them.</param>
    /// <param name="Messages">The text(s) that would go, or empty when nothing is missing.</param>
    /// <param name="SentAt">When the room was last sent a reminder today, UTC, or null.</param>
    /// <param name="SentSlots">Which slots — "08:00", "12:00", "manual" — the room has been sent today.</param>
    public record Room(
        string LineGroupId, string GroupName, long SupplierId, string Supplier,
        IReadOnlyList<LineReminder.JobLine> Jobs, IReadOnlyList<string> Messages, DateTimeOffset? SentAt, string SentBy,
        IReadOnlyList<string> SentSlots);

    /// <summary>A room and every job of its haulier on the day — what both the reminder and the chase read.</summary>
    public record RoomJobs(string LineGroupId, string GroupName, long SupplierId, string Supplier,
        IReadOnlyList<LineReminder.JobLine> AllJobs);

    /// <summary>Every active vendor room, with what today's reminder would say to it.</summary>
    public async Task<IReadOnlyList<Room>> PreviewAsync(DateOnly day, CancellationToken token)
    {
        // Today's sends, so a room already asked is shown as such.
        var since = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(7)).ToUniversalTime();
        var sent = await db.AuditEvents.AsNoTracking()
            .Where(one => one.Entity == Entity && one.Action == Action && one.At >= since)
            .OrderByDescending(one => one.At)
            .Select(one => new { one.EntityId, one.At, one.Who, one.Field })
            .ToListAsync(token);

        var rooms = new List<Room>();
        foreach (var room in await RoomsAsync(day, token))
        {
            var mine = room.AllJobs.Where(LineReminder.Wanted).ToList();
            var last = sent.FirstOrDefault(one => one.EntityId == room.LineGroupId);
            var slots = sent.Where(one => one.EntityId == room.LineGroupId).Select(one => one.Field).Distinct().ToList();
            rooms.Add(new Room(room.LineGroupId, room.GroupName, room.SupplierId, room.Supplier,
                mine, LineReminder.Compose(room.Supplier, day, mine), last?.At, last?.Who ?? "", slots));
        }
        return rooms;
    }

    /// <summary>Every active vendor room with a supplier, and that haulier's jobs on the day.</summary>
    public async Task<IReadOnlyList<RoomJobs>> RoomsAsync(DateOnly day, CancellationToken token)
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
        var aliasRows = await db.SupplierAliases.AsNoTracking()
            .Where(one => supplierIds.Contains(one.SupplierId))
            .Select(one => new { one.SupplierId, one.Alias })
            .ToListAsync(token);
        var aliases = aliasRows.GroupBy(one => (long)one.SupplierId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(one => one.Alias).ToList());

        var rows = await db.OperationJobs.AsNoTracking()
            .Where(one => one.WorkDate == Formats.PlanDate(day))
            .Select(one => new { one.Key, one.Cat, one.Status, one.Customer, one.Trucker, one.JobCode, one.Container, one.WorkDate, one.Data })
            .ToListAsync(token);
        var lines = rows.Select(one => ToLine(one.Key, one.Cat, one.Status, one.Customer, one.Trucker, one.JobCode, one.Container, one.WorkDate, one.Data)).ToList();

        var rooms = new List<RoomJobs>();
        foreach (var group in groups)
        {
            var supplier = suppliers.GetValueOrDefault(group.SupplierId, "");
            if (supplier.Length == 0) continue;
            // A room's jobs are the ones written under its supplier's name or
            // any spelling the alias table says is the same haulier.
            var speaker = new LineAuthority.SpeakerGroup(true, group.IsActive, group.GroupType, supplier,
                aliases.GetValueOrDefault(group.SupplierId));
            var mine = lines.Where(one => LineAuthority.SameCarrier(speaker, one.Carrier))
                .Select(one => one.Line)
                .ToList();
            rooms.Add(new RoomJobs(group.LineGroupId, group.GroupName, group.SupplierId, supplier, mine));
        }
        return rooms;
    }

    /// <summary>
    /// Sends one room its reminder and writes the ledger row. Returns the
    /// failure in words, or empty. A room with nothing missing is not sent
    /// and says so.
    /// </summary>
    /// <param name="slot">The clock time this send is for ("08:00", "12:00"), or "manual" — the ledger's key.</param>
    public async Task<string> SendAsync(Room room, AppUser by, string source, string slot, CancellationToken token)
    {
        if (room.Messages.Count == 0) return "ไม่มีงานที่ขาดข้อมูลวันนี้ — ไม่ได้ส่ง";
        var failure = await notifier.PushAsync(room.LineGroupId, room.Messages, token);
        if (failure.Length > 0) return failure;

        await audit.RecordAsync(by, Action, Entity, room.LineGroupId, room.Supplier,
            slot, "", $"{room.Jobs.Count} งาน · {room.Messages.Count} ข้อความ",
            source, token, EventSource.Line);
        log.LogInformation("LINE reminder sent to {Group} ({Supplier}): {Jobs} job(s)", room.GroupName, room.Supplier, room.Jobs.Count);
        return "";
    }

    /// <summary>
    /// The scheduler's pass for one slot: every room not yet sent that slot
    /// today and still short of something — the noon ask goes only where
    /// the morning's went unanswered. Returns how many were sent.
    /// </summary>
    public async Task<int> SendDueAsync(DateOnly day, string slot, CancellationToken token)
    {
        var by = new AppUser("scheduler", "", "SCMOS", "System", "", "system", Recognised: true);
        var sent = 0;
        foreach (var room in await PreviewAsync(day, token))
        {
            if (room.SentSlots.Contains(slot) || room.Messages.Count == 0) continue;
            var failure = await SendAsync(room, by, $"กำหนดเวลา {slot}", slot, token);
            if (failure.Length == 0) sent++;
            else log.LogWarning("LINE reminder to {Group} at {Slot} failed: {Why}", room.GroupName, slot, failure);
        }
        return sent;
    }

    /// <summary>The cells the message needs, out of the row and its JSON.</summary>
    private static (string Carrier, LineReminder.JobLine Line) ToLine(string key, string cat, string status,
        string customer, string trucker, string jobCode, string container, string workDate, string data)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var json = JsonDocument.Parse(data);
            foreach (var name in new[] { "booking", "destination", "plant", "returnLoc", "emptyReturn", "planTime", "licence", "driver", "contact", "jobNo", "wh", "province", "zip", "arrDate", "arrTime" })
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
            JobNo: F("jobNo"), Warehouse: F("wh"), Province: province,
            Date: workDate, ArrDate: F("arrDate"), ArrTime: F("arrTime")));
    }
}

/// <summary>
/// Sends the reminder at each hour, Bangkok time — 08:00 and 12:00 unless
/// <c>Line__RemindAt</c> says otherwise, or says "off". The second ask goes
/// only to a room still short of something.
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
                foreach (var at in reminders.RemindTimes)
                {
                    // Within a ten-minute window after the hour, so a pass that
                    // waited on the database still counts as this slot's.
                    if (now.TimeOfDay < at.ToTimeSpan() || now.TimeOfDay >= at.ToTimeSpan().Add(TimeSpan.FromMinutes(10))) continue;
                    var slot = at.ToString("HH:mm");
                    var sent = await reminders.SendDueAsync(DateOnly.FromDateTime(now.DateTime), slot, stopping);
                    if (sent > 0) log.LogInformation("LINE reminder: {Count} room(s) sent at {At}", sent, slot);
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
