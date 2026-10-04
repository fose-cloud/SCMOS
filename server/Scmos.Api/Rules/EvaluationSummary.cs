using System.Globalization;
using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// An AI summary of a carrier's annual evaluation, held to its evidence (2 Oct 2026, Annual Evaluation Phase 12). Pure.
///
/// <para>
/// The model is given the evaluation as numbered facts — F1, F2 … — each one a figure of the snapshot, a line of the score
/// or a department's answers, and is asked to cite them. What it writes back is then checked line by line, here, rather
/// than trusted: a line is kept only when it cites at least one fact that exists, every number in it is a number of the
/// facts it cites, and it does not speak the language of a decision. Whatever fails is dropped and counted. So a summary
/// can be shorter than the model wrote, never more than the evidence says — and every line on screen points to its fact.
/// </para>
/// </summary>
public static partial class EvaluationSummary
{
    /// <param name="Kind">score · history · kpi · metric · department · question · comment</param>
    public sealed record Fact(string Id, string Kind, string Text);

    public const int MaxFacts = 60;
    public const int MaxComment = 200;

    /// <summary>The facts numbered in the order given, F1 first; empty texts left out, at most <see cref="MaxFacts"/>.</summary>
    public static IReadOnlyList<Fact> Number(IEnumerable<(string Kind, string Text)> facts) =>
        facts.Where(fact => fact.Text.Trim().Length > 0).Take(MaxFacts)
            .Select((fact, at) => new Fact($"F{(at + 1).ToString(CultureInfo.InvariantCulture)}", fact.Kind, OneLine(fact.Text))).ToList();

    /// <summary>An evaluator's comment as a fact quotes it: one line, at most <see cref="MaxComment"/> characters.</summary>
    public static string Quote(string comment)
    {
        var text = OneLine(comment);
        return text.Length > MaxComment ? text[..MaxComment] + "…" : text;
    }

    /// <summary>The facts a line cites — [F3], [F3][F7] or [F3, F7].</summary>
    public static IReadOnlyList<string> Cites(string line) =>
        Citation().Matches(line).SelectMany(match => FactId().Matches(match.Value).Select(id => "F" + id.Groups[1].Value)).Distinct().ToList();

    /// <summary>
    /// The numbers a text holds, as they are compared: thousands separators gone, leading and trailing zeros dropped and,
    /// when <paramref name="rounded"/>, a number with decimals also as it rounds to two, one and no places — a fact's
    /// 86.8095 lets a line say 86.81. A line's own numbers are taken as written. Fact ids are not numbers.
    /// </summary>
    public static IReadOnlySet<string> NumbersIn(string text, bool rounded = true)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Digits().Matches(FactId().Replace(text, " ")))
        {
            if (!decimal.TryParse(match.Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) continue;
            found.Add(Shape(value));
            if (rounded && value != decimal.Truncate(value))
                foreach (var places in new[] { 2, 1, 0 }) found.Add(Shape(Math.Round(value, places, MidpointRounding.AwayFromZero)));
        }
        return found;
    }

    public sealed record Grounding(IReadOnlyList<string> Kept, IReadOnlyList<string> Dropped);

    /// <summary>
    /// A field's lines, each kept only when it cites facts that exist, every number in it is one of theirs (or allowed —
    /// the campaign's year), and it holds none of the forbidden words (the decisions, which are management's).
    /// </summary>
    public static Grounding Ground(string? field, IReadOnlyList<Fact> facts, IReadOnlySet<string> allowed, IReadOnlyList<string> forbidden)
    {
        var known = facts.ToDictionary(fact => fact.Id, StringComparer.Ordinal);
        List<string> kept = [], dropped = [];
        foreach (var raw in (field ?? "").Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim().TrimStart('-', '•', '*').Trim();
            if (line.Length == 0) continue;
            var cites = Cites(line);
            var grounded = cites.Count > 0 && cites.All(known.ContainsKey)
                && !forbidden.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase));
            if (grounded)
            {
                var permitted = new HashSet<string>(allowed, StringComparer.Ordinal);
                foreach (var cite in cites) permitted.UnionWith(NumbersIn(known[cite].Text));
                grounded = NumbersIn(line, rounded: false).All(permitted.Contains);
            }
            (grounded ? kept : dropped).Add(line);
        }
        return new Grounding(kept, dropped);
    }

    /// <summary>
    /// The development stand-in for the model (AI:MockMode): each field quotes its facts verbatim, cited, so it passes the
    /// same check a real answer does — the score; the two best-scored KPIs at 50 or more; the weakest below 50, then what
    /// has no data; earlier years, or this one alone. No data leaves the server.
    /// </summary>
    public static (string Summary, string Strengths, string Improvements, string Trends) Mock(IReadOnlyList<Fact> facts)
    {
        string Lines(IEnumerable<Fact> chosen) => string.Join("\n", chosen.Select(fact => $"{fact.Text} [{fact.Id}]"));
        var score = facts.Where(fact => fact.Kind == "score").Take(1).ToList();
        if (score.Count == 0) score = facts.Take(1).ToList();
        var measured = facts.Where(fact => fact.Kind == "kpi").Select(fact => (Fact: fact, Score: KpiScore(fact.Text)))
            .Where(line => line.Score is not null).OrderByDescending(line => line.Score).ToList();
        var strengths = measured.Where(line => line.Score >= 50).Take(2).Select(line => line.Fact);
        var weakest = measured.Where(line => line.Score < 50).OrderBy(line => line.Score).Select(line => line.Fact);
        var missing = facts.Where(fact => fact.Kind is "metric" or "kpi" && fact.Text.Contains("ไม่มีข้อมูล"));
        var history = facts.Where(fact => fact.Kind == "history").Take(2).ToList();
        return (Lines(score), Lines(strengths), Lines(weakest.Concat(missing).Take(3)), Lines(history.Count > 0 ? history : score));
    }

    /// <summary>The score a KPI fact ends on, "… → คะแนน 82"; null for a KPI not counted.</summary>
    private static decimal? KpiScore(string text)
    {
        var match = KpiScoreText().Match(text);
        return match.Success ? decimal.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    private static string Shape(decimal value) => value.ToString("0.##########", CultureInfo.InvariantCulture);

    private static string OneLine(string text) => Regex.Replace((text ?? "").Trim(), @"\s+", " ");

    [GeneratedRegex(@"\[\s*F\d{1,3}(?:\s*,\s*F\d{1,3})*\s*\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"→ คะแนน (\d+(?:\.\d+)?)$")]
    private static partial Regex KpiScoreText();

    [GeneratedRegex(@"F(\d{1,3})")]
    private static partial Regex FactId();

    [GeneratedRegex(@"\d{1,3}(?:,\d{3})+(?:\.\d+)?|\d+(?:\.\d+)?")]
    private static partial Regex Digits();
}
