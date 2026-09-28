using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai.Otd;
using Scmos.Api.Ai.Validation;
using Scmos.Api.Data;

namespace Scmos.Api.Ai;

/// <param name="Code">ok, or why the agent did not run — the governance gate's code.</param>
public sealed record ScanSummary(string AgentId, string Code, int Jobs, int Findings, int Created, int Unchanged,
    int Superseded, int Resolved, int Refused);

/// <summary>
/// The rule-first agents' pass over the register (AI Agent Platform
/// specification §18, §74): the event router in the shape SCMOS already
/// uses — a scheduled scan of state, as the corrections, LINE and webhooks
/// do — rather than a broker nobody here runs.
///
/// <para>
/// Each pass asks governance first (the agent's flag, the platform's
/// execution switch, its own autonomy, status and breaker — a recommendation
/// needs L2); an agent refused writes nothing and closes nothing. What it
/// concludes goes to the decision log through <see cref="AiDecisionLog.Stage"/>,
/// so nothing unchecked is stored, and the log keeps the history: a finding
/// seen again is left as it is, a different finding supersedes it, a finding
/// gone is resolved, and one a person already answered is not raised again
/// until it changes.
/// </para>
/// </summary>
public sealed class AgentScanner(ScmosDbContext db, JobRegisterCache register, AgentRegistry agents, IAiGovernance governance,
    AiDecisionLog decisions, IOptions<AiOptions> options, TimeProvider clock, ILogger<AgentScanner> log)
{
    /// <summary>The rule-first agents this pass runs, in order.</summary>
    public static readonly string[] Agents = [OtdAgent.Id, ValidationAgent.Id];

    public async Task<IReadOnlyList<ScanSummary>> ScanAsync(CancellationToken token, string? only = null)
    {
        var ai = options.Value;
        var snapshot = await governance.SnapshotAsync(token);
        var now = clock.GetUtcNow();
        JobRegisterSnapshot? jobs = null;
        var summaries = new List<ScanSummary>();
        foreach (var id in Agents.Where(id => only is null || id == only))
        {
            var agent = agents.Find(id)!;
            var gate = snapshot.Gate(agent, ai.Enabled && AgentRegistry.Enabled(agent, ai), AgentNeed.Recommend);
            if (!gate.Allowed) { summaries.Add(new(id, gate.Code, 0, 0, 0, 0, 0, 0, 0)); continue; }
            // A background pass can wait for the register; it never takes the stale-while-revalidate
            // answer a person's screen does, so it judges "now" against the register as it is.
            jobs ??= await register.ReadAsync(token);
            var setting = snapshot.SettingOf(agent);
            summaries.Add(await PassAsync(agent, jobs, now, setting.ShadowMode, gate.Effective, token));
        }
        return summaries;
    }

    private AgentResult? Assess(string agentId, CachedJobRow row, DateTimeOffset now) => agentId switch
    {
        OtdAgent.Id => OtdAgent.Assess(row, now, options.Value.OtdWatchMinutes, options.Value.OtdHighMinutes),
        ValidationAgent.Id => ValidationAgent.Assess(row, now),
        _ => null,
    };

    private async Task<ScanSummary> PassAsync(AgentDefinition agent, JobRegisterSnapshot jobs, DateTimeOffset now,
        bool shadow, AiAutonomy autonomy, CancellationToken token)
    {
        var findings = new Dictionary<string, (AgentResult Result, string Owner)>(StringComparer.Ordinal);
        foreach (var row in jobs.Rows)
            if (Assess(agent.Id, row, now) is { } result) findings[result.EntityId] = (result, row.Record?.OpId ?? "");

        var open = await db.AiDecisions.Where(one => one.AgentId == agent.Id && one.Status == AiDecisionLog.Open).ToListAsync(token);
        var openByEntity = open.GroupBy(one => one.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(one => one.Id).ToList(), StringComparer.Ordinal);

        // What a person already answered, for the findings with nothing open: the same finding is not raised again.
        var unopened = findings.Keys.Where(key => !openByEntity.ContainsKey(key)).ToList();
        var answered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in unopened.Chunk(500))
        {
            var rows = await db.AiDecisions.AsNoTracking()
                .Where(one => one.AgentId == agent.Id && chunk.Contains(one.EntityId)
                    && (one.Status == AiDecisionLog.Accepted || one.Status == AiDecisionLog.Overridden || one.Status == AiDecisionLog.Dismissed))
                .Select(one => new { one.Id, one.EntityId, one.Fingerprint }).ToListAsync(token);
            foreach (var latest in rows.GroupBy(one => one.EntityId).Select(group => group.MaxBy(one => one.Id)!))
                answered[latest.EntityId] = latest.Fingerprint;
        }

        int created = 0, unchanged = 0, superseded = 0, resolved = 0, refused = 0;
        void Close(AiDecision row, string status, string note)
        {
            row.Status = status;
            row.DecidedAt = now;
            row.DecidedBy = "system";
            row.OverrideReason = note;
        }
        foreach (var (key, (result, owner)) in findings)
        {
            var fingerprint = AiDecisionLog.FingerprintOf(result);
            if (openByEntity.TryGetValue(key, out var current))
            {
                if (current[0].Fingerprint == fingerprint) { unchanged++; foreach (var extra in current.Skip(1)) { Close(extra, AiDecisionLog.Superseded, "ซ้ำ"); superseded++; } continue; }
                foreach (var old in current) { Close(old, AiDecisionLog.Superseded, "ผลการตรวจรอบใหม่ต่างจากเดิม"); superseded++; }
            }
            else if (answered.TryGetValue(key, out var seen) && seen == fingerprint) { unchanged++; continue; }
            var (row, problems) = decisions.Stage(result, "", owner.Length <= 20 ? owner : "", shadow, autonomy);
            if (row is null)
            {
                refused++;
                log.LogWarning("{Agent} finding on {Key} refused by the decision contract: {Problems}", agent.Id, key, string.Join("; ", problems));
                continue;
            }
            row.CreatedAt = now;
            created++;
        }
        foreach (var (key, rows) in openByEntity)
        {
            if (findings.ContainsKey(key)) continue;
            foreach (var row in rows) { Close(row, AiDecisionLog.Resolved, "รอบตรวจใหม่ไม่พบประเด็นนี้แล้ว"); resolved++; }
        }
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateConcurrencyException)
        {
            // A person answered one of these decisions while the pass ran. Their answer stands;
            // this pass writes nothing, and the next one starts from what they decided.
            db.ChangeTracker.Clear();
            log.LogInformation("{Agent}: a decision was answered during the pass; nothing written this time", agent.Id);
            return new ScanSummary(agent.Id, "answered_meanwhile", jobs.Rows.Count, findings.Count, 0, 0, 0, 0, refused);
        }
        if (created + superseded + resolved > 0)
            log.LogInformation("{Agent}: {Created} new, {Superseded} superseded, {Resolved} resolved, {Unchanged} unchanged over {Jobs} jobs",
                agent.Id, created, superseded, resolved, unchanged, jobs.Rows.Count);
        return new ScanSummary(agent.Id, "ok", jobs.Rows.Count, findings.Count, created, unchanged, superseded, resolved, refused);
    }
}

/// <summary>Runs the rule-first agents every <c>AI__AgentScanMinutes</c> (0 turns it off). Governance decides whether each writes anything.</summary>
public sealed class AgentScanScheduler(IServiceProvider services, IOptions<AiOptions> options, ILogger<AgentScanScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var minutes = options.Value.AgentScanMinutes;
        if (minutes <= 0) return;
        try { await Task.Delay(TimeSpan.FromMinutes(4), stopping); }
        catch (OperationCanceledException) { return; }
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<AgentScanner>().ScanAsync(stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception problem) { log.LogError(problem, "Agent scan failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }
}
