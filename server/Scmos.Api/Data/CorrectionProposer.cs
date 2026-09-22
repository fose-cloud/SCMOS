using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>What one pass proposed, for the log and the report.</summary>
public sealed record ProposalSummary(string Batch, int Jobs, int Proposed, int Queued, int AlreadyOpen, int Superseded, int Declined);

/// <summary>
/// Runs the correction rules over the register and queues what they
/// propose for the jobs' owners.
///
/// <code>
///   dotnet run -- --propose-corrections            see what would be proposed
///   dotnet run -- --propose-corrections --queue    put the proposals in the owners' queues
/// </code>
///
/// <para>
/// Two kinds of rule. <see cref="CorrectionRules"/> judge the five dropdown
/// columns against their lists. <see cref="DelayReasonRule"/> (22 Sep 2026,
/// later the same day) proposes a REASON / DELAY for an IMPORT or EXPORT
/// shipment that arrived late and says nothing about why — off the
/// haulier's own messages first, the department's two main reasons by the
/// route second. Both queue proposals; neither writes a cell.
/// </para>
///
/// <para>
/// There is no <c>--apply</c>. The department's word was that a correction
/// waits for the job's owner to press Approve, the way a LINE message does
/// — so this writes proposals, and only <see cref="CorrectionService"/> ever
/// writes a cell, one owner's click at a time. Nothing here needs undoing
/// because nothing here changes a job.
/// </para>
///
/// <para>
/// A cell that already has an open proposal for the same value is left as it
/// is; one whose open proposal named a different value is superseded (the
/// old one marked stale) — the lists may have moved between runs. A proposal
/// the owner rejected is not made again for the same cell and value — and
/// for a delay reason, not again at all: the owner said no to being asked.
/// A value no rule can read is counted and printed, not guessed at: that list
/// is what the department reads to tell this file what a spelling meant.
/// </para>
///
/// <para>
/// <see cref="CorrectionScheduler"/> runs the same pass every half hour, so a
/// shipment that arrives late this morning is asked about this morning and
/// a spelling an import brought in is proposed the same day.
/// </para>
/// </summary>
public static class CorrectionProposer
{
    public const string Actor = "ai-reconcile";

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        await ProposeAsync(scope.ServiceProvider, args.Contains("--queue"), Console.Out, CancellationToken.None);
        return 0;
    }

    /// <summary>One pass: report to <paramref name="output"/> when given, queue when asked.</summary>
    public static async Task<ProposalSummary> ProposeAsync(IServiceProvider services, bool queue, TextWriter? output, CancellationToken token)
    {
        var db = services.GetRequiredService<ScmosDbContext>();
        var register = services.GetRequiredService<JobRegisterCache>();
        var clock = services.GetRequiredService<TimeProvider>();
        var lists = await ListsAsync(services, token);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Formats.Zone).DateTime);
        var batch = clock.GetUtcNow().ToString("yyyyMMddHHmmss");

        // A summarising reader: a snapshot a few minutes old is fine for a proposal the owner decides later.
        var snapshot = await register.ReadAsync(token, staleOk: true);
        var open = await db.JobCorrections.Where(row => row.State == CorrectionState.Pending).ToListAsync(token);
        var openByCell = open.ToDictionary(row => (row.JobKey, row.Field), row => row);
        var declined = await db.JobCorrections.AsNoTracking().Where(row => row.State == CorrectionState.Rejected)
            .Select(row => new { row.JobKey, row.Field, row.FromValue, row.ToValue, row.Rule }).ToListAsync(token);
        var declinedCells = declined.Where(row => DelayReasonRule.IsReasonRule(row.Rule)).Select(row => row.JobKey).ToHashSet(StringComparer.Ordinal);
        var declinedValues = declined.Where(row => !DelayReasonRule.IsReasonRule(row.Rule)).Select(row => (row.JobKey, row.Field, row.FromValue, row.ToValue)).ToHashSet();
        var messages = await MessagesAsync(db, today, token);

        var byRule = new Dictionary<string, int>(StringComparer.Ordinal);
        var mappings = new Dictionary<string, int>(StringComparer.Ordinal);
        var leftAlone = new Dictionary<string, int>(StringComparer.Ordinal);
        var byOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        // For a bare customer name the rotation holds under several sites: what the jobs' destinations
        // and plants actually say, so the department can add the synonym the rule is missing.
        var ambiguous = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        int proposed = 0, alreadyOpen = 0, superseded = 0, skippedDeclined = 0, cancelledJobs = 0, lateAsked = 0;

        foreach (var row in snapshot.Rows)
        {
            if (row.Record is not { } job) continue;
            var cells = CorrectionRules.Fields.Concat(CorrectionRules.Evidence).Distinct(StringComparer.Ordinal)
                .ToDictionary(field => field, field => row.Raw.TryGetProperty(field, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "", StringComparer.Ordinal);
            // A cancelled job is history; its spelling is nobody's to approve.
            if (string.Equals(cells["status"], JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)) { cancelledJobs++; continue; }
            if (cells["type"].Length > 0) typeCounts[cells["type"]] = typeCounts.GetValueOrDefault(cells["type"]) + 1;

            var proposals = CorrectionRules.Propose(job.Cat, cells, lists).ToList();
            var reason = DelayReasonRule.Propose(job, messages.GetValueOrDefault(row.Key, []), today);
            if (reason is not null) { lateAsked++; proposals.Add(reason); }
            // A delay reason still waiting on a job that no longer asks for one — a reason typed since, an
            // export answered under REASON before the column moved, a row queued under the wrong column —
            // is retired, so the drawer shows only what is still a question.
            if (queue)
            {
                foreach (var column in new[] { DelayReasonRule.Field, DelayReasonRule.ExportField })
                {
                    if (!openByCell.TryGetValue((row.Key, column), out var waiting) || !DelayReasonRule.IsReasonRule(waiting.Rule)) continue;
                    var still = reason is not null && reason.Field == column;
                    if (still) continue;
                    waiting.State = CorrectionState.Stale;
                    waiting.DecidedAt = clock.GetUtcNow();
                    waiting.Note = reason is null ? $"งานมีเหตุผลแล้วหรือไม่เข้าเงื่อนไขแล้ว (รอบ {batch})" : $"ย้ายไปคอลัมน์ {reason.Field} (รอบ {batch})";
                    openByCell.Remove((row.Key, column));
                    superseded++;
                }
            }

            foreach (var field in CorrectionRules.Fields)
            {
                var value = cells[field];
                if (value.Length == 0 || proposals.Any(c => c.Field == field) || OnList(field, value, job.Cat, lists)) continue;
                // A bare customer name the rotation holds under several sites is said so: the fix is a site in the destination, not a new rotation row.
                var sites = field == "customer" ? CorrectionRules.Sites(value, lists.Customers) : 0;
                var label = sites > 1 ? $"{field}: {Show(value)} — หลายไซต์ใน Job Rotation ({sites}) ปลายทาง/โรงงานไม่ระบุ" : $"{field}: {Show(value)}";
                leftAlone[label] = leftAlone.GetValueOrDefault(label) + 1;
                if (sites > 1)
                {
                    var evidence = $"{CorrectionRules.Squash(cells["destination"])} | {CorrectionRules.Squash(cells["plant"])}";
                    var seen = ambiguous.TryGetValue(value, out var found) ? found : ambiguous[value] = new Dictionary<string, int>(StringComparer.Ordinal);
                    seen[evidence] = seen.GetValueOrDefault(evidence) + 1;
                }
            }

            foreach (var one in proposals)
            {
                if (DelayReasonRule.IsReasonRule(one.Rule) ? declinedCells.Contains(row.Key) : declinedValues.Contains((row.Key, one.Field, one.From, one.To)))
                { skippedDeclined++; continue; }
                byRule[one.Rule] = byRule.GetValueOrDefault(one.Rule) + 1;
                var mapping = $"{one.Field}: {Show(one.From)} → {Show(one.To)}";
                mappings[mapping] = mappings.GetValueOrDefault(mapping) + 1;
                var owner = job.OpId.Length > 0 ? job.OpId : "(no owner)";
                byOwner[owner] = byOwner.GetValueOrDefault(owner) + 1;
                proposed++;
                if (!queue) continue;

                if (openByCell.TryGetValue((row.Key, one.Field), out var existing))
                {
                    if (existing.FromValue == one.From && existing.ToValue == one.To) { alreadyOpen++; continue; }
                    existing.State = CorrectionState.Stale;
                    existing.DecidedAt = clock.GetUtcNow();
                    existing.Note = $"แทนที่ด้วยข้อเสนอรอบ {batch}";
                    superseded++;
                }
                var next = new JobCorrection
                {
                    Batch = batch, JobKey = row.Key, JobCode = job.JobCode, OwnerId = job.OpId,
                    Field = one.Field, FromValue = one.From, ToValue = one.To, Rule = one.Rule, Reason = one.Reason,
                    ProposedBy = Actor, ProposedAt = clock.GetUtcNow(), State = CorrectionState.Pending,
                };
                db.JobCorrections.Add(next);
                openByCell[(row.Key, one.Field)] = next;
            }
        }

        var queued = 0;
        if (queue)
        {
            queued = proposed - alreadyOpen;
            if (queued > 0 || superseded > 0) await db.SaveChangesAsync(token);
        }
        var summary = new ProposalSummary(batch, snapshot.Rows.Count, proposed, queued, alreadyOpen, superseded, skippedDeclined);
        if (output is not null) Report(output, summary, queue, lists, cancelledJobs, lateAsked, byRule, mappings, byOwner, leftAlone, ambiguous, typeCounts);
        return summary;
    }

    private static void Report(TextWriter o, ProposalSummary s, bool queue, CorrectionLists lists, int cancelledJobs, int lateAsked,
        Dictionary<string, int> byRule, Dictionary<string, int> mappings, Dictionary<string, int> byOwner, Dictionary<string, int> leftAlone,
        Dictionary<string, Dictionary<string, int>> ambiguous, Dictionary<string, int> typeCounts)
    {
        if (queue) o.WriteLine($"Batch {s.Batch}: {s.Queued} proposal(s) queued for the owners ({s.AlreadyOpen} already waiting, {s.Superseded} superseded, {s.Declined} not asked again).");
        o.WriteLine();
        o.WriteLine($"{(queue ? "Proposed" : "Would propose")} {s.Proposed} cell(s) on {s.Jobs} jobs ({cancelledJobs} cancelled jobs skipped; {lateAsked} late shipments asked for a reason). Lists: {lists.TypeCodes.Count} types · {lists.Customers.Count} customers.");
        var duplicates = lists.TypeCodes.Where(code => JobVehicleType.Canonical(code) != code && JobVehicleType.IsKnown(JobVehicleType.Canonical(code))).ToList();
        if (duplicates.Count > 0)
        {
            o.WriteLine();
            o.WriteLine("Active vehicle types the rule spells another way — retire these from the list and the next run proposes the cells holding them:");
            foreach (var code in duplicates) o.WriteLine($"   {typeCounts.GetValueOrDefault(code),5}  {Show(code)} → {JobVehicleType.Canonical(code)}");
        }
        foreach (var (rule, count) in byRule.OrderByDescending(entry => entry.Value)) o.WriteLine($"   {count,5}  {rule}");
        o.WriteLine();
        o.WriteLine("By spelling:");
        foreach (var (mapping, count) in mappings.OrderByDescending(entry => entry.Value).Take(60)) o.WriteLine($"   {count,5}  {mapping}");
        o.WriteLine();
        o.WriteLine("Waiting on each owner:");
        foreach (var (owner, count) in byOwner.OrderByDescending(entry => entry.Value)) o.WriteLine($"   {count,5}  {owner}");
        if (leftAlone.Count > 0)
        {
            o.WriteLine();
            o.WriteLine("Left exactly as typed — not on the list and no rule can read them, so a person should:");
            foreach (var (value, count) in leftAlone.OrderByDescending(entry => entry.Value).Take(60)) o.WriteLine($"   {count,5}  {value}");
            if (leftAlone.Count > 60) o.WriteLine($"   … and {leftAlone.Count - 60} more spellings");
        }
        if (ambiguous.Count > 0)
        {
            o.WriteLine();
            o.WriteLine("Several sites, no evidence — the rotation's sites, and what the jobs' DESTINATION | PLANT LOADING say (add a synonym in CorrectionRules.SiteSynonyms when a pair is clear):");
            foreach (var (name, seen) in ambiguous.OrderByDescending(entry => entry.Value.Values.Sum()))
            {
                o.WriteLine($"   {Show(name)} → {string.Join(" · ", CorrectionRules.SiteNames(name, lists.Customers))}");
                foreach (var (evidence, count) in seen.OrderByDescending(entry => entry.Value).Take(8)) o.WriteLine($"      {count,5}  {evidence}");
            }
        }
        if (!queue)
        {
            o.WriteLine();
            o.WriteLine("Nothing was written. Add --queue to put the proposals in the owners' queues; a cell changes only when its owner approves.");
        }
    }

    /// <summary>The lists as the grid's dropdowns offer them, read once for the run.</summary>
    public static async Task<CorrectionLists> ListsAsync(IServiceProvider services, CancellationToken token)
    {
        var types = await services.GetRequiredService<VehicleTypeService>().ActiveCodesAsync(token);
        var customers = await services.GetRequiredService<RotationService>().CustomersAsync(token);
        var carriers = await services.GetRequiredService<CarrierDirectory>().ReadAsync(token);
        return new CorrectionLists(types, customers, spelling => carriers.Knows(spelling) ? carriers.Company(spelling) : null);
    }

    /// <summary>What the hauliers said about each job within the look-back, newest first — the text, never the room or the sender.</summary>
    private static async Task<Dictionary<string, List<string>>> MessagesAsync(ScmosDbContext db, DateOnly today, CancellationToken token)
    {
        var since = new DateTimeOffset(today.AddDays(-DelayReasonRule.LookbackDays - 7).ToDateTime(TimeOnly.MinValue), Formats.Zone);
        var rows = await db.LineEvents.AsNoTracking()
            .Where(one => one.JobKey != "" && one.RawText != "" && one.ReceivedAt >= since
                && (one.ProcessingStatus == LineProcessing.Processed || one.ProcessingStatus == LineProcessing.NeedReview))
            .OrderByDescending(one => one.ReceivedAt)
            .Select(one => new { one.JobKey, one.RawText })
            .ToListAsync(token);
        var byKey = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var one in rows) (byKey.TryGetValue(one.JobKey, out var list) ? list : byKey[one.JobKey] = []).Add(one.RawText);
        return byKey;
    }

    /// <summary>Whether a value is on its column's list as spelled — the values that are neither proposed nor on the list are the report's "left alone".</summary>
    private static bool OnList(string field, string value, string category, CorrectionLists lists) => field switch
    {
        "cat" => value is "IMPORT" or "EXPORT" or "DELIVERY",
        "customer" => lists.Customers.Contains(value, StringComparer.Ordinal),
        "trucker" => lists.CarrierOf(value) == value,
        "type" => lists.TypeCodes.Contains(value, StringComparer.Ordinal) || JobVehicleType.IsKnown(value),
        "status" => JobStatus.For(CorrectionRules.Squash(category).ToUpperInvariant()).Contains(value, StringComparer.Ordinal),
        _ => true,
    };

    private static string Show(string value)
    {
        var shown = value.Replace(((char)0xA0).ToString(), "[nbsp]").Replace("\t", "[tab]");
        return $"\"{shown}\"";
    }
}

/// <summary>
/// The same pass, every half hour, so a late arrival this morning is asked
/// about this morning. Off when <c>Corrections:AutoMinutes</c> is 0. A pass
/// reads the register through its cache (a stale snapshot will do) and the
/// hauliers' messages once, and writes only proposals.
/// </summary>
public class CorrectionScheduler(IServiceProvider services, IConfiguration configuration, ILogger<CorrectionScheduler> log) : BackgroundService
{
    public const string EveryKey = "Corrections:AutoMinutes";
    public const int DefaultMinutes = 30;

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var minutes = configuration.GetValue(EveryKey, DefaultMinutes);
        if (minutes <= 0) return;
        try { await Task.Delay(TimeSpan.FromMinutes(3), stopping); }
        catch (OperationCanceledException) { return; }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var summary = await CorrectionProposer.ProposeAsync(scope.ServiceProvider, queue: true, output: null, stopping);
                if (summary.Queued > 0 || summary.Superseded > 0)
                    log.LogInformation("Corrections {Batch}: {Queued} proposal(s) queued for the owners ({Open} already waiting, {Superseded} superseded)",
                        summary.Batch, summary.Queued, summary.AlreadyOpen, summary.Superseded);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception problem)
            {
                log.LogError(problem, "Correction pass failed");
            }

            try { await Task.Delay(TimeSpan.FromMinutes(minutes), stopping); }
            catch (OperationCanceledException) { return; }
        }
    }
}
