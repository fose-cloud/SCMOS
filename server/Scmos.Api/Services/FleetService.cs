using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record FleetFileView(long Id, string FileName, string ExpiryDate, bool CanShow, string UploadedBy,
    DateTimeOffset UploadedAt);

/// <param name="State"><see cref="SupplierCompliance.State"/>: valid · expiring · expired · missing · no-expiry.</param>
/// <param name="Versions">How many files have been uploaded for it; the newest is <paramref name="File"/>.</param>
public record FleetPaperView(string Code, string English, string Thai, bool Expires, string State, int? DaysLeft,
    FleetFileView? File, int Versions);

public record FleetTruckView(int Id, int SupplierId, string Supplier, string Plate, string Kind, string VehicleType,
    bool DgCapable, string Status, string State, IReadOnlyList<FleetPaperView> Papers, string CreatedBy,
    DateTimeOffset? CreatedAt);

public record FleetDriverView(int Id, int SupplierId, string Supplier, string Name, string Phone, string LicenceNo,
    string LicenceExpiry, string Status, string State, IReadOnlyList<FleetPaperView> Papers, string CreatedBy,
    DateTimeOffset? CreatedAt);

public record FleetRequirementView(string Code, string English, string Thai, bool Expires);

public record FleetView(IReadOnlyList<FleetTruckView> Trucks, IReadOnlyList<FleetDriverView> Drivers,
    IReadOnlyList<FleetRequirementView> TruckPapers, IReadOnlyList<FleetRequirementView> DriverPapers,
    IReadOnlyList<string> VehicleTypes, bool StorageReady);

public record FleetResult(bool Ok, string Message, int Status = StatusCodes.Status200OK, int? Id = null);

public record TruckInput(string Plate, string Kind, string VehicleType, bool DgCapable);
public record DriverInput(string Name, string Phone, string LicenceNo);
public record PaperInput(string Code, IFormFile? File, string ExpiryDate);

/// <summary>
/// A carrier's registered trucks — heads and tails — and drivers, with the papers each must carry
/// (30 Sep 2026, Capacity Planning).
///
/// <para>
/// The carrier writes its own, through its portal; the department reads every carrier's on its own Capacity
/// screen. These are the rows a job's truck and driver are assigned from (<c>AssignResourcesForAsync</c>, the
/// TMS <c>/fleet</c>), which until now nothing on screen could add to.
/// </para>
///
/// <para>
/// Registration and its papers are one request. The row is written only once every required file is there
/// and every check has passed; nothing is deleted afterwards — a truck sold is set inactive, a renewed policy is
/// uploaded over the old one, and both the old row and the old file stay.
/// </para>
/// </summary>
public class FleetService(ScmosDbContext db, DocumentService documents, AuditService audit)
{
    public const string Active = "active";
    public const string Inactive = "inactive";

    public async Task<FleetView> ReadAsync(int? supplierId, CancellationToken token)
    {
        var truckRows = await db.SupplierTrucks.AsNoTracking()
            .Where(row => supplierId == null || row.SupplierId == supplierId).ToListAsync(token);
        var driverRows = await db.SupplierDrivers.AsNoTracking()
            .Where(row => supplierId == null || row.SupplierId == supplierId).ToListAsync(token);
        var supplierIds = truckRows.Select(row => row.SupplierId).Concat(driverRows.Select(row => row.SupplierId))
            .Distinct().ToList();
        var names = await db.Suppliers.AsNoTracking().Where(row => supplierIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, row => row.Name, token);

        var truckIds = truckRows.Select(row => row.Id).ToList();
        var driverIds = driverRows.Select(row => row.Id).ToList();
        var files = await db.Documents.AsNoTracking()
            .Where(row => (row.TruckId != null && truckIds.Contains(row.TruckId.Value))
                || (row.FleetDriverId != null && driverIds.Contains(row.FleetDriverId.Value)))
            .ToListAsync(token);
        var today = Formats.DateNumber(Formats.Now.ToString("dd/MM/yyyy"));

        var trucks = truckRows.Select(row =>
        {
            var papers = Papers(FleetDocuments.Truck, files.Where(file => file.TruckId == row.Id).ToList(), today);
            return new FleetTruckView(row.Id, row.SupplierId, names.GetValueOrDefault(row.SupplierId, ""), row.Plate,
                row.Kind, row.VehicleType, row.DgCapable, row.Status,
                SupplierCompliance.Worst(papers.Select(paper => paper.State)), papers, row.CreatedBy, row.CreatedAt);
        })
            .OrderBy(row => row.Status == Active ? 0 : 1).ThenBy(row => row.Supplier)
            .ThenBy(row => row.Kind == FleetDocuments.Head ? 0 : 1).ThenBy(row => row.Plate).ToList();
        var drivers = driverRows.Select(row =>
        {
            var papers = Papers(FleetDocuments.Driver, files.Where(file => file.FleetDriverId == row.Id).ToList(), today);
            return new FleetDriverView(row.Id, row.SupplierId, names.GetValueOrDefault(row.SupplierId, ""), row.Name,
                row.Phone, row.LicenceNo, row.LicenceExpiry, row.Status,
                SupplierCompliance.Worst(papers.Select(paper => paper.State)), papers, row.CreatedBy, row.CreatedAt);
        })
            .OrderBy(row => row.Status == Active ? 0 : 1).ThenBy(row => row.Supplier).ThenBy(row => row.Name).ToList();

        return new FleetView(trucks, drivers, Requirements(FleetDocuments.Truck), Requirements(FleetDocuments.Driver),
            CapacityService.VehicleTypes, documents.StorageReady);
    }

    /// <summary>Registers a head or a tail with its registration book and both insurances, for this carrier.</summary>
    public async Task<FleetResult> AddTruckAsync(AppUser user, Supplier company, TruckInput input,
        IReadOnlyList<PaperInput> papers, CancellationToken token)
    {
        var plate = FleetDocuments.Tidy(input.Plate);
        var kind = (input.Kind ?? "").Trim().ToLowerInvariant();
        var vehicle = (input.VehicleType ?? "").Trim().ToUpperInvariant();
        if (FleetDocuments.Key(plate).Length < 2) return Refused("ต้องระบุทะเบียนรถ");
        if (plate.Length > 60) return Refused("ทะเบียนรถยาวเกิน 60 ตัวอักษร");
        if (!FleetDocuments.Kinds.Contains(kind)) return Refused("ต้องเลือกว่าเป็นหัวหรือหาง");
        if (!CapacityService.VehicleTypes.Contains(vehicle))
            return Refused("ประเภทรถที่ใช้ได้: " + string.Join(", ", CapacityService.VehicleTypes));
        var problem = CheckPapers(FleetDocuments.Truck, papers);
        if (problem is not null) return problem;

        var clash = await TruckClashAsync(company.Id, plate, null, token);
        if (clash is not null) return Refused(clash, StatusCodes.Status409Conflict);

        var truck = new SupplierTruck
        {
            SupplierId = company.Id, Plate = plate, Kind = kind, VehicleType = vehicle, DgCapable = input.DgCapable,
            Status = Active, CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SupplierTrucks.Add(truck);
        await db.SaveChangesAsync(token);
        var failed = await StorePapersAsync(user, company, truck, null, FleetDocuments.Truck, papers, token);
        await audit.RecordAsync(user, AuditActions.Register, "fleet-truck", truck.Id.ToString(), plate, "truck", "",
            $"{(kind == FleetDocuments.Head ? "หัว" : "หาง")} · {vehicle}{(input.DgCapable ? " · DG" : "")}",
            $"{company.Name} ลงทะเบียนรถเอง", token);
        return failed.Count == 0
            ? new FleetResult(true, $"ลงทะเบียน{(kind == FleetDocuments.Head ? "หัว" : "หาง")} {plate} พร้อมเอกสารครบแล้ว", Id: truck.Id)
            : new FleetResult(true, $"ลงทะเบียน {plate} แล้ว แต่อัปโหลดไม่สำเร็จ: {string.Join(", ", failed)} — อัปโหลดใหม่ได้ที่แถวของรถ", Id: truck.Id);
    }

    /// <summary>Registers a driver with the driving licence, for this carrier.</summary>
    public async Task<FleetResult> AddDriverAsync(AppUser user, Supplier company, DriverInput input,
        IReadOnlyList<PaperInput> papers, CancellationToken token)
    {
        var name = FleetDocuments.Tidy(input.Name);
        var phone = (input.Phone ?? "").Trim();
        var licence = FleetDocuments.Tidy(input.LicenceNo);
        if (name.Length == 0) return Refused("ต้องระบุชื่อพนักงานขับรถ");
        if (name.Length > 120) return Refused("ชื่อยาวเกิน 120 ตัวอักษร");
        if (FleetDocuments.Key(licence).Length < 4) return Refused("ต้องระบุเลขที่ใบขับขี่");
        if (licence.Length > 60) return Refused("เลขที่ใบขับขี่ยาวเกิน 60 ตัวอักษร");
        if (phone.Length > 40) return Refused("เบอร์โทรยาวเกิน 40 ตัวอักษร");
        var problem = CheckPapers(FleetDocuments.Driver, papers);
        if (problem is not null) return problem;

        var clash = await DriverClashAsync(company.Id, licence, null, token);
        if (clash is not null) return Refused(clash, StatusCodes.Status409Conflict);

        var driver = new SupplierDriver
        {
            SupplierId = company.Id, Name = name, Phone = phone, LicenceNo = licence,
            LicenceExpiry = LicenceExpiry(papers), Status = Active, CreatedBy = user.Signature,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.SupplierDrivers.Add(driver);
        await db.SaveChangesAsync(token);
        var failed = await StorePapersAsync(user, company, null, driver, FleetDocuments.Driver, papers, token);
        if (failed.Count > 0 && driver.LicenceExpiry.Length > 0)
        {
            // The date came with a file that did not land; the row says what is actually held.
            driver.LicenceExpiry = "";
            await db.SaveChangesAsync(token);
        }
        await audit.RecordAsync(user, AuditActions.Register, "fleet-driver", driver.Id.ToString(), name, "driver", "",
            $"ใบขับขี่ {licence}", $"{company.Name} ลงทะเบียนพนักงานขับรถเอง", token);
        return failed.Count == 0
            ? new FleetResult(true, $"ลงทะเบียน {name} พร้อมใบขับขี่แล้ว", Id: driver.Id)
            : new FleetResult(true, $"ลงทะเบียน {name} แล้ว แต่อัปโหลดใบขับขี่ไม่สำเร็จ — อัปโหลดใหม่ได้ที่แถวของพนักงาน", Id: driver.Id);
    }

    /// <summary>A newer file for one of a truck's or driver's papers — a renewed policy, a new licence.</summary>
    public async Task<FleetResult> ReplacePaperAsync(AppUser user, Supplier company, bool truck, int id,
        PaperInput paper, CancellationToken token)
    {
        var requirement = truck ? FleetDocuments.TruckRequirement(paper.Code) : FleetDocuments.DriverRequirement(paper.Code);
        if (requirement is null) return Refused("ไม่รู้จักเอกสารนี้");
        var problem = CheckPapers([requirement], [paper with { Code = requirement.Code }]);
        if (problem is not null) return problem;

        var truckRow = truck ? await db.SupplierTrucks.FirstOrDefaultAsync(row => row.Id == id && row.SupplierId == company.Id, token) : null;
        var driverRow = truck ? null : await db.SupplierDrivers.FirstOrDefaultAsync(row => row.Id == id && row.SupplierId == company.Id, token);
        if (truckRow is null && driverRow is null)
            return Refused(truck ? "ไม่พบรถคันนี้ในทะเบียนของบริษัท" : "ไม่พบพนักงานขับรถคนนี้ในทะเบียนของบริษัท", StatusCodes.Status404NotFound);

        var expiry = requirement.Expires ? (paper.ExpiryDate ?? "").Trim() : "";
        var stored = await documents.AddToFleetAsync(company, truckRow, driverRow, requirement.Code,
            expiry, paper.File!, user, token);
        if (!stored.Ok) return Refused(stored.Message, documents.StorageReady ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable);
        if (driverRow is not null && requirement.Code == "driver-licence")
        {
            driverRow.LicenceExpiry = expiry;
            await db.SaveChangesAsync(token);
        }
        await audit.RecordAsync(user, AuditActions.Upload, truck ? "fleet-truck" : "fleet-driver", id.ToString(),
            truckRow?.Plate ?? driverRow!.Name, requirement.Code, "", stored.Document!.ObjectKey,
            expiry.Length > 0 ? $"หมดอายุ {expiry}" : "", token);
        return new FleetResult(true, $"อัปโหลด{requirement.Thai}ของ {truckRow?.Plate ?? driverRow!.Name} แล้ว", Id: id);
    }

    /// <summary>Takes a truck or driver off the list jobs are assigned from, or puts it back. Nothing is deleted.</summary>
    public async Task<FleetResult> SetActiveAsync(AppUser user, Supplier company, bool truck, int id, bool active,
        CancellationToken token)
    {
        var truckRow = truck ? await db.SupplierTrucks.FirstOrDefaultAsync(row => row.Id == id && row.SupplierId == company.Id, token) : null;
        var driverRow = truck ? null : await db.SupplierDrivers.FirstOrDefaultAsync(row => row.Id == id && row.SupplierId == company.Id, token);
        if (truckRow is null && driverRow is null)
            return Refused(truck ? "ไม่พบรถคันนี้ในทะเบียนของบริษัท" : "ไม่พบพนักงานขับรถคนนี้ในทะเบียนของบริษัท", StatusCodes.Status404NotFound);
        var before = truckRow?.Status ?? driverRow!.Status;
        var wanted = active ? Active : Inactive;
        var label = truckRow?.Plate ?? driverRow!.Name;
        if (before == wanted) return new FleetResult(true, active ? $"{label} ใช้งานอยู่แล้ว" : $"{label} เลิกใช้อยู่แล้ว", Id: id);
        if (active)
        {
            var clash = truckRow is not null
                ? await TruckClashAsync(company.Id, truckRow.Plate, truckRow.Id, token)
                : await DriverClashAsync(company.Id, driverRow!.LicenceNo, driverRow.Id, token);
            if (clash is not null) return Refused(clash, StatusCodes.Status409Conflict);
        }
        if (truckRow is not null) truckRow.Status = wanted; else driverRow!.Status = wanted;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.StatusChange, truck ? "fleet-truck" : "fleet-driver", id.ToString(),
            label, "status", before, wanted, "", token);
        return new FleetResult(true, active ? $"นำ {label} กลับมาใช้งานแล้ว" : $"เลิกใช้ {label} แล้ว — ไม่แสดงในรายการจัดรถ", Id: id);
    }

    /* ------------------------------------------------------------------ helpers */

    private static FleetResult Refused(string message, int status = StatusCodes.Status400BadRequest) =>
        new(false, message, status);

    /// <summary>Every required paper present, not empty, not too large, and any date readable.</summary>
    private FleetResult? CheckPapers(IReadOnlyList<FleetDocuments.Requirement> required, IReadOnlyList<PaperInput> papers)
    {
        var missing = required.Where(need => papers.FirstOrDefault(paper => paper.Code == need.Code)?.File is not { Length: > 0 })
            .Select(need => need.Thai).ToList();
        if (missing.Count > 0) return Refused("ต้องแนบ " + string.Join(", ", missing));
        foreach (var need in required)
        {
            var paper = papers.First(one => one.Code == need.Code);
            if (paper.File!.Length > DocumentService.MaxBytes) return Refused($"{need.Thai}: ไฟล์ใหญ่เกิน 32 MB");
            var expiry = (paper.ExpiryDate ?? "").Trim();
            if (need.Expires && expiry.Length > 0 && Formats.DateNumber(expiry) == 0)
                return Refused($"{need.Thai}: วันหมดอายุต้องเป็นรูปแบบ DD/MM/YYYY");
        }
        return documents.StorageReady ? null
            : Refused("ยังไม่ได้ตั้งค่าที่เก็บไฟล์ — อัปโหลดเอกสารไม่ได้", StatusCodes.Status503ServiceUnavailable);
    }

    private async Task<List<string>> StorePapersAsync(AppUser user, Supplier company, SupplierTruck? truck,
        SupplierDriver? driver, IReadOnlyList<FleetDocuments.Requirement> required, IReadOnlyList<PaperInput> papers,
        CancellationToken token)
    {
        var failed = new List<string>();
        foreach (var need in required)
        {
            var paper = papers.First(one => one.Code == need.Code);
            try
            {
                var stored = await documents.AddToFleetAsync(company, truck, driver, need.Code,
                    need.Expires ? paper.ExpiryDate ?? "" : "", paper.File!, user, token);
                if (!stored.Ok) failed.Add(need.Thai);
            }
            catch (Exception) when (!token.IsCancellationRequested)
            {
                failed.Add(need.Thai);
            }
        }
        return failed;
    }

    private static string LicenceExpiry(IReadOnlyList<PaperInput> papers) =>
        (papers.FirstOrDefault(paper => paper.Code == "driver-licence")?.ExpiryDate ?? "").Trim();

    /// <summary>Why this plate cannot be active for this carrier, or null — one truck, one active row, anywhere.</summary>
    private async Task<string?> TruckClashAsync(int supplierId, string plate, int? except, CancellationToken token)
    {
        var key = FleetDocuments.Key(plate);
        var rows = (await db.SupplierTrucks.AsNoTracking().Where(row => row.Id != except)
                .Select(row => new { row.SupplierId, row.Plate, row.Status }).ToListAsync(token))
            .Where(row => FleetDocuments.Key(row.Plate) == key).ToList();
        if (rows.Any(row => row.Status == Active && row.SupplierId == supplierId)) return $"ทะเบียน {plate} อยู่ในรายการของบริษัทแล้ว";
        if (rows.Any(row => row.Status == Active)) return $"ทะเบียน {plate} ลงทะเบียนไว้กับผู้ขนส่งรายอื่น — ติดต่อ Leschaco";
        if (except is null && rows.Any(row => row.SupplierId == supplierId))
            return $"ทะเบียน {plate} เคยลงทะเบียนและเลิกใช้ไว้ — กด “กลับมาใช้” ที่แถวเดิม";
        return null;
    }

    private async Task<string?> DriverClashAsync(int supplierId, string licence, int? except, CancellationToken token)
    {
        var key = FleetDocuments.Key(licence);
        var rows = (await db.SupplierDrivers.AsNoTracking().Where(row => row.Id != except)
                .Select(row => new { row.SupplierId, row.LicenceNo, row.Status }).ToListAsync(token))
            .Where(row => FleetDocuments.Key(row.LicenceNo) == key).ToList();
        if (rows.Any(row => row.Status == Active && row.SupplierId == supplierId)) return $"ใบขับขี่ {licence} อยู่ในรายการของบริษัทแล้ว";
        if (rows.Any(row => row.Status == Active)) return $"ใบขับขี่ {licence} ลงทะเบียนไว้กับผู้ขนส่งรายอื่น — ติดต่อ Leschaco";
        if (except is null && rows.Any(row => row.SupplierId == supplierId))
            return $"ใบขับขี่ {licence} เคยลงทะเบียนและเลิกใช้ไว้ — กด “กลับมาใช้” ที่แถวเดิม";
        return null;
    }

    private static List<FleetPaperView> Papers(IReadOnlyList<FleetDocuments.Requirement> required,
        IReadOnlyList<StoredDocument> files, int today) =>
        required.Select(need =>
        {
            var mine = files.Where(file => file.Kind == need.Code).OrderByDescending(file => file.Id).ToList();
            var current = mine.FirstOrDefault();
            var due = current is null ? 0 : Formats.DateNumber(current.ExpiryDate);
            return new FleetPaperView(need.Code, need.English, need.Thai, need.Expires,
                SupplierCompliance.StateOf(current is not null, current?.ExpiryDate ?? "", today, need.Expires),
                need.Expires && due > 0 ? SupplierCompliance.DaysBetween(today, due) : null,
                current is null ? null : new FleetFileView(current.Id, current.FileName, current.ExpiryDate,
                    InlineViewing.CanShow(current.FileName), current.UploadedBy, current.UploadedAt),
                mine.Count);
        }).ToList();

    private static IReadOnlyList<FleetRequirementView> Requirements(IReadOnlyList<FleetDocuments.Requirement> required) =>
        required.Select(need => new FleetRequirementView(need.Code, need.English, need.Thai, need.Expires)).ToList();
}
