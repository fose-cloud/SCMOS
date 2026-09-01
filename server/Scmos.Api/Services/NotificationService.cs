using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record Alert(
    string Kind, string English, string Thai, string Level, string Action, string Screen,
    string Title, string Detail,
    /// <summary>What to open: the job key, the supplier id, the case reference.</summary>
    string TargetId,
    string TargetKind,
    int Count);

public record AlertFeed(IReadOnlyList<Alert> Alerts, int Critical, int Warning, int Information);

/// <summary>
/// The alert feed, computed from the register rather than stored.
///
/// Nothing here is a saved notification row, and that is deliberate: an alert is
/// a fact about the current state, so it should stop existing the moment the
/// state changes. A notifications table would need a rule for when to mark each
/// row read and another for when to delete it, and the first time those
/// disagreed with reality somebody would be chasing a truck that already
/// arrived.
///
/// Alerts are grouped rather than listed one per job. "84 jobs missing a plate"
/// is actionable; eighty-four separate rows are a wall somebody scrolls past.
/// </summary>
public class NotificationService(ScmosDbContext db, KpiEngine kpi, JobRegisterCache register,
    DelegationService delegations)
{
    public async Task<AlertFeed> BuildAsync(string? ownerId, CancellationToken token)
    {
        var alerts = new List<Alert>();
        var today = Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));

        var snapshot = await register.ReadAsync(token);
        var jobs = snapshot.Rows.Select(row => row.Record).OfType<JobRecord>().ToList();

        // Narrowed to one person's work when asked. A supervisor wants the
        // team's alerts; an operator opening their own workspace wants theirs.
        if (!string.IsNullOrWhiteSpace(ownerId))
            jobs = jobs.Where(job => job.OpId == ownerId).ToList();

        /* ---- 0. holding somebody else's work ---- */
        // Before the rest, because it changes what every other alert on this
        // feed is about: some of those jobs are not yours.
        if (!string.IsNullOrWhiteSpace(ownerId))
        {
            var covering = await delegations.CoveringForAsync(ownerId, token);
            foreach (var grant in covering)
            {
                Add(alerts, AlertKind.ActingForColleague, 1,
                    $"คุณกำลังถืองานของ {grant.OwnerName}",
                    $"ตั้งแต่ {grant.FromDate} ถึง {grant.ToDate} · {grant.Reason}",
                    grant.OwnerId, "operator");
            }

            foreach (var grant in await delegations.ArrangedForYouAsync(ownerId, token))
            {
                Add(alerts, AlertKind.CoverArrangedForYou, 1,
                    $"{grant.CreatedBy} มอบสิทธิ์งานของคุณให้ {grant.DelegateName}",
                    $"ตั้งแต่ {grant.FromDate} ถึง {grant.ToDate} · {grant.Reason}",
                    grant.DelegateId, "operator");
            }
        }

        /* ---- 1. supplier not confirmed ---- */
        var noCarrier = jobs.Where(Notifications.NeedsCarrier).ToList();
        var urgent = noCarrier.Where(job =>
            Notifications.DaysAway(job, today) is int days && days <= Notifications.CarrierWarningDays).ToList();
        Add(alerts, AlertKind.SupplierNotConfirmed, noCarrier.Count,
            $"{noCarrier.Count} งานยังไม่มีผู้ขนส่ง",
            urgent.Count > 0
                ? $"{urgent.Count} งานจะถึงกำหนดภายใน {Notifications.CarrierWarningDays} วัน"
                : "ยังไม่มีงานที่ใกล้กำหนด",
            First(urgent.Count > 0 ? urgent : noCarrier), "job",
            // Escalated when something is close: the same fact is a different
            // problem two days out from what it is next month.
            urgent.Count > 0 ? AlertLevel.Critical : AlertLevel.Warning);

        /* ---- 2. booking missing data ---- */
        var incomplete = jobs.Where(Notifications.MissingBookingData).ToList();
        Add(alerts, AlertKind.BookingMissingData, incomplete.Count,
            $"{incomplete.Count} งานมีผู้ขนส่งแล้วแต่ยังไม่มีทะเบียนรถหรือคนขับ",
            "รถจะมาแต่ไม่มีใครรู้ว่าคันไหน", First(incomplete), "job");

        /* ---- 3. pre-run not confirmed ---- */
        var preRun = await db.PreRunChecks.AsNoTracking()
            .Where(check => check.Outcome == "pending").ToListAsync(token);
        Add(alerts, AlertKind.PreRunNotConfirmed, preRun.Count,
            $"{preRun.Count} รายการตรวจก่อนออกงานยังไม่ได้รับคำตอบ",
            preRun.Count > 0 ? $"เก่าสุดส่งไปเมื่อ {preRun.Min(c => c.SentAt):dd/MM HH:mm}" : "",
            preRun.FirstOrDefault()?.JobKey ?? "", "job");

        /* ---- 4. truck delay ---- */
        var delays = await db.DelayRecords.AsNoTracking()
            .Where(delay => delay.ResolvedAt == null).ToListAsync(token);
        var delayedJobs = jobs.Where(job => JobRules.IsDelayed(job.Status)).ToList();
        var delayCount = Math.Max(delays.Count, delayedJobs.Count);
        Add(alerts, AlertKind.TruckDelay, delayCount,
            $"{delayCount} งานล่าช้าและยังไม่ปิด",
            delays.Count > 0
                ? $"หมวดที่พบมากสุด: {delays.GroupBy(d => d.Category).OrderByDescending(g => g.Count()).First().Key}"
                : "ยังไม่ได้บันทึกหมวดความล่าช้า",
            delays.FirstOrDefault()?.JobKey ?? First(delayedJobs), "job");

        /* ---- 5. E-Card mismatch ---- */
        var badContainer = jobs.Where(Notifications.ContainerWillNotMatch).ToList();
        Add(alerts, AlertKind.ECardMismatch, badContainer.Count,
            $"{badContainer.Count} งานมีเลขตู้ผิดรูปแบบ",
            "เลขตู้ที่ไม่ตรงมาตรฐานจะไม่ตรงกับ E-Card ที่หน้าท่า", First(badContainer), "job");

        /* ---- 6 & 7. documents ---- */
        // Unclear and missing are counted from what is attached, so both are
        // honest zeroes until people start uploading rather than invented
        // figures that make the screen look busy.
        var documents = await db.Documents.AsNoTracking().ToListAsync(token);
        var unclear = documents.Count(d => d.Note.Contains("ไม่ชัด") || d.Note.Contains("unclear"));
        Add(alerts, AlertKind.DocumentUnclear, unclear,
            $"{unclear} เอกสารถูกทำเครื่องหมายว่าอ่านไม่ชัด", "ขอไฟล์ใหม่จากผู้ส่ง", "", "document");

        var deliveredKeys = jobs.Where(job => JobRules.IsDone(job.Status))
            .Select(job => job.Identity).ToHashSet(StringComparer.Ordinal);
        var withPod = documents.Where(d => d.Folder == "POD").Select(d => d.JobKey).ToHashSet(StringComparer.Ordinal);
        var missingPod = deliveredKeys.Count(key => !withPod.Contains(key));
        Add(alerts, AlertKind.PodMissing, missingPod,
            $"{missingPod} งานที่เสร็จแล้วยังไม่มีใบรับของ", "ขอ POD ก่อนวางบิล", "", "document");

        /* ---- 8 & 9. supplier paperwork ---- */
        var supplierDocs = documents.Where(d => d.SupplierId != null && d.ExpiryDate.Length > 0).ToList();
        var expiring = supplierDocs.Count(d =>
            d.Folder != "Audit" && (DocumentService.IsExpiring(d.ExpiryDate) || DocumentService.IsExpired(d.ExpiryDate)));
        Add(alerts, AlertKind.SupplierDocumentExpiring, expiring,
            $"{expiring} เอกสารผู้ขนส่งใกล้หมดอายุหรือหมดแล้ว",
            $"นับภายใน {Notifications.ExpiryWarningDays} วัน", "", "supplier");

        var audits = supplierDocs.Count(d =>
            d.Folder == "Audit" && (DocumentService.IsExpiring(d.ExpiryDate) || DocumentService.IsExpired(d.ExpiryDate)));
        Add(alerts, AlertKind.AuditExpiring, audits,
            $"{audits} ผลตรวจประเมินใกล้หมดอายุ", "นัดตรวจรอบใหม่", "", "supplier");

        /* ---- 10. CAR/PAR overdue ---- */
        var cases = await db.IncidentCases.AsNoTracking()
            .Where(record => record.Stage != "closed").ToListAsync(token);
        var overdue = cases.Where(record =>
            Formats.DateNumber(record.DueDate) > 0 && Formats.DateNumber(record.DueDate) < today).ToList();
        Add(alerts, AlertKind.CarParOverdue, overdue.Count,
            $"{overdue.Count} เคส CAR/PAR เกินกำหนด",
            overdue.Count > 0 ? $"เก่าสุด: {overdue.OrderBy(c => Formats.DateNumber(c.DueDate)).First().Reference}" : "",
            overdue.FirstOrDefault()?.Reference ?? "", "incident");

        /* ---- 11. capacity shortage ---- */
        // Measured against what carriers have actually told us they have. Nobody
        // has yet, so this reports that rather than a shortage it cannot know
        // about — an invented capacity risk would send people chasing trucks
        // that were never short.
        var capacity = await db.SupplierCapacities.AsNoTracking().CountAsync(token);
        if (capacity == 0)
        {
            alerts.Add(Describe(AlertKind.CapacityShortage, 0,
                "ยังประเมินกำลังรถไม่ได้",
                "ยังไม่มีผู้ขนส่งรายใดแจ้งจำนวนรถที่ว่างเข้ามา", "", "supplier", AlertLevel.Information));
        }
        else
        {
            var short_ = await db.SupplierCapacities.AsNoTracking()
                .CountAsync(row => row.Committed > row.Available, token);
            Add(alerts, AlertKind.CapacityShortage, short_,
                $"{short_} วันที่งานที่รับไว้เกินจำนวนรถที่แจ้ง", "หาผู้ขนส่งรายอื่นหรือเลื่อนงาน", "", "supplier");
        }

        /* ---- 12. KPI below target ---- */
        var report = await kpi.BuildAsync(Period.All, token);
        foreach (var measure in report.Measures)
        {
            if (measure.Value is not double value) continue;
            var target = measure.Id switch
            {
                nameof(MeasureId.OnTimeDelivery) => Notifications.OnTimeTarget,
                _ => 0,
            };
            if (target == 0 || value >= target) continue;

            alerts.Add(Describe(AlertKind.KpiBelowTarget, 1,
                $"{measure.Thai} {value:0.#}% ต่ำกว่าเป้า {target}%",
                $"คิดจาก {measure.Base} งานที่วัดได้", measure.Id, "kpi", AlertLevel.Warning));
        }

        /* ---- driver training ---- */
        // Counted per certificate, not per driver: one driver with two lapsed
        // courses is two pieces of work, and the number the team acts on is the
        // number of certificates to chase.
        var trainingToday = DateOnly.FromDateTime(DateTime.Now);
        var certificates = await db.DriverTrainings.AsNoTracking()
            .Where(record => !record.Voided)
            .Select(record => new { record.DriverId, record.CourseId, record.ExpiryDate })
            .ToListAsync(token);

        var activeDrivers = await db.Drivers.AsNoTracking()
            .Where(driver => driver.Active).Select(driver => driver.Id).ToListAsync(token);
        var active = activeDrivers.ToHashSet();

        // Only the most recent certificate per driver and course counts. An
        // older one that has lapsed is history, not a thing to chase.
        var current = certificates
            .Where(record => active.Contains(record.DriverId))
            .GroupBy(record => (record.DriverId, record.CourseId))
            .Select(group => group
                .OrderByDescending(record => TrainingRules.ParseDate(record.ExpiryDate) ?? DateOnly.MinValue)
                .First())
            .ToList();

        var expiringSoon = current.Count(record =>
            TrainingRules.DaysLeft(record.ExpiryDate, trainingToday) is int days && days > 0 && days <= 60);
        var alreadyExpired = current.Count(record =>
            TrainingRules.DaysLeft(record.ExpiryDate, trainingToday) is int days && days <= 0);

        Add(alerts, AlertKind.DriverTrainingExpiring, expiringSoon,
            $"{expiringSoon} ใบรับรองจะหมดอายุภายใน 60 วัน",
            "จัดอบรมต่ออายุก่อนถึงกำหนด มิฉะนั้นคนขับจะรับงานของลูกค้าที่กำหนดไม่ได้",
            "", "training");

        Add(alerts, AlertKind.DriverTrainingExpired, alreadyExpired,
            $"{alreadyExpired} ใบรับรองหมดอายุแล้ว",
            "คนขับที่ถือใบเหล่านี้รับงานของลูกค้าที่กำหนดหลักสูตรนั้นไม่ได้",
            "", "training");

        var raised = alerts.Where(alert => alert.Count > 0 || alert.Level == nameof(AlertLevel.Information)).ToList();
        return new AlertFeed(
            raised.OrderByDescending(alert => alert.Level == nameof(AlertLevel.Critical))
                .ThenByDescending(alert => alert.Level == nameof(AlertLevel.Warning))
                .ThenByDescending(alert => alert.Count).ToList(),
            raised.Count(a => a.Level == nameof(AlertLevel.Critical)),
            raised.Count(a => a.Level == nameof(AlertLevel.Warning)),
            raised.Count(a => a.Level == nameof(AlertLevel.Information)));
    }

    /// <summary>Adds an alert only when it has something to say. Zero is not news.</summary>
    private static void Add(List<Alert> alerts, AlertKind kind, int count, string title, string detail,
        string targetId, string targetKind, AlertLevel? level = null)
    {
        if (count <= 0) return;
        alerts.Add(Describe(kind, count, title, detail, targetId, targetKind, level));
    }

    private static Alert Describe(AlertKind kind, int count, string title, string detail,
        string targetId, string targetKind, AlertLevel? level = null)
    {
        var definition = Notifications.Of(kind);
        return new Alert(
            kind.ToString(), definition.English, definition.Thai,
            (level ?? definition.Level).ToString(), definition.Action, definition.Screen,
            title, detail, targetId, targetKind, count);
    }

    private static string First(IReadOnlyList<JobRecord> jobs) =>
        jobs.Count > 0 ? jobs[0].Identity : "";
}
