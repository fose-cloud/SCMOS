using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record StaffView(
    string Id, string Email, string Name, string Account, string Role, bool Active, string Note,
    /// <summary>Jobs currently assigned to this person. Why deactivating is not deleting.</summary>
    int Jobs,
    IReadOnlyList<string> Can,
    string UpdatedBy, DateTimeOffset UpdatedAt);

public record StaffResult(bool Ok, string Message, string? Id = null);

/// <summary>
/// The staff directory, as data an administrator can edit.
///
/// It was a hardcoded array, so adding a colleague meant a code change and a
/// deployment. The rules it enforces are the ones that make handing this control
/// to a screen safe rather than merely convenient:
///
/// <b>The last administrator cannot be removed or demoted.</b> Nothing else in
/// the system can undo that mistake from inside — an organisation with no
/// administrator has no way back except an app setting and somebody who knows it
/// exists.
///
/// <b>Nobody changes their own role.</b> Not because it would be an escalation —
/// only an administrator can reach this at all — but because the realistic
/// accident is a demotion, and it locks the person out of the screen they would
/// need to fix it.
///
/// <b>Nobody is deleted.</b> A person who has left still owns the jobs they
/// worked; deleting the row orphans every one of them. Deactivating keeps the
/// history and stops the sign-in.
/// </summary>
public class StaffService(ScmosDbContext db)
{
    /// <summary>The directory as it was before it was a table. Seeded once.</summary>
    private static readonly (string Id, string Name, string Account, string Role)[] Original =
    [
        ("OP-01", "Watsana", "watsana", Roles.Operation),
        ("OP-02", "Uthai", "uthai", Roles.Operation),
        ("OP-03", "Ananya", "ananya", Roles.Operation),
        ("OP-04", "Maliwan", "maliwan", Roles.Operation),
        ("OP-05", "Jiratchaya", "jiratchaya", Roles.Operation),
        ("SV-01", "Titchanatorn", "titchanatorn", Roles.Supervisor),
        ("AM-01", "Nattikorn", "nattikorn", Roles.AssistantManager),
        ("AD-01", "Admin", "admin", Roles.Admin),
        ("CS-01", "Customerservice", "cs", Roles.CustomerService),
        ("MG-01", "Management", "management", Roles.Management),
        ("VW-01", "Viewer", "viewer", Roles.Viewer),
        ("SC-01", "Subcontractor", "subcontractor", Roles.Subcontractor),
    ];

    /// <summary>
    /// Fills an empty table with the directory the code used to hold, so an
    /// upgrade does not start with nobody in the system. Runs once: if there is
    /// a single row already, somebody has been administering this and the code's
    /// idea of the directory is out of date, not authoritative.
    /// </summary>
    public async Task SeedAsync(CancellationToken token)
    {
        if (await db.Staff.AnyAsync(token)) return;

        var now = DateTimeOffset.UtcNow;
        db.Staff.AddRange(Original.Select(person => new StaffMember
        {
            Id = person.Id, Name = person.Name, Account = person.Account, Role = person.Role,
            Email = "", Active = true,
            Note = "จากทะเบียนเดิมในโค้ด",
            CreatedBy = "system", CreatedAt = now, UpdatedBy = "system", UpdatedAt = now,
        }));
        await db.SaveChangesAsync(token);
    }

    public async Task<IReadOnlyList<StaffView>> ListAsync(CancellationToken token)
    {
        var people = await db.Staff.AsNoTracking().OrderBy(p => p.Id).ToListAsync(token);

        var jobs = await db.OperationJobs.AsNoTracking()
            .Where(job => job.OwnerId != "")
            .GroupBy(job => job.OwnerId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        return people.Select(person => Describe(person, jobs.GetValueOrDefault(person.Id))).ToList();
    }

    public static StaffView Describe(StaffMember person, int jobs) => new(
        person.Id, person.Email, person.Name, person.Account, person.Role, person.Active, person.Note,
        jobs,
        Enum.GetValues<Capability>()
            .Where(capability => capability != Capability.None && Roles.Can(person.Role, capability))
            .Select(capability => capability.ToString()).ToList(),
        person.UpdatedBy, person.UpdatedAt);

    /* ------------------------------------------------------------- lookups */

    /// <summary>Who a signed-in identity is, or null when nobody has added them.</summary>
    public async Task<StaffMember?> MatchAsync(string email, string displayName, CancellationToken token)
    {
        var people = await db.Staff.AsNoTracking().Where(p => p.Active).ToListAsync(token);
        return MatchIn(people, email, displayName);
    }

    /// <summary>
    /// The matching itself, over a list already in memory.
    ///
    /// Exact email first, because that is the only unambiguous key. Then the
    /// local part and the stem before its first dot — Entra introduces an
    /// operator as <c>watsana.k@…</c> while the plan calls her "Watsana", and
    /// without those two steps every operator loses every job on the first real
    /// sign-in. The display name is the last resort.
    /// </summary>
    public static StaffMember? MatchIn(IReadOnlyList<StaffMember> people, string email, string displayName)
    {
        var address = (email ?? "").Trim();

        var byEmail = people.FirstOrDefault(p =>
            p.Email.Length > 0 && string.Equals(p.Email, address, StringComparison.OrdinalIgnoreCase));
        if (byEmail is not null) return byEmail;

        var local = (address.Contains('@') ? address[..address.IndexOf('@')] : address).Trim().ToLowerInvariant();
        if (local.Length > 0)
        {
            var byAccount = people.FirstOrDefault(p =>
                p.Account.Length > 0 && string.Equals(p.Account, local, StringComparison.OrdinalIgnoreCase));
            if (byAccount is not null) return byAccount;

            var stem = local.Split('.')[0];
            var byStem = people.FirstOrDefault(p =>
                p.Account.Length > 0 && string.Equals(p.Account, stem, StringComparison.OrdinalIgnoreCase));
            if (byStem is not null) return byStem;
        }

        var first = (displayName ?? "").Trim()
            .Split([' '], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is null
            ? null
            : people.FirstOrDefault(p => string.Equals(p.Name, first, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The owner id for an operator name off a plan workbook.</summary>
    public async Task<string> IdForNameAsync(string? name, CancellationToken token)
    {
        var first = (name ?? "").Trim().Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (first is null) return "";
        var match = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Name == first, token);
        return match?.Id ?? "";
    }

    /* --------------------------------------------------------------- edits */

    public async Task<StaffResult> CreateAsync(string email, string name, string role, string note,
        AppUser by, CancellationToken token)
    {
        var address = email.Trim();
        var person = name.Trim();

        if (address.Length == 0) return new StaffResult(false, "ต้องระบุอีเมลที่ใช้ลงชื่อเข้าใช้");
        if (person.Length == 0) return new StaffResult(false, "ต้องระบุชื่อ");
        if (Roles.Find(role) is null)
            return new StaffResult(false, "บทบาทที่ใช้ได้: " + string.Join(", ", Roles.All.Select(r => r.Name)));

        if (await db.Staff.AnyAsync(p => p.Email == address, token))
            return new StaffResult(false, "อีเมลนี้มีอยู่ในทะเบียนแล้ว");

        var id = await NextIdAsync(role, token);
        var now = DateTimeOffset.UtcNow;

        db.Staff.Add(new StaffMember
        {
            Id = id, Email = address, Name = person, Account = "", Role = role,
            Active = true, Note = note.Trim(),
            CreatedBy = by.Signature, CreatedAt = now, UpdatedBy = by.Signature, UpdatedAt = now,
        });
        await db.SaveChangesAsync(token);

        return new StaffResult(true, $"เพิ่ม {person} ({id}) เป็น {role} แล้ว", id);
    }

    public async Task<StaffResult> UpdateAsync(string id, string? email, string? name, string? role,
        bool? active, string? note, AppUser by, CancellationToken token)
    {
        var person = await db.Staff.FirstOrDefaultAsync(p => p.Id == id, token);
        if (person is null) return new StaffResult(false, "ไม่พบบัญชีนี้");

        var self = string.Equals(person.Id, by.OperatorId, StringComparison.OrdinalIgnoreCase);

        if (role is not null && role != person.Role)
        {
            if (Roles.Find(role) is null)
                return new StaffResult(false, "บทบาทไม่ถูกต้อง");
            if (self)
                return new StaffResult(false, "เปลี่ยนบทบาทของตัวเองไม่ได้ — ให้ผู้ดูแลอีกคนเป็นคนเปลี่ยน");
            if (await WouldRemoveLastAdminAsync(person, role, person.Active, token))
                return new StaffResult(false, "นี่คือผู้ดูแลระบบคนสุดท้าย — ตั้งคนอื่นเป็นผู้ดูแลก่อน");
            person.Role = role;
        }

        if (active is not null && active != person.Active)
        {
            if (self && active == false)
                return new StaffResult(false, "ปิดบัญชีตัวเองไม่ได้");
            if (await WouldRemoveLastAdminAsync(person, person.Role, active.Value, token))
                return new StaffResult(false, "นี่คือผู้ดูแลระบบคนสุดท้าย — ตั้งคนอื่นเป็นผู้ดูแลก่อน");
            person.Active = active.Value;
        }

        if (email is not null)
        {
            var address = email.Trim();
            if (address.Length > 0 && await db.Staff.AnyAsync(p => p.Email == address && p.Id != id, token))
                return new StaffResult(false, "อีเมลนี้เป็นของบัญชีอื่นแล้ว");
            person.Email = address;
        }

        // The name is what the owner-id backfill matches on, so changing it is a
        // real decision rather than a label edit. It is allowed — a misspelling
        // has to be fixable — and the note says what it costs.
        if (name is not null && name.Trim().Length > 0) person.Name = name.Trim();
        if (note is not null) person.Note = note.Trim();

        person.UpdatedBy = by.Signature;
        person.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return new StaffResult(true, $"บันทึก {person.Name} แล้ว", person.Id);
    }

    /// <summary>
    /// Whether this change would leave nobody able to administer the system.
    /// Counted over active administrators other than the one being changed.
    /// </summary>
    private async Task<bool> WouldRemoveLastAdminAsync(StaffMember person, string role, bool active,
        CancellationToken token)
    {
        var wasAdmin = person.Active && Roles.Can(person.Role, Capability.AdministerData);
        if (!wasAdmin) return false;

        var willBeAdmin = active && Roles.Can(role, Capability.AdministerData);
        if (willBeAdmin) return false;

        var others = await db.Staff.AsNoTracking()
            .Where(p => p.Id != person.Id && p.Active)
            .Select(p => p.Role)
            .ToListAsync(token);

        return !others.Any(other => Roles.Can(other, Capability.AdministerData));
    }

    /// <summary>
    /// The next free id for a role's prefix. Ids are what jobs store, so they
    /// are never reused — the search starts above the highest one taken.
    /// </summary>
    private async Task<string> NextIdAsync(string role, CancellationToken token)
    {
        var prefix = role switch
        {
            Roles.Admin => "AD",
            Roles.Manager or Roles.AssistantManager => "AM",
            Roles.Supervisor => "SV",
            Roles.CustomerService => "CS",
            Roles.Management => "MG",
            Roles.Subcontractor => "SC",
            Roles.Viewer => "VW",
            _ => "OP",
        };

        var taken = await db.Staff.AsNoTracking()
            .Where(p => p.Id.StartsWith(prefix))
            .Select(p => p.Id)
            .ToListAsync(token);

        var highest = taken
            .Select(id => int.TryParse(id.Split('-').LastOrDefault(), out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}-{highest + 1:D2}";
    }
}
