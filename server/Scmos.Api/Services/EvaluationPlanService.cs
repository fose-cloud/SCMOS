using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record PlanStep(string Action, string ExpectedResult);
/// <summary>
/// A development plan as the evaluation would open it — the Action Plan wizard's own fields, for a person to change before
/// anything is created. Nothing is written by asking for one.
/// </summary>
public record PlanDraft(string DevelopmentType, string TargetType, int SupplierId, string Category, string Title, string DevelopmentArea,
    string CurrentLevel, string TargetLevel, string Gap, string Objective, string Metric, decimal? Baseline, decimal? TargetValue, string Priority,
    IReadOnlyList<PlanStep> Items, IReadOnlyList<ActionReferenceInput> References, IReadOnlyList<EvaluationFindings.Finding> Findings);
/// <summary>A development plan opened from a carrier's evaluation, as the campaign shows it.</summary>
public record EvaluationPlanRow(int EvaluationCarrierId, long PlanId, string Number, string Title, string Status, bool Overdue, int? Progress,
    string TargetDate, string OwnerName);

/// <summary>
/// Annual Evaluation and the Action Plan module (2 Oct 2026, Phase 10). A carrier's improvement plan is an Action Plan —
/// there is no second action list — opened from the evaluation with its findings filled in and a reference back
/// (<c>evaluation</c>, <see cref="EvaluationFindings.ReferenceOf"/>); the campaign then shows each carrier's plans by that
/// reference, with their status, progress and whether they are overdue, and which decisions still wait for one.
/// </summary>
public class EvaluationPlanService(ScmosDbContext db)
{
    public const string ReferenceKind = "evaluation";

    public async Task<PlanDraft?> DraftAsync(AppUser user, int campaignId, int carrierId, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return null;
        var row = await db.EvaluationCarriers.AsNoTracking().FirstOrDefaultAsync(one => one.Id == carrierId && one.CampaignId == campaignId, token);
        if (row is null) return null;
        var supplier = await db.Suppliers.AsNoTracking().FirstAsync(one => one.Id == row.SupplierId, token);
        var name = supplier.LegalName.Length > 0 ? supplier.LegalName : supplier.Name;
        var result = await db.EvaluationResults.AsNoTracking().FirstOrDefaultAsync(one => one.EvaluationCarrierId == row.Id && one.Current, token);

        // The KPIs as the score worked them out, each with the value that would have scored in full.
        var definitions = await db.EvaluationKpis.AsNoTracking().Where(kpi => kpi.CampaignId == campaignId).ToListAsync(token);
        var definitionIds = definitions.Select(kpi => kpi.Id).ToList();
        var bands = (await db.EvaluationKpiBands.AsNoTracking().Where(band => definitionIds.Contains(band.KpiId)).ToListAsync(token)).ToLookup(band => band.KpiId);
        var kpis = new List<EvaluationFindings.KpiBasis>();
        if (result is not null && JsonDocument.Parse(result.Detail).RootElement.TryGetProperty("kpis", out var lines))
            foreach (var line in lines.EnumerateArray())
            {
                var code = line.GetProperty("code").GetString() ?? "";
                var definition = definitions.FirstOrDefault(kpi => kpi.Code == code);
                kpis.Add(new EvaluationFindings.KpiBasis(code, line.GetProperty("name").GetString() ?? code, line.GetProperty("weight").GetDecimal(),
                    definition?.Direction ?? AnnualEvaluationRules.Higher, line.GetProperty("method").GetString() ?? "", Number(line, "value"), Number(line, "score"),
                    line.GetProperty("counted").GetBoolean(),
                    definition is null ? null : EvaluationFindings.TargetOf(bands[definition.Id].Select(band => new EvaluationScoring.Band(band.Threshold, band.Score)).ToList(),
                        definition.Direction)));
            }

        // The departments' answers to each question, averaged over everybody who rated it (N/A left out).
        var questions = await db.EvaluationQuestions.AsNoTracking().Where(question => question.CampaignId == campaignId && question.Enabled)
            .OrderBy(question => question.Position).ToListAsync(token);
        var responseIds = await db.EvaluationResponses.AsNoTracking().Where(response => response.EvaluationCarrierId == row.Id)
            .Select(response => response.Id).ToListAsync(token);
        var ratings = (await db.EvaluationAnswers.AsNoTracking().Where(answer => responseIds.Contains(answer.ResponseId) && answer.Rating != null)
            .Select(answer => new { answer.QuestionId, Rating = answer.Rating!.Value }).ToListAsync(token)).ToLookup(answer => answer.QuestionId);
        var asked = questions.Select(question =>
        {
            var given = ratings[question.Id].Select(answer => (decimal)answer.Rating).ToList();
            return new EvaluationFindings.QuestionBasis(question.Code, question.TextTh.Length > 0 ? question.TextTh : question.Text,
                given.Count == 0 ? null : Math.Round(given.Average(), 2, MidpointRounding.AwayFromZero), given.Count);
        }).ToList();

        var findings = EvaluationFindings.Of(kpis, asked);
        var main = findings.FirstOrDefault(finding => finding.Kind == "kpi") ?? findings.FirstOrDefault();
        // One line: the plan's gap is a single-line field, so the findings are run together with a separator rather than newlines.
        var gap = findings.Count == 0 ? "ไม่พบจุดที่ต่ำกว่าเป้าในผลประเมิน" : string.Join(" · ", findings.Select(finding => finding.Text));
        var objective = row.Decision.Length == 0 ? "" : EvaluationReview.LabelOf(row.Decision) + (row.DecisionNote.Length > 0 ? " — " + row.DecisionNote : "");
        var final = result?.FinalScore is { } score ? $" · {score.ToString("0.00", CultureInfo.InvariantCulture)}" : "";
        return new PlanDraft(ActionPlanRules.Subcontractor, "carrier", row.SupplierId, EvaluationFindings.CategoryOf(main),
            Cut($"{name} — แผนพัฒนาจาก {campaign.Code}", 200), Cut(main?.Name ?? "", 200),
            main is { Kind: "kpi", Value: { } value } ? Cut(Text(value), 200) : "", main?.Target is { } target ? Cut(Text(target), 200) : "",
            Cut(gap, 1000), Cut(objective, 2000), main is { Kind: "kpi" } ? Cut(main.Name, 200) : "",
            main is { Kind: "kpi" } ? Round(main.Value) : null, main is { Kind: "kpi" } ? Round(main.Target) : null,
            EvaluationFindings.PriorityOf(row.Decision),
            findings.Take(EvaluationFindings.MaxSteps).Select(finding => new PlanStep(Cut($"ปรับปรุง {finding.Name}", 300),
                finding.Target is { } goal ? $"ถึงเป้า {Text(goal)}" : "")).ToList(),
            [new ActionReferenceInput(ReferenceKind, EvaluationFindings.ReferenceOf(campaign.Code, row.Id), Cut($"Annual Evaluation {campaign.Code} · {name}{final}", 300))],
            findings);
    }

    public async Task<IReadOnlyList<EvaluationPlanRow>?> PlansAsync(AppUser user, int campaignId, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var campaign = await db.EvaluationCampaigns.AsNoTracking().FirstOrDefaultAsync(row => row.Id == campaignId, token);
        if (campaign is null) return null;
        return (await LinkedAsync(db, campaign, token)).Where(one => ActionPlanService.CanSee(user, one.Plan)).Select(one => one.Row).ToList();
    }

    /// <summary>Every plan that names one of the campaign's carriers, with its progress and whether it is overdue.</summary>
    internal static async Task<List<(EvaluationPlanRow Row, ActionPlan Plan)>> LinkedAsync(ScmosDbContext db, EvaluationCampaign campaign,
        CancellationToken token)
    {
        var prefix = campaign.Code + ":";
        // A reference counts only when the row it names is one of this campaign's carriers.
        var rows = (await db.EvaluationCarriers.AsNoTracking().Where(row => row.CampaignId == campaign.Id).Select(row => row.Id).ToListAsync(token)).ToHashSet();
        var references = (await db.ActionPlanReferences.AsNoTracking()
                .Where(reference => reference.Kind == ReferenceKind && reference.RefId.StartsWith(prefix)).ToListAsync(token))
            .Select(reference => (reference.PlanId, Carrier: EvaluationFindings.CarrierOf(campaign.Code, reference.RefId)))
            .Where(reference => reference.Carrier is { } carrier && rows.Contains(carrier)).Distinct().ToList();
        var planIds = references.Select(reference => reference.PlanId).Distinct().ToList();
        var plans = await db.ActionPlans.AsNoTracking().Where(plan => planIds.Contains(plan.Id)).ToListAsync(token);
        var items = (await db.ActionPlanItems.AsNoTracking().Where(item => planIds.Contains(item.PlanId))
            .Select(item => new { item.PlanId, item.Status, item.Progress }).ToListAsync(token)).ToLookup(item => item.PlanId);
        var today = Formats.DateNumber(Formats.Now.ToString("dd/MM/yyyy"));
        return references.Join(plans, reference => reference.PlanId, plan => plan.Id, (reference, plan) => (new EvaluationPlanRow(reference.Carrier!.Value, plan.Id,
                plan.Number, plan.Title, plan.Status, ActionPlanRules.Overdue(plan.Status, plan.TargetDate, today),
                ActionPlanRules.Progress(items[plan.Id].Select(item => (item.Status, item.Progress))), plan.TargetDate, plan.OwnerName), plan))
            .OrderBy(one => one.Item1.EvaluationCarrierId).ThenByDescending(one => one.Item1.PlanId).ToList();
    }

    private static decimal? Number(JsonElement line, string name) =>
        line.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private static decimal? Round(decimal? value) => value is { } exact ? Math.Round(exact, 2, MidpointRounding.AwayFromZero) : null;

    private static string Text(decimal value) => value.ToString(value == Math.Round(value) ? "0" : "0.##", CultureInfo.InvariantCulture);

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;
}
