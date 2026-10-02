using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <summary>A figure an evaluator may see: its code, whether it could be measured, and the number. No records, no notes.</summary>
public record ExternalMetric(string Code, string Status, decimal? Value);
public record ExternalQuestion(string Code, string Text, string TextTh, bool Required, int CommentRequiredAtOrBelow);
public record ExternalAnswer(string Code, int? Rating, bool NotApplicable, string Comment);
/// <summary>
/// What the page outside SCMOS is sent — and nothing else. Every field is chosen: no ids, no other carrier, no rates,
/// no internal notes, no record references. The evaluator's own answers come back once submitted.
/// </summary>
public record ExternalView(string Campaign, int Year, string PeriodStart, string PeriodEnd, string? DueOn, string Carrier, string Evaluator,
    string Department, bool Submitted, DateTimeOffset? SubmittedAt, IReadOnlyList<ExternalMetric> Performance, IReadOnlyList<ExternalQuestion> Questions,
    IReadOnlyList<ExternalAnswer> Answers, string Comment);
public record ExternalSubmission(IReadOnlyList<EvaluationInvitations.Answer>? Answers, string? Comment);
public record ExternalOutcome(int Status, string Message, ExternalView? View = null);

/// <summary>
/// The evaluation page outside SCMOS (1 Oct 2026, Annual Evaluation Phase 6). A token is the whole of the evaluator's
/// identity: it names one campaign, one evaluator and one carrier, and nothing on the request can change which — there is
/// no id to alter. Unknown, revoked, expired and closed are refused before anything is read; a sheet is submitted once,
/// its response, answers and the link's state together (Phase 13). A refused or failed submission is logged by the link's
/// row id — never by its token or the token's hash.
/// </summary>
public class ExternalEvaluationService(ScmosDbContext db, AuditService audit, ILogger<ExternalEvaluationService>? log = null)
{
    private const string Unknown = "ลิงก์ไม่ถูกต้อง";

    /// <summary>The figures an evaluator is shown, in the order the page lays them out.</summary>
    public static readonly string[] Shown =
        ["total-jobs", "completed-jobs", "carrier-otd", "incidents-major", "incidents-minor", "claims", "billing-accuracy", "billing-sla", "pod-compliance"];

    public async Task<ExternalOutcome> ReadAsync(string? token, CancellationToken cancel)
    {
        var (found, refusal) = await FindAsync(token, cancel);
        if (found is null) return refusal!;
        var (invitation, evaluator, campaign) = found.Value;
        if (invitation.Status is AnnualEvaluationRules.InvitationPending or AnnualEvaluationRules.InvitationSent)
        {
            var tracked = await db.EvaluationInvitations.FirstAsync(row => row.Id == invitation.Id, cancel);
            tracked.Status = AnnualEvaluationRules.InvitationOpened;
            tracked.OpenedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancel);
            await audit.RecordAsync(Evaluator(evaluator), AuditActions.Update, "annual-evaluation", campaign.Id.ToString(CultureInfo.InvariantCulture),
                campaign.Code, $"invitation:{invitation.Id}", invitation.Status, AnnualEvaluationRules.InvitationOpened, "", cancel, "external");
        }
        return new ExternalOutcome(StatusCodes.Status200OK, "", await ViewAsync(invitation, evaluator, campaign, cancel));
    }

    public async Task<ExternalOutcome> SubmitAsync(string? token, ExternalSubmission submission, CancellationToken cancel)
    {
        var (found, refusal) = await FindAsync(token, cancel);
        if (found is null) return Refused(refusal!, null);
        var (invitation, evaluator, campaign) = found.Value;
        if (invitation.Status == AnnualEvaluationRules.InvitationSubmitted)
            return Refused(new ExternalOutcome(StatusCodes.Status409Conflict, AlreadySent), invitation.Id);
        if (!Accepting(campaign)) return Refused(new ExternalOutcome(StatusCodes.Status410Gone, "ปิดรับการประเมินแล้ว"), invitation.Id);

        var asked = await AskedAsync(campaign, evaluator.DepartmentId, cancel);
        var answers = submission.Answers ?? [];
        var problems = EvaluationInvitations.Problems(asked.Select(one => one.Asked).ToList(), answers, submission.Comment);
        if (problems.Count > 0) return Refused(new ExternalOutcome(StatusCodes.Status400BadRequest, string.Join(" · ", problems)), invitation.Id);

        var now = DateTimeOffset.UtcNow;
        var byCode = asked.ToDictionary(one => one.Asked.Code, one => one.Id);
        try
        {
            // One transaction: a failure between the response and its answers left the link submitted with nothing in it.
            await EvaluationWrites.InOneAsync(db, async () =>
            {
                var response = new EvaluationResponse
                {
                    InvitationId = invitation.Id, CampaignId = campaign.Id, EvaluationCarrierId = invitation.EvaluationCarrierId,
                    EvaluatorId = evaluator.Id, DepartmentId = evaluator.DepartmentId, Comment = (submission.Comment ?? "").Trim(), SubmittedAt = now,
                };
                db.EvaluationResponses.Add(response);
                var tracked = await db.EvaluationInvitations.FirstAsync(row => row.Id == invitation.Id, cancel);
                tracked.Status = AnnualEvaluationRules.InvitationSubmitted;
                tracked.SubmittedAt = now;
                tracked.OpenedAt ??= now;
                await db.SaveChangesAsync(cancel);
                db.EvaluationAnswers.AddRange(answers.Where(answer => byCode.ContainsKey((answer.Code ?? "").Trim())).Select(answer => new EvaluationAnswer
                {
                    ResponseId = response.Id, QuestionId = byCode[(answer.Code ?? "").Trim()],
                    Rating = answer.NotApplicable ? null : answer.Rating, Comment = (answer.Comment ?? "").Trim(),
                }));
                await db.SaveChangesAsync(cancel);
            }, cancel);
        }
        catch (DbUpdateException problem) when (EvaluationWrites.Duplicate(problem))
        {
            // Two submissions at once: the unique index on the invitation keeps the first.
            return Refused(new ExternalOutcome(StatusCodes.Status409Conflict, AlreadySent), invitation.Id);
        }
        catch (Exception problem) when (problem is not OperationCanceledException)
        {
            log?.LogError(problem, "External evaluation: the submission for invitation {Invitation} ({Campaign}) failed — nothing saved, the link stays open",
                invitation.Id, campaign.Code);
            return new ExternalOutcome(StatusCodes.Status500InternalServerError, "ส่งไม่สำเร็จ — ยังไม่ได้บันทึก กรุณากดส่งอีกครั้ง");
        }
        await audit.RecordAsync(Evaluator(evaluator), AuditActions.Register, "annual-evaluation", campaign.Id.ToString(CultureInfo.InvariantCulture),
            campaign.Code, $"invitation:{invitation.Id}", "", AnnualEvaluationRules.InvitationSubmitted, "", cancel, "external");
        invitation.Status = AnnualEvaluationRules.InvitationSubmitted;
        invitation.SubmittedAt = now;
        return new ExternalOutcome(StatusCodes.Status200OK, "ส่งแบบประเมินแล้ว ขอบคุณครับ", await ViewAsync(invitation, evaluator, campaign, cancel));
    }

    /* ------------------------------------------------------------------ helpers */

    private const string AlreadySent = "ส่งแบบประเมินนี้แล้ว — ส่งได้ครั้งเดียว";

    /// <summary>A refused submission, logged by the link's row id when it is known — the token never is, nor its hash.</summary>
    private ExternalOutcome Refused(ExternalOutcome outcome, long? invitationId)
    {
        log?.LogWarning("External evaluation: submission refused ({Status}) for invitation {Invitation}: {Reason}",
            outcome.Status, invitationId?.ToString(CultureInfo.InvariantCulture) ?? "unknown", outcome.Message);
        return outcome;
    }

    private async Task<((EvaluationInvitation Invitation, EvaluationEvaluator Evaluator, EvaluationCampaign Campaign)? Found, ExternalOutcome? Refusal)>
        FindAsync(string? token, CancellationToken cancel)
    {
        if (!EvaluationInvitations.LooksLikeToken(token)) return (null, new ExternalOutcome(StatusCodes.Status404NotFound, Unknown));
        var hash = EvaluationInvitations.HashOf(token!);
        var invitation = await db.EvaluationInvitations.AsNoTracking().FirstOrDefaultAsync(row => row.TokenHash == hash, cancel);
        if (invitation is null) return (null, new ExternalOutcome(StatusCodes.Status404NotFound, Unknown));
        if (invitation.Status == AnnualEvaluationRules.InvitationRevoked) return (null, new ExternalOutcome(StatusCodes.Status410Gone, "ลิงก์นี้ถูกยกเลิกแล้ว"));
        if (EvaluationInvitations.StateOf(invitation.Status, invitation.ExpiresAt, DateTimeOffset.UtcNow) == EvaluationInvitations.Expired)
            return (null, new ExternalOutcome(StatusCodes.Status410Gone, "ลิงก์หมดอายุแล้ว"));
        var evaluator = await db.EvaluationEvaluators.AsNoTracking().FirstAsync(row => row.Id == invitation.EvaluatorId, cancel);
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstAsync(row => row.Id == invitation.CampaignId, cancel);
        var carrier = await db.EvaluationCarriers.AsNoTracking().FirstAsync(row => row.Id == invitation.EvaluationCarrierId, cancel);
        if (!evaluator.Active || !carrier.Included) return (null, new ExternalOutcome(StatusCodes.Status410Gone, "ลิงก์นี้ใช้ไม่ได้แล้ว"));
        if (invitation.Status != AnnualEvaluationRules.InvitationSubmitted && !Accepting(campaign))
            return (null, new ExternalOutcome(StatusCodes.Status410Gone, campaign.Status is AnnualEvaluationRules.Draft or AnnualEvaluationRules.DataPreparation
                or AnnualEvaluationRules.Ready ? "ยังไม่เปิดรับการประเมิน" : "ปิดรับการประเมินแล้ว"));
        return ((invitation, evaluator, campaign), null);
    }

    /// <summary>Open, and today (Bangkok) inside the campaign's opening and closing days.</summary>
    private static bool Accepting(EvaluationCampaign campaign)
    {
        if (campaign.Status != AnnualEvaluationRules.Open) return false;
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7)).DateTime);
        return (campaign.OpenOn is null || campaign.OpenOn <= today) && (campaign.DueOn is null || today <= campaign.DueOn);
    }

    private async Task<List<(int Id, EvaluationInvitations.Asked Asked, EvaluationQuestion Question)>> AskedAsync(EvaluationCampaign campaign, int departmentId,
        CancellationToken cancel)
    {
        var questions = await db.EvaluationQuestions.AsNoTracking().Where(row => row.CampaignId == campaign.Id && row.Enabled).OrderBy(row => row.Position)
            .ToListAsync(cancel);
        var ids = questions.Select(row => row.Id).ToList();
        var asked = await db.EvaluationQuestionDepartments.AsNoTracking()
            .Where(row => ids.Contains(row.QuestionId) && row.DepartmentId == departmentId && row.Enabled).ToListAsync(cancel);
        return questions.Where(question => asked.Any(row => row.QuestionId == question.Id)).Select(question =>
        {
            var row = asked.First(one => one.QuestionId == question.Id);
            return (question.Id, new EvaluationInvitations.Asked(question.Code, row.Required, row.CommentRequiredAtOrBelow ?? campaign.CommentRequiredAtOrBelow), question);
        }).ToList();
    }

    private async Task<ExternalView> ViewAsync(EvaluationInvitation invitation, EvaluationEvaluator evaluator, EvaluationCampaign campaign, CancellationToken cancel)
    {
        var carrier = await (from row in db.EvaluationCarriers.AsNoTracking()
                             join supplier in db.Suppliers.AsNoTracking() on row.SupplierId equals supplier.Id
                             where row.Id == invitation.EvaluationCarrierId
                             select supplier.LegalName != "" ? supplier.LegalName : supplier.Name).FirstAsync(cancel);
        var department = await db.EvaluationDepartments.AsNoTracking().Where(row => row.Id == evaluator.DepartmentId).Select(row => row.Name).FirstOrDefaultAsync(cancel) ?? "";
        var snapshot = await db.EvaluationSnapshots.AsNoTracking().Where(row => row.EvaluationCarrierId == invitation.EvaluationCarrierId && row.Current)
            .Select(row => (long?)row.Id).FirstOrDefaultAsync(cancel);
        var metrics = snapshot is null ? [] : await db.EvaluationSnapshotMetrics.AsNoTracking()
            .Where(row => row.SnapshotId == snapshot && row.External && Shown.Contains(row.Code)).ToListAsync(cancel);
        var asked = await AskedAsync(campaign, evaluator.DepartmentId, cancel);
        var response = await db.EvaluationResponses.AsNoTracking().FirstOrDefaultAsync(row => row.InvitationId == invitation.Id, cancel);
        var answers = response is null ? [] : await db.EvaluationAnswers.AsNoTracking().Where(row => row.ResponseId == response.Id).ToListAsync(cancel);
        var codeOf = asked.ToDictionary(one => one.Id, one => one.Asked.Code);
        return new ExternalView(campaign.Name, campaign.Year, Day(campaign.PeriodStart), Day(campaign.PeriodEnd), campaign.DueOn is { } due ? Day(due) : null,
            carrier, evaluator.Name, department, response is not null, response?.SubmittedAt,
            Shown.Select(code => metrics.FirstOrDefault(row => row.Code == code)).OfType<EvaluationSnapshotMetric>()
                .Select(row => new ExternalMetric(row.Code, row.Status, row.Value)).ToList(),
            asked.Select(one => new ExternalQuestion(one.Asked.Code, one.Question.Text, one.Question.TextTh, one.Asked.Required, one.Asked.CommentRequiredAtOrBelow)).ToList(),
            answers.Where(answer => codeOf.ContainsKey(answer.QuestionId))
                .Select(answer => new ExternalAnswer(codeOf[answer.QuestionId], answer.Rating, answer.Rating is null, answer.Comment)).ToList(),
            response?.Comment ?? "");
    }

    /// <summary>The evaluator as the audit trail names them: not an SCMOS account, and said so.</summary>
    private static AppUser Evaluator(EvaluationEvaluator evaluator) =>
        new($"evaluator:{evaluator.Id}", evaluator.Email, evaluator.Name, "External Evaluator", "", "external-evaluation");

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
