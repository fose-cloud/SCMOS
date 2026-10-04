using System.Text.Json;
using Microsoft.Extensions.Options;
using Scmos.Api.Ai.Booking;
using Scmos.Api.Ai.Carrier;
using Scmos.Api.Ai.Communication;
using Scmos.Api.Ai.Otd;
using Scmos.Api.Ai.Validation;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

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
///
/// <para>
/// For the Carrier Agent a closed decision also records what a person did
/// instead: the first carrier asked after it was raised (or, with no request,
/// the carrier now on the job), and whether that was the one recommended.
/// </para>
/// </summary>
public sealed class AgentScanner(AiPassRepository data, JobRegisterCache register, AgentRegistry agents, IAiGovernance governance,
    AiDecisionLog decisions, SupplierService suppliers, IOptions<AiOptions> options, TimeProvider clock, ILogger<AgentScanner> log,
    BookingMailPass? bookingMail = null, IAiPolicyGateway? policyGateway = null)
{
    /// <summary>The rule-first agents this pass runs, in order.</summary>
    public static readonly string[] Agents = [OtdAgent.Id, ValidationAgent.Id, CarrierAgent.Id, CommunicationAgent.Id, BookingAgent.Id];

    /// <summary>
    /// The flag of the agent's own pass where it has a separate one, or null where the agent's switch is
    /// its pass's. The Communication Agent's flag already runs its chat read in production, so its drafts
    /// need their own switch as well; the Booking Agent's flag turns on the pasted-text read, and its pass
    /// over mail has a switch of its own.
    /// </summary>
    public static bool? PassFlag(string agentId, AiOptions ai) => agentId switch
    {
        CommunicationAgent.Id => ai.CommunicationDraftsEnabled,
        BookingAgent.Id => ai.BookingMailEnabled,
        _ => null,
    };

    /// <summary>
    /// Whether the agent's pass is switched on: AI__Enabled, then the agent — and its separate pass, where it
    /// has one — by the Control Tower's switch when one is stored, otherwise by the flag in configuration.
    /// </summary>
    public static bool Switched(AgentDefinition agent, AiOptions ai, AgentSetting setting) => ai.Enabled
        && AgentGovernance.SwitchedOn(setting, AgentRegistry.Enabled(agent, ai))
        && (PassFlag(agent.Id, ai) is not { } pass || (setting.PassEnabled ?? pass));

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
            var setting = snapshot.SettingOf(agent);
            // AI__Enabled first, the server's own switch; the gate holds the agent's; a separate pass's switch is the pass's own.
            var gate = ai.Enabled ? snapshot.Gate(agent, AgentRegistry.Enabled(agent, ai), AgentNeed.Recommend)
                : new GovernanceGate(false, "agent_disabled", "SCMOS AI is disabled in configuration.", AiAutonomy.Disabled);
            if (gate.Allowed && !Switched(agent, ai, setting)) gate = new(false, "agent_disabled", "This pass is switched off.", gate.Effective);
            if (!gate.Allowed) { summaries.Add(new(id, gate.Code, 0, 0, 0, 0, 0, 0, 0)); continue; }
            var (action, tool) = AiPolicyEntry.PassContract(id);
            var authorization = await AiPolicyEntry.AuthorizeAsync(policyGateway, AiAuthorizationRequest.Pass(id, action, tool), token);
            if (!authorization.Allowed) { summaries.Add(new(id, authorization.ReasonCode, 0, 0, 0, 0, 0, 0, 0)); continue; }
            // A background pass can wait for the register; it never takes the stale-while-revalidate
            // answer a person's screen does, so it judges "now" against the register as it is.
            jobs ??= await register.ReadAsync(token);
            if (agent.Id == BookingAgent.Id)
            {
                // Mail, not the register: the booking pass keeps its own lifecycle (read once, never re-read).
                var customers = RegisterCustomers.Of(jobs.Rows);
                summaries.Add(bookingMail is null ? new(id, "not_connected", 0, 0, 0, 0, 0, 0, 0)
                    : await bookingMail.PassAsync(agent, setting.ShadowMode, gate.Effective, customers, token));
                continue;
            }
            summaries.Add(await PassAsync(agent, jobs, now, setting.ShadowMode, gate.Effective, token));
        }
        return summaries;
    }

    /// <summary>The agent's judgement of one row, with whatever it needs read once for the whole pass.</summary>
    private async Task<Func<CachedJobRow, Task<AgentResult?>>> AssessorAsync(string agentId, JobRegisterSnapshot jobs,
        DateTimeOffset now, CancellationToken token)
    {
        var ai = options.Value;
        switch (agentId)
        {
            case OtdAgent.Id: return row => Task.FromResult(OtdAgent.Assess(row, now, ai.OtdWatchMinutes, ai.OtdHighMinutes));
            case ValidationAgent.Id: return row => Task.FromResult(ValidationAgent.Assess(row, now));
            case CarrierAgent.Id:
                var context = await CarrierContextAsync(jobs, now, token);
                return row => Task.FromResult(CarrierAgent.Assess(row, now, context));
            case CommunicationAgent.Id:
                var communication = await CommunicationContextAsync(jobs, now, token);
                return row => AiGateway.DraftCommunicationAsync(policyGateway,
                    AiAuthorizationRequest.Pass(AgentIds.Communication, AiAction.CommunicationDraft, "scan_communication")
                        with { ResourceType = "shipment", ResourceId = row.Key }, row, communication, now, token);
            default: return _ => Task.FromResult<AgentResult?>(null);
        }
    }

    /// <summary>
    /// The requests already made for the jobs in the Carrier Agent's scope, the
    /// workflow's order for their customers — ranked from this same register
    /// read, over the same rows <see cref="WorkflowService.PriorityForAsync"/>
    /// selects — and the Supplier Register as the workflow resolves it.
    /// </summary>
    private async Task<CarrierContext> CarrierContextAsync(JobRegisterSnapshot jobs, DateTimeOffset now, CancellationToken token)
    {
        var today = DateOnly.FromDateTime(now.ToOffset(Formats.Zone).DateTime);
        var scope = jobs.Rows.Where(row => row.Key.Length > 0 && row.Record is { } job && CarrierAgent.InScope(job, [], today)).ToList();

        var attempts = new Dictionary<string, IReadOnlyList<Attempt>>(StringComparer.Ordinal);
        foreach (var chunk in scope.Select(row => row.Key).Chunk(500))
        {
            var asked = await data.CarrierAttemptsAsync(chunk, token);
            foreach (var group in asked.GroupBy(one => one.JobKey, StringComparer.Ordinal))
                attempts[group.Key] = group.Select(one => new Attempt(one.Carrier, one.Outcome, one.Rank)).ToList();
        }

        static (string, string) Of(JobRecord job) => (Formats.Clean(job.Customer).ToUpperInvariant(), Formats.Clean(job.Cat).ToUpperInvariant());
        var wanted = scope.Select(row => Of(row.Record!)).Where(pair => pair.Item1.Length > 0).ToHashSet();
        var priority = jobs.Rows
            .Where(row => row.Record is { } job && Formats.Clean(row.Trucker).Length > 0 && wanted.Contains(Of(job)))
            .GroupBy(row => Of(row.Record!))
            .ToDictionary(group => (group.Key.Item1, group.Key.Item2),
                group => WorkflowService.Rank(group.Select(row => (row.Trucker, row.Record))));

        IReadOnlyList<SupplierSummary> register = scope.Count == 0 ? [] : await suppliers.ListAsync(null, null, token);
        var byId = register.ToDictionary(one => one.Id);
        var carriers = register.Where(one => one.IsCarrier).Select(one => (one.Id, one.Name, one.Code)).ToList();
        var aliases = register.SelectMany(one => one.Aliases.Select(alias => (one.Id, alias))).ToList();
        var resolved = new Dictionary<string, SupplierSummary?>(StringComparer.OrdinalIgnoreCase);
        SupplierSummary? Supplier(string name)
        {
            if (resolved.TryGetValue(name, out var known)) return known;
            var id = WorkflowService.ResolveSupplier(name, carriers, aliases);
            return resolved[name] = id is { } found && byId.TryGetValue(found, out var summary) ? summary : null;
        }
        return new CarrierContext(priority, attempts, Supplier);
    }

    /// <summary>
    /// The waiting requests, the POD files and the carriers' messages still waiting for a person —
    /// for the jobs the drafts could be about, read the way the bell reads them.
    /// </summary>
    private async Task<CommunicationContext> CommunicationContextAsync(JobRegisterSnapshot jobs, DateTimeOffset now, CancellationToken token)
    {
        var ai = options.Value;
        var today = DateOnly.FromDateTime(now.ToOffset(Formats.Zone).DateTime);
        var pending = await data.PendingRequestsAsync(token);

        // Only the finished jobs inside the POD window can be drafted for, so only theirs are looked up.
        var done = jobs.Rows.Where(row => row.Key.Length > 0 && row.Record is { } job && JobRules.IsDone(job.Status)
                && Ai.Documents.DocumentsReadService.DoneOn(WorkspaceTabs.JobView.From(row.Raw)).Day is { } day
                && day < today && day >= today.AddDays(-ai.PodReminderDays))
            .Select(row => row.Key).ToList();
        var withPod = new HashSet<string>(StringComparer.Ordinal);
        foreach (var chunk in done.Chunk(500))
            withPod.UnionWith(await data.PodKeysAsync(chunk, token));

        var wrote = (await data.ReviewMessageKeysAsync(token)).ToHashSet(StringComparer.Ordinal);
        return new CommunicationContext(pending, withPod, wrote, ai.CarrierReminderMinutes, ai.PodReminderDays);
    }

    private async Task<ScanSummary> PassAsync(AgentDefinition agent, JobRegisterSnapshot jobs, DateTimeOffset now,
        bool shadow, AiAutonomy autonomy, CancellationToken token)
    {
        var assess = await AssessorAsync(agent.Id, jobs, now, token);
        var findings = new Dictionary<string, (AgentResult Result, string Owner)>(StringComparer.Ordinal);
        foreach (var row in jobs.Rows)
            if (await assess(row) is { } result) findings[result.EntityId] = (result, row.Record?.OpId ?? "");

        var open = await data.OpenDecisionsAsync(agent.Id, token);
        var openByEntity = open.GroupBy(one => one.EntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(one => one.Id).ToList(), StringComparer.Ordinal);

        // What a person already answered, for the findings with nothing open: the same finding is not raised again.
        var unopened = findings.Keys.Where(key => !openByEntity.ContainsKey(key)).ToList();
        var answered = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var chunk in unopened.Chunk(500))
        {
            foreach (var entry in await data.AnsweredAsync(agent.Id, chunk, token)) answered[entry.Key] = entry.Value;
        }

        int created = 0, unchanged = 0, superseded = 0, resolved = 0, refused = 0;
        var closed = new List<AiDecision>();
        void Close(AiDecision row, string status, string note)
        {
            row.Status = status;
            row.DecidedAt = now;
            row.DecidedBy = "system";
            row.OverrideReason = note;
            closed.Add(row);
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
        if (agent.Id == CarrierAgent.Id && closed.Count > 0) await ObserveCarrierChoicesAsync(closed, jobs, token);
        if (!await data.SaveAsync(agent.Id, token))
        {
            // A person answered one of these decisions while the pass ran. Their answer stands;
            // this pass writes nothing, and the next one starts from what they decided.
            log.LogInformation("{Agent}: a decision was answered during the pass; nothing written this time", agent.Id);
            return new ScanSummary(agent.Id, "answered_meanwhile", jobs.Rows.Count, findings.Count, 0, 0, 0, 0, refused);
        }
        if (created + superseded + resolved > 0)
            log.LogInformation("{Agent}: {Created} new, {Superseded} superseded, {Resolved} resolved, {Unchanged} unchanged over {Jobs} jobs",
                agent.Id, created, superseded, resolved, unchanged, jobs.Rows.Count);
        return new ScanSummary(agent.Id, "ok", jobs.Rows.Count, findings.Count, created, unchanged, superseded, resolved, refused);
    }

    /// <summary>
    /// The shadow comparison for closed Carrier decisions: the first carrier a
    /// person asked after the decision was raised — or, when the job was given
    /// a carrier without a request, the carrier on it now — and whether that is
    /// the carrier the agent said to ask first. A decision nobody acted on
    /// records nothing.
    /// </summary>
    private async Task ObserveCarrierChoicesAsync(IReadOnlyList<AiDecision> closed, JobRegisterSnapshot jobs, CancellationToken token)
    {
        var keys = closed.Select(one => one.EntityId).Distinct(StringComparer.Ordinal).ToList();
        var requests = new List<PassCarrierChoice>();
        foreach (var chunk in keys.Chunk(500))
            requests.AddRange(await data.CarrierChoicesAsync(chunk, token));
        var rows = jobs.Rows.Where(row => row.Key.Length > 0).GroupBy(row => row.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var decision in closed)
        {
            var first = requests.Where(one => one.JobKey == decision.EntityId && one.At >= decision.CreatedAt)
                .OrderBy(one => one.At).ThenBy(one => one.Id).Select(one => ((string, string)?)(one.Carrier, one.By)).FirstOrDefault();
            var (choice, note) = first is { } asked
                ? (Formats.Clean(asked.Item1), $"ถาม {Formats.Clean(asked.Item1)} แล้ว" + (asked.Item2.Length > 0 ? $" ({asked.Item2})" : ""))
                : decision.Status == AiDecisionLog.Resolved && rows.TryGetValue(decision.EntityId, out var row) && Formats.Clean(row.Trucker).Length > 0
                    ? (Formats.Clean(row.Trucker), $"งานได้ผู้ขนส่ง {Formats.Clean(row.Trucker)} แล้ว")
                    : ("", "");
            if (choice.Length == 0) continue;
            string[] references;
            try { references = JsonSerializer.Deserialize<string[]>(decision.RuleReferences) ?? []; }
            catch (JsonException) { references = []; }
            decision.HumanChoice = choice.Length <= 400 ? choice : choice[..400];
            decision.HumanMatches = CarrierAgent.Recommended(references) is { } recommended
                ? CarrierDirectory.Lookup.Key(choice) == recommended : null;
            decision.OverrideReason = note.Length <= 400 ? note : note[..400];
        }
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
