namespace Scmos.Api.Rules;

/// <summary>
/// How a carrier's annual evaluation is scored (1 Oct 2026, Annual Evaluation Phase 4). Pure and decimal throughout;
/// nothing is rounded here — the screens round for display.
///
/// <list type="number">
/// <item><b>A KPI</b> scores through its campaign's bands: higher-is-better takes the best band whose threshold the value
/// reaches, lower-is-better the best band whose threshold it stays within, else the fallback. A manual KPI is the
/// assessor's own 0–100. A KPI with no usable figure is left out, never scored nought.</item>
/// <item><b>The system score</b> is the measured KPIs' scores weighted by their weights, over the weight that was measured —
/// unless that weight is under the campaign's minimum share, when there is no system score.</item>
/// <item><b>An evaluator's score</b> is their ratings (1–5, as rating ÷ 5 × 100) weighted by question; N/A is left out of
/// both sides, so the remaining questions carry the weight.</item>
/// <item><b>A department's score</b> is the mean of its evaluators' — ten people in Operation are one Operation.</item>
/// <item><b>The department part</b> is the departments' scores weighted by their campaign weights, over the departments
/// that answered.</item>
/// <item><b>The final score</b> is system × system weight + department part × human weight (both out of 100). With a part
/// missing whose weight is not nought there is no final score — the parts are kept, and the result says why.</item>
/// <item><b>The band</b> is the highest score band the final score reaches. A decision is never derived from it.</item>
/// </list>
/// </summary>
public static class EvaluationScoring
{
    public sealed record Band(decimal Threshold, decimal Score);

    /// <param name="Value">The figure, or null when the snapshot could not give one.</param>
    /// <param name="ManualScore">A manual KPI's assessed score, or null when nobody has assessed it.</param>
    public sealed record KpiInput(string Code, string Name, decimal Weight, string Method, string Direction, decimal FallbackScore,
        IReadOnlyList<Band> Bands, string Metric, string MetricStatus, decimal? Value, decimal? ManualScore);

    public sealed record KpiLine(string Code, string Name, decimal Weight, string Method, string Metric, string MetricStatus, decimal? Value,
        decimal? Score, bool Counted, string Why);

    public sealed record Rating(int QuestionId, int? Value);
    public sealed record Answered(int EvaluatorId, int DepartmentId, IReadOnlyList<Rating> Ratings);

    public sealed record DepartmentLine(int DepartmentId, decimal Weight, decimal? Score, int Responses, int Scored);

    public sealed record Outcome(IReadOnlyList<KpiLine> Kpis, decimal? SystemScore, decimal SystemWeightAvailable, decimal Coverage,
        IReadOnlyList<DepartmentLine> Departments, decimal? HumanScore, decimal? FinalScore, string Band, string Status, string Why);

    /* ---- one KPI ---- */

    public static decimal ScoreBands(string direction, IReadOnlyList<Band> bands, decimal fallback, decimal value)
    {
        if (direction == AnnualEvaluationRules.Lower)
        {
            foreach (var band in bands.OrderBy(band => band.Threshold))
                if (value <= band.Threshold) return band.Score;
            return fallback;
        }
        foreach (var band in bands.OrderByDescending(band => band.Threshold))
            if (value >= band.Threshold) return band.Score;
        return fallback;
    }

    public static KpiLine ScoreKpi(KpiInput kpi)
    {
        if (kpi.Method == AnnualEvaluationRules.Manual)
            return kpi.ManualScore is { } assessed
                ? new(kpi.Code, kpi.Name, kpi.Weight, kpi.Method, "", "", null, assessed, true, "ประเมินโดย Subcontract Management")
                : new(kpi.Code, kpi.Name, kpi.Weight, kpi.Method, "", "", null, null, false, "ยังไม่ได้ประเมิน");
        if (kpi.MetricStatus != AnnualEvaluationRules.Available || kpi.Value is null)
            return new(kpi.Code, kpi.Name, kpi.Weight, kpi.Method, kpi.Metric, kpi.MetricStatus, kpi.Value, null, false,
                kpi.MetricStatus == AnnualEvaluationRules.InsufficientData ? "ข้อมูลไม่พอ" : "ไม่มีข้อมูล");
        var score = ScoreBands(kpi.Direction, kpi.Bands, kpi.FallbackScore, kpi.Value.Value);
        return new(kpi.Code, kpi.Name, kpi.Weight, kpi.Method, kpi.Metric, kpi.MetricStatus, kpi.Value, score, true, "");
    }

    /* ---- the system part ---- */

    /// <returns>The score (null below the minimum coverage), the weight measured, and that weight as a share of the system weight.</returns>
    public static (decimal? Score, decimal Available, decimal Coverage) SystemScore(IReadOnlyList<KpiLine> lines, decimal systemWeight,
        decimal minimumCoverage)
    {
        var counted = lines.Where(line => line.Counted && line.Weight > 0).ToList();
        var available = counted.Sum(line => line.Weight);
        var coverage = systemWeight <= 0 ? 0 : available / systemWeight * 100m;
        if (available <= 0 || coverage < minimumCoverage) return (null, available, coverage);
        return (counted.Sum(line => line.Score!.Value * line.Weight) / available, available, coverage);
    }

    /* ---- the department part ---- */

    /// <summary>One evaluator's score from their ratings; null when every question they were asked was N/A.</summary>
    public static decimal? EvaluatorScore(IReadOnlyList<Rating> ratings, IReadOnlyDictionary<int, decimal> questionWeights)
    {
        var rated = ratings.Where(rating => rating.Value is >= 1 and <= 5 && questionWeights.TryGetValue(rating.QuestionId, out var weight) && weight > 0)
            .ToList();
        var weight = rated.Sum(rating => questionWeights[rating.QuestionId]);
        if (weight <= 0) return null;
        return rated.Sum(rating => rating.Value!.Value / 5m * 100m * questionWeights[rating.QuestionId]) / weight;
    }

    public static (IReadOnlyList<DepartmentLine> Departments, decimal? Score) HumanScore(IReadOnlyList<Answered> responses,
        IReadOnlyDictionary<int, decimal> questionWeights, IReadOnlyDictionary<int, decimal> departmentWeights)
    {
        var lines = new List<DepartmentLine>();
        foreach (var (department, weight) in departmentWeights.OrderBy(pair => pair.Key))
        {
            var mine = responses.Where(response => response.DepartmentId == department).ToList();
            var scores = mine.Select(response => EvaluatorScore(response.Ratings, questionWeights)).OfType<decimal>().ToList();
            lines.Add(new DepartmentLine(department, weight, scores.Count == 0 ? null : scores.Average(), mine.Count, scores.Count));
        }
        var scored = lines.Where(line => line.Score is not null && line.Weight > 0).ToList();
        var total = scored.Sum(line => line.Weight);
        return (lines, total <= 0 ? null : scored.Sum(line => line.Score!.Value * line.Weight) / total);
    }

    /* ---- together ---- */

    public static string BandOf(decimal? final, IReadOnlyList<(string Code, decimal MinScore)> bands) =>
        final is { } score ? bands.Where(band => band.MinScore <= score).OrderByDescending(band => band.MinScore).Select(band => band.Code).FirstOrDefault() ?? "" : "";

    public const string Calculated = "calculated";
    public const string Incomplete = "insufficient-data";

    /// <summary>
    /// The places a result is stored to (decimal(9,4)). The two parts are held to it before they are combined, so anybody
    /// recomputing the final score from the stored parts gets exactly the stored final score.
    /// </summary>
    public const int Places = 4;

    public static decimal? Held(decimal? value) => value is { } exact ? Math.Round(exact, Places, MidpointRounding.AwayFromZero) : null;

    public static Outcome Score(IReadOnlyList<KpiInput> kpis, decimal systemWeight, decimal humanWeight, decimal minimumCoverage,
        IReadOnlyList<Answered> responses, IReadOnlyDictionary<int, decimal> questionWeights, IReadOnlyDictionary<int, decimal> departmentWeights,
        IReadOnlyList<(string Code, decimal MinScore)> scoreBands)
    {
        var lines = kpis.Select(ScoreKpi).ToList();
        var (exactSystem, available, coverage) = SystemScore(lines, systemWeight, minimumCoverage);
        var (departments, exactHuman) = HumanScore(responses, questionWeights, departmentWeights);
        var system = Held(exactSystem);
        var human = Held(exactHuman);

        var missing = new List<string>();
        if (system is null && systemWeight > 0) missing.Add($"วัด KPI ได้ {coverage:0.##}% ของน้ำหนัก ต่ำกว่าขั้นต่ำ {minimumCoverage:0.##}%");
        if (human is null && humanWeight > 0) missing.Add("ยังไม่มีคะแนนจากแผนก");
        decimal? final = missing.Count > 0 ? null : Held((system ?? 0) * systemWeight / 100m + (human ?? 0) * humanWeight / 100m);
        return new Outcome(lines, system, available, coverage, departments, human, final, BandOf(final, scoreBands),
            missing.Count > 0 ? Incomplete : Calculated, string.Join(" · ", missing));
    }
}
