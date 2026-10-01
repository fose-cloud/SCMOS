using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record SkillCell(string EmployeeId, int SkillId, int Level, int? TargetLevel, string Note, string AssessedBy, DateTimeOffset AssessedAt);

public record SkillMatrixView(IReadOnlyList<ActionPlanSkill> Skills, IReadOnlyList<PersonView> People, IReadOnlyList<SkillCell> Cells,
    bool CanAssess, bool CanConfigure);

public record SkillHistoryRow(long Id, string Skill, string Category, int Level, int? TargetLevel, string Note, string AssessedBy, DateTimeOffset AssessedAt);

/// <summary>
/// The department's Skill Matrix (1 Oct 2026, Action Plan round two): a level 1–5 per person per skill, with the
/// level wanted, kept as a row per assessment.
///
/// <para>
/// It is personal data, read by the rule people development plans are read by: those who review plans
/// (supervisors upward) see and assess everybody; anybody else sees their own row and changes nothing.
/// </para>
/// </summary>
public class SkillMatrixService(ScmosDbContext db, AuditService audit)
{
    public async Task<SkillMatrixView> ReadAsync(AppUser user, CancellationToken token)
    {
        var skills = (await SkillsAsync(token)).Where(skill => skill.Active).ToList();
        var everyone = user.Can(Capability.ReviewActionPlans);
        var people = await db.Staff.AsNoTracking()
            .Where(row => row.Active && row.Role != Roles.Subcontractor && (everyone || row.Id == user.OperatorId))
            .OrderBy(row => row.Name).Select(row => new PersonView(row.Id, row.Name, row.Role)).ToListAsync(token);
        var ids = people.Select(person => person.Id).ToList();
        var rows = await db.SkillAssessments.AsNoTracking().Where(row => ids.Contains(row.EmployeeId)).ToListAsync(token);
        var cells = rows.GroupBy(row => (row.EmployeeId, row.SkillId))
            .Select(group => group.OrderByDescending(row => row.Id).First())
            .Select(row => new SkillCell(row.EmployeeId, row.SkillId, row.Level, row.TargetLevel, row.Note, row.AssessedBy, row.AssessedAt))
            .ToList();
        return new SkillMatrixView(skills, people, cells, everyone, user.Can(Capability.AdministerData));
    }

    public async Task<IReadOnlyList<SkillHistoryRow>?> HistoryAsync(AppUser user, string employeeId, CancellationToken token)
    {
        if (!user.Can(Capability.ReviewActionPlans) && employeeId != user.OperatorId) return null;
        var skills = (await SkillsAsync(token)).ToDictionary(skill => skill.Id);
        return (await db.SkillAssessments.AsNoTracking().Where(row => row.EmployeeId == employeeId).OrderByDescending(row => row.Id).Take(500).ToListAsync(token))
            .Select(row => new SkillHistoryRow(row.Id, skills.GetValueOrDefault(row.SkillId)?.Name ?? "", skills.GetValueOrDefault(row.SkillId)?.Category ?? "",
                row.Level, row.TargetLevel, row.Note, row.AssessedBy, row.AssessedAt)).ToList();
    }

    public async Task<ActionPlanResult> AssessAsync(AppUser user, string? employeeId, int skillId, int level, int? targetLevel, string? note,
        CancellationToken token)
    {
        if (!user.Can(Capability.ReviewActionPlans)) return new(false, "เฉพาะหัวหน้าขึ้นไปที่ประเมินทักษะได้", StatusCodes.Status403Forbidden);
        var id = (employeeId ?? "").Trim();
        var person = await db.Staff.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id && row.Active && row.Role != Roles.Subcontractor, token);
        if (person is null) return new(false, "ไม่พบพนักงานคนนี้", StatusCodes.Status404NotFound);
        var skill = await db.ActionPlanSkills.AsNoTracking().FirstOrDefaultAsync(row => row.Id == skillId && row.Active, token);
        if (skill is null) return new(false, "ไม่พบทักษะนี้", StatusCodes.Status404NotFound);
        if (level is < 1 or > 5 || targetLevel is < 1 or > 5) return new(false, "ระดับต้องอยู่ระหว่าง 1–5");
        var before = await db.SkillAssessments.AsNoTracking().Where(row => row.EmployeeId == id && row.SkillId == skillId)
            .OrderByDescending(row => row.Id).Select(row => new { row.Level, row.TargetLevel }).FirstOrDefaultAsync(token);
        var text = (note ?? "").Trim();
        db.SkillAssessments.Add(new SkillAssessment { EmployeeId = id, SkillId = skillId, Level = level, TargetLevel = targetLevel,
            Note = text.Length > 500 ? text[..500] : text, AssessedBy = user.Signature, AssessedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, "skill-assessment", id, person.Name, skill.Name,
            before is null ? "" : $"{before.Level} → {before.TargetLevel}", $"{level} → {targetLevel}", text, token);
        return new(true, $"บันทึก {skill.Name} ของ {person.Name}: ระดับ {level}{(targetLevel is null ? "" : $" → {targetLevel}")}");
    }

    public async Task<ActionPlanResult> SaveSkillAsync(AppUser user, string? category, string? name, bool active, CancellationToken token)
    {
        var area = (category ?? "").Trim();
        var text = (name ?? "").Trim();
        if (area.Length is 0 or > 60 || text.Length is 0 or > 120) return new(false, "ระบุหมวดและชื่อทักษะ");
        var row = await db.ActionPlanSkills.FirstOrDefaultAsync(one => one.Category == area && one.Name == text, token);
        if (row is null)
        {
            row = new ActionPlanSkill { Category = area, Name = text,
                Position = (await db.ActionPlanSkills.Where(one => one.Category == area).MaxAsync(one => (int?)one.Position, token) ?? -1) + 1 };
            db.ActionPlanSkills.Add(row);
        }
        row.Active = active;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Configure, "skill", row.Id.ToString(), text, "active", "", active ? "on" : "off", area, token);
        return new(true, active ? $"เปิดใช้ทักษะ {text} แล้ว" : $"ปิดทักษะ {text} แล้ว", Id: row.Id);
    }

    /// <summary>The skills, filled with the department's list the first time.</summary>
    private async Task<List<ActionPlanSkill>> SkillsAsync(CancellationToken token)
    {
        if (!await db.ActionPlanSkills.AnyAsync(token))
        {
            foreach (var (category, names) in ActionPlanRules.DefaultSkills)
                db.ActionPlanSkills.AddRange(names.Select((name, position) => new ActionPlanSkill { Category = category, Name = name, Position = position }));
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateException) { db.ChangeTracker.Clear(); }   // another request seeded them first
        }
        var order = ActionPlanRules.DefaultSkills.Select(group => group.Category).ToList();
        return (await db.ActionPlanSkills.AsNoTracking().ToListAsync(token))
            .OrderBy(skill => order.IndexOf(skill.Category) is var at && at < 0 ? int.MaxValue : at).ThenBy(skill => skill.Category)
            .ThenBy(skill => skill.Position).ToList();
    }
}
