using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Runs <see cref="CorrectionRules"/> over the register and queues what they
/// propose for the jobs' owners.
///
/// <code>
///   dotnet run -- --propose-corrections            see what would be proposed
///   dotnet run -- --propose-corrections --queue    put the proposals in the owners' queues
/// </code>
///
/// <para>
/// There is no <c>--apply</c>. The department's word (22 Sep 2026) was that
/// a correction waits for the job's owner to press Approve, the way a LINE
/// message does — so this writes proposals, and only
/// <see cref="CorrectionService"/> ever writes a cell, one owner's click at a
/// time. Nothing here needs undoing because nothing here changes a job.
/// </para>
///
/// <para>
/// A cell that already has an open proposal for the same value is left as it
/// is; one whose open proposal named a different value is superseded (the
/// old one marked stale) — the lists may have moved between runs. A value
/// no rule can read is counted and printed, not guessed at: that list is
/// what the department reads to tell this file what a spelling meant.
/// </para>
/// </summary>
public static class CorrectionProposer
{
    public const string Actor = "ai-reconcile";

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var queue = args.Contains("--queue");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        var lists = await ListsAsync(scope.ServiceProvider, db, CancellationToken.None);
        var batch = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");

        var jobs = await db.OperationJobs.AsNoTracking()
            .Select(job => new { job.Key, job.Cat, job.OwnerId, job.JobCode, job.Data })
            .ToListAsync();
        var open = await db.JobCorrections.Where(row => row.State == CorrectionState.Pending).ToListAsync();
        var openByCell = open.ToDictionary(row => (row.JobKey, row.Field), row => row);

        var byRule = new Dictionary<string, int>(StringComparer.Ordinal);
        var mappings = new Dictionary<string, int>(StringComparer.Ordinal);
        var leftAlone = new Dictionary<string, int>(StringComparer.Ordinal);
        var byOwner = new Dictionary<string, int>(StringComparer.Ordinal);
        var proposed = 0;
        var alreadyOpen = 0;
        var superseded = 0;
        var cancelledJobs = 0;
        var typeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        // For a bare customer name the rotation holds under several sites: what the jobs' destinations
        // and plants actually say, so the department can add the synonym the rule is missing.
        var ambiguous = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);

        foreach (var job in jobs)
        {
            JsonObject? node;
            try { node = JsonNode.Parse(job.Data)?.AsObject(); }
            catch (System.Text.Json.JsonException) { continue; }
            if (node is null) continue;
            var cells = CorrectionRules.Fields.Concat(CorrectionRules.Evidence).ToDictionary(field => field, field => node[field]?.GetValue<string>() ?? "", StringComparer.Ordinal);
            // A cancelled job is history; its spelling is nobody's to approve.
            if (string.Equals(cells["status"], JobStatus.Cancelled, StringComparison.OrdinalIgnoreCase)) { cancelledJobs++; continue; }

            if (cells["type"].Length > 0) typeCounts[cells["type"]] = typeCounts.GetValueOrDefault(cells["type"]) + 1;
            var proposals = CorrectionRules.Propose(job.Cat, cells, lists);
            foreach (var field in CorrectionRules.Fields)
            {
                var value = cells[field];
                if (Formats.Clean(value).Length == 0) continue;
                var one = proposals.FirstOrDefault(c => c.Field == field);
                if (one is null)
                {
                    if (!OnList(field, value, job.Cat, lists))
                    {
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
                    continue;
                }
                byRule[one.Rule] = byRule.GetValueOrDefault(one.Rule) + 1;
                mappings[$"{field}: {Show(one.From)} → {Show(one.To)}"] = mappings.GetValueOrDefault($"{field}: {Show(one.From)} → {Show(one.To)}") + 1;
                byOwner[job.OwnerId.Length > 0 ? job.OwnerId : "(no owner)"] = byOwner.GetValueOrDefault(job.OwnerId.Length > 0 ? job.OwnerId : "(no owner)") + 1;
                proposed++;
                if (!queue) continue;

                if (openByCell.TryGetValue((job.Key, field), out var existing))
                {
                    if (existing.FromValue == one.From && existing.ToValue == one.To) { alreadyOpen++; continue; }
                    existing.State = CorrectionState.Stale;
                    existing.DecidedAt = DateTimeOffset.UtcNow;
                    existing.Note = $"แทนที่ด้วยข้อเสนอรอบ {batch}";
                    superseded++;
                }
                var row = new JobCorrection
                {
                    Batch = batch, JobKey = job.Key, JobCode = job.JobCode, OwnerId = job.OwnerId,
                    Field = field, FromValue = one.From, ToValue = one.To, Rule = one.Rule, Reason = one.Reason,
                    ProposedBy = Actor, ProposedAt = DateTimeOffset.UtcNow, State = CorrectionState.Pending,
                };
                db.JobCorrections.Add(row);
                openByCell[(job.Key, field)] = row;
            }
        }

        if (queue)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"Batch {batch}: {proposed - alreadyOpen} proposal(s) queued for the owners ({alreadyOpen} already waiting, {superseded} superseded).");
        }

        Console.WriteLine();
        Console.WriteLine($"{(queue ? "Proposed" : "Would propose")} {proposed} cell(s) on {jobs.Count} jobs ({cancelledJobs} cancelled jobs skipped). Lists: {lists.TypeCodes.Count} types · {lists.Customers.Count} customers.");
        // The list itself, when it holds a spelling the rule would write another way: a cell
        // matching such a code is "on the list" and never proposed, so the duplicate has to be
        // retired from the list first — said here, with the count of cells it is holding back.
        var duplicates = lists.TypeCodes.Where(code => JobVehicleType.Canonical(code) != code && JobVehicleType.IsKnown(JobVehicleType.Canonical(code))).ToList();
        if (duplicates.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Active vehicle types the rule spells another way — retire these from the list and the next run proposes the cells holding them:");
            foreach (var code in duplicates)
            {
                Console.WriteLine($"   {typeCounts.GetValueOrDefault(code),5}  {Show(code)} → {JobVehicleType.Canonical(code)}");
            }
        }
        foreach (var (rule, count) in byRule.OrderByDescending(entry => entry.Value))
            Console.WriteLine($"   {count,5}  {rule}");
        Console.WriteLine();
        Console.WriteLine("By spelling:");
        foreach (var (mapping, count) in mappings.OrderByDescending(entry => entry.Value).Take(60))
            Console.WriteLine($"   {count,5}  {mapping}");
        Console.WriteLine();
        Console.WriteLine("Waiting on each owner:");
        foreach (var (owner, count) in byOwner.OrderByDescending(entry => entry.Value))
            Console.WriteLine($"   {count,5}  {owner}");
        if (leftAlone.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Left exactly as typed — not on the list and no rule can read them, so a person should:");
            foreach (var (value, count) in leftAlone.OrderByDescending(entry => entry.Value).Take(60))
                Console.WriteLine($"   {count,5}  {value}");
            if (leftAlone.Count > 60) Console.WriteLine($"   … and {leftAlone.Count - 60} more spellings");
        }
        if (ambiguous.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Several sites, no evidence — the rotation's sites, and what the jobs' DESTINATION | PLANT LOADING say (add a synonym in CorrectionRules.SiteSynonyms when a pair is clear):");
            foreach (var (name, seen) in ambiguous.OrderByDescending(entry => entry.Value.Values.Sum()))
            {
                Console.WriteLine($"   {Show(name)} → {string.Join(" · ", CorrectionRules.SiteNames(name, lists.Customers))}");
                foreach (var (evidence, count) in seen.OrderByDescending(entry => entry.Value).Take(8))
                    Console.WriteLine($"      {count,5}  {evidence}");
            }
        }
        if (!queue)
        {
            Console.WriteLine();
            Console.WriteLine("Nothing was written. Add --queue to put the proposals in the owners' queues; a cell changes only when its owner approves.");
        }
        return 0;
    }

    /// <summary>The lists as the grid's dropdowns offer them, read once for the run.</summary>
    public static async Task<CorrectionLists> ListsAsync(IServiceProvider services, ScmosDbContext db, CancellationToken token)
    {
        var types = await services.GetRequiredService<VehicleTypeService>().ActiveCodesAsync(token);
        var customers = await services.GetRequiredService<RotationService>().CustomersAsync(token);
        var carriers = await services.GetRequiredService<CarrierDirectory>().ReadAsync(token);
        return new CorrectionLists(types, customers, spelling => carriers.Knows(spelling) ? carriers.Company(spelling) : null);
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
