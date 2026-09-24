using System.Text.Json;
using Scmos.Api.Ai.Semantic;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai.Data;

/// <summary>The rule a figure was measured by, as the answer carries it — id, version, where in the code, what it means, what missing data does to it.</summary>
public sealed record DataRule(string Id, string Version, string Source, string Meaning, string MissingData);

/// <summary>What the question was narrowed to. Empty strings mean "not narrowed".</summary>
public sealed record DataFilters(string Customer, string Trucker, string Owner);

/// <summary>One carrier's line in the figure: its jobs, how many could be measured, how many met the rule.</summary>
public sealed record DataCarrier(string Carrier, int Total, int Measured, int OnTime, int Percent);

/// <summary>
/// The Data Agent's answer — Phase 2 of the AI platform (20 Sep 2026): the
/// department's own KPI, for one period and optional filters, with the
/// provenance a figure needs before anyone repeats it: which rule, which
/// version, how many records it was measured over, how many it could not
/// be, and when the register it was read from was last changed.
///
/// <para>
/// Every number is <see cref="KpiService"/>'s — the same code the KPI screen
/// shows — and nothing here is calculated by a model. A customer's contract
/// rule is <c>unknown</c> unless one has been registered; none has.
/// </para>
/// </summary>
public sealed record DataAnswer(
    string View, string Period, string PeriodLabel, DataFilters Filters,
    int Total, int Measured, int OnTime, int OnTimePercent,
    /// <summary>Records in scope the rule could not be applied to — no plan time, no arrival time, or no usable date.</summary>
    int NotAssessable,
    int Undated, int FormatErrors, int ActionRequired,
    IReadOnlyList<Counted> ByCategory,
    IReadOnlyList<DataCarrier> Carriers, int CarriersTotal, int Returned, bool Truncated,
    DataRule Rule, string CustomerContract,
    DateTimeOffset RetrievedAt, DateTimeOffset? SourceUpdatedAt, string Basis,
    string Source = "operation_jobs");

/// <summary>
/// Reuses <see cref="KpiService"/> through <see cref="IKpiReports"/>. No LLM
/// calculates a figure or sees a row. In a host with no KPI service (the
/// offline checks' minimal hosts) the service exists but is not connected,
/// and the registry binds no handler to it.
/// </summary>
public sealed class DataReadService(IKpiReports? kpi, TimeProvider clock)
{
    public const string Tool = "query_kpi";

    /// <summary>Whether a KPI service stands behind this read.</summary>
    public bool Connected => kpi is not null;
    public const string View = "kpi";
    public const int CarrierLimit = 50;
    public const string RuleId = "arrival.on_time";

    /// <summary>
    /// A period as the tool takes it — "2026", "2026-09" or "2026-09-20" —
    /// as the register's rule wants it (year, zero-padded month and day), or
    /// null when it is not a calendar period.
    /// </summary>
    public static Period? ParsePeriod(string? text)
    {
        var value = (text ?? "").Trim();
        if (value.Length == 4 && int.TryParse(value, out var y) && y is >= 2000 and <= 2100) return new(value, "", "");
        if (value.Length == 7 && value[4] == '-' && int.TryParse(value[..4], out y) && y is >= 2000 and <= 2100
            && int.TryParse(value[5..], out var m) && m is >= 1 and <= 12 && value[5..].Length == 2)
            return new(value[..4], value[5..], "");
        if (value.Length == 10 && value[4] == '-' && value[7] == '-' && int.TryParse(value[..4], out y) && y is >= 2000 and <= 2100
            && int.TryParse(value[5..7], out m) && m is >= 1 and <= 12 && int.TryParse(value[8..], out var d) && d >= 1
            && d <= DateTime.DaysInMonth(y, m))
            return new(value[..4], value[5..7], value[8..]);
        return null;
    }

    /// <summary>The period as a person reads it: "09/2026", "2026", "20/09/2026".</summary>
    public static string Label(Period period) =>
        period.Day.Length > 0 ? $"{period.Day}/{period.Month}/{period.Year}"
        : period.Month.Length > 0 ? $"{period.Month}/{period.Year}" : period.Year;

    /// <summary>A filter as the tool takes it: trimmed, bounded, plain, or empty.</summary>
    public static string CleanFilter(string? text)
    {
        var value = new string((text ?? "").Trim().Where(c => !char.IsControl(c)).ToArray());
        return value.Length > 120 ? value[..120] : value;
    }

    public async Task<DataAnswer> ReadAsync(string tool, JsonElement arguments, AiToolContext context, CancellationToken token)
    {
        if (tool != Tool) throw new InvalidOperationException("Unknown read tool.");
        if (!context.Scope.Team && string.IsNullOrWhiteSpace(context.Scope.OperatorId))
            throw new UnauthorizedAccessException("An explicit operator scope is required.");
        var period = ParsePeriod(arguments.GetProperty("period").GetString())
            ?? throw new InvalidOperationException("Invalid period.");
        var limit = arguments.GetProperty("limit").GetInt32();
        if (limit is < 1 or > CarrierLimit) throw new InvalidOperationException("Invalid read arguments.");
        var customer = arguments.TryGetProperty("customer", out var c) && c.ValueKind == JsonValueKind.String ? CleanFilter(c.GetString()) : "";
        var trucker = arguments.TryGetProperty("trucker", out var t) && t.ValueKind == JsonValueKind.String ? CleanFilter(t.GetString()) : "";
        // The scope is the server's: a restricted account reads its own jobs' figure, never the department's.
        var owner = context.Scope.Team ? "" : context.Scope.OperatorId!;

        if (kpi is null) throw new InvalidOperationException("No KPI service is connected.");
        var report = await kpi.BuildAsync(period, new KpiFilter(customer, trucker, owner), token);
        var carriers = report.Carriers.Take(limit)
            .Select(one => new DataCarrier(Text(one.Carrier), one.Total, one.Measured, one.OnTime, one.Percent)).ToList();
        var rule = BusinessRuleRegistry.Resolve(RuleId)
            ?? throw new InvalidOperationException("The on-time rule is not registered.");
        return new DataAnswer(View, PeriodText(period), Label(period), new(customer, trucker, owner),
            report.Total, report.OnTime.Base, report.OnTime.Met, report.OnTime.Percent,
            report.Total - report.OnTime.Base,
            report.Undated, report.FormatErrors, report.ActionRequired,
            report.ByCategory, carriers, report.Carriers.Count, carriers.Count, report.Carriers.Count > carriers.Count,
            new(rule.Id, rule.Version, rule.SourceMember, rule.Meaning, rule.MissingData),
            // The customer's registered term when the question named one that has one; otherwise the
            // department-wide 30-minute rule is what was applied, and the answer says so.
            CustomerTerms.ForCustomer(customer).Select(CustomerTerms.ContractLabel).FirstOrDefault()
                ?? $"department default: on time within {CustomerTerms.DefaultGraceMinutes} minutes of plan",
            context.AsOf ?? clock.GetUtcNow(), report.SourceUpdatedAt,
            $"SCMOS KpiService over JobRules.IsMeasurable / IsOnTime ({CustomerTerms.DefaultGraceMinutes}-minute department default unless a registered customer/job-type term matches — "
            + CustomerTerms.Describe() + "); figures calculated from scoped records, not model-generated");
    }

    private static string PeriodText(Period period) =>
        period.Day.Length > 0 ? $"{period.Year}-{period.Month}-{period.Day}"
        : period.Month.Length > 0 ? $"{period.Year}-{period.Month}" : period.Year;

    private static string Text(string text) => new(text.Take(80).Where(c => !char.IsControl(c)).ToArray());
}

public sealed class DataReadHandler(DataReadService service) : IAiReadToolHandler
{
    public async Task<JsonElement> ReadAsync(JsonElement arguments, AiToolContext context, CancellationToken token)
        => JsonSerializer.SerializeToElement(await service.ReadAsync(DataReadService.Tool, arguments, context, token));
}
