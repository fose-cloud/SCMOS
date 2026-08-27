using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Code">What is stored on a job.</param>
/// <param name="Label">How it reads in a dropdown.</param>
/// <param name="Active">Offered for new work.</param>
/// <param name="InUse">How many jobs already carry this code.</param>
public record VehicleTypeView(int Id, string Code, string Label, int Sort, bool Active, int InUse);

public record VehicleTypeResult(bool Ok, string Message);

/// <summary>
/// The list of things the team dispatches, and the one place that answers what
/// belongs on it.
///
/// <para>
/// Seeded from <see cref="JobVehicleType"/> the first time it is read, so a
/// fresh database starts with the sixteen the register was already using
/// rather than empty. After that the table is the authority and the code is
/// only history — an Admin adding a type does not need a deployment, which is
/// the entire point of moving it out of a C# array.
/// </para>
/// </summary>
public class VehicleTypeService(ScmosDbContext db)
{
    /// <summary>
    /// Everything on the list, retired rows included, with a count of the jobs
    /// already carrying each code.
    ///
    /// The count is what makes retiring safe to offer: somebody about to remove
    /// a type can see that four hundred jobs are written against it first.
    /// </summary>
    public async Task<IReadOnlyList<VehicleTypeView>> ReadAsync(CancellationToken token)
    {
        await SeedAsync(token);

        var rows = await db.VehicleTypes.AsNoTracking()
            .OrderBy(row => row.Sort).ThenBy(row => row.Code)
            .ToListAsync(token);

        var counts = await CountByTypeAsync(token);

        return rows
            .Select(row => new VehicleTypeView(row.Id, row.Code, row.Label, row.Sort, row.Active,
                counts.GetValueOrDefault(row.Code)))
            .ToList();
    }

    /// <summary>The codes a dropdown should offer, in order.</summary>
    public async Task<IReadOnlyList<string>> ActiveCodesAsync(CancellationToken token)
    {
        await SeedAsync(token);
        return await db.VehicleTypes.AsNoTracking()
            .Where(row => row.Active)
            .OrderBy(row => row.Sort).ThenBy(row => row.Code)
            .Select(row => row.Code)
            .ToListAsync(token);
    }

    /// <summary>
    /// Adds a type, or brings a retired one back.
    ///
    /// The code is put through <see cref="JobVehicleType.Canonical"/> on the
    /// way in, so somebody typing `1x20` onto the list gets the same `1X20'`
    /// the register already holds rather than founding a seventeenth category
    /// on the spot.
    /// </summary>
    public async Task<VehicleTypeResult> AddAsync(string code, string label, string who, CancellationToken token)
    {
        var wanted = JobVehicleType.Canonical(code);
        if (wanted.Length == 0) return new VehicleTypeResult(false, "ต้องระบุรหัสประเภทรถ");
        if (wanted.Length > 40) return new VehicleTypeResult(false, "รหัสยาวเกินไป");

        var existing = await db.VehicleTypes
            .FirstOrDefaultAsync(row => row.Code == wanted, token);

        if (existing is not null)
        {
            if (existing.Active) return new VehicleTypeResult(false, $"มี {wanted} อยู่ในรายการแล้ว");
            existing.Active = true;
            existing.UpdatedBy = who;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);
            return new VehicleTypeResult(true, $"นำ {wanted} กลับมาใช้แล้ว");
        }

        var last = await db.VehicleTypes.MaxAsync(row => (int?)row.Sort, token) ?? 0;
        db.VehicleTypes.Add(new VehicleTypeRow
        {
            Code = wanted,
            Label = string.IsNullOrWhiteSpace(label) ? wanted : label.Trim(),
            Sort = last + 10,
            Active = true,
            UpdatedBy = who,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(token);
        return new VehicleTypeResult(true, $"เพิ่ม {wanted} แล้ว");
    }

    /// <summary>
    /// Retires a type.
    ///
    /// Never a delete. The code is written on real jobs and in their history,
    /// and a row pointing at a type that no longer exists reads as a blank —
    /// so it leaves the dropdowns and stays legible everywhere it was already
    /// used. The message says how many jobs that is, because somebody
    /// retiring a type with four hundred jobs behind it should hear so.
    /// </summary>
    public async Task<VehicleTypeResult> RetireAsync(int id, string who, CancellationToken token)
    {
        var row = await db.VehicleTypes.FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (row is null) return new VehicleTypeResult(false, "ไม่พบประเภทรถนี้");
        if (!row.Active) return new VehicleTypeResult(false, $"{row.Code} ถูกนำออกไปแล้ว");

        var inUse = (await CountByTypeAsync(token)).GetValueOrDefault(row.Code);

        row.Active = false;
        row.UpdatedBy = who;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return new VehicleTypeResult(true, inUse > 0
            ? $"นำ {row.Code} ออกจากตัวเลือกแล้ว · {inUse} งานที่ใช้อยู่เดิมยังแสดงค่านี้ตามปกติ"
            : $"นำ {row.Code} ออกจากตัวเลือกแล้ว");
    }

    /// <summary>
    /// How many jobs carry each type, counted once for the whole register.
    ///
    /// The type is not a column — a job is a handful of indexed fields and then
    /// its whole self as JSON — so this reads the payload. One pass, on a
    /// screen an Admin opens on purpose, rather than a query per type.
    /// </summary>
    private async Task<Dictionary<string, int>> CountByTypeAsync(CancellationToken token)
    {
        var payloads = await db.OperationJobs.AsNoTracking()
            .Select(job => job.Data)
            .ToListAsync(token);

        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var payload in payloads)
        {
            var record = JobRecord.From(payload);
            var type = (record?.Type ?? "").Trim();
            if (type.Length == 0) continue;
            counts[type] = counts.GetValueOrDefault(type) + 1;
        }
        return counts;
    }

    /// <summary>
    /// Fills an empty table from the rule file, once.
    ///
    /// Only when it is empty: after that the table is what the team decided,
    /// and re-adding a type they retired would undo their decision every time
    /// the API restarted.
    /// </summary>
    private async Task SeedAsync(CancellationToken token)
    {
        if (await db.VehicleTypes.AnyAsync(token)) return;

        var sort = 0;
        foreach (var vehicle in JobVehicleType.All)
        {
            sort += 10;
            db.VehicleTypes.Add(new VehicleTypeRow
            {
                Code = vehicle.Code,
                Label = vehicle.Label,
                Sort = sort,
                Active = true,
                UpdatedBy = "seed",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync(token);
    }
}
