using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Invited">Links for this carrier still live (revoked ones not counted) · <paramref name="Responses"/> of them answered.</param>
/// <param name="Stale">Why the current score no longer matches the evidence (Phase 8); empty when it does.</param>
public record ResultRow(int EvaluationCarrierId, int SupplierId, string Code, string Carrier, string Eligibility, int? TotalJobs,
    int Version, decimal? SystemScore, decimal? HumanScore, decimal? FinalScore, decimal SystemWeightAvailable, string Band, string Status,
    string Reason, DateTimeOffset? CalculatedAt, string Decision, string DecisionNote = "", string DecidedBy = "", DateTimeOffset? DecidedAt = null,
    int Invited = 0, int Responses = 0, IReadOnlyList<string>? Stale = null, IReadOnlyList<CarrierDepartmentLine>? Departments = null);
public record ResultDetail(ResultRow Row, JsonElement Detail, IReadOnlyList<int> Versions);

/// <summary>
/// Annual Evaluation scoring (1 Oct 2026, Phase 4): the campaign's rules, the current snapshot, the manual assessments and
/// the departments' answers, through <see cref="EvaluationScoring"/>. Each calculation is a new version of the carrier's
/// result, with the whole breakdown kept as it was worked out — a recalculation never overwrites the one before. Once the
/// campaign has closed, recalculating a carrier that already has a result needs a reason. A calculation lands for every
/// carrier or for none, and a failure is logged (Phase 13).
/// </summary>
public class EvaluationScoringService(ScmosDbContext db, AuditService audit, ILogger<EvaluationScoringService>? log = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AnnualEvaluationResult> CalculateAsync(AppUser user, int campaignId, IReadOnlyList<int>? only, string? reason,
        CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return Refused("บัญชีนี้ไม่มีสิทธิ์คำนวณคะแนน", StatusCodes.Status403Forbidden);
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound);
        if (campaign.Status is AnnualEvaluationRules.Approved or AnnualEvaluationRules.Finalized or AnnualEvaluationRules.Archived)
            return Refused("แคมเปญอนุมัติแล้ว — คะแนนไม่เปลี่ยนอีก", StatusCodes.Status409Conflict);
        var why = (reason ?? "").Trim();
        var afterCollection = campaign.Status is AnnualEvaluationRules.Closed or AnnualEvaluationRules.UnderReview;

        var rows = await db.EvaluationCarriers.Where(row => row.CampaignId == campaignId && row.Included).ToListAsync(token);
        if (only is { Count: > 0 }) rows = rows.Where(row => only.Contains(row.Id)).ToList();
        if (rows.Count == 0) return Refused("ไม่มีผู้ขนส่งที่จะคำนวณ");
        var rowIds = rows.Select(row => row.Id).ToList();
        var results = await db.EvaluationResults.Where(row => rowIds.Contains(row.EvaluationCarrierId)).ToListAsync(token);
        if (afterCollection && why.Length < 4 && results.Any())
            return Refused("ปิดรับการประเมินแล้ว — การคำนวณใหม่ต้องระบุเหตุผล (อย่างน้อย 4 ตัวอักษร)");
        var snapshots = await db.EvaluationSnapshots.AsNoTracking().Where(row => rowIds.Contains(row.EvaluationCarrierId) && row.Current).ToListAsync(token);
        var withoutEvidence = rows.Where(row => snapshots.All(one => one.EvaluationCarrierId != row.Id)).ToList();
        if (withoutEvidence.Count > 0) return Refused($"ยังไม่มี snapshot {withoutEvidence.Count} ราย — สร้าง snapshot ก่อน");

        // The rules, read once.
        var kpis = await db.EvaluationKpis.AsNoTracking().Where(row => row.CampaignId == campaignId && row.Enabled).OrderBy(row => row.Position).ToListAsync(token);
        var kpiIds = kpis.Select(row => row.Id).ToList();
        var bands = (await db.EvaluationKpiBands.AsNoTracking().Where(row => kpiIds.Contains(row.KpiId)).ToListAsync(token)).ToLookup(row => row.KpiId);
        var questionWeights = await db.EvaluationQuestions.AsNoTracking().Where(row => row.CampaignId == campaignId && row.Enabled)
            .ToDictionaryAsync(row => row.Id, row => row.Weight, token);
        var departmentWeights = await db.EvaluationCampaignDepartments.AsNoTracking().Where(row => row.CampaignId == campaignId && row.Enabled)
            .ToDictionaryAsync(row => row.DepartmentId, row => row.Weight, token);
        var scoreBands = (await db.EvaluationScoreBands.AsNoTracking().Where(row => row.CampaignId == campaignId).ToListAsync(token))
            .Select(row => (row.Code, row.MinScore)).ToList();

        var snapshotIds = snapshots.Select(row => row.Id).ToList();
        var metrics = (await db.EvaluationSnapshotMetrics.AsNoTracking().Where(row => snapshotIds.Contains(row.SnapshotId)).ToListAsync(token))
            .ToLookup(row => row.SnapshotId);
        var manual = (await db.EvaluationManualScores.AsNoTracking().Where(row => rowIds.Contains(row.EvaluationCarrierId)).ToListAsync(token))
            .ToLookup(row => row.EvaluationCarrierId);
        var responses = await db.EvaluationResponses.AsNoTracking().Where(row => rowIds.Contains(row.EvaluationCarrierId)).ToListAsync(token);
        var responseIds = responses.Select(row => row.Id).ToList();
        var answers = (await db.EvaluationAnswers.AsNoTracking().Where(row => responseIds.Contains(row.ResponseId)).ToListAsync(token))
            .ToLookup(row => row.ResponseId);

        var now = DateTimeOffset.UtcNow;
        var started = Stopwatch.GetTimestamp();
        try
        {
            // Every carrier's new result or none (Phase 13); read again inside, as a retry starts from a clean change tracker.
            await EvaluationWrites.InOneAsync(db, async () =>
            {
                var held = await db.EvaluationResults.Where(row => rowIds.Contains(row.EvaluationCarrierId)).ToListAsync(token);
                foreach (var row in rows)
                {
                    var snapshot = snapshots.First(one => one.EvaluationCarrierId == row.Id);
                    var figures = metrics[snapshot.Id].ToDictionary(metric => metric.Code);
                    var inputs = kpis.Select(kpi =>
                    {
                        var code = EvaluationEvidence.KpiMetric.GetValueOrDefault(kpi.Code, "");
                        var figure = figures.GetValueOrDefault(code);
                        return new EvaluationScoring.KpiInput(kpi.Code, kpi.Name, kpi.Weight, kpi.Method, kpi.Direction, kpi.FallbackScore,
                            bands[kpi.Id].Select(band => new EvaluationScoring.Band(band.Threshold, band.Score)).ToList(),
                            code, figure?.Status ?? AnnualEvaluationRules.NotAvailable, figure?.Value,
                            manual[row.Id].FirstOrDefault(one => one.KpiCode == kpi.Code)?.Score);
                    }).ToList();
                    var answered = responses.Where(one => one.EvaluationCarrierId == row.Id)
                        .Select(one => new EvaluationScoring.Answered(one.EvaluatorId, one.DepartmentId,
                            answers[one.Id].Select(answer => new EvaluationScoring.Rating(answer.QuestionId, answer.Rating)).ToList())).ToList();
                    var outcome = EvaluationScoring.Score(inputs, campaign.SystemWeight, campaign.HumanWeight, campaign.MinimumSystemCoverage,
                        answered, questionWeights, departmentWeights, scoreBands);

                    foreach (var old in held.Where(one => one.EvaluationCarrierId == row.Id && one.Current)) old.Current = false;
                    var result = new EvaluationResult
                    {
                        EvaluationCarrierId = row.Id, Current = true, SnapshotId = snapshot.Id, CampaignVersion = campaign.Version,
                        Version = held.Where(one => one.EvaluationCarrierId == row.Id).Select(one => one.Version).DefaultIfEmpty(0).Max() + 1,
                        SystemScore = outcome.SystemScore, HumanScore = outcome.HumanScore, FinalScore = outcome.FinalScore,
                        SystemWeightAvailable = outcome.SystemWeightAvailable, Band = outcome.Band, Status = outcome.Status,
                        Detail = JsonSerializer.Serialize(new
                        {
                            weights = new { system = campaign.SystemWeight, human = campaign.HumanWeight, minimumCoverage = campaign.MinimumSystemCoverage },
                            snapshotVersion = snapshot.Version, coverage = outcome.Coverage, kpis = outcome.Kpis, departments = outcome.Departments,
                            why = outcome.Why,
                        }, Json),
                        Reason = why.Length > 500 ? why[..500] : why, CalculatedBy = user.Signature, CalculatedAt = now,
                    };
                    db.EvaluationResults.Add(result);
                    await db.SaveChangesAsync(token);
                    db.EvaluationDepartmentScores.AddRange(outcome.Departments.Select(line => new EvaluationDepartmentScore
                    {
                        ResultId = result.Id, DepartmentId = line.DepartmentId, Score = line.Score, Responses = line.Responses, Weight = line.Weight,
                    }));
                    await db.SaveChangesAsync(token);
                    held.Add(result);
                }
            }, token);
        }
        catch (DbUpdateException problem) when (EvaluationWrites.Duplicate(problem))
        {
            log?.LogWarning("Annual evaluation {Campaign}: calculation by {User} met another made at the same moment — nothing written",
                campaign.Code, user.Signature);
            return Refused("มีการคำนวณพร้อมกันอีกรายการ — ไม่ได้บันทึก ลองใหม่อีกครั้ง", StatusCodes.Status409Conflict);
        }
        catch (Exception problem) when (problem is not OperationCanceledException)
        {
            log?.LogError(problem, "Annual evaluation {Campaign}: calculation of {Count} carrier(s) by {User} failed — nothing written",
                campaign.Code, rows.Count, user.Signature);
            return Refused("คำนวณคะแนนไม่สำเร็จ — ไม่มีการบันทึกใด ๆ ลองใหม่อีกครั้ง", StatusCodes.Status500InternalServerError);
        }
        log?.LogInformation("Annual evaluation {Campaign}: {Count} carrier(s) calculated by {User} in {Elapsed:0} ms",
            campaign.Code, rows.Count, user.Signature, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        await audit.RecordAsync(user, AuditActions.Update, "annual-evaluation", campaignId.ToString(CultureInfo.InvariantCulture), campaign.Code,
            "score", "", $"{rows.Count} ราย", why, token);
        return new AnnualEvaluationResult(true, $"คำนวณคะแนน {rows.Count} ราย", Id: campaignId);
    }

    /// <summary>
    /// Subcontract Management's own 0–100 for a manual KPI (pricing, until a benchmark can be trusted). Kept as one row
    /// per carrier and KPI; every change is in the audit trail with what it was before. Closed once the campaign is approved.
    /// </summary>
    public async Task<AnnualEvaluationResult> SetManualScoreAsync(AppUser user, int campaignId, int evaluationCarrierId, string? kpiCode,
        decimal score, string? note, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return Refused("บัญชีนี้ไม่มีสิทธิ์ให้คะแนน", StatusCodes.Status403Forbidden);
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return Refused("ไม่พบแคมเปญนี้", StatusCodes.Status404NotFound);
        if (campaign.Status is AnnualEvaluationRules.Approved or AnnualEvaluationRules.Finalized or AnnualEvaluationRules.Archived)
            return Refused("แคมเปญอนุมัติแล้ว — คะแนนไม่เปลี่ยนอีก", StatusCodes.Status409Conflict);
        var carrier = await db.EvaluationCarriers.AsNoTracking().FirstOrDefaultAsync(row => row.Id == evaluationCarrierId && row.CampaignId == campaignId, token);
        if (carrier is null) return Refused("ไม่พบผู้ขนส่งในแคมเปญนี้", StatusCodes.Status404NotFound);
        var code = (kpiCode ?? "").Trim();
        var kpi = await db.EvaluationKpis.AsNoTracking().FirstOrDefaultAsync(row => row.CampaignId == campaignId && row.Code == code, token);
        if (kpi is null || kpi.Method != AnnualEvaluationRules.Manual) return Refused("KPI นี้ไม่ได้ให้คะแนนด้วยมือ");
        if (score is < 0 or > 100) return Refused("คะแนนต้องอยู่ระหว่าง 0–100");
        var text = (note ?? "").Trim();
        if (text.Length < 4) return Refused("ระบุเหตุผลประกอบคะแนน (อย่างน้อย 4 ตัวอักษร)");

        var row = await db.EvaluationManualScores.FirstOrDefaultAsync(one => one.EvaluationCarrierId == evaluationCarrierId && one.KpiCode == code, token);
        var before = row?.Score.ToString("0.##", CultureInfo.InvariantCulture) ?? "";
        if (row is null) db.EvaluationManualScores.Add(row = new EvaluationManualScore { EvaluationCarrierId = evaluationCarrierId, KpiCode = code });
        row.Score = score;
        row.Note = text.Length > 2000 ? text[..2000] : text;
        row.AssessedBy = user.Signature;
        row.AssessedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        await audit.RecordAsync(user, AuditActions.Update, "annual-evaluation", campaignId.ToString(CultureInfo.InvariantCulture), campaign.Code,
            $"manual:{code}:{evaluationCarrierId}", before, score.ToString("0.##", CultureInfo.InvariantCulture), text, token);
        return new AnnualEvaluationResult(true, $"บันทึกคะแนน {kpi.Name} แล้ว", Id: evaluationCarrierId);
    }

    /// <summary>
    /// The campaign's board (Phase 8, 2 Oct 2026): each carrier's current score, whether it still matches its evidence, how
    /// many of its links were answered — in all and by department — and the decision on it.
    /// </summary>
    public async Task<IReadOnlyList<ResultRow>?> ResultsAsync(AppUser user, int campaignId, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return null;
        var board = await EvaluationReviewService.BoardAsync(db, campaign, token);
        var resultIds = board.Where(row => row.Result is not null).Select(row => row.Result!.Id).ToList();
        var departmentScores = (await db.EvaluationDepartmentScores.AsNoTracking().Where(row => resultIds.Contains(row.ResultId)).ToListAsync(token))
            .ToLookup(row => row.ResultId);
        var links = await (from invitation in db.EvaluationInvitations.AsNoTracking()
                           join evaluator in db.EvaluationEvaluators.AsNoTracking() on invitation.EvaluatorId equals evaluator.Id
                           where invitation.CampaignId == campaignId && invitation.Status != AnnualEvaluationRules.InvitationRevoked
                           select new { invitation.EvaluationCarrierId, evaluator.DepartmentId, invitation.Status }).ToListAsync(token);
        var departments = await db.EvaluationCampaignDepartments.AsNoTracking().Where(row => row.CampaignId == campaignId && row.Enabled)
            .OrderBy(row => row.DepartmentId).Select(row => row.DepartmentId).ToListAsync(token);
        return board.Select(one =>
        {
            var mine = links.Where(link => link.EvaluationCarrierId == one.Carrier.Id).ToList();
            var scores = one.Result is null ? [] : departmentScores[one.Result.Id].ToList();
            var lines = departments.Select(department => new CarrierDepartmentLine(department,
                scores.FirstOrDefault(score => score.DepartmentId == department)?.Score,
                mine.Count(link => link.DepartmentId == department && link.Status == AnnualEvaluationRules.InvitationSubmitted),
                mine.Count(link => link.DepartmentId == department))).ToList();
            return Row(one.Carrier, one.Code, one.Name, one.Result) with
            {
                Invited = mine.Count, Responses = mine.Count(link => link.Status == AnnualEvaluationRules.InvitationSubmitted), Stale = one.Stale,
                Departments = lines,
            };
        }).OrderByDescending(row => row.FinalScore ?? -1).ThenBy(row => row.Carrier).ToList();
    }

    public async Task<ResultDetail?> ResultAsync(AppUser user, int campaignId, int evaluationCarrierId, int? version, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var row = await db.EvaluationCarriers.AsNoTracking().FirstOrDefaultAsync(one => one.Id == evaluationCarrierId && one.CampaignId == campaignId, token);
        if (row is null) return null;
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(one => one.Id == row.SupplierId, token);
        var all = await db.EvaluationResults.AsNoTracking().Where(one => one.EvaluationCarrierId == row.Id).OrderByDescending(one => one.Version).ToListAsync(token);
        var chosen = version is { } wanted ? all.FirstOrDefault(one => one.Version == wanted) : all.FirstOrDefault(one => one.Current);
        if (chosen is null) return null;
        return new ResultDetail(Row(row, supplier.Code, supplier.LegalName != "" ? supplier.LegalName : supplier.Name, chosen),
            JsonDocument.Parse(chosen.Detail).RootElement.Clone(), all.Select(one => one.Version).ToList());
    }

    private static ResultRow Row(EvaluationCarrier row, string code, string name, EvaluationResult? result) =>
        new(row.Id, row.SupplierId, code, name, row.Eligibility, row.TotalJobs, result?.Version ?? 0, result?.SystemScore, result?.HumanScore,
            result?.FinalScore, result?.SystemWeightAvailable ?? 0, result?.Band ?? "", result?.Status ?? "", result?.Reason ?? "",
            result?.CalculatedAt, row.Decision, row.DecisionNote, row.DecidedBy, row.DecidedAt);

    private static AnnualEvaluationResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);
}
