using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Ai;

/// <summary>
/// Which prompt answered a run. Every agent's instructions are written in code
/// and change only through a reviewed commit, so the commit this build was made
/// from names the exact prompt text — it is recorded on every run's start
/// (AI Agent Platform specification §17, "do not silently change production
/// prompts"). A local build without the commit says so rather than inventing one.
/// </summary>
public static class AiBuild
{
    public static string Commit { get; } = Read();

    public static string PromptVersion => Commit.Length > 0 ? "build:" + Commit[..Math.Min(12, Commit.Length)] : "build:local";

    private static string Read()
    {
        var info = typeof(AiBuild).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var plus = info.IndexOf('+');
        var commit = plus >= 0 ? info[(plus + 1)..] : "";
        return commit.All(char.IsAsciiLetterOrDigit) ? commit.ToLowerInvariant() : "";
    }
}

/// <summary>
/// The governance state one run is judged against: the platform's ceiling, the
/// agents' settings and what their recent runs say. Read once and asked many
/// times, so every gate in a run — the run itself, each step of a plan — sees
/// the same state.
/// </summary>
public sealed class GovernanceSnapshot(AgentRegistry agents, AgentSetting platform,
    IReadOnlyDictionary<string, AgentSetting> settings, IReadOnlyDictionary<string, AgentHealth> health, bool available)
{
    public AgentSetting Platform { get; } = platform;
    public bool Available { get; } = available;

    /// <summary>No rows, no history: every agent exactly as it was before governance existed.</summary>
    public static GovernanceSnapshot Defaults(AgentRegistry agents) =>
        new(agents, AgentGovernance.PlatformDefault, new Dictionary<string, AgentSetting>(), new Dictionary<string, AgentHealth>(), true);

    /// <summary>The state could not be read: every gate refuses, as the Operations switch does when its row cannot be read.</summary>
    public static GovernanceSnapshot Unavailable(AgentRegistry agents) =>
        new(agents, AgentGovernance.PlatformDefault, new Dictionary<string, AgentSetting>(), new Dictionary<string, AgentHealth>(), false);

    public AgentSetting SettingOf(AgentDefinition agent) =>
        settings.TryGetValue(agent.Id, out var stored) ? stored : AgentGovernance.Default(agent);

    public AgentHealth HealthOf(string agentId) => health.TryGetValue(agentId, out var known) ? known : AgentHealth.Unknown;

    public GovernanceGate Gate(AgentDefinition agent, bool flagEnabled, AgentNeed need) => !Available
        ? new(false, "governance_unavailable", "AI governance settings could not be read; nothing runs until they can.", AiAutonomy.Disabled)
        : AgentGovernance.Evaluate(agent, flagEnabled, SettingOf(agent), Platform, HealthOf(agent.Id), need);

    public GovernanceGate Gate(string agentId, bool flagEnabled, AgentNeed need) => agents.Find(agentId) is { } agent
        ? Gate(agent, flagEnabled, need)
        : new(false, "unknown_agent", "This agent is not registered.", AiAutonomy.Disabled);
}

public interface IAiGovernance
{
    Task<GovernanceSnapshot> SnapshotAsync(CancellationToken token);
}

public sealed record AgentUsageView(long InputTokens24h, long OutputTokens24h, decimal? Cost24h,
    long InputTokens30d, long OutputTokens30d, decimal? Cost30d);

public sealed record AgentGovernanceView(string Id, string Name, bool FlagEnabled, int MaxAutonomy, int Autonomy,
    int EffectiveAutonomy, bool ShadowMode, string Status, string EffectiveStatus, string Reason, int Revision,
    string UpdatedBy, DateTimeOffset? UpdatedAt, bool Stored, int Runs24h, int Failures24h, double? FailureRate24h,
    int ConsecutiveFailures, DateTimeOffset? LastSuccess, DateTimeOffset? LastFailure, int? AverageMs, string Breaker,
    AgentUsageView Usage,
    // The shadow comparison autonomy is raised on (§66): decisions answered or acted on in 30 days
    // where what a person did can be set against what the agent said, and how many agreed.
    int Compared30d = 0, int Matched30d = 0,
    // The Control Tower's on/off switch (29 Sep 2026): whether this row has one, whether the agent is on
    // (under AI__Enabled; Operations by its own switch), and the stored choice — null while the flag in
    // configuration decides. Pass is the agent's separate scheduled pass, "drafts" or "mail", or ""; PassOn is its own
    // switch, which runs nothing while the agent is off.
    bool Switchable = false, bool On = false, bool? Switch = null,
    string Pass = "", bool PassOn = false, bool? PassSwitch = null);

public sealed record PlatformGovernanceView(int Autonomy, bool ExecutionEnabled, bool Stopped, string Reason, int Revision,
    string UpdatedBy, DateTimeOffset? UpdatedAt, bool Stored);

/// <param name="AiEnabled">AI__Enabled — the server's own switch, above every agent's.</param>
public sealed record GovernanceReport(bool Available, bool CanManage, PlatformGovernanceView Platform,
    IReadOnlyList<AgentGovernanceView> Agents, string PromptVersion, string PriceCurrency, bool PricesConfigured,
    int BreakerDegradedAfter, int BreakerPauseAfter, int BreakerCoolDownMinutes, bool AiEnabled = true);

/// <summary>
/// The AI platform's governance, from the database: settings rows, the audit's
/// recent runs, the prices in configuration. It reads and it records changes an
/// administrator makes; it decides nothing — <see cref="AgentGovernance"/> does.
/// </summary>
public sealed class AiGovernanceService(ScmosDbContext db, AgentRegistry agents, AuditService audit,
    IOptions<AiOptions> options, TimeProvider clock, ILogger<AiGovernanceService> log, IOperationsControl? control = null) : IAiGovernance
{
    /// <summary>How far back the breaker and the day's health look.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);

    /// <summary>The most recent completions one snapshot reads — the breaker needs a handful per agent.</summary>
    private const int RecentRuns = 2000;

    public static bool CanView(AppUser? user) => AiPermissionPolicy.Authenticated(user) && AiPermissionPolicy.InternalUser(user!)
        && (user!.Role == Roles.Admin || user.Can(Capability.ViewAudit));

    public static bool CanManage(AppUser? user) => OperationsControlService.CanManage(user);

    public async Task<GovernanceSnapshot> SnapshotAsync(CancellationToken token)
    {
        try
        {
            var (platform, settings) = await SettingsAsync(token);
            var runs = await RecentAsync(token);
            var health = agents.All.ToDictionary(agent => agent.Id, agent => HealthOf(agent.Id, runs, null));
            return new GovernanceSnapshot(agents, platform, settings, health, true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception problem)
        {
            log.LogWarning(problem, "AI governance state unavailable; refusing runs until it can be read");
            return GovernanceSnapshot.Unavailable(agents);
        }
    }

    public async Task<GovernanceReport> ReportAsync(AppUser user, CancellationToken token)
    {
        var ai = options.Value;
        var prices = AgentGovernance.ParsePrices(ai.PriceList);
        try
        {
            var (platform, settings) = await SettingsAsync(token);
            var runs = await RecentAsync(token);
            var now = clock.GetUtcNow();
            var since = now - Window;
            var runIds = runs.Select(run => run.RunId).ToList();
            var started = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
            foreach (var chunk in runIds.Chunk(500))
            {
                var rows = await db.AiAuditLogs.AsNoTracking()
                    .Where(row => row.Event == "run_started" && chunk.Contains(row.RunId))
                    .Select(row => new { row.RunId, row.At }).ToListAsync(token);
                foreach (var row in rows) started[row.RunId] = row.At;
            }
            var since30 = now.AddDays(-30);
            var usage = await db.AiAuditLogs.AsNoTracking()
                .Where(row => row.At >= since30 && (row.InputTokens != null || row.OutputTokens != null))
                .GroupBy(row => new { row.AgentId, row.Model })
                .Select(group => new
                {
                    group.Key.AgentId,
                    group.Key.Model,
                    In30 = group.Sum(row => (long)(row.InputTokens ?? 0)),
                    Out30 = group.Sum(row => (long)(row.OutputTokens ?? 0)),
                    In24 = group.Sum(row => row.At >= since ? (long)(row.InputTokens ?? 0) : 0L),
                    Out24 = group.Sum(row => row.At >= since ? (long)(row.OutputTokens ?? 0) : 0L),
                })
                .ToListAsync(token);
            var agreement = await db.AiDecisions.AsNoTracking()
                .Where(row => row.HumanMatches != null && row.DecidedAt >= since30)
                .GroupBy(row => row.AgentId)
                .Select(group => new { AgentId = group.Key, Compared = group.Count(), Matched = group.Count(row => row.HumanMatches == true) })
                .ToDictionaryAsync(row => row.AgentId, row => (row.Compared, row.Matched), token);
            // Operations answers to its own switch where one is registered, as the orchestrator judges it.
            bool? operationsOn = control is null ? null : OperationsControlService.Effective(await control.ReadAsync(token));

            var views = agents.All.Select(agent =>
            {
                var setting = settings.TryGetValue(agent.Id, out var stored) ? stored : AgentGovernance.Default(agent);
                var flag = AgentRegistry.Enabled(agent, ai);
                var passFlag = AgentScanner.PassFlag(agent.Id, ai);
                var on = agent.Id == "operations-agent" && operationsOn is { } operations ? operations
                    : ai.Enabled && AgentGovernance.SwitchedOn(setting, flag);
                var health = HealthOf(agent.Id, runs, started);
                var mine = usage.Where(row => row.AgentId == agent.Id).ToList();
                // A model without a price makes the total unknown, not smaller: null, shown as "no price set".
                decimal? CostOf(bool month)
                {
                    var total = 0m;
                    foreach (var row in mine)
                    {
                        var (input, output) = month ? (row.In30, row.Out30) : (row.In24, row.Out24);
                        if (input == 0 && output == 0) continue;
                        if (AgentGovernance.Cost(row.Model, input, output, prices) is not { } cost) return null;
                        total += cost;
                    }
                    return total;
                }
                return new AgentGovernanceView(agent.Id, agent.Name, flag, (int)agent.MaxAutonomy,
                    (int)setting.Autonomy, (int)AgentGovernance.Effective(agent, setting, platform), setting.ShadowMode,
                    setting.Status, AgentGovernance.EffectiveStatus(setting, health), setting.Reason, setting.Revision,
                    setting.UpdatedBy, setting.UpdatedAt, setting.Stored, health.Runs, health.Failures, health.FailureRate,
                    health.ConsecutiveFailures, health.LastSuccess, health.LastFailure, health.AverageMs, health.Breaker.ToString(),
                    new AgentUsageView(mine.Sum(row => row.In24), mine.Sum(row => row.Out24), CostOf(month: false),
                        mine.Sum(row => row.In30), mine.Sum(row => row.Out30), CostOf(month: true)),
                    agreement.GetValueOrDefault(agent.Id).Compared, agreement.GetValueOrDefault(agent.Id).Matched,
                    AgentGovernance.Switchable(agent), on, setting.Enabled,
                    passFlag is null ? "" : agent.Id == Booking.BookingAgent.Id ? "mail" : "drafts",
                    passFlag is { } pass && (setting.PassEnabled ?? pass), passFlag is null ? null : setting.PassEnabled);
            }).ToList();

            return new GovernanceReport(true, CanManage(user), PlatformView(platform), views, AiBuild.PromptVersion,
                ai.PriceCurrency, prices.Count > 0, ai.BreakerDegradedAfter, ai.BreakerPauseAfter, ai.BreakerCoolDownMinutes, ai.Enabled);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception problem)
        {
            log.LogWarning(problem, "AI governance report unavailable");
            return new GovernanceReport(false, CanManage(user), PlatformView(AgentGovernance.PlatformDefault), [],
                AiBuild.PromptVersion, ai.PriceCurrency, prices.Count > 0, ai.BreakerDegradedAfter, ai.BreakerPauseAfter,
                ai.BreakerCoolDownMinutes, ai.Enabled);
        }
    }

    private static PlatformGovernanceView PlatformView(AgentSetting platform) => new((int)platform.Autonomy,
        AgentGovernance.ExecutionEnabled(platform), platform.Autonomy == AiAutonomy.Disabled, platform.Reason,
        platform.Revision, platform.UpdatedBy, platform.UpdatedAt, platform.Stored);

    /// <summary>
    /// An administrator's change to one agent's settings, or to the platform's
    /// ceiling (<see cref="AgentGovernance.PlatformId"/>). One audit row per
    /// field that changed, in the same save as the change; a stale revision is a
    /// conflict, never a silent overwrite.
    /// </summary>
    public async Task<string> SetAsync(AppUser user, string agentId, int autonomy, bool shadowMode, string? status,
        string? reason, int revision, CancellationToken token)
    {
        if (!CanManage(user)) return "forbidden";
        var platform = agentId == AgentGovernance.PlatformId;
        var agent = platform ? null : agents.Find(agentId);
        if (platform) { shadowMode = false; status ??= AgentGovernance.Active; }
        if (AgentGovernance.SettingProblem(agent, platform, autonomy, status, reason) is { } refused) return refused;
        if (platform && status != AgentGovernance.Active) return "invalid_status";
        if (revision < 0) return "conflict";
        try
        {
            var row = await db.AiAgentConfigs.SingleOrDefaultAsync(one => one.AgentId == agentId, token);
            var current = row is null
                ? platform ? AgentGovernance.PlatformDefault : AgentGovernance.Default(agent!)
                : Read(row);
            if ((row?.Revision ?? 0) != revision) return "conflict";
            var changes = new List<(string Field, string From, string To)>();
            if ((int)current.Autonomy != autonomy) changes.Add(("autonomy", $"L{(int)current.Autonomy}", $"L{autonomy}"));
            if (current.ShadowMode != shadowMode) changes.Add(("shadow_mode", current.ShadowMode ? "on" : "off", shadowMode ? "on" : "off"));
            if (current.Status != status) changes.Add(("status", current.Status, status!));
            if (changes.Count == 0) return "ok";

            var now = clock.GetUtcNow();
            if (row is null)
            {
                row = new AiAgentConfig { AgentId = agentId, Revision = 0 };
                db.AiAgentConfigs.Add(row);
            }
            row.Autonomy = autonomy;
            row.ShadowMode = shadowMode;
            row.Status = status!;
            row.Reason = reason!.Trim();
            row.Revision = checked(row.Revision + 1);
            row.UpdatedBy = user.Signature;
            row.UpdatedAt = now;
            var label = platform ? "AI platform" : agent!.Name;
            foreach (var (field, from, to) in changes)
                audit.Stage(user, AuditActions.Configure, "ai-agent-settings", agentId, label, field, from, to, row.Reason);
            await db.SaveChangesAsync(token);
            return "ok";
        }
        catch (DbUpdateException) { return "conflict"; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception problem)
        {
            log.LogWarning(problem, "AI governance setting for {Agent} was not saved", agentId);
            return "unavailable";
        }
    }

    /// <summary>
    /// The Control Tower's on/off switch for one agent (<paramref name="target"/> "agent") or for its separate
    /// pass ("pass"). Like <see cref="SetAsync"/>: an administrator, the revision read, one audit row in the
    /// same save. Nothing else on the row changes; a row made for the switch alone starts from the agent's
    /// defaults. The stored choice holds over the flag in configuration until it is switched again.
    /// </summary>
    public async Task<string> SwitchAsync(AppUser user, string agentId, string target, bool on, int revision, CancellationToken token)
    {
        if (!CanManage(user)) return "forbidden";
        if (agents.Find(agentId) is not { } agent) return "unknown_agent";
        if (target is not ("agent" or "pass")) return "invalid_target";
        var pass = target == "pass";
        var passFlag = AgentScanner.PassFlag(agent.Id, options.Value);
        if (!AgentGovernance.Switchable(agent) || (pass && passFlag is null)) return "not_switchable";
        if (revision < 0) return "conflict";
        try
        {
            var row = await db.AiAgentConfigs.SingleOrDefaultAsync(one => one.AgentId == agentId, token);
            if ((row?.Revision ?? 0) != revision) return "conflict";
            var current = row is null ? AgentGovernance.Default(agent) : Read(row);
            var stored = pass ? current.PassEnabled : current.Enabled;
            if (stored == on) return "ok";
            // The value it had, and where it came from: the switch, or configuration while nothing was stored.
            var was = stored is { } choice ? choice ? "on" : "off"
                : (pass ? passFlag!.Value : AgentRegistry.Enabled(agent, options.Value)) ? "on (configuration)" : "off (configuration)";

            if (row is null)
            {
                row = new AiAgentConfig
                {
                    AgentId = agentId, Autonomy = (int)current.Autonomy, ShadowMode = current.ShadowMode,
                    Status = current.Status, Reason = "", Revision = 0,
                };
                db.AiAgentConfigs.Add(row);
            }
            if (pass) row.PassEnabled = on; else row.Enabled = on;
            row.Revision = checked(row.Revision + 1);
            row.UpdatedBy = user.Signature;
            row.UpdatedAt = clock.GetUtcNow();
            audit.Stage(user, AuditActions.Configure, "ai-agent-settings", agentId, agent.Name, pass ? "pass_enabled" : "enabled",
                was, on ? "on" : "off", "AI Control Tower switch");
            await db.SaveChangesAsync(token);
            return "ok";
        }
        catch (DbUpdateException) { return "conflict"; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception problem)
        {
            log.LogWarning(problem, "AI switch for {Agent} was not saved", agentId);
            return "unavailable";
        }
    }

    private async Task<(AgentSetting Platform, Dictionary<string, AgentSetting> Settings)> SettingsAsync(CancellationToken token)
    {
        var rows = await db.AiAgentConfigs.AsNoTracking().ToListAsync(token);
        var settings = rows.Where(row => row.AgentId != AgentGovernance.PlatformId)
            .ToDictionary(row => row.AgentId, Read, StringComparer.Ordinal);
        var platform = rows.FirstOrDefault(row => row.AgentId == AgentGovernance.PlatformId) is { } stored
            ? Read(stored) : AgentGovernance.PlatformDefault;
        return (platform, settings);
    }

    private static AgentSetting Read(AiAgentConfig row) => new(row.AgentId, (AiAutonomy)Math.Clamp(row.Autonomy, 0, 4),
        row.ShadowMode, row.Status, row.Reason, row.Revision, row.UpdatedBy, row.UpdatedAt, Stored: true,
        Enabled: row.Enabled, PassEnabled: row.PassEnabled);

    private sealed record Completion(string AgentId, string RunId, string Status, DateTimeOffset At);

    private async Task<List<Completion>> RecentAsync(CancellationToken token)
    {
        var since = clock.GetUtcNow() - Window;
        return await db.AiAuditLogs.AsNoTracking()
            .Where(row => row.Event == "run_completed" && row.At >= since)
            .OrderByDescending(row => row.At).ThenByDescending(row => row.Id)
            .Take(RecentRuns)
            .Select(row => new Completion(row.AgentId, row.RunId, row.Status, row.At))
            .ToListAsync(token);
    }

    private AgentHealth HealthOf(string agentId, IReadOnlyList<Completion> runs, IReadOnlyDictionary<string, DateTimeOffset>? started)
    {
        var ai = options.Value;
        var mine = runs.Where(run => run.AgentId == agentId).ToList();
        var (consecutive, breaker) = AgentGovernance.Breaker(mine.Select(run => (run.Status, run.At)).ToList(),
            clock.GetUtcNow(), ai.BreakerDegradedAfter, ai.BreakerPauseAfter, TimeSpan.FromMinutes(ai.BreakerCoolDownMinutes));
        var failures = mine.Where(run => AgentGovernance.FailureStatuses.Contains(run.Status, StringComparer.Ordinal)).ToList();
        var successes = mine.Where(run => AgentGovernance.SuccessStatuses.Contains(run.Status, StringComparer.Ordinal)).ToList();
        int? average = null;
        if (started is not null)
        {
            var spans = mine.Where(run => started.ContainsKey(run.RunId))
                .Select(run => (run.At - started[run.RunId]).TotalMilliseconds).Where(ms => ms >= 0).ToList();
            if (spans.Count > 0) average = (int)Math.Round(spans.Average());
        }
        return new AgentHealth(mine.Count, failures.Count, consecutive, successes.FirstOrDefault()?.At,
            failures.FirstOrDefault()?.At, average, breaker);
    }
}
