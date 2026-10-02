using System.Globalization;

namespace Scmos.Api.Rules;

/// <summary>
/// What a carrier's annual evaluation found short, as the starting point of its development plan (2 Oct 2026, Annual
/// Evaluation Phase 10). Pure.
///
/// <para>
/// A KPI is short when its score is below full marks: its target is the value of the band that gives the most, so OTD's is
/// 100% and a lower-is-better KPI's is the most it may reach and still score in full. A department question is short when
/// its evaluators' average rating is under "good" (<see cref="QuestionTarget"/>). Findings are ordered by what they cost:
/// KPIs by the points of the final score they lost, then questions from the lowest average. Nothing here creates a plan —
/// the plan is the Action Plan module's, opened with these filled in for a person to change.
/// </para>
/// </summary>
public static class EvaluationFindings
{
    /// <summary>A question averaging under 4 ("good") is a finding.</summary>
    public const decimal QuestionTarget = 4m;

    /// <summary>The most findings a draft plan carries as its first steps.</summary>
    public const int MaxSteps = 5;

    /// <param name="Target">The value that scores full marks; null for a manual KPI or one with no bands.</param>
    public sealed record KpiBasis(string Code, string Name, decimal Weight, string Direction, string Method, decimal? Value, decimal? Score,
        bool Counted, decimal? Target);

    public sealed record QuestionBasis(string Code, string Text, decimal? Average, int Ratings);

    /// <param name="Kind">kpi · question</param>
    /// <param name="PointsLost">Points of the final hundred a KPI lost; nought for a question, which is ordered by its average.</param>
    public sealed record Finding(string Kind, string Code, string Name, decimal? Value, decimal? Target, decimal? Score, decimal PointsLost, string Text);

    public static IReadOnlyList<Finding> Of(IReadOnlyList<KpiBasis> kpis, IReadOnlyList<QuestionBasis> questions)
    {
        var found = kpis.Where(kpi => kpi.Counted && kpi.Score is < 100m && kpi.Weight > 0)
            .Select(kpi =>
            {
                var lost = Math.Round((100m - kpi.Score!.Value) * kpi.Weight / 100m, 4, MidpointRounding.AwayFromZero);
                var text = kpi.Method == AnnualEvaluationRules.Manual || kpi.Value is null
                    ? $"{kpi.Name}: ประเมินได้ {Number(kpi.Score.Value)}/100"
                    : $"{kpi.Name}: {Number(kpi.Value.Value)}{(kpi.Target is { } target ? $" (เป้า {(kpi.Direction == AnnualEvaluationRules.Lower ? "≤" : "≥")} {Number(target)})" : "")} — ได้ {Number(kpi.Score.Value)} คะแนน";
                return new Finding("kpi", kpi.Code, kpi.Name, kpi.Value, kpi.Target, kpi.Score, lost, text);
            })
            .OrderByDescending(finding => finding.PointsLost).ThenBy(finding => finding.Code, StringComparer.Ordinal).ToList();
        found.AddRange(questions.Where(question => question.Ratings > 0 && question.Average is { } average && average < QuestionTarget)
            .OrderBy(question => question.Average).ThenBy(question => question.Code, StringComparer.Ordinal)
            .Select(question => new Finding("question", question.Code, question.Text, question.Average, QuestionTarget, null, 0m,
                $"{question.Text}: ผู้ประเมินให้เฉลี่ย {Number(question.Average!.Value)}/5 (เป้า {Number(QuestionTarget)}) จาก {question.Ratings} คน")));
        return found;
    }

    /// <summary>The value of the band that scores the most — the KPI's target; null when it has no bands.</summary>
    public static decimal? TargetOf(IReadOnlyList<EvaluationScoring.Band> bands, string direction)
    {
        if (bands.Count == 0) return null;
        var best = bands.Max(band => band.Score);
        var top = bands.Where(band => band.Score == best).Select(band => band.Threshold).ToList();
        // Several thresholds giving full marks: the easiest of them is what has to be reached.
        return direction == AnnualEvaluationRules.Lower ? top.Max() : top.Min();
    }

    /// <summary>The Action Plan category a carrier's main finding belongs to — the department's own list.</summary>
    public static string CategoryOf(Finding? main) => main is { Kind: "kpi" } ? main.Code switch
    {
        "safety" => "Safety Improvement",
        "claim" => "Quality Improvement",
        "billing" => "Billing Improvement",
        "pricing" => "Cost Improvement",
        "documents" => "Documentation Improvement",
        "certification" => "Compliance Improvement",
        _ => "Carrier Performance Development",
    } : "Carrier Performance Development";

    /// <summary>How urgent the plan is, from the decision that called for it.</summary>
    public static string PriorityOf(string decision) => decision switch
    {
        EvaluationReview.CorrectiveAction => "high",
        EvaluationReview.SuspendNewAllocation or EvaluationReview.Inactive => "critical",
        _ => "medium",
    };

    /// <summary>How a plan names the evaluation it came from: the campaign's code and the carrier's row in it.</summary>
    public static string ReferenceOf(string campaignCode, int evaluationCarrierId) =>
        $"{campaignCode}:{evaluationCarrierId.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The carrier's row a reference names, when it names one of this campaign's.</summary>
    public static int? CarrierOf(string campaignCode, string refId) =>
        refId.StartsWith(campaignCode + ":", StringComparison.Ordinal)
        && int.TryParse(refId.AsSpan(campaignCode.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    private static string Number(decimal value) => value.ToString(value == Math.Round(value) ? "0" : "0.##", CultureInfo.InvariantCulture);
}
