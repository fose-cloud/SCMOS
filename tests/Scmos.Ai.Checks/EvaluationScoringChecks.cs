using Scmos.Api.Rules;

/// <summary>
/// Annual Evaluation Phase 4 (1 Oct 2026): the scoring arithmetic, on figures that can be worked out by hand — the
/// band edges, the share of weight that was measured, N/A, one department of ten against one of one, and the final
/// score in decimals.
/// </summary>
static class EvaluationScoringChecks
{
    public static void Run(Action<bool, string> check)
    {
        EvaluationScoring.Band[] otd = [new(100, 100), new(96, 75), new(91, 50), new(85, 25)];
        decimal Otd(decimal value) => EvaluationScoring.ScoreBands(AnnualEvaluationRules.Higher, otd, 0, value);
        check(Otd(100m) == 100 && Otd(99.99m) == 75 && Otd(96m) == 75 && Otd(95.99m) == 50 && Otd(91m) == 50 && Otd(90.99m) == 25
            && Otd(85m) == 25 && Otd(84.99m) == 0,
            "scoring: the OTD bands at their edges — 100 · 96–99.99 · 91–95.99 · 85–90.99 · below 85");
        EvaluationScoring.Band[] incidents = [new(0, 100), new(1, 60)];
        decimal Safety(decimal value) => EvaluationScoring.ScoreBands(AnnualEvaluationRules.Lower, incidents, 0, value);
        check(Safety(0m) == 100 && Safety(0.5m) == 60 && Safety(1m) == 60 && Safety(1.01m) == 0,
            "scoring: a lower-is-better KPI takes the best band it stays within");

        EvaluationScoring.KpiInput Kpi(string code, decimal weight, string status, decimal? value, EvaluationScoring.Band[]? bands = null,
            string method = AnnualEvaluationRules.Band, decimal? manual = null) =>
            new(code, code, weight, method, AnnualEvaluationRules.Higher, 0, bands ?? [], code, status, value, manual);
        var kpis = new List<EvaluationScoring.KpiInput>
        {
            Kpi("otd", 20, AnnualEvaluationRules.Available, 97m, otd),
            Kpi("safety", 15, AnnualEvaluationRules.NotAvailable, null),
            Kpi("claim", 10, AnnualEvaluationRules.InsufficientData, 50m, [new(0, 100)]),
            Kpi("billing", 10, AnnualEvaluationRules.Available, 100m, [new(98, 100), new(90, 60)]),
            Kpi("pricing", 5, "", null, method: AnnualEvaluationRules.Manual, manual: 80m),
            Kpi("documents", 5, AnnualEvaluationRules.NotAvailable, null),
            Kpi("certification", 5, AnnualEvaluationRules.Available, 60m, [new(100, 100), new(80, 50)]),
        };
        var lines = kpis.Select(EvaluationScoring.ScoreKpi).ToList();
        var (system, available, coverage) = EvaluationScoring.SystemScore(lines, 70, 50);
        var (strict, _, _) = EvaluationScoring.SystemScore(lines, 70, 60);
        check(available == 40 && system == 72.5m && Math.Round(coverage, 4) == 57.1429m && strict is null
            && !lines.Single(line => line.Code == "claim").Counted && lines.Single(line => line.Code == "certification").Score == 0,
            "scoring: the system score is over the weight measured (40 of 70 → 72.5); under the minimum share there is none; insufficient data is left out, not nought");

        var weights = new Dictionary<int, decimal> { [1] = 5, [2] = 5, [3] = 5 };
        check(EvaluationScoring.EvaluatorScore([new(1, 5), new(2, null), new(3, 3)], weights) == 80m
            && EvaluationScoring.EvaluatorScore([new(1, null), new(2, null)], weights) is null,
            "scoring: N/A is left out of both sides — 5 and 3 with one N/A is 80, not 53.3; all N/A is no score");

        const int ops = 1, ehsq = 2;
        var answered = Enumerable.Range(1, 10).Select(evaluator => new EvaluationScoring.Answered(evaluator, ops, [new(1, 5), new(2, 5), new(3, 5)]))
            .Append(new EvaluationScoring.Answered(11, ehsq, [new(1, 1), new(2, 1), new(3, 1)])).ToList();
        var (departments, human) = EvaluationScoring.HumanScore(answered, weights, new Dictionary<int, decimal> { [ops] = 1, [ehsq] = 1 });
        check(human == 60m && departments.Single(line => line.DepartmentId == ops).Responses == 10
            && departments.Single(line => line.DepartmentId == ehsq).Score == 20m,
            "scoring: ten Operation evaluators are one Operation — (100 + 20) ÷ 2 = 60, not 92.7");

        var bands = new List<(string, decimal)> { ("excellent", 90), ("good", 80), ("pass", 60), ("review", 0) };
        var final = EvaluationScoring.Score([Kpi("pricing", 70, "", null, method: AnnualEvaluationRules.Manual, manual: 86m)], 70, 30, 50,
            [new(1, ops, [new(1, 5), new(2, 4)])], new Dictionary<int, decimal> { [1] = 11, [2] = 9 }, new Dictionary<int, decimal> { [ops] = 1 }, bands);
        check(final.SystemScore == 86m && final.HumanScore == 91m && final.FinalScore == 87.50m && final.Band == "good"
            && final.Status == EvaluationScoring.Calculated,
            "scoring: 86 × 0.70 + 91 × 0.30 = 87.50 exactly, in decimals, and falls in the band it reaches");

        var noHuman = EvaluationScoring.Score([Kpi("pricing", 70, "", null, method: AnnualEvaluationRules.Manual, manual: 86m)], 70, 30, 50,
            [], new Dictionary<int, decimal>(), new Dictionary<int, decimal> { [ops] = 1 }, bands);
        var systemOnly = EvaluationScoring.Score([Kpi("pricing", 100, "", null, method: AnnualEvaluationRules.Manual, manual: 59.99m)], 100, 0, 50,
            [], new Dictionary<int, decimal>(), new Dictionary<int, decimal>(), bands);
        check(noHuman.FinalScore is null && noHuman.SystemScore == 86m && noHuman.Status == EvaluationScoring.Incomplete && noHuman.Band == ""
            && systemOnly.FinalScore == 59.99m && systemOnly.Band == "review",
            "scoring: no department answers means no final score while their weight counts; a part weighted nought is not waited for");
    }
}
