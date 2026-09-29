namespace Scmos.Api.Ai;

/// <summary>
/// How far an agent may act (AI Agent Platform specification §13). The flag
/// in configuration decides whether an agent exists at all; this decides what
/// it may do once it does. L0 is off, L1 reads, L2 recommends, L3 prepares an
/// action a person approves, L4 acts where a deterministic rule allows it.
/// </summary>
public enum AiAutonomy
{
    Disabled = 0,
    Observe = 1,
    Recommend = 2,
    ExecuteWithApproval = 3,
    RuleGovernedAutonomous = 4,
}

/// <summary>What a caller wants an agent to do — the gate is asked for exactly this.</summary>
public enum AgentNeed
{
    /// <summary>Read and answer a person's question.</summary>
    Run,
    /// <summary>Put a recommendation in front of a person.</summary>
    Recommend,
    /// <summary>Carry out an action a person has approved.</summary>
    ExecuteWithApproval,
    /// <summary>Carry out an action a rule allows, with nobody approving it.</summary>
    ExecuteAutonomously,
}

/// <summary>The circuit breaker, read from the agent's recent runs (§54).</summary>
public enum BreakerState
{
    Closed,
    /// <summary>Failing more than it should — still answering, shown DEGRADED.</summary>
    Degraded,
    /// <summary>Failing in a row, recently — refusing new runs until the cool-down passes.</summary>
    Open,
    /// <summary>The cool-down has passed after an open breaker — the next run is allowed and decides.</summary>
    HalfOpen,
}

/// <summary>An agent's stored settings, or its defaults when nobody has stored any.</summary>
/// <param name="Stored">Whether a row exists; false means the defaults below are the code's.</param>
/// <param name="Enabled">The Control Tower's on/off switch; null follows the flag in configuration.</param>
/// <param name="PassEnabled">The same for a separate scheduled pass (Communication drafts, Booking mail); null follows its flag.</param>
public sealed record AgentSetting(string AgentId, AiAutonomy Autonomy, bool ShadowMode, string Status,
    string Reason = "", int Revision = 0, string UpdatedBy = "", DateTimeOffset? UpdatedAt = null, bool Stored = false,
    bool? Enabled = null, bool? PassEnabled = null);

/// <summary>What the recent runs say about an agent.</summary>
public sealed record AgentHealth(int Runs, int Failures, int ConsecutiveFailures,
    DateTimeOffset? LastSuccess, DateTimeOffset? LastFailure, int? AverageMs, BreakerState Breaker)
{
    public static readonly AgentHealth Unknown = new(0, 0, 0, null, null, null, BreakerState.Closed);
    public double? FailureRate => Runs == 0 ? null : Math.Round((double)Failures / Runs, 3);
}

/// <summary>The gate's answer: allowed, or the code the caller shows and why.</summary>
public sealed record GovernanceGate(bool Allowed, string Code, string Reason, AiAutonomy Effective)
{
    public static GovernanceGate Allow(AiAutonomy effective) => new(true, "ok", "", effective);
}

/// <summary>
/// The governance rules of the AI platform — pure: settings, health and a
/// need in, a verdict out. <see cref="AiGovernanceService"/> reads the state;
/// the orchestrator, the Management plans, the document reader and the
/// Operations change pilot ask here before they act, so one rule decides for
/// all of them.
///
/// <para>
/// No row lifts an agent above the autonomy its code was built for
/// (<see cref="AgentDefinition.MaxAutonomy"/>). With no rows at all every
/// agent behaves exactly as it did before the settings existed — that is what
/// the defaults are for.
/// </para>
///
/// <para>
/// Since 29 Sep 2026 a row may also switch its agent on or off
/// (<see cref="SwitchedOn"/>), so turning an agent on no longer needs the
/// Portal and a restart. That is the one setting that widens what
/// configuration says, and it stops at the server's own switches:
/// <c>AI__Enabled</c> off still stops every agent, as
/// <c>AI__OperationsEmergencyDisabled</c> still stops Operations.
/// </para>
/// </summary>
public static class AgentGovernance
{
    /// <summary>The settings row that holds the platform's own ceiling — the execution kill switch (§15).</summary>
    public const string PlatformId = "platform";

    public const string Active = "ACTIVE";
    public const string Paused = "PAUSED";
    public const string Degraded = "DEGRADED";
    public const string Maintenance = "MAINTENANCE";
    public const string Disabled = "DISABLED";

    /// <summary>What an administrator may set. DEGRADED is the breaker's word, never stored.</summary>
    public static readonly string[] SettableStatuses = [Active, Paused, Maintenance, Disabled];

    /// <summary>Run statuses that are the platform failing — a provider down, a timeout, output that could not be used.</summary>
    public static readonly string[] FailureStatuses = ["failed", "timeout", "provider_unavailable", "source_unavailable", "invalid_tool", "audit_failed"];

    /// <summary>Run statuses that are the platform working, whatever the answer was.</summary>
    public static readonly string[] SuccessStatuses = ["succeeded", "clarification_required"];

    /// <summary>The platform's defaults: execution allowed, as it was before this row existed.</summary>
    public static AgentSetting PlatformDefault => new(PlatformId, AiAutonomy.RuleGovernedAutonomous, false, Active);

    public static AgentSetting Default(AgentDefinition agent) => new(agent.Id, agent.DefaultAutonomy, agent.DefaultShadow, Active);

    /// <summary>Whether the platform lets anything execute: its ceiling at L3 or above.</summary>
    public static bool ExecutionEnabled(AgentSetting platform) => platform.Autonomy >= AiAutonomy.ExecuteWithApproval;

    /// <summary>The autonomy an agent actually has: its setting, under the platform's ceiling and its own design limit.</summary>
    public static AiAutonomy Effective(AgentDefinition agent, AgentSetting setting, AgentSetting platform) =>
        (AiAutonomy)Math.Min((int)setting.Autonomy, Math.Min((int)platform.Autonomy, (int)agent.MaxAutonomy));

    /// <summary>The status a person sees: an administrator's word when it is not ACTIVE, otherwise what the breaker says.</summary>
    public static string EffectiveStatus(AgentSetting setting, AgentHealth health) =>
        setting.Status != Active ? setting.Status
        : health.Breaker == BreakerState.Open ? Paused
        : health.Breaker is BreakerState.Degraded or BreakerState.HalfOpen ? Degraded
        : Active;

    /// <summary>Whether an agent is switched on: the Control Tower's switch when one is stored, otherwise its flag in configuration.</summary>
    public static bool SwitchedOn(AgentSetting setting, bool flagEnabled) => setting.Enabled ?? flagEnabled;

    /// <summary>
    /// Agents the Control Tower's switch does not hold. Operations has had its own switch since
    /// 8 Sep (<see cref="OperationsControlService"/>) with readiness checks of its own; the other three
    /// have no executor and no pass behind them yet, so switching one on would run nothing.
    /// </summary>
    public static readonly string[] NotSwitchable = ["operations-agent", "rate-agent", "incident-agent", "compliance-agent"];

    public static bool Switchable(AgentDefinition agent) => !NotSwitchable.Contains(agent.Id, StringComparer.Ordinal);

    public static GovernanceGate Evaluate(AgentDefinition agent, bool flagEnabled, AgentSetting setting,
        AgentSetting platform, AgentHealth health, AgentNeed need)
    {
        var effective = Effective(agent, setting, platform);
        if (!SwitchedOn(setting, flagEnabled))
            return new(false, "agent_disabled", setting.Enabled == false
                ? "This specialist is switched off in the AI Control Tower." : "This specialist is disabled in configuration.", effective);
        if (platform.Autonomy == AiAutonomy.Disabled || platform.Status != Active)
            return new(false, "ai_stopped", "AI is stopped by an administrator. Core SCMOS remains available.", effective);
        switch (setting.Status)
        {
            case Paused: return new(false, "agent_paused", ReasonOr(setting, "This specialist is paused by an administrator."), effective);
            case Maintenance: return new(false, "agent_maintenance", ReasonOr(setting, "This specialist is under maintenance."), effective);
            case Disabled: return new(false, "agent_disabled", ReasonOr(setting, "This specialist is disabled by an administrator."), effective);
        }
        if (health.Breaker == BreakerState.Open)
            return new(false, "agent_circuit_open", $"This specialist failed {health.ConsecutiveFailures} times in a row and is resting; try again shortly.", effective);

        var (wanted, execute) = need switch
        {
            AgentNeed.Run => (AiAutonomy.Observe, false),
            AgentNeed.Recommend => (AiAutonomy.Recommend, false),
            AgentNeed.ExecuteWithApproval => (AiAutonomy.ExecuteWithApproval, true),
            _ => (AiAutonomy.RuleGovernedAutonomous, true),
        };
        if (execute && !ExecutionEnabled(platform))
            return new(false, "execution_disabled", "AI execution is switched off; the AI may read and recommend only.", effective);
        if (execute && setting.ShadowMode)
            return new(false, "shadow_mode", "This specialist is in shadow mode: it records what it would do and a person acts.", effective);
        if (effective < wanted)
            return new(false, effective == AiAutonomy.Disabled ? "agent_disabled" : "autonomy_insufficient",
                $"This specialist is set to L{(int)effective}; this needs L{(int)wanted}.", effective);
        return GovernanceGate.Allow(effective);
    }

    private static string ReasonOr(AgentSetting setting, string fallback) =>
        setting.Reason.Trim().Length > 0 ? $"{fallback} ({setting.Reason.Trim()})" : fallback;

    /// <summary>
    /// The breaker from an agent's completed runs, newest first. A run that
    /// failed for the platform's reasons counts; one the platform answered —
    /// even "please be clearer" — ends the count; one that never got as far
    /// (busy, cancelled, not connected) says nothing either way.
    /// </summary>
    public static (int Consecutive, BreakerState State) Breaker(IReadOnlyList<(string Status, DateTimeOffset At)> newestFirst,
        DateTimeOffset now, int degradedAfter, int pauseAfter, TimeSpan coolDown)
    {
        var consecutive = 0;
        DateTimeOffset? lastFailure = null;
        foreach (var (status, at) in newestFirst)
        {
            if (SuccessStatuses.Contains(status, StringComparer.Ordinal)) break;
            if (!FailureStatuses.Contains(status, StringComparer.Ordinal)) continue;
            consecutive++;
            lastFailure ??= at;
        }
        if (consecutive >= pauseAfter)
            return (consecutive, now - lastFailure!.Value < coolDown ? BreakerState.Open : BreakerState.HalfOpen);
        return (consecutive, consecutive >= degradedAfter ? BreakerState.Degraded : BreakerState.Closed);
    }

    /// <summary>
    /// What a change to a setting would be refused for, or null. The reason is
    /// required: every change is an audit row, and a row that cannot say why is
    /// half an answer.
    /// </summary>
    public static string? SettingProblem(AgentDefinition? agent, bool platform, int autonomy, string? status, string? reason)
    {
        if (!platform && agent is null) return "unknown_agent";
        if (autonomy is < 0 or > 4) return "invalid_autonomy";
        if (!platform && autonomy > (int)agent!.MaxAutonomy) return "autonomy_above_design";
        if (status is null || !SettableStatuses.Contains(status, StringComparer.Ordinal)) return "invalid_status";
        var why = (reason ?? "").Trim();
        if (why.Length is 0 or > 200 || why.Any(char.IsControl)) return "reason_required";
        return null;
    }

    /// <summary>
    /// The prices from <c>AI__PriceList</c> — "model=input/output; model=input/output",
    /// per million tokens, e.g. <c>gpt-4.1=2.00/8.00</c>. One setting, because a
    /// model name carries dots an app-setting key cannot. An entry that cannot be
    /// read is left out, never guessed; an empty list means cost is not shown.
    /// </summary>
    public static IReadOnlyDictionary<string, AiPrice> ParsePrices(string? list)
    {
        var prices = new Dictionary<string, AiPrice>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in (list ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length is 0 or > 100) continue;
            var amounts = parts[1].Split('/', StringSplitOptions.TrimEntries);
            if (amounts.Length != 2
                || !decimal.TryParse(amounts[0], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var input)
                || !decimal.TryParse(amounts[1], System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var output)
                || input < 0 || output < 0) continue;
            prices[parts[0]] = new AiPrice { InputPerMillion = input, OutputPerMillion = output };
        }
        return prices;
    }

    /// <summary>Estimated cost of tokens at a configured price per million; null when the model has no price (CONFIGURATION_REQUIRED).</summary>
    public static decimal? Cost(string model, long inputTokens, long outputTokens, IReadOnlyDictionary<string, AiPrice> prices) =>
        prices.TryGetValue(model, out var price)
            ? decimal.Round((inputTokens * price.InputPerMillion + outputTokens * price.OutputPerMillion) / 1_000_000m, 4)
            : null;
}

/// <summary>A model's price per million tokens, in <see cref="AiOptions.PriceCurrency"/> — configuration, never guessed.</summary>
public sealed class AiPrice
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
}
