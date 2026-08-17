using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record RiskShipment(string JobKey, string Reference, string Customer, string Carrier,
    string Date, string Status, string Reason);

public record RiskGroup(string Customer, int Shipments, string Reason, string ReasonTh,
    IReadOnlyList<RiskShipment> Examples);

public record RiskAnswer(
    string Question,
    string Headline,
    IReadOnlyList<RiskGroup> Groups,
    IReadOnlyList<string> RecommendedActions,
    /// <summary>How the answer was arrived at. Always stated, never implied.</summary>
    string Basis,
    string ComputedAt);

/// <summary>
/// "วันนี้มีงานอะไรเสี่ยงบ้าง" — answered from the register.
///
/// The rules are the ones the operation already uses: a job with no carrier
/// close to its date, a job whose carrier has not confirmed, a container number
/// that will not match the card at the gate. That makes the answer explainable —
/// every shipment listed carries the reason it was listed — and it makes it
/// reproducible, which a model's answer to the same question would not be.
///
/// The screen calls this the AI assistant, and the answer says plainly that it
/// came from rules rather than a model. An assistant that quietly lets people
/// believe a language model reviewed their day is one they will eventually trust
/// with something it never looked at.
/// </summary>
public class RiskService(ScmosDbContext db)
{
    /// <summary>Beyond this a group's examples are a list nobody reads.</summary>
    private const int ExamplesPerGroup = 5;

    public async Task<RiskAnswer> TodayAsync(string? ownerId, CancellationToken token)
    {
        var rows = await db.OperationJobs.AsNoTracking().Select(job => job.Data).ToListAsync(token);
        var jobs = rows.Select(JobRecord.From).OfType<JobRecord>()
            .Where(job => !JobRules.IsDone(job.Status))
            .ToList();

        if (!string.IsNullOrWhiteSpace(ownerId))
            jobs = jobs.Where(job => job.OpId == ownerId).ToList();

        var today = Formats.DateNumber(DateTimeOffset.Now.ToString("dd/MM/yyyy"));

        // Nearest first: a job three days out is a bigger risk than the same job
        // three weeks out, and a list sorted any other way buries the urgent one.
        var risks = new List<RiskShipment>();
        foreach (var job in jobs)
        {
            var reason = Assess(job);
            if (reason is null) continue;
            risks.Add(new RiskShipment(job.Identity, job.Reference, job.Customer,
                job.Trucker, job.Date, job.Status, reason));
        }

        var groups = risks
            .GroupBy(risk => (risk.Customer, risk.Reason))
            .Select(group => new RiskGroup(
                group.Key.Customer.Length > 0 ? group.Key.Customer : "(ไม่ระบุลูกค้า)",
                group.Count(),
                group.Key.Reason,
                ReasonTh(group.Key.Reason),
                group.OrderBy(risk => Sortable(risk.Date)).Take(ExamplesPerGroup).ToList()))
            .OrderByDescending(group => Severity(group.Reason))
            .ThenByDescending(group => group.Shipments)
            .Take(12)
            .ToList();

        var actions = new List<string>();
        if (groups.Any(g => g.Reason == NoCarrier))
            actions.Add("ติดต่อผู้ขนส่ง หรือส่งต่อรายถัดไปตามลำดับที่อนุมัติไว้ (ทีละราย)");
        if (groups.Any(g => g.Reason == NoTruck))
            actions.Add("ขอทะเบียนรถและชื่อคนขับจากผู้ขนส่งที่รับงานแล้ว");
        if (groups.Any(g => g.Reason == BadContainer))
            actions.Add("ให้ CS ตรวจสอบเลขตู้กับ E-Card ก่อนรถถึงหน้าท่า");
        if (groups.Any(g => g.Reason == Held))
            actions.Add("ตามงานที่พักไว้ให้ปลดล็อก พร้อมบันทึกสาเหตุ");
        if (actions.Count == 0) actions.Add("ไม่มีงานที่เข้าเกณฑ์เสี่ยงในตอนนี้");

        var total = groups.Sum(group => group.Shipments);

        return new RiskAnswer(
            "วันนี้มีงานอะไรเสี่ยงบ้าง?",
            total == 0
                ? "ไม่พบงานที่เข้าเกณฑ์เสี่ยง"
                : $"{total} งานเสี่ยง จาก {groups.Select(g => g.Customer).Distinct().Count()} ลูกค้า",
            groups,
            actions,
            "คำนวณจากกฎในทะเบียนงาน ไม่ได้ใช้โมเดลภาษา — ทุกงานที่แสดงมีเหตุผลกำกับ และให้ผลเดิมทุกครั้งที่ถามซ้ำ",
            DateTimeOffset.UtcNow.ToString("O"));
    }

    private const string NoCarrier = "truck not confirmed";
    private const string NoTruck = "no plate or driver";
    private const string BadContainer = "E-Card mismatch";
    private const string Held = "job on hold";

    /// <summary>Why this job is a risk, or null when it is not one.</summary>
    private static string? Assess(JobRecord job)
    {
        if (Notifications.NeedsCarrier(job)) return NoCarrier;
        if (Notifications.ContainerWillNotMatch(job)) return BadContainer;
        if (JobRules.IsDelayed(job.Status)) return Held;
        if (Notifications.MissingBookingData(job)) return NoTruck;
        return null;
    }

    private static string ReasonTh(string reason) => reason switch
    {
        NoCarrier => "ยังไม่มีผู้ขนส่งยืนยัน",
        NoTruck => "ยังไม่มีทะเบียนรถหรือคนขับ",
        BadContainer => "เลขตู้ไม่ตรงมาตรฐาน จะไม่ตรงกับ E-Card",
        Held => "งานถูกพักไว้",
        _ => reason,
    };

    private static int Severity(string reason) => reason switch
    {
        NoCarrier => 4,
        BadContainer => 3,
        Held => 2,
        _ => 1,
    };

    /// <summary>Undated jobs sort last rather than first — see the register loader.</summary>
    private static int Sortable(string date)
    {
        var number = Formats.DateNumber(date);
        return number == 0 ? int.MaxValue : number;
    }
}
