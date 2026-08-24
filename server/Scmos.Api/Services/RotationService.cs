using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;

namespace Scmos.Api.Services;

/// <summary>One customer's assignment, as the rotation screen draws it.</summary>
public record RotationView(
    long Id, string Customer, string Sheet,
    bool Import, bool Export, bool Fcl, bool Lcl, bool Domestic,
    string PrimaryContact, string PrimaryEmail, string PrimaryId, string PrimaryName,
    string BackupContact, string BackupEmail, string BackupName,
    string Backup2Contact, string Backup2Email, string Backup2Name,
    string SubFcl, string SubLcl, string CsLcb,
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

        string NameFor(string email) =>
            email.Length > 0 && byEmail.TryGetValue(email, out var person) ? person.Name : "";

        string IdForEmail(string email) =>
            email.Length > 0 && byEmail.TryGetValue(email, out var person) ? person.Id : "";

        return rows.Select(row =>
        {
            jobsByCustomer.TryGetValue(row.Customer.Trim(), out var owners);
            var allowed = new HashSet<string>(
                new[] { row.PrimaryId, IdForEmail(row.PrimaryEmail), IdForEmail(row.BackupEmail),
                        IdForEmail(row.Backup2Email) }.Where(id => id.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            // An unassigned job is a visible problem elsewhere in the system and
            // not this screen's to raise, so it is not counted as misassigned.
            var elsewhere = owners is null || allowed.Count == 0
                ? 0
                : owners.Count(id => id.Length > 0 && !allowed.Contains(id));

            return new RotationView(
                row.Id, row.Customer, row.Sheet,
                row.Import, row.Export, row.Fcl, row.Lcl, row.Domestic,
                row.PrimaryContact, row.PrimaryEmail, row.PrimaryId, NameFor(row.PrimaryEmail),
                row.BackupContact, row.BackupEmail, NameFor(row.BackupEmail),
                row.Backup2Contact, row.Backup2Email, NameFor(row.Backup2Email),
                row.SubFcl, row.SubLcl, row.CsLcb,
                owners?.Count ?? 0, elsewhere);
        }).ToList();
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
}
