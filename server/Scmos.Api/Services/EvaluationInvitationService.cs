using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record EvaluatorInput(string? Name, string? Email, int DepartmentId);
public record InvitationRequest(IReadOnlyList<int>? Evaluators, IReadOnlyList<int>? Carriers, string? ExpiresOn);
public record InvitationRow(long Id, int EvaluatorId, int EvaluationCarrierId, string Carrier, string State, DateTimeOffset ExpiresAt,
    DateTimeOffset? SentAt, DateTimeOffset? OpenedAt, DateTimeOffset? SubmittedAt, string RevokeReason);
public record EvaluatorRow(int Id, string Name, string Email, int DepartmentId, string Department, IReadOnlyList<InvitationRow> Invitations);
/// <summary>A link as it is made — the one time its token exists outside the evaluator's browser.</summary>
public record IssuedLink(long InvitationId, string Evaluator, string Department, string Carrier, string Token, DateTimeOffset ExpiresAt);
public record InvitationOutcome(bool Ok, string Message, int Status = StatusCodes.Status200OK, IReadOnlyList<IssuedLink>? Links = null);

/// <summary>
/// Evaluators and their links (1 Oct 2026, Annual Evaluation Phase 7). An evaluator is named, given a department, and
/// sent one link per carrier; the link's token is returned once and only its hash is kept. Nothing is emailed: the admin
/// copies the link and sends it themselves, then marks it sent. A lost link is replaced, never shown again.
/// </summary>
public class EvaluationInvitationService(ScmosDbContext db, AuditService audit)
{
    public async Task<IReadOnlyList<EvaluatorRow>?> EvaluatorsAsync(AppUser user, int campaignId, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var departments = await db.EvaluationDepartments.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, token);
        var evaluators = await db.EvaluationEvaluators.AsNoTracking().Where(row => row.CampaignId == campaignId).OrderBy(row => row.DepartmentId)
            .ThenBy(row => row.Name).ToListAsync(token);
        var invitations = await (from invitation in db.EvaluationInvitations.AsNoTracking()
                                 join carrier in db.EvaluationCarriers.AsNoTracking() on invitation.EvaluationCarrierId equals carrier.Id
                                 join supplier in db.Suppliers.AsNoTracking() on carrier.SupplierId equals supplier.Id
                                 where invitation.CampaignId == campaignId
                                 select new { invitation, Name = supplier.LegalName != "" ? supplier.LegalName : supplier.Name }).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;
        return evaluators.Select(evaluator => new EvaluatorRow(evaluator.Id, evaluator.Name, evaluator.Email, evaluator.DepartmentId,
            departments.GetValueOrDefault(evaluator.DepartmentId, ""),
            invitations.Where(one => one.invitation.EvaluatorId == evaluator.Id).OrderBy(one => one.Name).Select(one => new InvitationRow(
                one.invitation.Id, evaluator.Id, one.invitation.EvaluationCarrierId, one.Name,
                EvaluationInvitations.StateOf(one.invitation.Status, one.invitation.ExpiresAt, now), one.invitation.ExpiresAt,
                one.invitation.SentAt, one.invitation.OpenedAt, one.invitation.SubmittedAt, one.invitation.RevokeReason)).ToList())).ToList();
    }

    public async Task<InvitationOutcome> AddEvaluatorAsync(AppUser user, int campaignId, EvaluatorInput input, CancellationToken token)
    {
        var (campaign, refusal) = await ManageableAsync(user, campaignId, token);
        if (campaign is null) return refusal!;
        var name = (input.Name ?? "").Trim();
        var email = (input.Email ?? "").Trim();
        if (name.Length is 0 or > 200) return Refused("ระบุชื่อผู้ประเมิน");
        if (email.Length > 254 || (email.Length > 0 && (!email.Contains('@') || email.Contains(' ')))) return Refused("อีเมลไม่ถูกต้อง");
        var asked = await db.EvaluationCampaignDepartments.AsNoTracking()
            .AnyAsync(row => row.CampaignId == campaignId && row.DepartmentId == input.DepartmentId && row.Enabled, token);
        if (!asked) return Refused("แผนกนี้ไม่ได้ร่วมประเมินในแคมเปญนี้");
        if (email.Length > 0 && await db.EvaluationEvaluators.AnyAsync(row => row.CampaignId == campaignId && row.Email == email, token))
            return Refused("อีเมลนี้เป็นผู้ประเมินในแคมเปญนี้อยู่แล้ว", StatusCodes.Status409Conflict);
        db.EvaluationEvaluators.Add(new EvaluationEvaluator
        {
            CampaignId = campaignId, Name = name, Email = email, DepartmentId = input.DepartmentId, CreatedBy = user.Signature, CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync(token);
        await Record(user, campaign, "evaluator", "", $"{name} · {input.DepartmentId}", "", token);
        return new InvitationOutcome(true, $"เพิ่มผู้ประเมิน {name} แล้ว");
    }

    /// <summary>
    /// A link per evaluator and carrier, for every pair that has no live link yet (one revoked or expired does not
    /// count). Valid until the campaign's closing day unless another day is given.
    /// </summary>
    public async Task<InvitationOutcome> GenerateAsync(AppUser user, int campaignId, InvitationRequest request, CancellationToken token)
    {
        var (campaign, refusal) = await ManageableAsync(user, campaignId, token);
        if (campaign is null) return refusal!;
        if (campaign.Status is not (AnnualEvaluationRules.DataPreparation or AnnualEvaluationRules.Ready or AnnualEvaluationRules.Open))
            return Refused("สร้างลิงก์ได้ระหว่างเตรียมข้อมูลจนถึงช่วงเปิดรับประเมิน", StatusCodes.Status409Conflict);
        var expires = ExpiryOf(request.ExpiresOn, campaign);
        if (expires is null) return Refused("วันหมดอายุของลิงก์ไม่ถูกต้อง (ต้องเป็นวันในอนาคต รูปแบบ YYYY-MM-DD)");

        var evaluatorIds = (request.Evaluators ?? []).Distinct().ToList();
        var evaluators = await db.EvaluationEvaluators.AsNoTracking().Where(row => row.CampaignId == campaignId && row.Active && evaluatorIds.Contains(row.Id))
            .ToListAsync(token);
        if (evaluators.Count == 0) return Refused("เลือกผู้ประเมินก่อน");
        var carriers = await (from row in db.EvaluationCarriers.AsNoTracking()
                              join supplier in db.Suppliers.AsNoTracking() on row.SupplierId equals supplier.Id
                              where row.CampaignId == campaignId && row.Included
                              select new { row.Id, Name = supplier.LegalName != "" ? supplier.LegalName : supplier.Name }).ToListAsync(token);
        if (request.Carriers is { Count: > 0 } chosen) carriers = carriers.Where(row => chosen.Contains(row.Id)).ToList();
        if (carriers.Count == 0) return Refused("ไม่มีผู้ขนส่งที่จะส่งลิงก์ให้ประเมิน");
        var departments = await db.EvaluationDepartments.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Name, token);

        var now = DateTimeOffset.UtcNow;
        var existing = await db.EvaluationInvitations.AsNoTracking()
            .Where(row => row.CampaignId == campaignId && evaluatorIds.Contains(row.EvaluatorId)).ToListAsync(token);
        var links = new List<(EvaluationInvitation Row, IssuedLink Link)>();
        foreach (var evaluator in evaluators)
            foreach (var carrier in carriers)
            {
                var live = existing.Any(one => one.EvaluatorId == evaluator.Id && one.EvaluationCarrierId == carrier.Id
                    && EvaluationInvitations.StateOf(one.Status, one.ExpiresAt, now) is not (AnnualEvaluationRules.InvitationRevoked or EvaluationInvitations.Expired));
                if (live) continue;
                var secret = EvaluationInvitations.NewToken();
                var row = new EvaluationInvitation
                {
                    CampaignId = campaignId, EvaluatorId = evaluator.Id, EvaluationCarrierId = carrier.Id, TokenHash = EvaluationInvitations.HashOf(secret),
                    ExpiresAt = expires.Value, CreatedBy = user.Signature, CreatedAt = now,
                };
                db.EvaluationInvitations.Add(row);
                links.Add((row, new IssuedLink(0, evaluator.Name, departments.GetValueOrDefault(evaluator.DepartmentId, ""), carrier.Name, secret, expires.Value)));
            }
        await db.SaveChangesAsync(token);
        await Record(user, campaign, "invitations", "", $"{links.Count} ลิงก์", "", token);
        return new InvitationOutcome(true, links.Count == 0 ? "ทุกคู่มีลิงก์ที่ยังใช้ได้อยู่แล้ว" : $"สร้าง {links.Count} ลิงก์ — คัดลอกตอนนี้ ลิงก์จะไม่แสดงอีก",
            Links: links.Select(one => one.Link with { InvitationId = one.Row.Id }).ToList());
    }

    public async Task<InvitationOutcome> RevokeAsync(AppUser user, int campaignId, long invitationId, string? reason, CancellationToken token)
    {
        var (campaign, refusal) = await ManageableAsync(user, campaignId, token);
        if (campaign is null) return refusal!;
        var why = (reason ?? "").Trim();
        if (why.Length < 4) return Refused("ระบุเหตุผลที่ยกเลิกลิงก์ (อย่างน้อย 4 ตัวอักษร)");
        var row = await db.EvaluationInvitations.FirstOrDefaultAsync(one => one.Id == invitationId && one.CampaignId == campaignId, token);
        if (row is null) return Refused("ไม่พบลิงก์นี้", StatusCodes.Status404NotFound);
        if (row.Status == AnnualEvaluationRules.InvitationSubmitted) return Refused("ผู้ประเมินส่งคำตอบแล้ว — ยกเลิกไม่ได้", StatusCodes.Status409Conflict);
        if (row.Status == AnnualEvaluationRules.InvitationRevoked) return Refused("ลิงก์นี้ถูกยกเลิกแล้ว", StatusCodes.Status409Conflict);
        var before = row.Status;
        row.Status = AnnualEvaluationRules.InvitationRevoked;
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.RevokedBy = user.Signature;
        row.RevokeReason = why.Length > 500 ? why[..500] : why;
        await db.SaveChangesAsync(token);
        await Record(user, campaign, $"invitation:{invitationId}", before, AnnualEvaluationRules.InvitationRevoked, why, token, AuditActions.Revoke);
        return new InvitationOutcome(true, "ยกเลิกลิงก์แล้ว");
    }

    /// <summary>A lost or leaked link: the old one is revoked and a new one made for the same evaluator and carrier.</summary>
    public async Task<InvitationOutcome> RenewAsync(AppUser user, int campaignId, long invitationId, CancellationToken token)
    {
        var (campaign, refusal) = await ManageableAsync(user, campaignId, token);
        if (campaign is null) return refusal!;
        if (campaign.Status is not (AnnualEvaluationRules.DataPreparation or AnnualEvaluationRules.Ready or AnnualEvaluationRules.Open))
            return Refused("สร้างลิงก์ได้ระหว่างเตรียมข้อมูลจนถึงช่วงเปิดรับประเมิน", StatusCodes.Status409Conflict);
        var row = await db.EvaluationInvitations.FirstOrDefaultAsync(one => one.Id == invitationId && one.CampaignId == campaignId, token);
        if (row is null) return Refused("ไม่พบลิงก์นี้", StatusCodes.Status404NotFound);
        if (row.Status == AnnualEvaluationRules.InvitationSubmitted) return Refused("ผู้ประเมินส่งคำตอบแล้ว", StatusCodes.Status409Conflict);
        var now = DateTimeOffset.UtcNow;
        if (row.Status != AnnualEvaluationRules.InvitationRevoked)
        {
            row.Status = AnnualEvaluationRules.InvitationRevoked;
            row.RevokedAt = now;
            row.RevokedBy = user.Signature;
            row.RevokeReason = "แทนด้วยลิงก์ใหม่";
        }
        var expires = row.ExpiresAt > now ? row.ExpiresAt : ExpiryOf(null, campaign)!.Value;
        var secret = EvaluationInvitations.NewToken();
        var fresh = new EvaluationInvitation
        {
            CampaignId = campaignId, EvaluatorId = row.EvaluatorId, EvaluationCarrierId = row.EvaluationCarrierId,
            TokenHash = EvaluationInvitations.HashOf(secret), ExpiresAt = expires, CreatedBy = user.Signature, CreatedAt = now,
        };
        db.EvaluationInvitations.Add(fresh);
        await db.SaveChangesAsync(token);
        var evaluator = await db.EvaluationEvaluators.AsNoTracking().FirstAsync(one => one.Id == row.EvaluatorId, token);
        var carrier = await (from one in db.EvaluationCarriers.AsNoTracking()
                             join supplier in db.Suppliers.AsNoTracking() on one.SupplierId equals supplier.Id
                             where one.Id == row.EvaluationCarrierId
                             select supplier.LegalName != "" ? supplier.LegalName : supplier.Name).FirstAsync(token);
        var department = await db.EvaluationDepartments.AsNoTracking().Where(one => one.Id == evaluator.DepartmentId).Select(one => one.Name).FirstOrDefaultAsync(token) ?? "";
        await Record(user, campaign, $"invitation:{invitationId}", "", $"แทนด้วย {fresh.Id}", "", token);
        return new InvitationOutcome(true, "สร้างลิงก์ใหม่แล้ว — ลิงก์เดิมใช้ไม่ได้อีก",
            Links: [new IssuedLink(fresh.Id, evaluator.Name, department, carrier, secret, expires)]);
    }

    public async Task<InvitationOutcome> ExtendAsync(AppUser user, int campaignId, long invitationId, string? expiresOn, CancellationToken token)
    {
        var (campaign, refusal) = await ManageableAsync(user, campaignId, token);
        if (campaign is null) return refusal!;
        var row = await db.EvaluationInvitations.FirstOrDefaultAsync(one => one.Id == invitationId && one.CampaignId == campaignId, token);
        if (row is null) return Refused("ไม่พบลิงก์นี้", StatusCodes.Status404NotFound);
        if (row.Status is AnnualEvaluationRules.InvitationSubmitted or AnnualEvaluationRules.InvitationRevoked)
            return Refused("ลิงก์นี้ใช้เสร็จหรือถูกยกเลิกแล้ว", StatusCodes.Status409Conflict);
        var expires = ExpiryOf(expiresOn, campaign);
        if (expires is null || string.IsNullOrWhiteSpace(expiresOn)) return Refused("วันหมดอายุไม่ถูกต้อง (ต้องเป็นวันในอนาคต รูปแบบ YYYY-MM-DD)");
        var before = row.ExpiresAt;
        row.ExpiresAt = expires.Value;
        await db.SaveChangesAsync(token);
        await Record(user, campaign, $"invitation:{invitationId}", before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            expires.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), "", token);
        return new InvitationOutcome(true, "ขยายเวลาลิงก์แล้ว");
    }

    /// <summary>The admin sent it from their own Outlook. Recorded so the screen can tell who has not been sent anything.</summary>
    public async Task<InvitationOutcome> MarkSentAsync(AppUser user, int campaignId, long invitationId, CancellationToken token)
    {
        var (campaign, refusal) = await ManageableAsync(user, campaignId, token);
        if (campaign is null) return refusal!;
        var row = await db.EvaluationInvitations.FirstOrDefaultAsync(one => one.Id == invitationId && one.CampaignId == campaignId, token);
        if (row is null) return Refused("ไม่พบลิงก์นี้", StatusCodes.Status404NotFound);
        row.SentAt ??= DateTimeOffset.UtcNow;
        if (row.Status == AnnualEvaluationRules.InvitationPending) row.Status = AnnualEvaluationRules.InvitationSent;
        await db.SaveChangesAsync(token);
        await Record(user, campaign, $"invitation:{invitationId}", "", "sent", "", token);
        return new InvitationOutcome(true, "บันทึกว่าส่งลิงก์แล้ว");
    }

    /// <summary>The day a link stops working: the one asked for, else the campaign's closing day, else thirty days; end of day in Bangkok.</summary>
    private static DateTimeOffset? ExpiryOf(string? text, EvaluationCampaign campaign)
    {
        DateOnly? day = string.IsNullOrWhiteSpace(text)
            ? campaign.DueOn ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30))
            : DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ? parsed : null;
        if (day is null) return null;
        var end = new DateTimeOffset(day.Value.Year, day.Value.Month, day.Value.Day, 23, 59, 59, TimeSpan.FromHours(7));
        return end <= DateTimeOffset.UtcNow ? null : end;
    }

    private async Task<(EvaluationCampaign? Campaign, InvitationOutcome? Refusal)> ManageableAsync(AppUser user, int campaignId, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return (null, Refused("บัญชีนี้ไม่มีสิทธิ์จัดการผู้ประเมิน", StatusCodes.Status403Forbidden));
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return (null, Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound));
        if (campaign.Status is AnnualEvaluationRules.Approved or AnnualEvaluationRules.Finalized or AnnualEvaluationRules.Archived)
            return (null, Refused("แคมเปญอนุมัติแล้ว", StatusCodes.Status409Conflict));
        return (campaign, null);
    }

    private Task Record(AppUser user, EvaluationCampaign campaign, string field, string before, string after, string reason, CancellationToken token,
        string action = AuditActions.Update) =>
        audit.RecordAsync(user, action, "annual-evaluation", campaign.Id.ToString(CultureInfo.InvariantCulture), campaign.Code, field, before, after, reason, token);

    private static InvitationOutcome Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);
}
