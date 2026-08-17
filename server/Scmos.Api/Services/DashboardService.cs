using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// A figure, and whether it could be measured at all.
///
/// <see cref="Value"/> is null when the records that would answer it do not
/// exist. That is not the same as zero, and the screen renders it as
/// "ยังวัดไม่ได้" rather than a reassuring 0% — a management dashboard that
/// shows an unmeasured rate as green is worse than one that shows nothing.
/// </summary>
public record Figure(string Id, string English, string Thai, double? Value, int Base, string Unit, string Note);

public record TodayBoard(
    string Date,
    IReadOnlyList<Figure> Volume,
    IReadOnlyList<Figure> Performance,
    IReadOnlyList<Figure> Attention,
    string ComputedAt);

/// <summary>
/// The front page: what is happening today.
///
/// "Today" means the plan date, not the day a row was written — a job planned
/// for today is today's problem whether it was keyed last week or this morning.
/// When the plan holds nothing for today, the board says so and reports the
/// nearest planned day instead of showing five zeroes that look like a quiet
/// morning.
/// </summary>
public class DashboardService(ScmosDbContext db, KpiEngine kpi)
{
    public async Task<TodayBoard> TodayAsync(string? onDate, CancellationToken token)
    {
        var wanted = (onDate ?? "").Trim();
        var today = wanted.Length > 0 && Formats.IsDate(wanted)
            ? wanted
            : DateTimeOffset.Now.ToString("dd/MM/yyyy");
        var todayNumber = Formats.DateNumber(today);

        var rows = await db.OperationJobs.AsNoTracking().Select(job => job.Data).ToListAsync(token);
        var all = rows.Select(JobRecord.From).OfType<JobRecord>().ToList();

        var jobs = all.Where(job => Formats.DateNumber(job.Date) == todayNumber).ToList();
        var note = "";

        if (jobs.Count == 0)
        {
            // Nothing planned for today. Rather than five zeroes, report the
            // nearest planned day and say which one it is — the July plan is in
            // the past, so this is the normal case right now, not an edge one.
            var nearest = all
                .Select(job => Formats.DateNumber(job.Date))
                .Where(number => number > 0)
                .OrderBy(number => Math.Abs(number - todayNumber))
                .FirstOrDefault();

            if (nearest > 0)
            {
                jobs = all.Where(job => Formats.DateNumber(job.Date) == nearest).ToList();
                today = $"{nearest % 100:D2}/{nearest / 100 % 100:D2}/{nearest / 10000:D4}";
                note = "ไม่มีงานตามแผนของวันนี้ — แสดงวันที่ใกล้ที่สุดที่มีงาน";
            }
        }

        /* ------------------------------------------------------- volume */
        var completed = jobs.Count(job => JobRules.IsDone(job.Status));
        var delayed = jobs.Count(job => JobRules.IsDelayed(job.Status));
        var inTransit = jobs.Count(job => JobStatus.IsRunning(JobStatus.FromLegacy(job.Status)));
        // Everything that is not finished, running or held is still waiting to
        // start. Deriving it rather than testing for it means the four buckets
        // always add up to the total, which is what makes the row readable.
        var pending = jobs.Count - completed - delayed - inTransit;

        var volume = new List<Figure>
        {
            Count("total", "Total Shipment", "งานทั้งหมด", jobs.Count),
            Count("completed", "Completed", "เสร็จแล้ว", completed),
            Count("inTransit", "In Transit", "กำลังวิ่ง", inTransit),
            Count("pending", "Pending", "รอดำเนินการ", Math.Max(0, pending)),
            Count("delay", "Delay", "ล่าช้า", delayed),
        };

        /* -------------------------------------------------- performance */
        var withCarrier = jobs.Count(job => job.Trucker.Trim().Length > 0);
        var confirmation = jobs.Count == 0 ? null : (double?)(withCarrier * 100.0 / jobs.Count);

        var report = await kpi.BuildAsync(Period.All, token);
        var delivery = report.Measures.FirstOrDefault(m => m.Id == nameof(MeasureId.OnTimeDelivery));
        var pickup = report.Measures.FirstOrDefault(m => m.Id == nameof(MeasureId.OnTimePickup));

        var performance = new List<Figure>
        {
            new("truckConfirmation", "Truck Confirmation", "ยืนยันรถแล้ว", confirmation, jobs.Count, "%",
                jobs.Count == 0 ? "ไม่มีงานในวันนี้" : $"{withCarrier} จาก {jobs.Count} งานมีผู้ขนส่งแล้ว"),

            // Both come from the KPI engine over the whole register, not today
            // alone: one day's dozen jobs is not a rate anybody should steer by,
            // and the engine already refuses to report below its minimum sample.
            From("onTimePickup", pickup, "รับตู้ตรงเวลา"),
            From("onTimeDelivery", delivery, "ส่งมอบตรงเวลา"),
        };

        /* ---------------------------------------------------- attention */
        var openCases = await db.IncidentCases.AsNoTracking()
            .Where(record => record.Stage != "closed").ToListAsync(token);
        var documents = await db.Documents.AsNoTracking()
            .Where(document => document.SupplierId != null && document.ExpiryDate != "").ToListAsync(token);
        var capacity = await db.SupplierCapacities.AsNoTracking().ToListAsync(token);

        var attention = new List<Figure>
        {
            Count("openIncident", "Open Incident", "เหตุผิดปกติที่เปิดอยู่",
                openCases.Count(record => record.Category == "accident" || record.Category == "damage")),

            Count("openCarPar", "Open CAR/PAR", "CAR/PAR ที่เปิดอยู่", openCases.Count),

            Count("documentWarning", "Document Warning", "เอกสารใกล้หมดอายุ",
                documents.Count(document => DocumentService.IsExpiring(document.ExpiryDate)
                    || DocumentService.IsExpired(document.ExpiryDate))),

            capacity.Count == 0
                // No carrier has told us what they have, so the risk is unknown.
                // Reporting 0 would say "no shortage", which is a claim the
                // system has no basis for.
                ? new Figure("capacityRisk", "Capacity Risk", "ความเสี่ยงกำลังรถ", null, 0, "",
                    "ยังไม่มีผู้ขนส่งแจ้งจำนวนรถที่ว่าง")
                : Count("capacityRisk", "Capacity Risk", "ความเสี่ยงกำลังรถ",
                    capacity.Count(row => row.Committed > row.Available)),
        };

        return new TodayBoard(
            note.Length > 0 ? $"{today} · {note}" : today,
            volume, performance, attention,
            DateTimeOffset.UtcNow.ToString("O"));
    }

    private static Figure Count(string id, string english, string thai, int value) =>
        new(id, english, thai, value, value, "", "");

    private static Figure From(string id, Measure? measure, string thai) =>
        measure is null
            ? new Figure(id, id, thai, null, 0, "%", "ยังวัดไม่ได้")
            : new Figure(id, measure.English, thai, measure.Value, measure.Base, "%",
                measure.Available ? $"คิดจาก {measure.Base} งานที่วัดได้" : measure.Note);
}
