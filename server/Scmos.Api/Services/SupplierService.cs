using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record SupplierSummary(
    int Id, string Code, string Name, string Status, string ServiceType, string ServiceArea,
    bool DgCapable, bool ReeferCapable, bool IsoTankCapable, bool GpsEquipped,
    int Jobs, int Lanes, int Trucks, int Drivers,
    int? LastScore, string LastEvaluatedPeriod,
    IReadOnlyList<string> Aliases, int ExpiringDocuments);

public record SupplierProfileView(
    SupplierSummary Summary,
    IReadOnlyList<SupplierContact> Contacts,
    IReadOnlyList<SupplierDocument> Documents,
    IReadOnlyList<SupplierTruck> Trucks,
    IReadOnlyList<SupplierDriver> Drivers,
    IReadOnlyList<SupplierEvaluation> Evaluations,
    IReadOnlyList<SupplierCapacity> Capacity,
    /// <summary>On-time, confirmation and delay figures for this carrier only.</summary>
    SupplierScore? Performance,
    IReadOnlyList<IncidentCase> Incidents);

public record SupplierResult(bool Ok, string Message, int? Id = null);

/// <summary>
/// The supplier register.
///
/// One row per company, with every spelling anyone has typed pointing at it.
/// That reconciliation is what makes the rest possible: a supplier profile can
/// gather the jobs, the rates, the incidents and the score for a company that
/// the register calls three different things.
/// </summary>
public class SupplierService(ScmosDbContext db, KpiEngine kpi)
{
    /// <summary>The supplier a spelling means, or null when nobody has said.</summary>
    public async Task<int?> ResolveAsync(string spelling, CancellationToken token)
    {
        var key = (spelling ?? "").Trim().ToUpperInvariant();
        if (key.Length == 0) return null;
        var alias = await db.SupplierAliases.AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Alias == key, token);
        return alias?.SupplierId;
    }

    public async Task<IReadOnlyList<SupplierSummary>> ListAsync(string? status, string? query, CancellationToken token)
    {
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(supplier => supplier.Name).ToListAsync(token);
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            suppliers = suppliers.Where(supplier => supplier.Status == status).ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var wanted = query.Trim().ToLowerInvariant();
            suppliers = suppliers.Where(supplier =>
                supplier.Name.ToLowerInvariant().Contains(wanted) ||
                supplier.Code.ToLowerInvariant().Contains(wanted)).ToList();
        }

        var ids = suppliers.Select(supplier => supplier.Id).ToHashSet();
        var aliases = await db.SupplierAliases.AsNoTracking()
            .Where(alias => ids.Contains(alias.SupplierId)).ToListAsync(token);

        // Jobs are counted through the aliases, which is the entire point of
        // having them: TATIYAPOL's jobs and TATIYAPON's jobs are one company's
        // jobs once somebody has said the two spellings mean the same firm.
        var aliasToSupplier = aliases.ToDictionary(alias => alias.Alias, alias => alias.SupplierId);
        var jobCounts = new Dictionary<int, int>();
        foreach (var carrier in await db.OperationJobs.AsNoTracking()
                     .Where(job => job.Trucker != "").Select(job => job.Trucker).ToListAsync(token))
        {
            if (!aliasToSupplier.TryGetValue(carrier.Trim().ToUpperInvariant(), out var id)) continue;
            jobCounts[id] = jobCounts.GetValueOrDefault(id) + 1;
        }

        var laneCounts = await db.RateLanes.AsNoTracking()
            .Where(lane => lane.SupplierId != null)
            .GroupBy(lane => lane.SupplierId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var truckCounts = await db.SupplierTrucks.AsNoTracking()
            .GroupBy(truck => truck.SupplierId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var driverCounts = await db.SupplierDrivers.AsNoTracking()
            .GroupBy(driver => driver.SupplierId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var documents = await db.SupplierDocuments.AsNoTracking().ToListAsync(token);
        var soon = Formats.DateNumber(DateTimeOffset.Now.AddDays(60).ToString("dd/MM/yyyy"));

        return suppliers.Select(supplier => new SupplierSummary(
            supplier.Id, supplier.Code, supplier.Name, supplier.Status,
            supplier.ServiceType, supplier.ServiceArea,
            supplier.DgCapable, supplier.ReeferCapable, supplier.IsoTankCapable, supplier.GpsEquipped,
            jobCounts.GetValueOrDefault(supplier.Id),
            laneCounts.GetValueOrDefault(supplier.Id),
            truckCounts.GetValueOrDefault(supplier.Id),
            driverCounts.GetValueOrDefault(supplier.Id),
            supplier.LastScore, supplier.LastEvaluatedPeriod,
            aliases.Where(alias => alias.SupplierId == supplier.Id)
                .Select(alias => alias.Alias).OrderBy(name => name).ToList(),
            documents.Count(document => document.SupplierId == supplier.Id
                && Formats.DateNumber(document.ExpiryDate) > 0
                && Formats.DateNumber(document.ExpiryDate) <= soon))).ToList();
    }

    public async Task<SupplierProfileView?> ProfileAsync(int id, CancellationToken token)
    {
        var summaries = await ListAsync(null, null, token);
        var summary = summaries.FirstOrDefault(entry => entry.Id == id);
        if (summary is null) return null;

        var report = await kpi.BuildAsync(Period.All, token);
        var aliases = summary.Aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var performance = report.Suppliers.FirstOrDefault(score => aliases.Contains(score.Carrier));

        return new SupplierProfileView(
            summary,
            await db.SupplierContacts.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            await db.SupplierDocuments.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            await db.SupplierTrucks.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            await db.SupplierDrivers.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            await db.SupplierEvaluations.AsNoTracking().Where(x => x.SupplierId == id)
                .OrderByDescending(x => x.Period).ToListAsync(token),
            await db.SupplierCapacities.AsNoTracking().Where(x => x.SupplierId == id)
                .OrderBy(x => x.Date).Take(60).ToListAsync(token),
            performance,
            await db.IncidentCases.AsNoTracking().Where(x => x.JobKey != "").Take(0).ToListAsync(token));
    }

    /// <summary>Registers a new vendor. The onboarding flow starts it as a draft.</summary>
    public async Task<SupplierResult> RegisterAsync(string name, string code, string serviceType,
        string serviceArea, string by, CancellationToken token)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return new SupplierResult(false, "ต้องระบุชื่อผู้ขนส่ง");

        var key = new string(trimmed.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var clash = await db.SupplierAliases.AsNoTracking()
            .FirstOrDefaultAsync(alias => alias.Alias == trimmed.ToUpperInvariant(), token);
        if (clash is not null) return new SupplierResult(false, "มีผู้ขนส่งชื่อนี้อยู่แล้ว");

        var supplier = new Supplier
        {
            Name = trimmed,
            Code = code.Trim().Length > 0 ? code.Trim().ToUpperInvariant() : key[..Math.Min(6, key.Length)],
            ServiceType = serviceType.Trim(),
            ServiceArea = serviceArea.Trim(),
            Status = "draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(token);

        db.SupplierAliases.Add(new SupplierAlias
        {
            SupplierId = supplier.Id, Alias = trimmed.ToUpperInvariant(), Source = "manual", Confirmed = true,
        });
        await db.SaveChangesAsync(token);

        return new SupplierResult(true, $"ลงทะเบียน {trimmed} แล้ว (สถานะ: ร่าง)", supplier.Id);
    }

    /// <summary>
    /// Moves a vendor through onboarding. Approval is the only transition that
    /// records who made it, because it is the one that lets work be given to
    /// this company.
    /// </summary>
    public async Task<SupplierResult> SetStatusAsync(int id, string status, string by, CancellationToken token)
    {
        var allowed = new[] { "draft", "pending-audit", "approved", "suspended", "rejected" };
        var wanted = status.Trim().ToLowerInvariant();
        if (!allowed.Contains(wanted))
            return new SupplierResult(false, "สถานะที่ใช้ได้: " + string.Join(", ", allowed));

        var supplier = await db.Suppliers.FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        supplier.Status = wanted;
        supplier.UpdatedAt = DateTimeOffset.UtcNow;
        if (wanted == "approved")
        {
            supplier.ApprovedAt = DateTimeOffset.UtcNow;
            supplier.ApprovedBy = by;
        }

        await db.SaveChangesAsync(token);
        return new SupplierResult(true, $"{supplier.Name}: {wanted}", supplier.Id);
    }

    /// <summary>Attaches a spelling to a supplier — how TTP gets merged into a company.</summary>
    public async Task<SupplierResult> LinkAliasAsync(int id, string alias, string by, CancellationToken token)
    {
        var key = alias.Trim().ToUpperInvariant();
        if (key.Length == 0) return new SupplierResult(false, "ต้องระบุชื่อที่จะผูก");

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var existing = await db.SupplierAliases.FirstOrDefaultAsync(entry => entry.Alias == key, token);
        if (existing is not null)
        {
            if (existing.SupplierId == id) return new SupplierResult(false, "ผูกไว้แล้ว");
            existing.SupplierId = id;
            existing.Confirmed = true;
            existing.Source = "manual";
        }
        else
        {
            db.SupplierAliases.Add(new SupplierAlias
            { SupplierId = id, Alias = key, Source = "manual", Confirmed = true });
        }

        // Rate lanes carrying that spelling now belong to this supplier too.
        var lanes = await db.RateLanes.Where(lane => lane.Carrier.ToUpper() == key).ToListAsync(token);
        foreach (var lane in lanes) lane.SupplierId = id;

        await db.SaveChangesAsync(token);
        return new SupplierResult(true, $"ผูก {key} เข้ากับ {supplier.Name} แล้ว ({lanes.Count} เส้นทางราคา)", id);
    }

    /// <summary>
    /// Records an annual evaluation.
    ///
    /// The operational scores come from the KPI engine so the meeting argues
    /// with measured figures rather than remembered ones; safety and document
    /// scores are the assessor's own.
    /// </summary>
    public async Task<SupplierResult> EvaluateAsync(int id, string period, int? safety, int? documents,
        string note, string by, CancellationToken token)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");
        if (period.Trim().Length == 0) return new SupplierResult(false, "ต้องระบุรอบการประเมิน");

        var profile = await ProfileAsync(id, token);
        var performance = profile?.Performance;

        var onTime = performance?.OnTime is null ? null : (int?)Math.Round(performance.OnTime.Value);
        var confirmation = performance?.Confirmation is null ? null : (int?)Math.Round(performance.Confirmation.Value);
        var delayFree = performance?.DelayFree is null ? null : (int?)Math.Round(performance.DelayFree.Value);

        var parts = new List<int>();
        if (onTime is not null) parts.Add(onTime.Value);
        if (confirmation is not null) parts.Add(confirmation.Value);
        if (delayFree is not null) parts.Add(delayFree.Value);
        if (safety is not null) parts.Add(safety.Value);
        if (documents is not null) parts.Add(documents.Value);

        var total = parts.Count == 0 ? (int?)null : (int)Math.Round(parts.Average());
        var grade = total is null ? "" : total >= 85 ? "A" : total >= 70 ? "B" : total >= 55 ? "C" : "D";

        var existing = await db.SupplierEvaluations
            .FirstOrDefaultAsync(entry => entry.SupplierId == id && entry.Period == period.Trim(), token);

        if (existing is null)
        {
            existing = new SupplierEvaluation
            { SupplierId = id, Period = period.Trim(), CreatedAt = DateTimeOffset.UtcNow };
            db.SupplierEvaluations.Add(existing);
        }

        existing.OnTimeScore = onTime;
        existing.ConfirmationScore = confirmation;
        existing.DelayScore = delayFree;
        existing.SafetyScore = safety;
        existing.DocumentScore = documents;
        existing.TotalScore = total;
        existing.Grade = grade;
        existing.Note = note.Trim();
        existing.Stage = "submitted";
        existing.EvaluatedBy = by;

        supplier.LastScore = total;
        supplier.LastEvaluatedPeriod = period.Trim();
        supplier.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(token);
        return new SupplierResult(true,
            total is null
                ? "บันทึกการประเมินแล้ว — ยังไม่มีข้อมูลพอให้คะแนน"
                : $"ประเมิน {supplier.Name} รอบ {period}: {total} คะแนน (เกรด {grade})",
            id);
    }
}
