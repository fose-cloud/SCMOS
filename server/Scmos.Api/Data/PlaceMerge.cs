using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Puts one gate under one spelling.
///
/// <code>
///   dotnet run -- --merge-places            report what it would change
///   dotnet run -- --merge-places --apply    write it
///   dotnet run -- --merge-places --undo     put the last run back
/// </code>
///
/// <para>
/// The register holds one warehouse under ten spellings — HAZCHEM 107 times,
/// then Hazchemwarehouse, Hazchemwarehouse., HazchemWH, HAZCHEM WAREHOUSE, and
/// three more that differ only by a non-breaking space somebody pasted in.
/// Every one of them is a separate place to anything that groups by
/// destination, and a separate lane to <c>JourneyService.LookAsync</c>, which
/// matches a saved distance by the spelling it was saved under. So a distance
/// measured against one of them is invisible to the other nine.
/// </para>
///
/// <para>
/// <b>The mapping is written out by hand and nothing is guessed.</b> No fuzzy
/// matching, no edit distance, no "looks similar". These are operational
/// records with no history table behind them, and a rule clever enough to merge
/// spellings it was not shown is a rule clever enough to merge two places that
/// really are different. Every variant below was read off the register and
/// checked one at a time.
/// </para>
///
/// <para>
/// <b>What it deliberately does not touch.</b> "HAZCHEM     ( WAIT CARGO
/// RECEIPT)" is not a spelling of HAZCHEM — it is HAZCHEM plus a note that the
/// cargo receipt has not arrived. Merging it would delete the note, which is
/// the same mistake as the vehicle type that carried the DG flag. It is
/// reported and left alone, and so is anything else that looks related but is
/// not on the list.
/// </para>
///
/// <para>
/// <b>How it is undone.</b> There is no new table. Each change is written into
/// the job's own <c>hist</c> — the record operators already read — stamped with
/// the run it belonged to, and <c>--undo</c> reads those back. It restores a
/// value only where the current one is still what the merge wrote, so an edit
/// made since is never overwritten.
/// </para>
/// </summary>
public static class PlaceMerge
{
    /// <summary>Who the history says did it, and the prefix <c>--undo</c> looks for.</summary>
    private const string Actor = "place-merge";

    /// <summary>
    /// A non-breaking space, U+00A0.
    ///
    /// Named rather than typed. Three rows in the register carry one where an
    /// ordinary space belongs, pasted in from somewhere. As a literal character
    /// in this file it is invisible, indistinguishable from the plain-space
    /// spellings listed two lines above it, and one careless reformat away from
    /// silently becoming a space and matching nothing.
    /// </summary>
    private static readonly string Nbsp = ((char)0xA0).ToString();

    /// <summary>The fields that name a place. The same four the suggestions read.</summary>
    private static readonly string[] Fields = ["destination", "cyYard", "plant", "returnLoc"];

    /// <summary>
    /// One gate, and every spelling of it found in the register.
    ///
    /// The canonical form is the one already used most — 107 of 129 — because
    /// the object is to stop the drift, not to impose a new spelling on the
    /// people who have been typing the common one correctly all along.
    ///
    /// Two of them differ from the plain-space spellings above only by a
    /// non-breaking space, built from Nbsp so it is visible in this file.
    /// </summary>
    private static readonly (string Canonical, string[] Variants)[] Merges =
    [
        ("HAZCHEM",
        [
            "Hazchemwarehouse",
            "Hazchemwarehouse.",
            "HazchemWH",
            "HAZCHEM WAREHOUSE",
            // Found on production by the "left alone" list below, which is what
            // it is for: the development copy has neither of these, and a list
            // written against a stale copy would have missed four rows.
            "Hazchem",
            "HAZCHEM WH",
            "Hazchem warehouse",
            "Hazchem warehouse.",
            $"Hazchem{Nbsp}warehouse",
            $"Hazchem{Nbsp}warehouse.",
        ]),
    ];

    /// <summary>
    /// Anything whose letters and digits contain one of these is worth showing
    /// even when it is not being merged, so a spelling that appeared after this
    /// list was written cannot pass unnoticed.
    /// </summary>
    private static readonly string[] Families = ["HAZCHEM"];

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var apply = args.Contains("--apply");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();

        if (args.Contains("--undo")) return await UndoAsync(app, db);

        var batch = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        // The canonical maps to itself, so the pass counts the rows that are
        // already right alongside the ones that are not.
        var plan = Merges
            .SelectMany(one => one.Variants
                .Append(one.Canonical)
                .Select(v => (Variant: v, one.Canonical)))
            .ToDictionary(one => one.Variant, one => one.Canonical, StringComparer.Ordinal);

        var jobs = await db.OperationJobs.ToListAsync();
        var changed = 0;
        var touched = new List<string>();
        var related = new Dictionary<string, int>(StringComparer.Ordinal);
        // Tallied in this one pass. Counting each variant afterwards meant
        // re-parsing every job's JSON once per variant — eight times over the
        // whole register to print eight numbers already in front of us.
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var job in jobs)
        {
            if (string.IsNullOrWhiteSpace(job.Data)) continue;

            JsonNode? node;
            try { node = JsonNode.Parse(job.Data); }
            catch (System.Text.Json.JsonException) { continue; }
            if (node is not JsonObject obj) continue;

            var edits = new List<(string Field, string Before, string After)>();

            foreach (var field in Fields)
            {
                var before = obj[field]?.GetValue<string>();
                if (string.IsNullOrEmpty(before)) continue;

                // Trimmed for the lookup only. What goes in the history is the
                // value exactly as it was stored, or an undo would put back a
                // string that was never there.
                if (plan.TryGetValue(before.Trim(), out var canonical))
                {
                    var key = before.Trim();
                    counted[key] = counted.GetValueOrDefault(key) + 1;
                    // Already correct: counted above so the report shows how many
                    // rows the canonical spelling holds, but nothing to change.
                    if (before != canonical) edits.Add((field, before, canonical));
                }
                else if (IsRelated(before) && !plan.ContainsKey(before.Trim()))
                {
                    // Related but not on the list. Counted and reported, never
                    // touched — this is where "( WAIT CARGO RECEIPT)" lands, and
                    // where a spelling nobody has seen yet would land too.
                    related[before] = related.GetValueOrDefault(before) + 1;
                }
            }

            if (edits.Count == 0) continue;
            changed++;
            touched.Add(job.Key);
            if (!apply) continue;

            foreach (var (field, _, after) in edits) obj[field] = after;

            // Into the job's own history, which is both the audit trail and the
            // only thing --undo reads.
            if (obj["hist"] is not JsonArray history)
            {
                history = [];
                obj["hist"] = history;
            }
            foreach (var (field, before, after) in edits)
            {
                history.Add(new JsonObject
                {
                    ["ts"] = DateTimeOffset.Now.ToString("HH:mm"),
                    ["user"] = $"{Actor} {batch}",
                    ["field"] = $"{field} (รวมการสะกด)",
                    ["old"] = before,
                    ["neu"] = after,
                });
            }

            job.Data = obj.ToJsonString();
            job.UpdatedBy = Actor;
            job.UpdatedAt = DateTimeOffset.UtcNow;
        }

        /* ------------------------------------------------- what it found */

        Console.WriteLine();
        Console.WriteLine(apply ? "Merging place spellings." : "What merging place spellings would change.");
        Console.WriteLine();

        foreach (var (canonical, variants) in Merges)
        {
            Console.WriteLine($"  Into \"{canonical}\":");
            Console.WriteLine($"    {counted.GetValueOrDefault(canonical),4}  {Show(canonical)}   (already correct)");
            foreach (var variant in variants)
            {
                Console.WriteLine($"    {counted.GetValueOrDefault(variant),4}  {Show(variant)}");
            }
            Console.WriteLine();
        }

        if (related.Count > 0)
        {
            Console.WriteLine("  Related, and deliberately left alone — these say something extra,");
            Console.WriteLine("  or are spellings this list has not been told about:");
            foreach (var (name, count) in related.OrderByDescending(one => one.Value))
            {
                Console.WriteLine($"    {count,4}  {Show(name)}");
            }
            Console.WriteLine();
        }

        Console.WriteLine($"  {(apply ? "Changed" : "Would change")} {changed} of {jobs.Count} jobs.");

        if (apply && changed > 0)
        {
            await db.SaveChangesAsync();
            Console.WriteLine($"  Batch {batch}. Undo with --merge-places --undo.");
            app.Logger.LogInformation("Place merge {Batch} changed {Count} jobs: {Keys}",
                batch, changed, string.Join(", ", touched.Take(50)));
        }
        else if (!apply)
        {
            Console.WriteLine("  Nothing was written. Add --apply to write it.");
        }

        Console.WriteLine();
        return 0;
    }

    /// <summary>
    /// Puts the last run back.
    ///
    /// Reads the history the run wrote, newest batch first, and restores each
    /// field — but only where the value is still the one the merge put there. A
    /// field edited since belongs to whoever edited it.
    /// </summary>
    private static async Task<int> UndoAsync(WebApplication app, ScmosDbContext db)
    {
        var jobs = await db.OperationJobs
            .Where(job => job.UpdatedBy == Actor)
            .ToListAsync();

        // The most recent run across every job it touched, so one pass is
        // reversed without disturbing an earlier one.
        string? batch = null;
        foreach (var job in jobs)
        {
            foreach (var entry in Entries(job))
            {
                var stamp = entry.Stamp;
                if (stamp is not null && (batch is null || string.CompareOrdinal(stamp, batch) > 0))
                    batch = stamp;
            }
        }

        if (batch is null)
        {
            Console.WriteLine("Nothing to undo — no job carries a place-merge entry.");
            return 0;
        }

        var restored = 0;
        var skipped = 0;

        foreach (var job in jobs)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(job.Data); }
            catch (System.Text.Json.JsonException) { continue; }
            if (node is not JsonObject obj) continue;

            var mine = Entries(job).Where(one => one.Stamp == batch).ToList();
            if (mine.Count == 0) continue;

            var put = 0;
            // Newest first, so a field the run changed twice ends on its
            // earliest value rather than an intermediate one.
            foreach (var entry in Enumerable.Reverse(mine))
            {
                var now = obj[entry.Field]?.GetValue<string>();
                if (now != entry.New) { skipped++; continue; }
                obj[entry.Field] = entry.Old;
                put++;
            }

            if (put == 0) continue;

            // The history entries go too. Leaving them would make a second undo
            // try to reverse a run that is no longer there.
            if (obj["hist"] is JsonArray history)
            {
                for (var i = history.Count - 1; i >= 0; i--)
                {
                    if (history[i]?["user"]?.GetValue<string>() == $"{Actor} {batch}") history.RemoveAt(i);
                }
            }

            job.Data = obj.ToJsonString();
            job.UpdatedBy = $"{Actor}-undo";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            restored += put;
        }

        if (restored > 0) await db.SaveChangesAsync();

        Console.WriteLine();
        Console.WriteLine($"Undid batch {batch}: {restored} field(s) put back.");
        if (skipped > 0)
            Console.WriteLine($"{skipped} left alone — edited since the merge, so the newer value stands.");
        Console.WriteLine();
        app.Logger.LogInformation("Place merge {Batch} undone: {Restored} restored, {Skipped} left",
            batch, restored, skipped);
        return 0;
    }

    private record Entry(string Stamp, string Field, string Old, string New);

    /// <summary>Every place-merge entry in a job's history, oldest first.</summary>
    private static List<Entry> Entries(OperationJob job)
    {
        var found = new List<Entry>();
        if (string.IsNullOrWhiteSpace(job.Data)) return found;

        JsonNode? node;
        try { node = JsonNode.Parse(job.Data); }
        catch (System.Text.Json.JsonException) { return found; }
        if (node is not JsonObject obj || obj["hist"] is not JsonArray history) return found;

        foreach (var one in history)
        {
            var user = one?["user"]?.GetValue<string>() ?? "";
            if (!user.StartsWith($"{Actor} ", StringComparison.Ordinal)) continue;

            // "destination (รวมการสะกด)" — the field is the first word.
            var field = (one?["field"]?.GetValue<string>() ?? "").Split(' ')[0];
            if (field.Length == 0) continue;

            found.Add(new Entry(
                user[(Actor.Length + 1)..],
                field,
                one?["old"]?.GetValue<string>() ?? "",
                one?["neu"]?.GetValue<string>() ?? ""));
        }
        return found;
    }

    /// <summary>Whether a name belongs to a family this file knows about.</summary>
    private static bool IsRelated(string value)
    {
        var key = new string([.. value.Where(char.IsLetterOrDigit)]).ToUpperInvariant();
        return Families.Any(key.Contains);
    }

    /// <summary>
    /// A spelling with its invisible characters made visible.
    ///
    /// Three of these differ from another only by a non-breaking space. Printed
    /// raw they look like duplicate lines in the report, and the person reading
    /// it reasonably concludes the tool is broken.
    /// </summary>
    private static string Show(string value)
    {
        var shown = value.Replace(Nbsp, "[nbsp]").Replace("\t", "[tab]");
        return value == shown ? $"\"{value}\"" : $"\"{shown}\"";
    }
}
