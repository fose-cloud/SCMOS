using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Puts the register onto one spelling per kind of vehicle, and gives dangerous
/// goods a column of its own.
///
/// <code>
///   dotnet run -- --normalise-types            see what would change
///   dotnet run -- --normalise-types --apply    write it
///   dotnet run -- --normalise-types --undo     put the last run back
/// </code>
///
/// <para>
/// The dry run is the default here, unlike <see cref="StatusMigration"/>. This
/// touches two thousand rows of a register with no history table behind it, so
/// the safe thing has to be the thing that happens when somebody types the
/// command wrong.
/// </para>
///
/// <para>
/// Two changes, both from <see cref="JobVehicleType.Canonical"/> so the rule
/// lives in one place: sixty-four spellings collapse onto sixteen, and a type
/// that carries `DG` hands it to the product column. That second one exists
/// because export jobs had no product column until now, so dangerous goods was
/// being recorded as a kind of container — a hundred and one jobs where nothing
/// else on the row says what is in the box.
/// </para>
///
/// <para>
/// Every value it overwrites is written to <see cref="TypeMigrationBackup"/> in
/// the same transaction as the change, so there is no moment where the change
/// exists and the means to reverse it does not. `--undo` reads that table back.
/// The register has no history table of its own — that absence is the reason a
/// bulk write here needs arguing about, and this is the specific answer to it
/// rather than a general one.
/// </para>
///
/// <para>
/// Nothing is guessed. A type this cannot read is left exactly as typed and
/// listed at the end, and a job whose product column already says something
/// other than DG keeps both values and is listed too — moving DG onto a product
/// that disagrees with it would be inventing a fact about a real shipment.
/// </para>
/// </summary>
public static class TypeMigration
{
    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var apply = args.Contains("--apply");
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();

        if (args.Contains("--undo")) return await UndoAsync(app, db);

        var batch = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var backups = new List<TypeMigrationBackup>();

        var jobs = await db.OperationJobs.ToListAsync();
        app.Logger.LogInformation("Read {Count} jobs.", jobs.Count);

        var spellings = new Dictionary<string, int>(StringComparer.Ordinal);
        var unreadable = new Dictionary<string, int>(StringComparer.Ordinal);
        var productClash = new List<string>();
        var dgMoved = 0;
        var changed = 0;

        foreach (var job in jobs)
        {
            var node = JsonNode.Parse(job.Data)?.AsObject();
            if (node is null) continue;

            var before = (node["type"]?.GetValue<string>() ?? "").Trim();
            if (before.Length == 0) continue;

            var canon = JobVehicleType.Canonical(before);
            var product = (node["product"]?.GetValue<string>() ?? "").Trim();
            var newProduct = product;

            // Dangerous goods is what is in the box, not what the box is.
            //
            // Only on a value the rule actually recognised. "1X20 DG >> 1X40 DG"
            // also ends in DG, and running this over it stripped three
            // characters off a note and claimed a DG had been moved — harmless,
            // because the row was thrown out a line later for being unreadable,
            // but it made the count say a thing that had not happened.
            if (JobVehicleType.IsKnown(canon)
                && canon.EndsWith(" DG", StringComparison.OrdinalIgnoreCase))
            {
                var withoutDg = canon[..^3].Trim();
                var productSaysDg = product.Contains("DG", StringComparison.OrdinalIgnoreCase)
                    && !product.Contains("NON", StringComparison.OrdinalIgnoreCase);

                if (product.Length == 0)
                {
                    newProduct = "DG";
                    canon = withoutDg;
                    dgMoved++;
                }
                else if (productSaysDg)
                {
                    canon = withoutDg;
                }
                else
                {
                    // The row already says it is carrying something else. Both
                    // values stay and a person decides.
                    productClash.Add($"{job.Key} ({job.Cat}): type {before} · product {product}");
                }
            }

            if (canon == before && newProduct == product) continue;

            if (!JobVehicleType.IsKnown(canon))
            {
                unreadable[before] = unreadable.GetValueOrDefault(before) + 1;
                continue;
            }

            spellings[$"{before} → {canon}"] = spellings.GetValueOrDefault($"{before} → {canon}") + 1;
            changed++;

            if (!apply) continue;

            node["type"] = canon;
            if (newProduct != product) node["product"] = newProduct;

            // Written into the job's own history, like any other edit. A value
            // that changed overnight with nothing saying why is the kind of
            // thing that gets blamed on the person who touched the row last.
            if (node["hist"] is JsonArray history)
            {
                history.Add(new JsonObject
                {
                    ["ts"] = DateTimeOffset.Now.ToString("HH:mm"),
                    ["user"] = "type-migration",
                    ["field"] = newProduct != product ? "type + product (จัดมาตรฐาน)" : "type (จัดมาตรฐาน)",
                    ["old"] = before,
                    ["neu"] = newProduct != product ? $"{canon} · DG → product" : canon,
                });
            }

            job.Data = node.ToJsonString();
            job.UpdatedBy = "type-migration";
            job.UpdatedAt = DateTimeOffset.UtcNow;

            backups.Add(new TypeMigrationBackup
            {
                JobKey = job.Key, Batch = batch,
                OldType = before, NewType = canon,
                OldProduct = product, NewProduct = newProduct,
                TakenAt = DateTimeOffset.UtcNow,
            });
        }

        if (apply)
        {
            // The copy and the change go in together. The retry strategy
            // refuses a hand-opened transaction unless the whole unit of work
            // goes through it.
            db.TypeMigrationBackups.AddRange(backups);
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            });
            app.Logger.LogInformation("Batch {Batch} · {Count} rows copied first. Undo with --normalise-types --undo.",
                batch, backups.Count);
        }

        app.Logger.LogInformation("{Verb} {Changed} of {Total} jobs · {Dg} moved DG into the product column.",
            apply ? "Changed" : "Would change", changed, jobs.Count, dgMoved);

        foreach (var (mapping, count) in spellings.OrderByDescending(entry => entry.Value))
            app.Logger.LogInformation("   {Count,5}  {Mapping}", count, mapping);

        if (unreadable.Count > 0)
        {
            app.Logger.LogWarning("Left exactly as typed — this rule cannot read them, so a person should:");
            foreach (var (value, count) in unreadable.OrderByDescending(entry => entry.Value))
                app.Logger.LogWarning("   {Count,5}  {Value}", count, value);
        }

        if (productClash.Count > 0)
        {
            app.Logger.LogWarning("DG left on the type — the product column already says something else:");
            foreach (var line in productClash.Take(20)) app.Logger.LogWarning("   {Line}", line);
        }

        if (!apply)
        {
            app.Logger.LogInformation("Nothing was written. Add --apply to write it.");
        }

        return 0;
    }

    /// <summary>
    /// Puts the most recent run back, row by row, from the copy it took.
    ///
    /// Only the most recent: undoing an older batch when a newer one has run
    /// over the same rows would restore values that were true two changes ago.
    /// A row whose type has been edited by a person since the run is left
    /// alone and listed — their edit is newer than this, and newer wins.
    /// </summary>
    private static async Task<int> UndoAsync(WebApplication app, ScmosDbContext db)
    {
        var batch = await db.TypeMigrationBackups
            .OrderByDescending(row => row.TakenAt)
            .Select(row => row.Batch)
            .FirstOrDefaultAsync();

        if (batch is null)
        {
            app.Logger.LogWarning("No run to undo.");
            return 0;
        }

        var rows = await db.TypeMigrationBackups.Where(row => row.Batch == batch).ToListAsync();
        var jobs = await db.OperationJobs
            .Where(job => rows.Select(row => row.JobKey).Contains(job.Key))
            .ToListAsync();
        var byKey = jobs.ToDictionary(job => job.Key, StringComparer.Ordinal);

        var restored = 0;
        var editedSince = new List<string>();

        foreach (var row in rows)
        {
            if (!byKey.TryGetValue(row.JobKey, out var job)) continue;
            var node = JsonNode.Parse(job.Data)?.AsObject();
            if (node is null) continue;

            var current = (node["type"]?.GetValue<string>() ?? "").Trim();
            if (current != row.NewType) { editedSince.Add($"{row.JobKey}: {current}"); continue; }

            node["type"] = row.OldType;
            if (row.NewProduct != row.OldProduct) node["product"] = row.OldProduct;
            job.Data = node.ToJsonString();
            job.UpdatedBy = "type-migration-undo";
            job.UpdatedAt = DateTimeOffset.UtcNow;
            restored++;
        }

        db.TypeMigrationBackups.RemoveRange(rows);

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        });

        app.Logger.LogInformation("Undid batch {Batch} · restored {Restored} of {Total}.",
            batch, restored, rows.Count);
        if (editedSince.Count > 0)
        {
            app.Logger.LogWarning("Left alone — somebody edited these after the run, and their edit is newer:");
            foreach (var line in editedSince.Take(20)) app.Logger.LogWarning("   {Line}", line);
        }
        return 0;
    }
}
