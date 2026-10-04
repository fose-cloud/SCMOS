using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record DecisionOption(string Code, string Label);
/// <summary>A decision whose consequence is the Supplier Register's to carry out — suspending or retiring the carrier.</summary>
public record FollowUp(int EvaluationCarrierId, int SupplierId, string Carrier, string Decision, string Label, string Note);
/// <summary>The campaign at a glance: who was asked, who answered, what is scored and decided.</summary>
public record CampaignSummary(int Year, string Status, int Carriers, int Evaluators, int Invited, int Responses, int Pending, int Expired,
    decimal? Completion, int Calculated, int Insufficient, int NotCalculated, int Stale, int Decided, int Published,
    IReadOnlyList<string> ApprovalProblems, IReadOnlyList<FollowUp> FollowUps,
    /// <summary>Phase 10: Action Plans opened from the campaign and not cancelled, and the decisions still waiting for one.</summary>
    int Plans, IReadOnlyList<FollowUp> PlanFollowUps);
/// <summary>One department's part of one carrier: its score in the current result, and its links now.</summary>
public record CarrierDepartmentLine(int DepartmentId, decimal? Score, int Responses, int Invited);

/// <summary>
/// Management review and finalization of an annual evaluation (2 Oct 2026, Annual Evaluation Phases 8–9). A decision is
/// recorded per carrier while the campaign is under review; it is never read off the score. Approval waits until every
/// carrier has a score that still matches its evidence and a decision (<see cref="EvaluationReview.ApprovalProblems"/>), and
/// finalizing writes each carrier's result into the Supplier Register's evaluation history. A decision to suspend or retire a
/// carrier is listed for the register; the campaign does not change the carrier's status itself.
/// </summary>
public class EvaluationReviewService(ScmosDbContext db, AuditService audit)
{
    private const string Entity = "annual-evaluation";

    public async Task<CampaignSummary?> SummaryAsync(AppUser user, int campaignId, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return null;
        var board = await BoardAsync(db, campaign, token);
        var included = board.Select(row => row.Carrier.Id).ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var states = (await db.EvaluationInvitations.AsNoTracking().Where(row => row.CampaignId == campaignId)
                .Select(row => new { row.EvaluationCarrierId, row.Status, row.ExpiresAt }).ToListAsync(token))
            .Where(row => included.Contains(row.EvaluationCarrierId))
            .Select(row => EvaluationInvitations.StateOf(row.Status, row.ExpiresAt, now))
            .Where(state => state != AnnualEvaluationRules.InvitationRevoked).ToList();
        var responses = states.Count(state => state == AnnualEvaluationRules.InvitationSubmitted);
        var expired = states.Count(state => state == EvaluationInvitations.Expired);
        var evaluators = await db.EvaluationEvaluators.CountAsync(row => row.CampaignId == campaignId && row.Active, token);
        var published = await db.SupplierEvaluations.CountAsync(row => row.Source == SupplierEvaluation.CampaignSource && row.Period == campaign.Code, token);
        FollowUp FollowUpOf(BoardRow row) => new(row.Carrier.Id, row.Carrier.SupplierId, row.Name, row.Carrier.Decision,
            EvaluationReview.LabelOf(row.Carrier.Decision), row.Carrier.DecisionNote);
        var followUps = board.Where(row => EvaluationReview.NeedsRegisterFollowUp(row.Carrier.Decision)).Select(FollowUpOf).ToList();
        var plans = (await EvaluationPlanService.LinkedAsync(db, campaign, token)).Where(one => one.Row.Status != ActionPlanRules.Cancelled)
            .Select(one => one.Row).ToList();
        var planless = board.Where(row => EvaluationReview.NeedsActionPlan(row.Carrier.Decision) && plans.All(plan => plan.EvaluationCarrierId != row.Carrier.Id))
            .Select(FollowUpOf).ToList();
        return new CampaignSummary(campaign.Year, campaign.Status, board.Count, evaluators, states.Count, responses, states.Count - responses - expired, expired,
            EvaluationReview.Completion(responses, states.Count),
            board.Count(row => row.Result?.Status == EvaluationScoring.Calculated),
            board.Count(row => row.Result is not null && row.Result.Status != EvaluationScoring.Calculated),
            board.Count(row => row.Result is null), board.Count(row => row.Stale.Count > 0),
            board.Count(row => EvaluationReview.IsDecision(row.Carrier.Decision)), published,
            campaign.Status is AnnualEvaluationRules.Closed or AnnualEvaluationRules.UnderReview ? Problems(board) : [], followUps,
            plans.Count, planless);
    }

    /// <summary>
    /// A carrier's decision, recorded while the campaign is under review. Whoever runs the campaign may record one; approving
    /// the campaign — a decision-maker's move — is what confirms them all. Every decision but continuing as before, and every
    /// change, carries its reason.
    /// </summary>
    public async Task<AnnualEvaluationResult> DecideAsync(AppUser user, int campaignId, int carrierId, string? decision, string? note,
        CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation) && !user.Can(Capability.DecideAnnualEvaluation))
            return Refused("บัญชีนี้ไม่มีสิทธิ์บันทึกการตัดสิน", StatusCodes.Status403Forbidden);
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound);
        if (campaign.Status != AnnualEvaluationRules.UnderReview)
            return Refused("บันทึกการตัดสินได้เมื่อแคมเปญอยู่ในสถานะกำลังพิจารณาเท่านั้น", StatusCodes.Status409Conflict);
        var row = await db.EvaluationCarriers.FirstOrDefaultAsync(one => one.Id == carrierId && one.CampaignId == campaignId && one.Included, token);
        if (row is null) return Refused("ไม่พบผู้ขนส่งในแคมเปญนี้", StatusCodes.Status404NotFound);
        var code = (decision ?? "").Trim();
        if (!EvaluationReview.IsDecision(code)) return Refused("การตัดสินไม่ถูกต้อง");
        var text = (note ?? "").Trim();
        if (text.Length > 2000) text = text[..2000];
        if (EvaluationReview.NeedsNote(code, row.Decision) && text.Length < EvaluationReview.MinimumNote)
            return Refused($"ระบุเหตุผลของการตัดสิน (อย่างน้อย {EvaluationReview.MinimumNote} ตัวอักษร)");
        if (row.Decision == code && row.DecisionNote == text) return new AnnualEvaluationResult(true, "ไม่มีอะไรเปลี่ยน", Id: carrierId);

        var before = row.Decision;
        row.Decision = code;
        row.DecisionNote = text;
        row.DecidedBy = user.Signature;
        row.DecidedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, Entity, campaignId.ToString(CultureInfo.InvariantCulture), campaign.Code,
            $"decision:{carrierId}", before, code, text, token);
        return new AnnualEvaluationResult(true, $"บันทึกการตัดสิน: {EvaluationReview.LabelOf(code)}", Id: carrierId);
    }

    /* ------------------------------------------------------------------ shared with the campaign and the scores */

    /// <summary>One included carrier with its current result and why that result no longer matches its evidence.</summary>
    internal sealed record BoardRow(EvaluationCarrier Carrier, string Code, string Name, EvaluationResult? Result, IReadOnlyList<string> Stale);

    internal static async Task<List<BoardRow>> BoardAsync(ScmosDbContext db, EvaluationCampaign campaign, CancellationToken token)
    {
        var rows = await (from row in db.EvaluationCarriers.AsNoTracking()
                          join supplier in db.Suppliers.AsNoTracking() on row.SupplierId equals supplier.Id
                          where row.CampaignId == campaign.Id && row.Included
                          select new { Row = row, supplier.Code, Name = supplier.LegalName != "" ? supplier.LegalName : supplier.Name }).ToListAsync(token);
        var ids = rows.Select(one => one.Row.Id).ToList();
        var results = await db.EvaluationResults.AsNoTracking().Where(one => ids.Contains(one.EvaluationCarrierId) && one.Current).ToListAsync(token);
        var snapshots = await db.EvaluationSnapshots.AsNoTracking().Where(one => ids.Contains(one.EvaluationCarrierId) && one.Current)
            .Select(one => new { one.EvaluationCarrierId, one.Id }).ToListAsync(token);
        var answered = await db.EvaluationResponses.AsNoTracking().Where(one => ids.Contains(one.EvaluationCarrierId))
            .GroupBy(one => one.EvaluationCarrierId).Select(group => new { group.Key, Last = group.Max(one => one.SubmittedAt) }).ToListAsync(token);
        var assessed = await db.EvaluationManualScores.AsNoTracking().Where(one => ids.Contains(one.EvaluationCarrierId))
            .GroupBy(one => one.EvaluationCarrierId).Select(group => new { group.Key, Last = group.Max(one => one.AssessedAt) }).ToListAsync(token);
        return rows.Select(one =>
        {
            var result = results.FirstOrDefault(row => row.EvaluationCarrierId == one.Row.Id);
            IReadOnlyList<string> stale = result is null ? [] : EvaluationReview.Stale(new EvaluationReview.Freshness(result.CalculatedAt, result.SnapshotId,
                result.CampaignVersion, snapshots.FirstOrDefault(row => row.EvaluationCarrierId == one.Row.Id)?.Id, campaign.Version,
                answered.FirstOrDefault(row => row.Key == one.Row.Id)?.Last, assessed.FirstOrDefault(row => row.Key == one.Row.Id)?.Last));
            return new BoardRow(one.Row, one.Code, one.Name, result, stale);
        }).ToList();
    }

    internal static async Task<IReadOnlyList<string>> ApprovalProblemsAsync(ScmosDbContext db, EvaluationCampaign campaign, CancellationToken token) =>
        Problems(await BoardAsync(db, campaign, token));

    private static IReadOnlyList<string> Problems(IReadOnlyList<BoardRow> board) =>
        EvaluationReview.ApprovalProblems(board.Select(row =>
            new EvaluationReview.CarrierState(row.Name, row.Result is not null, row.Stale.Count > 0, row.Carrier.Decision)).ToList());

    /// <summary>
    /// The campaign's results into each carrier's evaluation history in the Supplier Register — one row per carrier, its
    /// period the campaign's code, marked as coming from the campaign. Added to the context, not saved: the move to finalized
    /// saves them with the campaign's own change. The carrier's latest score follows when it has one.
    /// </summary>
    internal static async Task<int> PublishAsync(ScmosDbContext db, EvaluationCampaign campaign, AppUser user, CancellationToken token)
    {
        var board = await BoardAsync(db, campaign, token);
        var labels = await db.EvaluationScoreBands.AsNoTracking().Where(row => row.CampaignId == campaign.Id)
            .ToDictionaryAsync(row => row.Code, row => row.Label, token);
        var supplierIds = board.Select(row => row.Carrier.SupplierId).ToList();
        var suppliers = await db.Suppliers.Where(row => supplierIds.Contains(row.Id)).ToListAsync(token);
        var existing = await db.SupplierEvaluations.Where(row => supplierIds.Contains(row.SupplierId) && row.Period == campaign.Code).ToListAsync(token);
        var now = DateTimeOffset.UtcNow;
        foreach (var row in board)
        {
            var record = existing.FirstOrDefault(one => one.SupplierId == row.Carrier.SupplierId);
            if (record is null) db.SupplierEvaluations.Add(record = new SupplierEvaluation { SupplierId = row.Carrier.SupplierId, Period = campaign.Code, CreatedAt = now });
            var final = row.Result?.FinalScore;
            record.Source = SupplierEvaluation.CampaignSource;
            record.FinalPercent = final;
            record.TotalScore = final is { } score ? (int)Math.Round(score, 0, MidpointRounding.AwayFromZero) : null;
            record.Result = Cut(final is null ? "ข้อมูลไม่พอ" : labels.GetValueOrDefault(row.Result!.Band, row.Result.Band), 60);
            record.Grade = "";
            record.Note = Cut(EvaluationReview.LabelOf(row.Carrier.Decision) + (row.Carrier.DecisionNote.Length > 0 ? " — " + row.Carrier.DecisionNote : ""), 1000);
            record.Stage = "approved";
            record.EvaluatedBy = Cut(campaign.Code, 120);
            record.ApprovedBy = Cut(user.Signature, 120);
            if (record.TotalScore is { } total && suppliers.FirstOrDefault(one => one.Id == row.Carrier.SupplierId) is { } supplier)
            {
                supplier.LastScore = total;
                supplier.LastEvaluatedPeriod = Cut(campaign.Code, 20);
                supplier.UpdatedAt = now;
            }
        }
        return board.Count;
    }

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

    private static AnnualEvaluationResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);
}
