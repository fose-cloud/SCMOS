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

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

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
    public async Task<Result> GrantAsync(AppUser owner, string delegateId, string from, string to,
        string reason, CancellationToken token)
    {
        if (owner.OperatorId.Length == 0)
            return new Result(false, "บัญชีนี้ยังไม่มีรหัสพนักงาน จึงยังไม่มีงานให้มอบหมาย");
        if (delegateId.Trim().Length == 0) return new Result(false, "ต้องเลือกผู้รับมอบหมาย");
        if (string.Equals(delegateId, owner.OperatorId, StringComparison.OrdinalIgnoreCase))
            return new Result(false, "มอบหมายให้ตัวเองไม่ได้");
        if (reason.Trim().Length < 4)
            return new Result(false, "ต้องระบุเหตุผล เช่น ลาพักร้อน 5–12 ส.ค.");

        var start = TrainingRules.ParseDate(from);
        var end = TrainingRules.ParseDate(to);
        if (start is null || end is null)
            return new Result(false, "ต้องระบุวันเริ่มและวันสิ้นสุด (วว/ดด/ปปปป)");
        if (end.Value < start.Value)
            return new Result(false, "วันสิ้นสุดต้องไม่อยู่ก่อนวันเริ่ม");

        var person = await db.Staff.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == delegateId && p.Active, token);
        if (person is null) return new Result(false, "ไม่พบผู้รับมอบหมาย หรือบัญชีถูกปิดอยู่");

        // A carrier's account works one company's jobs through its own portal
        // and has no place holding an operator's register work.
        if (string.Equals(person.Role, Roles.Subcontractor, StringComparison.OrdinalIgnoreCase))
            return new Result(false, "มอบหมายให้บัญชีผู้รับเหมาไม่ได้");

        var grant = new JobDelegation
        {
            OwnerId = owner.OperatorId,
            DelegateId = delegateId,
            FromDate = TrainingRules.Write(start.Value),
            ToDate = TrainingRules.Write(end.Value),
            Reason = reason.Trim(),
            CreatedBy = owner.Signature,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.JobDelegations.Add(grant);
        await db.SaveChangesAsync(token);

        return new Result(true,
            $"มอบสิทธิ์แก้ไขงานให้ {person.Name} ตั้งแต่ {grant.FromDate} ถึง {grant.ToDate} แล้ว",
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
