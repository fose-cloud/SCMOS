using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Demand">Jobs already planned for this date and vehicle type.</param>
/// <param name="Available">What the carrier says they have.</param>
/// <param name="Committed">What they have already promised, here or elsewhere.</param>
public record CapacityCell(
    int SupplierId, string Supplier, string Date, string VehicleType,
    int Available, int Committed, int Demand, string UpdatedBy, DateTimeOffset? UpdatedAt)
{
    public int Spare => Available - Committed;
    public bool Short => Committed > Available;
}

public record CapacityDay(string Date, int Available, int Committed, int Demand, bool Short);

public record CapacityBoard(
    IReadOnlyList<CapacityDay> Days,
    IReadOnlyList<CapacityCell> Cells,
    IReadOnlyList<string> VehicleTypes,
    /// <summary>Named so the screen can say what it is waiting for rather than showing zero.</summary>
    bool AnyReported);

public record CapacityResult(bool Ok, string Message);

/// <summary>
/// What the fleet can carry, against what has been planned.
///
/// The demand side has always existed — it is the register — but the supply side
/// needed somebody to say what they have, and nobody ever had a way to. Until
/// this, the capacity-shortage alert could only report that it was unable to
/// judge, which was true and unhelpful.
///
/// Availability is per supplier, per date, per vehicle type, because that is the
/// grain a shortage actually occurs at: having six trailers spare on Tuesday
/// does not help a job that needs a reefer on Monday.
/// </summary>
public class CapacityService(ScmosDbContext db)
{
    /// <summary>The vocabulary the rate cards use, so a promise can be priced.</summary>
    public static readonly string[] VehicleTypes =
        ["4W", "6W", "10W", "20F", "40F", "20RF", "40RF", "20TK", "TRAILER"];

    public async Task<CapacityBoard> ReadAsync(string? from, int days, CancellationToken token)
    {
        var start = Formats.IsDate(from ?? "") ? from! : DateTimeOffset.Now.ToString("dd/MM/yyyy");
        var startNumber = Formats.DateNumber(start);
        var wanted = Enumerable.Range(0, Math.Clamp(days, 1, 30))
            .Select(offset => DateOf(startNumber, offset))
            .Where(date => date.Length > 0)
            .ToList();
        var wantedNumbers = wanted.Select(Formats.DateNumber).ToHashSet();

        var suppliers = await db.Suppliers.AsNoTracking()
            .ToDictionaryAsync(supplier => supplier.Id, supplier => supplier.Name, token);

        var rows = (await db.SupplierCapacities.AsNoTracking().ToListAsync(token))
            .Where(row => wantedNumbers.Contains(Formats.DateNumber(row.Date)))
            .ToList();

        // Demand comes from the plan: jobs on that date, by the vehicle the job
        // says it needs. A job whose type will not map is counted against no
        // vehicle rather than guessed into one.
        var jobs = (await db.OperationJobs.AsNoTracking()
                .Where(job => job.Status != "")
                .Select(job => job.Data).ToListAsync(token))
            .Select(JobRecord.From).OfType<JobRecord>()
            .Where(job => wantedNumbers.Contains(Formats.DateNumber(job.Date)))
            .ToList();

        var demand = new Dictionary<(string Date, string Vehicle), int>();
        foreach (var job in jobs)
        {
            var vehicle = VehicleOf(job.Type);
            if (vehicle.Length == 0) continue;
            var key = (job.Date.Trim(), vehicle);
            demand[key] = demand.GetValueOrDefault(key) + 1;
        }

        var cells = rows.Select(row => new CapacityCell(
            row.SupplierId, suppliers.GetValueOrDefault(row.SupplierId, "(ไม่พบผู้ขนส่ง)"),
            row.Date, row.VehicleType, row.Available, row.Committed,
            demand.GetValueOrDefault((row.Date, row.VehicleType)),
            row.UpdatedBy, row.UpdatedAt)).ToList();

        var byDay = wanted.Select(date =>
        {
            var forDay = rows.Where(row => row.Date.Trim() == date).ToList();
            var demandForDay = demand.Where(entry => entry.Key.Date == date).Sum(entry => entry.Value);
            return new CapacityDay(date, forDay.Sum(r => r.Available), forDay.Sum(r => r.Committed),
                demandForDay, forDay.Any(r => r.Committed > r.Available));
        }).ToList();

        return new CapacityBoard(byDay, cells, VehicleTypes, rows.Count > 0);
    }

    /// <summary>
    /// Records what a carrier says they have. One row per supplier, date and
    /// vehicle type — saying it twice corrects it rather than adding to it.
    /// </summary>
    public async Task<CapacityResult> ReportAsync(int supplierId, string date, string vehicleType,
        int available, int committed, string by, CancellationToken token)
    {
        if (!Formats.IsDate(date)) return new CapacityResult(false, "วันที่ต้องเป็นรูปแบบ DD/MM/YYYY");
        if (available < 0 || committed < 0) return new CapacityResult(false, "จำนวนรถติดลบไม่ได้");

        var vehicle = vehicleType.Trim().ToUpperInvariant();
        if (!VehicleTypes.Contains(vehicle))
            return new CapacityResult(false, "ประเภทรถที่ใช้ได้: " + string.Join(", ", VehicleTypes));

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, token);
        if (supplier is null) return new CapacityResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var existing = await db.SupplierCapacities.FirstOrDefaultAsync(
            row => row.SupplierId == supplierId && row.Date == date && row.VehicleType == vehicle, token);

        if (existing is null)
        {
            existing = new SupplierCapacity { SupplierId = supplierId, Date = date, VehicleType = vehicle };
            db.SupplierCapacities.Add(existing);
        }

        existing.Available = available;
        existing.Committed = committed;
        existing.UpdatedBy = by;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return new CapacityResult(true,
            committed > available
                ? $"บันทึกแล้ว — {supplier.Name} รับงานไว้ {committed} เกินรถที่มี {available} คัน"
                : $"บันทึกแล้ว — {supplier.Name} {vehicle} ว่าง {available - committed} คัน");
    }

    /// <summary>
    /// The vehicle a job's type text means, in the rate cards' vocabulary.
    ///
    /// The plan writes the same truck as `1X6WH'`, `1x6 WH` and `6 WHEEL`, so
    /// this is the same normalisation the rate lookup does. Anything that will
    /// not map is left unmapped rather than guessed — a job counted against the
    /// wrong vehicle makes a shortage appear where there is none.
    /// </summary>
    public static string VehicleOf(string type)
    {
        var text = (type ?? "").ToUpperInvariant();
        if (text.Length == 0) return "";
        if (text.Contains("TRAILER")) return "TRAILER";
        if (text.Contains("40") && text.Contains("RF")) return "40RF";
        if (text.Contains("20") && text.Contains("RF")) return "20RF";
        if (text.Contains("TK") || text.Contains("TANK")) return "20TK";
        if (text.Contains("40")) return "40F";
        if (text.Contains("20")) return "20F";
        if (text.Contains("10W")) return "10W";
        if (text.Contains("6W")) return "6W";
        if (text.Contains("4W")) return "4W";
        return "";
    }

    /// <summary>DD/MM/YYYY, `offset` days after a YYYYMMDD number.</summary>
    private static string DateOf(int startNumber, int offset)
    {
        if (startNumber == 0) return "";
        var year = startNumber / 10000;
        var month = startNumber / 100 % 100;
        var day = startNumber % 100;
        try
        {
            return new DateTime(year, month, day).AddDays(offset).ToString("dd/MM/yyyy");
        }
        catch (ArgumentOutOfRangeException)
        {
            return "";
        }
    }
}
