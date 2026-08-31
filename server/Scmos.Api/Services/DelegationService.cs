using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>
/// Whose work somebody may edit besides their own, today.
///
/// One question, asked in one place, so the screen and the API cannot disagree
/// about it. The browser asks it to decide which rows to make editable; the
/// API asks it before accepting a write. The last time an edit rule lived in
/// both, they said different things for a week.
/// </summary>
public class DelegationService(ScmosDbContext db)
{
    public record Grant(
        long Id, string OwnerId, string OwnerName, string DelegateId, string DelegateName,
        string FromDate, string ToDate, string Reason, string Status, string CreatedBy);

    public record Result(bool Ok, string Message, long Id = 0);

    /// <param name="Id">The staff id a grant is written against.</param>
    public record Candidate(string Id, string Name, string Role);

    /// <summary>
    /// The longest a grant may run.
    ///
    /// Both dates were already required, which stops a grant with no end. It
    /// does not stop 31/12/2099, which is the same thing typed differently —
    /// and the person who sets one will not be the one who remembers to remove
    /// it. Ninety days covers any leave anybody here takes; longer than that is
    /// a change of who owns the work, and should be made as one.
    /// </summary>
    public const int MaxDays = 90;

    /// <summary>
    /// Why a grant cannot be made, or null when it can.
    ///
    /// Every limit in one place, taking the dates and the grants already on
    /// file rather than reading a clock or a database, so the rules can be
    /// checked without either. `--check-delegation` runs them.
    /// </summary>
    public static string? WhyRefused(
        DateOnly from, DateOnly to, DateOnly today,
        string delegateId, IEnumerable<JobDelegation> existing)
    {
        if (to < from) return "วันสิ้นสุดต้องไม่อยู่ก่อนวันเริ่ม";

        // Backdating writes a permission that was not in force at the time it
        // appears to cover. Read months later it is indistinguishable from
        // somebody arranging an alibi for edits already made.
        if (from < today) return "เริ่มย้อนหลังไม่ได้ — วันเริ่มต้องเป็นวันนี้หรือหลังจากนี้";

        var days = to.DayNumber - from.DayNumber + 1;
        if (days > MaxDays)
            return $"มอบได้ครั้งละไม่เกิน {MaxDays} วัน (ที่ขอมา {days} วัน) — ถ้ายาวกว่านี้ควรเปลี่ยนผู้รับผิดชอบงานแทน";

        // Two live grants to the same person over the same days are one
        // arrangement entered twice. Two people covering different halves of a
        // leave is a real thing, so only the same delegate is refused.
        var clash = existing.FirstOrDefault(grant =>
            !grant.Revoked
            && string.Equals(grant.DelegateId, delegateId, StringComparison.OrdinalIgnoreCase)
            && Formats.ParseDay(grant.FromDate) is DateOnly start
            && Formats.ParseDay(grant.ToDate) is DateOnly end
            && start <= to && from <= end);

        return clash is null
            ? null
            : $"มีการมอบสิทธิ์ให้คนนี้อยู่แล้วในช่วง {clash.FromDate} – {clash.ToDate}";
    }

    /// <summary>
    /// The roles that may be handed somebody else's register work: the
    /// operations line, and the two above it who cover for it.
    ///
    /// Named rather than excluded. Listing who may not — the carrier's account,
    /// the CS account, the administrator — leaves every role added afterwards
    /// allowed by default, and nobody notices until a Viewer appears in the
    /// dropdown. This way a new role is refused until somebody decides it
    /// belongs here.
    ///
    /// Administrator, Management, CS and Viewer are deliberately absent.
    /// Administrator and Management have the rights but not the work; CS and
    /// Viewer have neither. None of them covers an operator's leave.
    /// </summary>
    /// <summary>
    /// Whether somebody may arrange cover for a colleague rather than for
    /// themselves.
    ///
    /// Tied to AssignJobs — handing one job to another operator and handing a
    /// week of them are the same authority at different sizes, so this does not
    /// invent a second answer to who may do it.
    ///
    /// It is not an escalation. Every role that can assign work can already
    /// edit anybody's job, so a supervisor arranging cover for themselves gains
    /// nothing they did not have; what it gains is a record saying why they
    /// were in somebody else's shipments that week, which is the point of the
    /// feature rather than a hole in it.
    /// </summary>
    public static bool MayArrangeForOthers(AppUser actor) => actor.Can(Capability.AssignJobs);

    public static readonly string[] MayCover =
        [Roles.Operation, Roles.Supervisor, Roles.AssistantManager, Roles.Manager];

    /// <summary>
    /// Whether this person may be handed somebody else's register work.
    ///
    /// One reading, used by the form that offers the names and by the grant
    /// that accepts one — a list built from a different rule than the one that
    /// validates it offers names that are refused on the way in, which reads as
    /// the feature being broken.
    ///
    /// A closed account cannot be given work, and nobody covers for themselves.
    /// </summary>
    public static bool CanReceive(StaffMember person, string ownerId) =>
        person.Active
        && person.Id.Length > 0
        && MayCover.Contains(person.Role, StringComparer.OrdinalIgnoreCase)
        && !string.Equals(person.Id, ownerId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Who this person may hand their jobs to.
    ///
    /// Deliberately not behind ViewAudit, which /api/staff needs: going on
    /// leave is not an audit question, and the five people who do it most had
    /// no way to see this list at all.
    /// </summary>
    public async Task<IReadOnlyList<Candidate>> CandidatesAsync(string ownerId, CancellationToken token)
    {
        var people = await db.Staff.AsNoTracking().OrderBy(person => person.Name).ToListAsync(token);
        return people
            .Where(person => CanReceive(person, ownerId))
            .Select(person => new Candidate(person.Id, person.Name, person.Role))
            .ToList();
    }

    /// <summary>
    /// Today at the yard, not at the server.
    ///
    /// A grant runs between two dates somebody typed in Thailand. Read off a
    /// UTC clock, every one of them would start and end seven hours late —
    /// which for the first seven hours of a working day means the cover a
    /// colleague arranged is not yet in force.
    /// </summary>
    private static DateOnly Today => DateOnly.FromDateTime(Formats.Now.DateTime);

    /// <summary>
    /// Live now: not revoked, and today falls inside the dates.
    ///
    /// Derived rather than stored, so a grant ends on the day it says it ends
    /// whether or not anything ran overnight.
    /// </summary>
    public static bool IsLive(JobDelegation grant, DateOnly today)
    {
        if (grant.Revoked) return false;
        var from = TrainingRules.ParseDate(grant.FromDate);
        var to = TrainingRules.ParseDate(grant.ToDate);
        if (from is null || to is null) return false;
        return today >= from.Value && today <= to.Value;
    }

    public static string Describe(JobDelegation grant, DateOnly today)
    {
        if (grant.Revoked) return "ยกเลิกแล้ว";
        if (IsLive(grant, today)) return "กำลังใช้งาน";
        var from = TrainingRules.ParseDate(grant.FromDate);
        return from is not null && today < from.Value ? "รอถึงกำหนด" : "หมดอายุแล้ว";
    }

    /// <summary>
    /// The owner ids this person may edit for right now, their own excluded.
    ///
    /// Separated from the query so the rule can be exercised without a database
    /// or a session — it decides who may write to somebody else's work, and
    /// that deserves to be checkable on its own. `--check-delegation` runs it.
    ///
    /// Own id excluded for real, not only by the grant form refusing to create
    /// such a row: this is asked on every request, and a row that got in by any
    /// other route would otherwise widen the answer rather than being ignored.
    /// </summary>
    public static IReadOnlyList<string> ActingFor(
        IEnumerable<JobDelegation> grants, string delegateId, DateOnly today)
    {
        var who = (delegateId ?? "").Trim();
        if (who.Length == 0) return [];

        return grants
            .Where(grant => string.Equals(grant.DelegateId, who, StringComparison.OrdinalIgnoreCase))
            .Where(grant => IsLive(grant, today))
            .Select(grant => grant.OwnerId)
            .Where(owner => owner.Length > 0
                && !string.Equals(owner, who, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The owner ids this person may edit for right now, their own excluded.
    ///
    /// Empty for almost everybody, and that is the expected answer — this is a
    /// holiday arrangement, not a role.
    /// </summary>
    public async Task<IReadOnlyList<string>> ActingForAsync(string delegateId, CancellationToken token)
    {
        if (delegateId.Length == 0) return [];

        var grants = await db.JobDelegations.AsNoTracking()
            .Where(grant => grant.DelegateId == delegateId && !grant.Revoked)
            .ToListAsync(token);

        return ActingFor(grants, delegateId, Today);
    }

    /// <summary>
    /// Whose work this person may arrange cover for — the same operations line
    /// that may receive it, minus themselves.
    ///
    /// One list, so the two ends of the form cannot disagree about who counts
    /// as a colleague.
    /// </summary>
    public async Task<IReadOnlyList<Candidate>> OwnersAsync(string actorId, CancellationToken token) =>
        await CandidatesAsync(actorId, token);

    /// <summary>
    /// The live grants under which this person is holding somebody else's work.
    ///
    /// What the alert feed reads. `ActingFor` answers which owner ids, which is
    /// what the write check needs; this answers whose and until when, which is
    /// what a person needs to be told.
    /// </summary>
    public async Task<IReadOnlyList<Grant>> CoveringForAsync(string delegateId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(delegateId)) return [];

        var grants = await db.JobDelegations.AsNoTracking()
            .Where(grant => grant.DelegateId == delegateId && !grant.Revoked)
            .ToListAsync(token);

        var today = Today;
        var live = grants.Where(grant => IsLive(grant, today)
            && !string.Equals(grant.OwnerId, delegateId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (live.Count == 0) return [];

        var people = await db.Staff.AsNoTracking()
            .ToDictionaryAsync(person => person.Id, person => person.Name, token);

        return live.Select(grant => new Grant(
            grant.Id, grant.OwnerId, people.GetValueOrDefault(grant.OwnerId, grant.OwnerId),
            grant.DelegateId, people.GetValueOrDefault(grant.DelegateId, grant.DelegateId),
            grant.FromDate, grant.ToDate, grant.Reason,
            Describe(grant, today), grant.CreatedBy)).ToList();
    }

    /// <summary>
    /// The live grants over this person's own work that somebody else arranged.
    ///
    /// Cover arranged on your behalf is a thing you are told about, not a thing
    /// you discover. Only grants somebody else created: your own arrangements
    /// are not news to you.
    /// </summary>
    public async Task<IReadOnlyList<Grant>> ArrangedForYouAsync(string ownerId, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ownerId)) return [];

        var grants = await db.JobDelegations.AsNoTracking()
            .Where(grant => grant.OwnerId == ownerId && !grant.Revoked)
            .ToListAsync(token);

        var today = Today;
        var live = grants.Where(grant => IsLive(grant, today)).ToList();
        if (live.Count == 0) return [];

        var people = await db.Staff.AsNoTracking()
            .ToDictionaryAsync(person => person.Id, person => person.Name, token);

        // Arranged by somebody else, by id. A grant from before this was
        // recorded has an empty id and is read as the owner's own, which every
        // one of them was — nobody else could make one at the time.
        return live
            .Where(grant => grant.CreatedById.Length > 0
                && !string.Equals(grant.CreatedById, ownerId, StringComparison.OrdinalIgnoreCase))
            .Select(grant => new Grant(
                grant.Id, grant.OwnerId, people.GetValueOrDefault(grant.OwnerId, grant.OwnerId),
                grant.DelegateId, people.GetValueOrDefault(grant.DelegateId, grant.DelegateId),
                grant.FromDate, grant.ToDate, grant.Reason,
                Describe(grant, today),
                // The name, not the email the signature holds: this is read by
                // the person whose work it is, in an alert.
                people.GetValueOrDefault(grant.CreatedById, grant.CreatedBy)))
            .ToList();
    }

    /// <summary>Every grant this person made, or was given, most recent first.</summary>
    public async Task<IReadOnlyList<Grant>> ForPersonAsync(string staffId, bool all, CancellationToken token)
    {
        var query = db.JobDelegations.AsNoTracking();
        if (!all) query = query.Where(g => g.OwnerId == staffId || g.DelegateId == staffId);

        var grants = await query.OrderByDescending(g => g.Id).Take(200).ToListAsync(token);
        var people = await db.Staff.AsNoTracking()
            .ToDictionaryAsync(person => person.Id, person => person.Name, token);

        var today = Today;
        return grants.Select(grant => new Grant(
            grant.Id, grant.OwnerId, people.GetValueOrDefault(grant.OwnerId, grant.OwnerId),
            grant.DelegateId, people.GetValueOrDefault(grant.DelegateId, grant.DelegateId),
            grant.FromDate, grant.ToDate, grant.Reason,
            Describe(grant, today), grant.CreatedBy)).ToList();
    }

    /// <summary>
    /// Grants somebody the right to work my jobs between two dates.
    ///
    /// The owner is taken from the signed-in identity, never from the request:
    /// a body that named an owner would let anybody grant themselves access to
    /// anybody's work.
    /// </summary>
    /// <param name="actor">Who is signed in, and whose name goes on the record.</param>
    /// <param name="ownerId">
    /// Whose jobs are being handed over. Empty means the actor's own. Naming
    /// somebody else needs <see cref="MayArrangeForOthers"/>, and the request
    /// is refused rather than quietly treated as the actor's own — a grant
    /// silently made against the wrong person is worse than one refused.
    /// </param>
    public async Task<Result> GrantAsync(AppUser actor, string ownerId, string delegateId,
        string from, string to, string reason, CancellationToken token)
    {
        var forSomeoneElse = ownerId.Trim().Length > 0
            && !string.Equals(ownerId, actor.OperatorId, StringComparison.OrdinalIgnoreCase);

        if (forSomeoneElse && !MayArrangeForOthers(actor))
            return new Result(false, "มอบสิทธิ์แทนคนอื่นได้เฉพาะระดับหัวหน้างานขึ้นไป");

        var owner = forSomeoneElse ? ownerId.Trim() : actor.OperatorId;

        if (owner.Length == 0)
            return new Result(false, "บัญชีนี้ยังไม่มีรหัสพนักงาน จึงยังไม่มีงานให้มอบหมาย");
        if (delegateId.Trim().Length == 0) return new Result(false, "ต้องเลือกผู้รับมอบหมาย");
        if (string.Equals(delegateId, owner, StringComparison.OrdinalIgnoreCase))
            return new Result(false, "มอบหมายให้เจ้าของงานเองไม่ได้");

        if (forSomeoneElse)
        {
            var them = await db.Staff.AsNoTracking()
                .FirstOrDefaultAsync(person => person.Id == owner && person.Active, token);
            if (them is null) return new Result(false, "ไม่พบเจ้าของงาน หรือบัญชีถูกปิดอยู่");
        }
        if (reason.Trim().Length < 4)
            return new Result(false, "ต้องระบุเหตุผล เช่น ลาพักร้อน 5–12 ส.ค.");

        var start = TrainingRules.ParseDate(from);
        var end = TrainingRules.ParseDate(to);
        if (start is null || end is null)
            return new Result(false, "ต้องระบุวันเริ่มและวันสิ้นสุด (วว/ดด/ปปปป)");

        var mine = await db.JobDelegations.AsNoTracking()
            .Where(grant => grant.OwnerId == owner)
            .ToListAsync(token);

        var today = Today;
        var refused = WhyRefused(start.Value, end.Value, today, delegateId, mine);
        if (refused is not null) return new Result(false, refused);

        var person = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == delegateId, token);
        if (person is null) return new Result(false, "ไม่พบผู้รับมอบหมาย");

        // The same reading the list of names is built from, so a name that was
        // offered cannot be refused here.
        if (!CanReceive(person, owner))
        {
            return new Result(false, !person.Active
                ? "บัญชีผู้รับมอบหมายถูกปิดอยู่"
                : $"มอบหมายให้บทบาท {person.Role} ไม่ได้ — มอบได้เฉพาะ {string.Join(", ", MayCover)}");
        }

        var grant = new JobDelegation
        {
            OwnerId = owner,
            DelegateId = delegateId,
            FromDate = TrainingRules.Write(start.Value),
            ToDate = TrainingRules.Write(end.Value),
            Reason = reason.Trim(),
            // Whoever arranged it, which is not always whose work it is.
            CreatedBy = actor.Signature,
            CreatedById = actor.OperatorId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.JobDelegations.Add(grant);
        await db.SaveChangesAsync(token);

        var message = start.Value > today
            ? $"บันทึกการมอบสิทธิ์ล่วงหน้าให้ {person.Name} แล้ว · "
              + $"สิทธิ์จะเริ่มอัตโนมัติวันที่ {grant.FromDate} และสิ้นสุดวันที่ {grant.ToDate}"
            : $"มอบสิทธิ์แก้ไขงานให้ {person.Name} ตั้งแต่ {grant.FromDate} ถึง {grant.ToDate} แล้ว";

        return new Result(true,
            (forSomeoneElse ? "มอบสิทธิ์แทนเจ้าของงานแล้ว · " : "") + message,
            grant.Id);
    }

    /// <summary>
    /// Ends a grant early. Only the owner or an administrator, and never a
    /// delete — who could have edited what, and when, stays answerable.
    /// </summary>
    public async Task<Result> RevokeAsync(long id, AppUser by, CancellationToken token)
    {
        var grant = await db.JobDelegations.FirstOrDefaultAsync(g => g.Id == id, token);
        if (grant is null) return new Result(false, "ไม่พบรายการนี้");
        if (grant.Revoked) return new Result(false, "รายการนี้ถูกยกเลิกไปแล้ว");

        var isOwner = string.Equals(grant.OwnerId, by.OperatorId, StringComparison.OrdinalIgnoreCase);
        if (!isOwner && !by.Can(Capability.AdministerData))
            return new Result(false, "ยกเลิกได้เฉพาะเจ้าของงานหรือผู้ดูแลระบบ");

        grant.Revoked = true;
        grant.RevokedBy = by.Signature;
        grant.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);

        return new Result(true, "ยกเลิกการมอบสิทธิ์แล้ว", id);
    }
}
