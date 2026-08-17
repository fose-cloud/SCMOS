using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Builds the supplier register and the rate tables from what the system
/// already holds.
///
///   dotnet run -- --seed-suppliers [path/to/rates.json]
///
/// Suppliers come from the two places carriers are named: the 2,102 jobs and
/// the rate workbooks. Every spelling found becomes an alias pointing at one
/// supplier, which is what finally lets TATIYAPOL, TTP and TATIYAPON be counted
/// as one company.
///
/// Spellings are only merged when the match is beyond doubt — the same letters
/// with punctuation and spacing removed. TTP is not merged into TATIYAPON by
/// this: an abbreviation could be another company, and paying the wrong
/// subcontractor is worse than having two rows. Those are left as separate
/// suppliers and listed at the end for a person to merge.
/// </summary>
public static class SupplierSeeder
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        await db.Database.MigrateAsync();

        var index = Array.IndexOf(args, "--seed-suppliers");
        var ratesPath = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--")
            ? args[index + 1]
            : null;

        var suppliers = await SeedSuppliersAsync(app, db);
        if (ratesPath is not null) await SeedRatesAsync(app, db, ratesPath, suppliers);
        await SeedAiToolsAsync(app, db);

        return 0;
    }

    /* ---------------------------------------------------------- suppliers */

    /// <summary>Letters and digits only, so "A.C.N" and "ACN" are one key.</summary>
    private static string Key(string name) =>
        new(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static async Task<Dictionary<string, int>> SeedSuppliersAsync(WebApplication app, ScmosDbContext db)
    {
        var spellings = new Dictionary<string, (string Display, string Source, int Jobs)>(StringComparer.Ordinal);

        foreach (var carrier in await db.OperationJobs.AsNoTracking()
                     .Where(job => job.Trucker != "")
                     .Select(job => job.Trucker)
                     .ToListAsync())
        {
            var name = carrier.Trim();
            if (name.Length == 0) continue;
            var key = Key(name);
            if (key.Length == 0) continue;
            spellings[name.ToUpperInvariant()] = spellings.TryGetValue(name.ToUpperInvariant(), out var seen)
                ? (seen.Display, seen.Source, seen.Jobs + 1)
                : (name, "register", 1);
        }

        var existingAliases = await db.SupplierAliases.AsNoTracking()
            .ToDictionaryAsync(alias => alias.Alias, alias => alias.SupplierId);
        var byKey = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var supplier in await db.Suppliers.AsNoTracking().ToListAsync())
        {
            byKey[Key(supplier.Name)] = supplier.Id;
            usedCodes.Add(supplier.Code);
        }

        var created = 0;
        var linked = 0;

        // Longest, most complete spelling first: it becomes the supplier's name
        // and the shorter forms attach to it rather than the other way round.
        foreach (var (upper, entry) in spellings.OrderByDescending(pair => pair.Value.Display.Length))
        {
            if (existingAliases.ContainsKey(upper)) continue;
            var key = Key(entry.Display);

            if (!byKey.TryGetValue(key, out var supplierId))
            {
                var supplier = new Supplier
                {
                    Code = Code(entry.Display, usedCodes),
                    Name = entry.Display,
                    // Every one of these has been carrying real work, so they are
                    // approved in fact. Recording them as draft would mean the
                    // register is full of jobs given to unapproved carriers.
                    Status = "approved",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Suppliers.Add(supplier);
                await db.SaveChangesAsync();
                supplierId = supplier.Id;
                byKey[key] = supplierId;
                created++;
            }

            db.SupplierAliases.Add(new SupplierAlias
            {
                SupplierId = supplierId,
                Alias = upper,
                Source = entry.Source,
                // The alias is the supplier's own name, or the same letters with
                // different punctuation. Both are safe to confirm.
                Confirmed = true,
            });
            linked++;
        }

        await db.SaveChangesAsync();
        app.Logger.LogInformation("Suppliers: {Created} created, {Linked} spellings linked.", created, linked);

        var merged = spellings.Keys.GroupBy(Key).Where(group => group.Count() > 1).ToList();
        foreach (var group in merged)
            app.Logger.LogInformation("   merged: {Names}", string.Join(" = ", group));

        // Names that look related but are not provably the same company. Listed
        // rather than merged, because an abbreviation is a guess.
        var names = spellings.Values.Select(entry => entry.Display.ToUpperInvariant()).Distinct().ToList();
        var suspects = new List<string>();
        foreach (var a in names)
            foreach (var b in names)
            {
                if (string.CompareOrdinal(a, b) >= 0) continue;
                var ka = Key(a); var kb = Key(b);
                if (ka == kb) continue;
                if (ka.StartsWith(kb, StringComparison.Ordinal) || kb.StartsWith(ka, StringComparison.Ordinal))
                    suspects.Add($"{a}  /  {b}");
            }

        if (suspects.Count > 0)
        {
            app.Logger.LogWarning("Possibly the same company — not merged, a person has to decide:");
            foreach (var pair in suspects) app.Logger.LogWarning("   {Pair}", pair);
        }

        return await db.SupplierAliases.AsNoTracking()
            .ToDictionaryAsync(alias => alias.Alias, alias => alias.SupplierId);
    }

    /// <summary>
    /// A short unique code.
    ///
    /// Six letters is not enough on its own: TATIYAPOL and TATIYAPON both start
    /// TATIYA, and they are deliberately separate suppliers until somebody says
    /// otherwise, so the code has to tell them apart.
    /// </summary>
    private static string Code(string name, HashSet<string> used)
    {
        var letters = new string(name.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var stem = letters.Length > 0 ? letters[..Math.Min(6, letters.Length)] : "SUP";

        var code = stem;
        var suffix = 2;
        while (!used.Add(code))
        {
            var room = Math.Max(1, 6 - suffix.ToString().Length);
            code = stem[..Math.Min(room, stem.Length)] + suffix;
            suffix++;
        }
        return code;
    }

    /* -------------------------------------------------------------- rates */

    private static async Task SeedRatesAsync(WebApplication app, ScmosDbContext db, string path,
        Dictionary<string, int> aliases)
    {
        if (!File.Exists(path))
        {
            app.Logger.LogError("No such rate file: {Path}", path);
            return;
        }

        if (await db.RateLanes.AnyAsync())
        {
            app.Logger.LogInformation("Rates are already loaded — clearing and reloading.");
            await db.RatePrices.ExecuteDeleteAsync();
            await db.RateLanes.ExecuteDeleteAsync();
            await db.FuelBands.ExecuteDeleteAsync();
            await db.RateSurcharges.ExecuteDeleteAsync();
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        var root = document.RootElement;

        var bands = root.GetProperty("bands").EnumerateArray().Select((band, position) => new FuelBand
        {
            Label = band.GetProperty("label").GetString() ?? "",
            MinPrice = band.GetProperty("min").GetDecimal(),
            MaxPrice = band.GetProperty("max").GetDecimal(),
            Position = position,
        }).ToList();
        db.FuelBands.AddRange(bands);

        foreach (var charge in root.GetProperty("surcharges").EnumerateArray())
        {
            db.RateSurcharges.Add(new RateSurcharge
            {
                Service = Text(charge, "service"), No = Text(charge, "no"),
                Description = Text(charge, "description"), Currency = Text(charge, "currency"),
                Rate = Text(charge, "rate"), Unit = Text(charge, "unit"),
            });
        }
        await db.SaveChangesAsync();

        var lanes = 0;
        var prices = 0;
        var unmatched = new HashSet<string>(StringComparer.Ordinal);

        // Match rate carriers the same way supplier names were matched, not on
        // the exact spelling: "NEXT GEN" on a rate card and NEXTGEN in the
        // register are the same firm, and looking up the literal string would
        // leave that carrier's 3 lanes attached to nobody.
        var byKey = aliases
            .GroupBy(entry => Key(entry.Key))
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        foreach (var lane in root.GetProperty("lanes").EnumerateArray())
        {
            var carrier = Text(lane, "carrier");
            if (!aliases.TryGetValue(carrier.ToUpperInvariant(), out var supplierId))
                byKey.TryGetValue(Key(carrier), out supplierId);
            if (supplierId == 0) unmatched.Add(carrier);

            var row = new RateLane
            {
                SupplierId = supplierId == 0 ? null : supplierId,
                Carrier = carrier,
                Service = Text(lane, "service"),
                Customer = Text(lane, "customer"),
                FromPlace = Text(lane, "from"),
                ToPlace = Text(lane, "to"),
                County = Text(lane, "county"),
                Remark = Text(lane, "remark"),
            };
            db.RateLanes.Add(row);
            await db.SaveChangesAsync();
            lanes++;

            foreach (var entry in lane.GetProperty("prices").EnumerateObject())
            {
                var vehicle = entry.Name;
                var position = 0;
                foreach (var value in entry.Value.EnumerateArray())
                {
                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        db.RatePrices.Add(new RatePrice
                        {
                            LaneId = row.Id, Vehicle = vehicle,
                            BandPosition = position, Price = value.GetInt32(),
                        });
                        prices++;
                    }
                    position++;
                }
            }

            if (lanes % 200 == 0)
            {
                await db.SaveChangesAsync();
                app.Logger.LogInformation("   {Lanes} lanes…", lanes);
            }
        }

        await db.SaveChangesAsync();
        app.Logger.LogInformation("Rates: {Bands} bands, {Lanes} lanes, {Prices} prices.",
            bands.Count, lanes, prices);

        if (unmatched.Count > 0)
        {
            app.Logger.LogWarning("Rate carriers with no supplier row — they have quoted but never carried:");
            foreach (var carrier in unmatched.OrderBy(name => name))
                app.Logger.LogWarning("   {Carrier}", carrier);
        }
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    /* ------------------------------------------------------------ AI tools */

    /// <summary>
    /// The permission matrix, written into the database so it is data rather
    /// than a paragraph in a prompt. Nothing here is created for an agent that
    /// does not exist yet — these are the tools the API can already back.
    /// </summary>
    private static async Task SeedAiToolsAsync(WebApplication app, ScmosDbContext db)
    {
        if (await db.AiTools.AnyAsync()) return;

        db.AiTools.AddRange(AiPermissions.Catalogue.Select(tool => new AiTool
        {
            Name = tool.Name, Agent = tool.Agent,
            Permission = tool.Permission.ToString().ToLowerInvariant(),
            Description = tool.Description, Enabled = true,
        }));

        await db.SaveChangesAsync();
        app.Logger.LogInformation("AI tools: {Count} registered.", AiPermissions.Catalogue.Length);
    }
}
