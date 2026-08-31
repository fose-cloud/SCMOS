using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

/// <summary>One customer's assignment, as the rotation screen draws it.</summary>
public record RotationView(
    long Id, string Customer, string Sheet,
    bool Import, bool Export, bool Fcl, bool Lcl, bool Domestic,
    string PrimaryContact, string PrimaryEmail, string PrimaryId, string PrimaryName,
    string BackupContact, string BackupEmail, string BackupId, string BackupName,
    string Backup2Contact, string Backup2Email, string Backup2Id, string Backup2Name,
    string SubFcl, string SubLcl,
    IReadOnlyList<int> SubFclSupplierIds, IReadOnlyList<int> SubLclSupplierIds,
    string CsLcb,
    /// <summary>How many jobs in the register this customer currently has.</summary>
    int Jobs,
    /// <summary>
    /// Jobs of theirs that are assigned to somebody who is not the primary and
    /// not either backup. Zero is the normal answer.
    /// </summary>
    int Elsewhere);

/// <summary>An operator, and what the rotation says they carry.</summary>
public record RotationOwner(string Id, string Name, string Email, int Customers, int AsBackup);

public record RotationResult(bool Ok, string Message, int Added = 0, int Replaced = 0);

public record RotationPersonOption(string Id, string Name, string Email, bool Active);
public record RotationSupplierOption(
    int Id, string Code, string Name, string ServiceType, bool Fcl, bool Lcl);
public record RotationOptions(
    IReadOnlyList<RotationPersonOption> People,
    IReadOnlyList<RotationSupplierOption> Suppliers);

/// <summary>
/// A manual change to one row. Ids, not copied labels, are the input: the
/// service resolves people and carriers from their masters before storing it.
/// </summary>
public record RotationEdit(
    string Customer,
    bool Import, bool Export, bool Fcl, bool Lcl, bool Domestic,
    string PrimaryId, string BackupId, string Backup2Id,
    IReadOnlyList<int> SubFclSupplierIds, IReadOnlyList<int> SubLclSupplierIds,
    string CsLcb);

public record RotationMutationResult(bool Ok, string Message, long Id = 0);

/// <summary>
/// Who is responsible for which customer.
///
/// The register has always recorded which operator a job belongs to and never
/// what that ought to be. The team keeps that in a rotation workbook — one
/// sheet per operator, a customer on each row — and this is that workbook,
/// read back beside the jobs it is about. So "whose customer is this" and "is
/// this job with the right person" become the same question, answered from the
/// same page.
/// </summary>
public class RotationService(ScmosDbContext db, JobRegisterCache register)
{
    public async Task<IReadOnlyList<RotationView>> ListAsync(string? ownerId, string? customer,
        CancellationToken token)
    {
        var query = db.RotationAssignments.AsNoTracking();

        var wantedOwner = (ownerId ?? "").Trim();
        if (wantedOwner.Length > 0)
        {
            // Somebody's page shows what they hold and what they cover, because
            // covering is a real part of the job and a list that omits it
            // understates what a person is carrying.
            var email = await db.Staff.AsNoTracking()
                .Where(person => person.Id == wantedOwner)
                .Select(person => person.Email)
                .FirstOrDefaultAsync(token) ?? "";
            query = query.Where(row => row.PrimaryId == wantedOwner
                || (email != "" && (row.PrimaryEmail == email || row.BackupEmail == email
                    || row.Backup2Email == email)));
        }

        var wantedCustomer = (customer ?? "").Trim();
        if (wantedCustomer.Length > 0)
            query = query.Where(row => row.Customer.Contains(wantedCustomer));

        var rows = await query.OrderBy(row => row.Customer).ThenBy(row => row.Sheet).ToListAsync(token);
        if (rows.Count == 0) return [];

        // The directory, to turn the emails on the sheet into names and ids.
        var people = await db.Staff.AsNoTracking()
            .Select(person => new { person.Id, person.Name, person.Email })
            .ToListAsync(token);
        var byEmail = people
            .Where(person => person.Email.Length > 0)
            .GroupBy(person => person.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var byId = people.ToDictionary(person => person.Id, StringComparer.OrdinalIgnoreCase);
        var byName = people
            .Where(person => person.Name.Length > 0)
            .GroupBy(person => person.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        // Manual rows hold master names separated by semicolons. Imported rows
        // may still contain workbook text; an exact code/name match lets the
        // edit form select it without pretending an unmatched spelling is a
        // supplier id.
        var suppliers = await db.Suppliers.AsNoTracking()
            .Select(supplier => new { supplier.Id, supplier.Code, supplier.Name })
            .ToListAsync(token);
        var supplierIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var supplier in suppliers)
        {
            if (supplier.Code.Trim().Length > 0) supplierIds.TryAdd(supplier.Code.Trim(), supplier.Id);
            if (supplier.Name.Trim().Length > 0) supplierIds.TryAdd(supplier.Name.Trim(), supplier.Id);
        }

        // The jobs each of these customers currently holds, and who has them.
        var snapshot = await register.ReadAsync(token);
        var jobsByCustomer = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in snapshot.Rows)
        {
            var record = row.Record;
            if (record is null) continue;
            var name = (record.Customer ?? "").Trim();
            if (name.Length == 0) continue;
            if (!jobsByCustomer.TryGetValue(name, out var held))
            {
                held = [];
                jobsByCustomer[name] = held;
            }
            held.Add(record.OpId ?? "");
        }

        string IdFor(string email, string contact) =>
            email.Length > 0 && byEmail.TryGetValue(email, out var byAddress) ? byAddress.Id
            : contact.Length > 0 && byName.TryGetValue(contact, out var byContact) ? byContact.Id
            : "";

        string NameFor(string id, string email) =>
            id.Length > 0 && byId.TryGetValue(id, out var byDirectoryId) ? byDirectoryId.Name
            : email.Length > 0 && byEmail.TryGetValue(email, out var byAddress) ? byAddress.Name
            : "";

        return rows.Select(row =>
        {
            jobsByCustomer.TryGetValue(row.Customer.Trim(), out var owners);
            var allowed = new HashSet<string>(
                new[] { row.PrimaryId, IdFor(row.PrimaryEmail, row.PrimaryContact),
                        IdFor(row.BackupEmail, row.BackupContact),
                        IdFor(row.Backup2Email, row.Backup2Contact) }.Where(id => id.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            var backupId = IdFor(row.BackupEmail, row.BackupContact);
            var backup2Id = IdFor(row.Backup2Email, row.Backup2Contact);

            // An unassigned job is a visible problem elsewhere in the system and
            // not this screen's to raise, so it is not counted as misassigned.
            var elsewhere = owners is null || allowed.Count == 0
                ? 0
                : owners.Count(id => id.Length > 0 && !allowed.Contains(id));

            return new RotationView(
                row.Id, row.Customer, row.Sheet,
                row.Import, row.Export, row.Fcl, row.Lcl, row.Domestic,
                row.PrimaryContact, row.PrimaryEmail, row.PrimaryId, NameFor(row.PrimaryId, row.PrimaryEmail),
                row.BackupContact, row.BackupEmail, backupId, NameFor(backupId, row.BackupEmail),
                row.Backup2Contact, row.Backup2Email, backup2Id, NameFor(backup2Id, row.Backup2Email),
                row.SubFcl, row.SubLcl,
                CarrierIds(row.SubFcl, supplierIds), CarrierIds(row.SubLcl, supplierIds),
                row.CsLcb,
                owners?.Count ?? 0, elsewhere);
        }).ToList();
    }

    /// <summary>
    /// The two masters a manual rotation edit is allowed to choose from.
    /// Suspended/rejected suppliers stay visible in existing rows but are not
    /// offered for new work.
    /// </summary>
    public async Task<RotationOptions> OptionsAsync(CancellationToken token)
    {
        var people = await db.Staff.AsNoTracking()
            .OrderByDescending(person => person.Active).ThenBy(person => person.Name)
            .Select(person => new RotationPersonOption(
                person.Id, person.Name, person.Email, person.Active))
            .ToListAsync(token);

        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(supplier => supplier.Status != "suspended" && supplier.Status != "rejected")
            .OrderBy(supplier => supplier.Name)
            .Select(supplier => new
            {
                supplier.Id, supplier.Code, supplier.Name, supplier.ServiceType
            })
            .ToListAsync(token);

        return new RotationOptions(
            people,
            suppliers.Select(supplier => new RotationSupplierOption(
                supplier.Id, supplier.Code, supplier.Name, supplier.ServiceType,
                Supports(supplier.ServiceType, "FCL"),
                Supports(supplier.ServiceType, "LCL"))).ToList());
    }

    /// <summary>Everyone the rotation names, with how much each is holding.</summary>
    public async Task<IReadOnlyList<RotationOwner>> OwnersAsync(CancellationToken token)
    {
        var rows = await db.RotationAssignments.AsNoTracking()
            .Select(row => new { row.PrimaryEmail, row.PrimaryId, row.BackupEmail, row.Backup2Email })
            .ToListAsync(token);

        var people = await db.Staff.AsNoTracking()
            .Select(person => new { person.Id, person.Name, person.Email })
            .ToListAsync(token);

        var owners = new Dictionary<string, RotationOwner>(StringComparer.OrdinalIgnoreCase);

        foreach (var person in people)
        {
            if (person.Email.Length == 0) continue;
            var held = rows.Count(row => Same(row.PrimaryEmail, person.Email));
            var covers = rows.Count(row => Same(row.BackupEmail, person.Email)
                || Same(row.Backup2Email, person.Email));
            if (held == 0 && covers == 0) continue;
            owners[person.Email] = new RotationOwner(person.Id, person.Name, person.Email, held, covers);
        }

        // Anybody the sheet names that the directory has never heard of. Shown
        // rather than dropped: a rotation naming somebody who cannot sign in is
        // a thing worth seeing, not a row to quietly discard.
        foreach (var email in rows.Select(row => row.PrimaryEmail)
                     .Where(email => email.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (owners.ContainsKey(email)) continue;
            owners[email] = new RotationOwner("", email, email,
                rows.Count(row => Same(row.PrimaryEmail, email)),
                rows.Count(row => Same(row.BackupEmail, email) || Same(row.Backup2Email, email)));
        }

        return owners.Values.OrderByDescending(owner => owner.Customers).ThenBy(owner => owner.Name).ToList();
    }

    private static bool Same(string a, string b) =>
        a.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Replaces the rotation with what a workbook says.
    ///
    /// Replaces rather than merges, deliberately. This is one document that is
    /// reissued whole when the team reshuffles — the file even carries the date
    /// in its name — and merging two editions would leave customers assigned to
    /// somebody who has not held them for months, with nothing on screen saying
    /// which edition a row came from. The whole table goes and the new one lands
    /// in the same transaction, so a failed import leaves the old rotation
    /// standing rather than none at all.
    /// </summary>
    public async Task<RotationResult> ReplaceAsync(IReadOnlyList<RotationAssignment> rows,
        string by, CancellationToken token)
    {
        if (rows.Count == 0) return new RotationResult(false, "ไม่พบข้อมูลในไฟล์");

        // Emails to directory ids, so a row knows who it means.
        var directory = await db.Staff.AsNoTracking()
            .Where(person => person.Email != "")
            .Select(person => new { person.Id, person.Email })
            .ToListAsync(token);
        var ids = directory
            .GroupBy(person => person.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id, StringComparer.OrdinalIgnoreCase);

        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            row.PrimaryId = row.PrimaryEmail.Length > 0 && ids.TryGetValue(row.PrimaryEmail, out var id)
                ? id : "";
            row.UpdatedBy = by;
            row.UpdatedAt = now;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        var replaced = 0;
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(token);
            replaced = await db.RotationAssignments.ExecuteDeleteAsync(token);
            db.RotationAssignments.AddRange(rows);
            await db.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        });

        return new RotationResult(true, $"นำเข้า {rows.Count} รายการ", rows.Count, replaced);
    }

    public async Task<RotationMutationResult> CreateAsync(RotationEdit edit, string by,
        CancellationToken token)
    {
        var prepared = await PrepareAsync(edit, by, token);
        if (prepared.Row is null) return new RotationMutationResult(false, prepared.Error);

        db.RotationAssignments.Add(prepared.Row);
        await db.SaveChangesAsync(token);
        return new RotationMutationResult(true,
            $"เพิ่มลูกค้า {prepared.Row.Customer} ใน Job Rotation แล้ว", prepared.Row.Id);
    }

    public async Task<RotationMutationResult> UpdateAsync(long id, RotationEdit edit, string by,
        CancellationToken token)
    {
        var current = await db.RotationAssignments.FirstOrDefaultAsync(row => row.Id == id, token);
        if (current is null) return new RotationMutationResult(false, "ไม่พบรายการ Job Rotation นี้");

        var prepared = await PrepareAsync(edit, by, token);
        if (prepared.Row is null) return new RotationMutationResult(false, prepared.Error);

        Apply(current, prepared.Row);
        await db.SaveChangesAsync(token);
        return new RotationMutationResult(true,
            $"บันทึกลูกค้า {current.Customer} แล้ว", current.Id);
    }

    public async Task<RotationMutationResult> DeleteAsync(long id, CancellationToken token)
    {
        var current = await db.RotationAssignments.FirstOrDefaultAsync(row => row.Id == id, token);
        if (current is null) return new RotationMutationResult(false, "ไม่พบรายการ Job Rotation นี้");

        db.RotationAssignments.Remove(current);
        await db.SaveChangesAsync(token);
        return new RotationMutationResult(true,
            $"ลบลูกค้า {current.Customer} ออกจาก Job Rotation แล้ว", current.Id);
    }

    private async Task<(RotationAssignment? Row, string Error)> PrepareAsync(
        RotationEdit edit, string by, CancellationToken token)
    {
        var customer = Clean(edit.Customer, 200);
        if (customer.Length == 0) return (null, "ต้องระบุชื่อลูกค้า");

        var personIds = new[] { edit.PrimaryId, edit.BackupId, edit.Backup2Id }
            .Select(id => (id ?? "").Trim()).Where(id => id.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (edit.PrimaryId.Trim().Length == 0) return (null, "ต้องเลือกผู้รับผิดชอบหลัก");
        if (personIds.Count != new[] { edit.PrimaryId, edit.BackupId, edit.Backup2Id }
                .Count(id => !string.IsNullOrWhiteSpace(id)))
            return (null, "ผู้รับผิดชอบหลักและสำรองต้องไม่ซ้ำกัน");

        var people = await db.Staff.AsNoTracking()
            .Where(person => personIds.Contains(person.Id))
            .ToDictionaryAsync(person => person.Id, StringComparer.OrdinalIgnoreCase, token);
        if (people.Count != personIds.Count)
            return (null, "ไม่พบผู้รับผิดชอบที่เลือกในทะเบียนพนักงาน");
        if (personIds.Any(id => !people[id].Active))
            return (null, "เลือกผู้รับผิดชอบที่ปิดใช้งานแล้วไม่ได้");

        var fclIds = (edit.SubFclSupplierIds ?? []).Distinct().ToList();
        var lclIds = (edit.SubLclSupplierIds ?? []).Distinct().ToList();
        var carrierIds = fclIds.Concat(lclIds).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking()
            .Where(supplier => carrierIds.Contains(supplier.Id))
            .ToDictionaryAsync(supplier => supplier.Id, token);
        if (suppliers.Count != carrierIds.Count)
            return (null, "ไม่พบผู้ขนส่งที่เลือกใน Subcontractor Master");
        if (suppliers.Values.Any(supplier =>
                supplier.Status is "suspended" or "rejected"))
            return (null, "เลือกผู้ขนส่งที่ถูกระงับหรือไม่ผ่านไม่ได้");
        if (fclIds.Any(id => !Supports(suppliers[id].ServiceType, "FCL")))
            return (null, "มีผู้ขนส่งที่ไม่ได้ระบุบริการ FCL ใน Subcontractor Master");
        if (lclIds.Any(id => !Supports(suppliers[id].ServiceType, "LCL")))
            return (null, "มีผู้ขนส่งที่ไม่ได้ระบุบริการ LCL ใน Subcontractor Master");

        var primary = people[edit.PrimaryId.Trim()];
        people.TryGetValue(edit.BackupId.Trim(), out var backup);
        people.TryGetValue(edit.Backup2Id.Trim(), out var backup2);
        var now = DateTimeOffset.UtcNow;

        return (new RotationAssignment
        {
            Customer = customer,
            Sheet = "Manual",
            Import = edit.Import,
            Export = edit.Export,
            Fcl = edit.Fcl || fclIds.Count > 0,
            Lcl = edit.Lcl || lclIds.Count > 0,
            Domestic = edit.Domestic,
            PrimaryId = primary.Id,
            PrimaryEmail = primary.Email,
            PrimaryContact = ContactFor(primary),
            BackupEmail = backup?.Email ?? "",
            BackupContact = backup is null ? "" : ContactFor(backup),
            Backup2Email = backup2?.Email ?? "",
            Backup2Contact = backup2 is null ? "" : ContactFor(backup2),
            SubFcl = CarrierText(fclIds, suppliers),
            SubLcl = CarrierText(lclIds, suppliers),
            CsLcb = Clean(edit.CsLcb, 400),
            UpdatedBy = by,
            UpdatedAt = now,
        }, "");
    }

    private static void Apply(RotationAssignment target, RotationAssignment source)
    {
        target.Customer = source.Customer;
        // Keep the workbook sheet as provenance when an imported row is
        // corrected manually; a new row already says Manual.
        if (target.Sheet.Length == 0) target.Sheet = source.Sheet;
        target.Import = source.Import;
        target.Export = source.Export;
        target.Fcl = source.Fcl;
        target.Lcl = source.Lcl;
        target.Domestic = source.Domestic;
        target.PrimaryContact = source.PrimaryContact;
        target.PrimaryEmail = source.PrimaryEmail;
        target.PrimaryId = source.PrimaryId;
        target.BackupContact = source.BackupContact;
        target.BackupEmail = source.BackupEmail;
        target.Backup2Contact = source.Backup2Contact;
        target.Backup2Email = source.Backup2Email;
        target.SubFcl = source.SubFcl;
        target.SubLcl = source.SubLcl;
        target.CsLcb = source.CsLcb;
        target.UpdatedBy = source.UpdatedBy;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static string ContactFor(StaffMember person) =>
        person.Email.Trim().Length > 0 ? person.Email.Trim() : person.Name.Trim();

    private static string CarrierText(
        IReadOnlyList<int> ids, IReadOnlyDictionary<int, Supplier> suppliers) =>
        string.Join("; ", ids.Where(suppliers.ContainsKey).Select(id => suppliers[id].Name.Trim()));

    private static IReadOnlyList<int> CarrierIds(
        string value, IReadOnlyDictionary<string, int> suppliers)
    {
        var text = value.Trim();
        if (text.Length == 0) return [];
        if (suppliers.TryGetValue(text, out var exact)) return [exact];
        return text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(name => suppliers.TryGetValue(name, out var id) ? id : 0)
            .Where(id => id > 0).Distinct().ToList();
    }

    private static bool Supports(string serviceType, string mode) =>
        serviceType.Trim().Length == 0
        || serviceType.Contains(mode, StringComparison.OrdinalIgnoreCase);

    private static string Clean(string? value, int max)
    {
        var trimmed = (value ?? "").Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
