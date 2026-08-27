using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Puts the register onto one spelling per kind of vehicle, and gives dangerous
/// goods a column of its own.
///
/// <code>
///   dotnet run -- --normalise-types           see what would change
///   dotnet run -- --normalise-types --apply   write it
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
/// being recorded as a kind of container — seventy-five jobs where nothing else
/// on the row says what is in the box.
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
            if (canon.EndsWith(" DG", StringComparison.OrdinalIgnoreCase))
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
        }

        if (apply)
        {
            // The retry strategy refuses a hand-opened transaction unless the
            // whole unit of work goes through it.
            var strategy = db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            });
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
}
