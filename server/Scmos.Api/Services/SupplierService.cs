using Microsoft.EntityFrameworkCore;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

public record SupplierSummary(
    int Id, string Code, string Name, string Status, string ServiceType, string ServiceArea,
    bool DgCapable, bool ReeferCapable, bool IsoTankCapable, bool GpsEquipped,
    int Jobs, int Lanes, int Trucks, int Drivers,
    int? LastScore, string LastEvaluatedPeriod,
    IReadOnlyList<string> Aliases, int ExpiringDocuments,
    // Carried so the register's own screen can edit them. Everything stored
    // about a company is here; what is missing from this record is what is
    // counted rather than typed.
    string VendorNo, string TaxId, string Address,
    /// <summary>
    /// Everything hanging off this row, counted the same eight ways
    /// <see cref="SupplierService.HoldingsAsync"/> counts them.
    ///
    /// It exists so the screen and the API cannot disagree about whether a row
    /// can be removed. The screen greys its delete button on this number; the
    /// API refuses on its own count. Two rules for one decision is how they end
    /// up saying different things, so this is the same rule read twice, and the
    /// two must be changed together.
    /// </summary>
    int Attached);

public record SupplierProfileView(
    SupplierSummary Summary,
    IReadOnlyList<SupplierContact> Contacts,
    IReadOnlyList<DocumentView> Documents,
    IReadOnlyList<SupplierTruck> Trucks,
    IReadOnlyList<SupplierDriver> Drivers,
    IReadOnlyList<SupplierEvaluation> Evaluations,
    IReadOnlyList<SupplierCapacity> Capacity,
    /// <summary>On-time, confirmation and delay figures for this carrier only.</summary>
    SupplierScore? Performance,
    IReadOnlyList<IncidentCase> Incidents);

public record SupplierResult(bool Ok, string Message, int? Id = null);

/// <summary>
/// The supplier register.
///
/// One row per company, with every spelling anyone has typed pointing at it.
/// That reconciliation is what makes the rest possible: a supplier profile can
/// gather the jobs, the rates, the incidents and the score for a company that
/// the register calls three different things.
/// </summary>
public class SupplierService(ScmosDbContext db, KpiEngine kpi)
{
    /// <summary>The supplier a spelling means, or null when nobody has said.</summary>
    public async Task<int?> ResolveAsync(string spelling, CancellationToken token)
    {
        var key = (spelling ?? "").Trim().ToUpperInvariant();
        if (key.Length == 0) return null;
        var alias = await db.SupplierAliases.AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.Alias == key, token);
        return alias?.SupplierId;
    }

    public async Task<IReadOnlyList<SupplierSummary>> ListAsync(string? status, string? query, CancellationToken token)
    {
        var suppliers = await db.Suppliers.AsNoTracking().OrderBy(supplier => supplier.Name).ToListAsync(token);
        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            suppliers = suppliers.Where(supplier => supplier.Status == status).ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var wanted = query.Trim().ToLowerInvariant();
            suppliers = suppliers.Where(supplier =>
                supplier.Name.ToLowerInvariant().Contains(wanted) ||
                supplier.Code.ToLowerInvariant().Contains(wanted)).ToList();
        }

        var ids = suppliers.Select(supplier => supplier.Id).ToHashSet();
        var aliases = await db.SupplierAliases.AsNoTracking()
            .Where(alias => ids.Contains(alias.SupplierId)).ToListAsync(token);

        // Jobs are counted through the aliases, which is the entire point of
        // having them: TATIYAPOL's jobs and TATIYAPON's jobs are one company's
        // jobs once somebody has said the two spellings mean the same firm.
        var aliasToSupplier = aliases.ToDictionary(alias => alias.Alias, alias => alias.SupplierId);
        var jobCounts = new Dictionary<int, int>();
        foreach (var carrier in await db.OperationJobs.AsNoTracking()
                     .Where(job => job.Trucker != "").Select(job => job.Trucker).ToListAsync(token))
        {
            if (!aliasToSupplier.TryGetValue(carrier.Trim().ToUpperInvariant(), out var id)) continue;
            jobCounts[id] = jobCounts.GetValueOrDefault(id) + 1;
        }

        var laneCounts = await db.RateLanes.AsNoTracking()
            .Where(lane => lane.SupplierId != null)
            .GroupBy(lane => lane.SupplierId!.Value)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var truckCounts = await db.SupplierTrucks.AsNoTracking()
            .GroupBy(truck => truck.SupplierId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var driverCounts = await db.SupplierDrivers.AsNoTracking()
            .GroupBy(driver => driver.SupplierId)
            .Select(group => new { Id = group.Key, Count = group.Count() })
            .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var documents = await db.Documents.AsNoTracking()
            .Where(document => document.SupplierId != null && document.ExpiryDate != "")
            .ToListAsync(token);

        // The rest of what a row can be holding, in bulk. Counted here rather
        // than per supplier because the register runs to eighty-odd companies
        // and this screen opens on every visit.
        async Task<Dictionary<int, int>> Count<T>(IQueryable<T> set,
            System.Linq.Expressions.Expression<Func<T, int>> owner) where T : class
            => await set.AsNoTracking().GroupBy(owner)
                .Select(group => new { Id = group.Key, Count = group.Count() })
                .ToDictionaryAsync(entry => entry.Id, entry => entry.Count, token);

        var docCounts = await Count(db.Documents.Where(row => row.SupplierId != null),
            row => row.SupplierId!.Value);
        var evaluationCounts = await Count(db.SupplierEvaluations, row => row.SupplierId);
        var contactCounts = await Count(db.SupplierContacts, row => row.SupplierId);
        var capacityCounts = await Count(db.SupplierCapacities, row => row.SupplierId);

        return suppliers.Select(supplier => new SupplierSummary(
            supplier.Id, supplier.Code, supplier.Name, supplier.Status,
            supplier.ServiceType, supplier.ServiceArea,
            supplier.DgCapable, supplier.ReeferCapable, supplier.IsoTankCapable, supplier.GpsEquipped,
            jobCounts.GetValueOrDefault(supplier.Id),
            laneCounts.GetValueOrDefault(supplier.Id),
            truckCounts.GetValueOrDefault(supplier.Id),
            driverCounts.GetValueOrDefault(supplier.Id),
            supplier.LastScore, supplier.LastEvaluatedPeriod,
            aliases.Where(alias => alias.SupplierId == supplier.Id)
                .Select(alias => alias.Alias).OrderBy(name => name).ToList(),
            // Expired counts as expiring: a certificate that lapsed last month is
            // more urgent than one lapsing next month, and dropping it off the
            // count would make it disappear at exactly the wrong moment.
            documents.Count(document => document.SupplierId == supplier.Id
                && (DocumentService.IsExpiring(document.ExpiryDate)
                    || DocumentService.IsExpired(document.ExpiryDate))),
            supplier.VendorNo, supplier.TaxId, supplier.Address,
            jobCounts.GetValueOrDefault(supplier.Id)
                + laneCounts.GetValueOrDefault(supplier.Id)
                + docCounts.GetValueOrDefault(supplier.Id)
                + evaluationCounts.GetValueOrDefault(supplier.Id)
                + contactCounts.GetValueOrDefault(supplier.Id)
                + truckCounts.GetValueOrDefault(supplier.Id)
                + driverCounts.GetValueOrDefault(supplier.Id)
                + capacityCounts.GetValueOrDefault(supplier.Id))).ToList();
    }

    public async Task<SupplierProfileView?> ProfileAsync(int id, CancellationToken token)
    {
        var summaries = await ListAsync(null, null, token);
        var summary = summaries.FirstOrDefault(entry => entry.Id == id);
        if (summary is null) return null;

        var report = await kpi.BuildAsync(Period.All, token);
        var aliases = summary.Aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var performance = report.Suppliers.FirstOrDefault(score => aliases.Contains(score.Carrier));

        return new SupplierProfileView(
            summary,
            await db.SupplierContacts.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            (await db.Documents.AsNoTracking().Where(x => x.SupplierId == id)
                .OrderByDescending(x => x.Id).ToListAsync(token))
                .Select(DocumentService.Describe).ToList(),
            await db.SupplierTrucks.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            await db.SupplierDrivers.AsNoTracking().Where(x => x.SupplierId == id).ToListAsync(token),
            await db.SupplierEvaluations.AsNoTracking().Where(x => x.SupplierId == id)
                .OrderByDescending(x => x.Period).ToListAsync(token),
            await db.SupplierCapacities.AsNoTracking().Where(x => x.SupplierId == id)
                .OrderBy(x => x.Date).Take(60).ToListAsync(token),
            performance,
            await db.IncidentCases.AsNoTracking().Where(x => x.JobKey != "").Take(0).ToListAsync(token));
    }

    /// <summary>Registers a new vendor. The onboarding flow starts it as a draft.</summary>
    public async Task<SupplierResult> RegisterAsync(string name, string code, string serviceType,
        string serviceArea, string by, CancellationToken token)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0) return new SupplierResult(false, "ต้องระบุชื่อผู้ขนส่ง");

        var key = new string(trimmed.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        var clash = await db.SupplierAliases.AsNoTracking()
            .FirstOrDefaultAsync(alias => alias.Alias == trimmed.ToUpperInvariant(), token);
        if (clash is not null) return new SupplierResult(false, "มีผู้ขนส่งชื่อนี้อยู่แล้ว");

        var supplier = new Supplier
        {
            Name = trimmed,
            Code = code.Trim().Length > 0 ? code.Trim().ToUpperInvariant() : key[..Math.Min(6, key.Length)],
            ServiceType = serviceType.Trim(),
            ServiceArea = serviceArea.Trim(),
            Status = "draft",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(token);

        db.SupplierAliases.Add(new SupplierAlias
        {
            SupplierId = supplier.Id, Alias = trimmed.ToUpperInvariant(), Source = "manual", Confirmed = true,
        });
        await db.SaveChangesAsync(token);

        return new SupplierResult(true, $"ลงทะเบียน {trimmed} แล้ว (สถานะ: ร่าง)", supplier.Id);
    }

    /// <summary>
    /// Moves a vendor through onboarding. Approval is the only transition that
    /// records who made it, because it is the one that lets work be given to
    /// this company.
    /// </summary>
    public async Task<SupplierResult> SetStatusAsync(int id, string status, string by, CancellationToken token)
    {
        var allowed = new[] { "draft", "pending-audit", "approved", "suspended", "rejected" };
        var wanted = status.Trim().ToLowerInvariant();
        if (!allowed.Contains(wanted))
            return new SupplierResult(false, "สถานะที่ใช้ได้: " + string.Join(", ", allowed));

        var supplier = await db.Suppliers.FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        supplier.Status = wanted;
        supplier.UpdatedAt = DateTimeOffset.UtcNow;
        if (wanted == "approved")
        {
            supplier.ApprovedAt = DateTimeOffset.UtcNow;
            supplier.ApprovedBy = by;
        }

        await db.SaveChangesAsync(token);
        return new SupplierResult(true, $"{supplier.Name}: {wanted}", supplier.Id);
    }

    /// <summary>What one import of the carrier directory did.</summary>
    public record DirectoryResult(
        int Added, int AlreadyThere, int Renamed, int AliasesLinked,
        IReadOnlyList<string> AliasesWithNoCompany);

    /// <summary>
    /// A six-letter code nothing else holds.
    ///
    /// The column is unique, and the register was seeded with codes cut the
    /// same way, so the obvious one is often already taken. A digit is added
    /// until one is free rather than letting the insert fail — which is what it
    /// did, with a five hundred and no explanation.
    /// </summary>
    private static string Free(string stem, HashSet<string> taken)
    {
        var code = stem[..Math.Min(6, stem.Length)];
        if (!taken.Contains(code)) return code;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var tail = suffix.ToString();
            var head = code[..Math.Max(1, Math.Min(code.Length, 6 - tail.Length))];
            var candidate = head + tail;
            if (!taken.Contains(candidate)) return candidate;
        }
        return code + Guid.NewGuid().ToString("N")[..4];
    }

    /// <summary>
    /// Loads the agreed list of haulage companies, and the short forms the plan
    /// sheets write them as.
    /// </summary>
    /// <remarks>
    /// The list is pasted in rather than shipped with the application, and that
    /// is deliberate. Sixty-odd company names are the same kind of data as the
    /// customer list and the rate book: they belong to the business, not to the
    /// software, and the rule here has been that they live in the database and
    /// never in the repository or a deployment package.
    ///
    /// Nothing is deleted and nothing is overwritten. A company already on the
    /// register keeps its status, its vendor number and its documents — an
    /// import is somebody saying "these companies exist", not "these are the
    /// only companies that have ever existed". Removing a haulier is a decision
    /// with paperwork attached and does not belong in a bulk paste.
    ///
    /// The aliases are what make the whole thing worth doing. The register
    /// spells one company four ways — SANGJA and SJ, ACN and A.C.N — and every
    /// figure grouped by haulier has been counting those as different firms.
    /// </remarks>
    public async Task<DirectoryResult> ImportDirectoryAsync(
        IReadOnlyList<string> names,
        IReadOnlyList<(string Alias, string Company)> aliases,
        string by, CancellationToken token)
    {
        static string Key(string value) =>
            new(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

        /// The company's own name, with the legal suffix and the country off.
        static string Stem(string name)
        {
            var text = System.Text.RegularExpressions.Regex.Replace(name,
                @"\((Thailand|Thailnad)\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text,
                @"(Co\.?,?\s*Ltd\.?|Company Limited|Limited Partnership|Ltd\.?,?\s*Partnership|Public Company Limited|Ltd\.?)\s*$",
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return Key(text);
        }

        var added = 0;
        var already = 0;
        var renamed = 0;
        var linked = 0;
        var orphans = new List<string>();

        // All of it or none of it.
        //
        // This used to save after every company with nothing around it, so the
        // run that failed on a duplicate code had already committed everything
        // before the failure — and left JTC Logistics on the register twice,
        // one of them with no aliases because the alias was still unsaved in
        // the tracker when the throw came. A partial import is worse than a
        // failed one: a failed one you simply run again.
        //
        // Through the execution strategy because this context retries on
        // failure, and a retrying strategy refuses a transaction opened by
        // hand. The lambda can run more than once, so the counters are reset
        // inside it rather than outside.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var work = await db.Database.BeginTransactionAsync(token);
            added = 0; already = 0; renamed = 0; linked = 0;
            orphans.Clear();

            // The register as it stands, read once and kept in step as the loop
            // changes it. Read once because seventy-two names meant seventy-two
            // reads of the whole table; kept in step because a company added or
            // renamed earlier in this same run has to be a candidate for the
            // next one — which is how a half-finished row from an earlier
            // import gets recognised instead of doubled.
            var known = (await db.Suppliers.AsNoTracking()
                .Select(row => new { row.Id, row.Name })
                .ToListAsync(token))
                .Select(row => (row.Id, row.Name)).ToList();

            // Every code in use, so a new one never collides. The column is
            // unique and the register was seeded with six-letter codes cut the
            // same way, so WEALTH was taken long before this import went
            // anywhere near "Wealthy Logistic Co., Ltd."
            var takenCodes = new HashSet<string>(
                await db.Suppliers.AsNoTracking().Select(row => row.Code).ToListAsync(token),
                StringComparer.OrdinalIgnoreCase);

            foreach (var raw in names)
            {
                var name = raw.Trim();
                if (name.Length == 0) continue;

                var key = name.ToUpperInvariant();
                var exact = await db.SupplierAliases.AsNoTracking()
                    .FirstOrDefaultAsync(alias => alias.Alias == key, token);
                if (exact is not null) { already++; continue; }

                // The same company under the short name the team has always
                // used. The register holds WEALTHY with its documents and its
                // evaluations; this list calls it Wealthy Logistic Co., Ltd.
                // Adding a second row would give one haulier two entries and
                // split its history, so the row already there takes the
                // official name instead — but only when exactly one existing
                // company could be meant. Anything less certain is left alone.
                var stem = Stem(name);
                var sameFirm = known
                    .Where(row => Key(row.Name).Length >= 2
                        && stem.StartsWith(Key(row.Name), StringComparison.Ordinal))
                    .ToList();

                if (sameFirm.Count == 1)
                {
                    var row = await db.Suppliers.FirstAsync(entry => entry.Id == sameFirm[0].Id, token);
                    row.Name = name;
                    row.UpdatedAt = DateTimeOffset.UtcNow;
                    db.SupplierAliases.Add(new SupplierAlias
                    { SupplierId = row.Id, Alias = key, Source = "directory", Confirmed = true });
                    await db.SaveChangesAsync(token);
                    known.Remove(sameFirm[0]);
                    known.Add((row.Id, row.Name));
                    renamed++;
                    continue;
                }

                var stub = Key(name);
                var code = Free(stub.Length > 0 ? stub : "SUP", takenCodes);
                takenCodes.Add(code);

                var supplier = new Supplier
                {
                    Name = name,
                    Code = code,
                    Status = "approved",
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                db.Suppliers.Add(supplier);
                await db.SaveChangesAsync(token);

                // Its own name is its first alias, so a job spelling the
                // company out in full resolves without anybody adding anything.
                db.SupplierAliases.Add(new SupplierAlias
                { SupplierId = supplier.Id, Alias = key, Source = "directory", Confirmed = true });
                await db.SaveChangesAsync(token);
                known.Add((supplier.Id, supplier.Name));
                added++;
            }

            foreach (var (shortForm, company) in aliases)
            {
                var alias = shortForm.Trim().ToUpperInvariant();
                var wanted = company.Trim().ToUpperInvariant();
                if (alias.Length == 0 || wanted.Length == 0) continue;

                var owner = await db.SupplierAliases.AsNoTracking()
                    .FirstOrDefaultAsync(entry => entry.Alias == wanted, token);
                if (owner is null)
                {
                    // Named a company that is not on the list. Reported rather
                    // than created: a haulier invented by a typo in an alias
                    // line is exactly the sort of row nobody later dares
                    // delete.
                    orphans.Add($"{shortForm.Trim()} → {company.Trim()}");
                    continue;
                }

                var held = await db.SupplierAliases.FirstOrDefaultAsync(entry => entry.Alias == alias, token);
                if (held is null)
                {
                    db.SupplierAliases.Add(new SupplierAlias
                    { SupplierId = owner.SupplierId, Alias = alias, Source = "directory", Confirmed = true });
                    linked++;
                }
                else if (held.SupplierId != owner.SupplierId)
                {
                    held.SupplierId = owner.SupplierId;
                    held.Confirmed = true;
                    held.Source = "directory";
                    linked++;
                }
            }
            await db.SaveChangesAsync(token);

            await work.CommitAsync(token);
        });

        return new DirectoryResult(added, already, renamed, linked, orphans);
    }

    /// <summary>Attaches a spelling to a supplier — how TTP gets merged into a company.</summary>
    public async Task<SupplierResult> LinkAliasAsync(int id, string alias, string by, CancellationToken token)
    {
        var key = alias.Trim().ToUpperInvariant();
        if (key.Length == 0) return new SupplierResult(false, "ต้องระบุชื่อที่จะผูก");

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var existing = await db.SupplierAliases.FirstOrDefaultAsync(entry => entry.Alias == key, token);
        if (existing is not null)
        {
            if (existing.SupplierId == id) return new SupplierResult(false, "ผูกไว้แล้ว");
            existing.SupplierId = id;
            existing.Confirmed = true;
            existing.Source = "manual";
        }
        else
        {
            db.SupplierAliases.Add(new SupplierAlias
            { SupplierId = id, Alias = key, Source = "manual", Confirmed = true });
        }

        // Rate lanes carrying that spelling now belong to this supplier too.
        var lanes = await db.RateLanes.Where(lane => lane.Carrier.ToUpper() == key).ToListAsync(token);
        foreach (var lane in lanes) lane.SupplierId = id;

        await db.SaveChangesAsync(token);
        return new SupplierResult(true, $"ผูก {key} เข้ากับ {supplier.Name} แล้ว ({lanes.Count} เส้นทางราคา)", id);
    }

    /// <summary>
    /// Records an annual evaluation.
    ///
    /// The operational scores come from the KPI engine so the meeting argues
    /// with measured figures rather than remembered ones; safety and document
    /// scores are the assessor's own.
    /// </summary>
    public async Task<SupplierResult> EvaluateAsync(int id, string period, int? safety, int? documents,
        string note, string by, CancellationToken token)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(entry => entry.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");
        if (period.Trim().Length == 0) return new SupplierResult(false, "ต้องระบุรอบการประเมิน");

        var profile = await ProfileAsync(id, token);
        var performance = profile?.Performance;

        var onTime = performance?.OnTime is null ? null : (int?)Math.Round(performance.OnTime.Value);
        var confirmation = performance?.Confirmation is null ? null : (int?)Math.Round(performance.Confirmation.Value);
        var delayFree = performance?.DelayFree is null ? null : (int?)Math.Round(performance.DelayFree.Value);

        var parts = new List<int>();
        if (onTime is not null) parts.Add(onTime.Value);
        if (confirmation is not null) parts.Add(confirmation.Value);
        if (delayFree is not null) parts.Add(delayFree.Value);
        if (safety is not null) parts.Add(safety.Value);
        if (documents is not null) parts.Add(documents.Value);

        var total = parts.Count == 0 ? (int?)null : (int)Math.Round(parts.Average());
        var grade = total is null ? "" : total >= 85 ? "A" : total >= 70 ? "B" : total >= 55 ? "C" : "D";

        var existing = await db.SupplierEvaluations
            .FirstOrDefaultAsync(entry => entry.SupplierId == id && entry.Period == period.Trim(), token);

        if (existing is null)
        {
            existing = new SupplierEvaluation
            { SupplierId = id, Period = period.Trim(), CreatedAt = DateTimeOffset.UtcNow };
            db.SupplierEvaluations.Add(existing);
        }

        existing.OnTimeScore = onTime;
        existing.ConfirmationScore = confirmation;
        existing.DelayScore = delayFree;
        existing.SafetyScore = safety;
        existing.DocumentScore = documents;
        existing.TotalScore = total;
        existing.Grade = grade;
        existing.Note = note.Trim();
        existing.Stage = "submitted";
        existing.EvaluatedBy = by;

        supplier.LastScore = total;
        supplier.LastEvaluatedPeriod = period.Trim();
        supplier.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(token);
        return new SupplierResult(true,
            total is null
                ? "บันทึกการประเมินแล้ว — ยังไม่มีข้อมูลพอให้คะแนน"
                : $"ประเมิน {supplier.Name} รอบ {period}: {total} คะแนน (เกรด {grade})",
            id);
    }

    /// <summary>One haulier the register holds more than once.</summary>
    /// <param name="Keep">The row with the history — the one to keep.</param>
    /// <param name="Fold">The rows holding nothing, to go into it.</param>
    public record DuplicateGroup(string Name, SupplierSide Keep, IReadOnlyList<SupplierSide> Fold);

    /// <param name="Attached">
    /// Everything hanging off this row. Jobs are not counted here: a job names
    /// a haulier by spelling, not by id, so what a row really holds is its
    /// aliases and its paperwork.
    /// </param>
    public record SupplierSide(int Id, string Code, string Name, int Aliases, int Attached);

    /// <summary>The name with punctuation, spacing and the legal suffix off.</summary>
    private static string NameStem(string name)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(name,
            @"\((Thailand|Thailnad)\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(text,
            @"(Co\.?,?\s*Ltd\.?|Company Limited|Limited Partnership|Ltd\.?,?\s*Partnership|Public Company Limited|Ltd\.?)\s*$",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return new(text.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    /// <summary>
    /// Companies the register lists twice, and which of the two has the history.
    ///
    /// Two rows are the same haulier when their names match once punctuation,
    /// spacing and the legal suffix are off — SANGJA and "Sangja Transport
    /// Co., Ltd." are one firm — so the comparison is on the stem, not the
    /// string. The row that keeps its history wins: most aliases, then most
    /// paperwork, then the oldest, because the oldest is what other things were
    /// attached to first.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateGroup>> DuplicatesAsync(CancellationToken token)
    {
        var rows = await db.Suppliers.AsNoTracking()
            .Select(row => new { row.Id, row.Code, row.Name, row.CreatedAt })
            .ToListAsync(token);

        var aliasCount = (await db.SupplierAliases.AsNoTracking()
            .GroupBy(alias => alias.SupplierId)
            .Select(group => new { Id = group.Key, N = group.Count() })
            .ToListAsync(token))
            .ToDictionary(entry => entry.Id, entry => entry.N);

        var attached = new Dictionary<int, int>();
        async Task Tally<T>(IQueryable<T> set, System.Linq.Expressions.Expression<Func<T, int>> owner)
            where T : class
        {
            var counted = await set.AsNoTracking().GroupBy(owner)
                .Select(group => new { Id = group.Key, N = group.Count() })
                .ToListAsync(token);
            foreach (var entry in counted)
                attached[entry.Id] = attached.TryGetValue(entry.Id, out var had) ? had + entry.N : entry.N;
        }

        await Tally(db.SupplierContacts, row => row.SupplierId);
        await Tally(db.SupplierTrucks, row => row.SupplierId);
        await Tally(db.SupplierDrivers, row => row.SupplierId);
        await Tally(db.SupplierCapacities, row => row.SupplierId);
        await Tally(db.SupplierEvaluations, row => row.SupplierId);
        await Tally(db.RateLanes.Where(row => row.SupplierId != null), row => row.SupplierId!.Value);
        await Tally(db.Documents.Where(row => row.SupplierId != null), row => row.SupplierId!.Value);

        SupplierSide Side(int id, string code, string name) => new(
            id, code, name,
            aliasCount.TryGetValue(id, out var a) ? a : 0,
            attached.TryGetValue(id, out var t) ? t : 0);

        var groups = new List<DuplicateGroup>();
        foreach (var group in rows.GroupBy(row => NameStem(row.Name)).Where(g => g.Key.Length > 0))
        {
            if (group.Count() < 2) continue;

            var ordered = group
                .OrderByDescending(row => aliasCount.TryGetValue(row.Id, out var a) ? a : 0)
                .ThenByDescending(row => attached.TryGetValue(row.Id, out var t) ? t : 0)
                .ThenBy(row => row.CreatedAt)
                .ToList();

            // The longest spelling is the official one, so the merged row takes
            // it even when the history sits on the row still called "JTC".
            var full = group.OrderByDescending(row => row.Name.Length).First().Name;

            groups.Add(new DuplicateGroup(full,
                Side(ordered[0].Id, ordered[0].Code, ordered[0].Name),
                ordered.Skip(1).Select(row => Side(row.Id, row.Code, row.Name)).ToList()));
        }

        return groups.OrderBy(group => group.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Folds one row of the register into another and removes the emptied one.
    ///
    /// This is the only place a supplier row is ever removed, and it removes
    /// one only after everything it held has been moved to the row that
    /// survives: aliases, contacts, lorries, drivers, capacity, evaluations,
    /// rate lanes and documents. What goes is an empty row, not a record —
    /// nothing written about the haulier is lost, it simply all ends up in one
    /// place.
    ///
    /// It exists because an import that failed halfway left rows behind, and
    /// because two rows for one haulier is not a cosmetic problem: every figure
    /// grouped by supplier counts them as two firms.
    /// </summary>
    public async Task<SupplierResult> MergeAsync(int keepId, int foldId, string by, CancellationToken token)
    {
        if (keepId == foldId) return new SupplierResult(false, "เป็นรายการเดียวกัน");

        var keep = await db.Suppliers.FirstOrDefaultAsync(row => row.Id == keepId, token);
        var fold = await db.Suppliers.FirstOrDefaultAsync(row => row.Id == foldId, token);
        if (keep is null || fold is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var moved = 0;
        var clashes = 0;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var work = await db.Database.BeginTransactionAsync(token);
            moved = 0;
            clashes = 0;

            // An alias the surviving row already holds is dropped rather than
            // moved: the column is unique, and two rows spelled the same way is
            // the very thing being undone.
            var held = await db.SupplierAliases.Where(row => row.SupplierId == keepId)
                .Select(row => row.Alias).ToListAsync(token);
            foreach (var alias in await db.SupplierAliases.Where(row => row.SupplierId == foldId).ToListAsync(token))
            {
                if (held.Contains(alias.Alias)) db.SupplierAliases.Remove(alias);
                else { alias.SupplierId = keepId; moved++; }
            }

            foreach (var row in await db.SupplierContacts.Where(r => r.SupplierId == foldId).ToListAsync(token))
            { row.SupplierId = keepId; moved++; }
            foreach (var row in await db.SupplierTrucks.Where(r => r.SupplierId == foldId).ToListAsync(token))
            { row.SupplierId = keepId; moved++; }
            foreach (var row in await db.SupplierDrivers.Where(r => r.SupplierId == foldId).ToListAsync(token))
            { row.SupplierId = keepId; moved++; }
            foreach (var row in await db.SupplierCapacities.Where(r => r.SupplierId == foldId).ToListAsync(token))
            { row.SupplierId = keepId; moved++; }
            foreach (var row in await db.RateLanes.Where(r => r.SupplierId == foldId).ToListAsync(token))
            { row.SupplierId = keepId; moved++; }
            foreach (var row in await db.Documents.Where(r => r.SupplierId == foldId).ToListAsync(token))
            { row.SupplierId = keepId; moved++; }

            // One evaluation per supplier per period, so a period both rows
            // were scored in keeps the surviving row's score and the other is
            // left where it is. The row cannot then be removed, and the caller
            // is told why rather than a score being quietly thrown away.
            var periods = await db.SupplierEvaluations.Where(r => r.SupplierId == keepId)
                .Select(r => r.Period).ToListAsync(token);
            foreach (var row in await db.SupplierEvaluations.Where(r => r.SupplierId == foldId).ToListAsync(token))
            {
                if (periods.Contains(row.Period)) { clashes++; continue; }
                row.SupplierId = keepId;
                moved++;
            }

            // The official spelling survives the merge even when the history
            // sat on the row spelled short.
            if (fold.Name.Length > keep.Name.Length) keep.Name = fold.Name;
            keep.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(token);

            if (clashes == 0)
            {
                db.Suppliers.Remove(fold);
                await db.SaveChangesAsync(token);
            }

            await work.CommitAsync(token);
        });

        return new SupplierResult(true,
            clashes > 0
                ? $"ย้ายข้อมูล {moved} รายการมาที่ {keep.Name} แล้ว — แต่ยังลบ {fold.Code} ไม่ได้ เพราะมีผลประเมินรอบเดียวกันทั้งสองราย"
                : $"รวม {fold.Code} เข้ากับ {keep.Name} แล้ว — ย้ายข้อมูล {moved} รายการ",
            keepId);
    }

    /// <summary>What a supplier row is holding, and therefore whether it can go.</summary>
    /// <param name="Jobs">
    /// Counted through the aliases, the same way the register's own list counts
    /// them, so a company whose jobs are all spelled the short way is not
    /// mistaken for one that has never worked.
    /// </param>
    public record SupplierHoldings(
        int Jobs, int Lanes, int Documents, int Evaluations,
        int Contacts, int Trucks, int Drivers, int Capacity)
    {
        public int Total => Jobs + Lanes + Documents + Evaluations
            + Contacts + Trucks + Drivers + Capacity;

        /// <summary>What is in the way, named, for somebody to read.</summary>
        public string Describe() => string.Join(" · ", new[]
        {
            Jobs > 0 ? $"งาน {Jobs}" : null,
            Lanes > 0 ? $"เส้นทางราคา {Lanes}" : null,
            Documents > 0 ? $"เอกสาร {Documents}" : null,
            Evaluations > 0 ? $"ผลประเมิน {Evaluations}" : null,
            Contacts > 0 ? $"ผู้ติดต่อ {Contacts}" : null,
            Trucks > 0 ? $"รถ {Trucks}" : null,
            Drivers > 0 ? $"พนักงานขับรถ {Drivers}" : null,
            Capacity > 0 ? $"แผนกำลังรถ {Capacity}" : null,
        }.Where(part => part is not null));
    }

    /// <summary>
    /// Everything hanging off one supplier row.
    ///
    /// The same eight things <see cref="SupplierSummary.Attached"/> totals for
    /// the list. That total is what greys the screen's delete button and this
    /// is what refuses the call, so the two have to count the same set — change
    /// one and change the other.
    /// </summary>
    internal async Task<SupplierHoldings> HoldingsAsync(int id, CancellationToken token)
    {
        var aliases = await db.SupplierAliases.AsNoTracking()
            .Where(alias => alias.SupplierId == id)
            .Select(alias => alias.Alias).ToListAsync(token);
        var spellings = aliases.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var jobs = 0;
        if (spellings.Count > 0)
        {
            foreach (var carrier in await db.OperationJobs.AsNoTracking()
                         .Where(job => job.Trucker != "").Select(job => job.Trucker).ToListAsync(token))
                if (spellings.Contains(carrier.Trim().ToUpperInvariant())) jobs++;
        }

        return new SupplierHoldings(
            jobs,
            await db.RateLanes.CountAsync(row => row.SupplierId == id, token),
            await db.Documents.CountAsync(row => row.SupplierId == id, token),
            await db.SupplierEvaluations.CountAsync(row => row.SupplierId == id, token),
            await db.SupplierContacts.CountAsync(row => row.SupplierId == id, token),
            await db.SupplierTrucks.CountAsync(row => row.SupplierId == id, token),
            await db.SupplierDrivers.CountAsync(row => row.SupplierId == id, token),
            await db.SupplierCapacities.CountAsync(row => row.SupplierId == id, token));
    }

    /// <summary>
    /// Removes a supplier that is holding nothing.
    ///
    /// The register is a list of companies, and a list nobody may take a wrong
    /// name off is a list that only ever grows — a typo, a company that never
    /// traded, a name pasted twice. So a row can be removed, and the rule is
    /// what makes it safe rather than a confirmation box: a row with a single
    /// job, rate, document, evaluation, contact, lorry, driver or capacity line
    /// against it is refused, and what is in the way is named. Nothing is
    /// cascaded and nothing is orphaned, because there is by definition nothing
    /// there to cascade to.
    ///
    /// Its own spellings go with it. An alias is not a record about the
    /// company, it is how the company is written, and leaving them behind would
    /// point at a row that no longer exists.
    ///
    /// A company that has traded is not deleted, it is merged — see
    /// <see cref="MergeAsync"/>, which moves the history first and only then
    /// removes the row it emptied.
    /// </summary>
    public async Task<SupplierResult> RemoveAsync(int id, string by, CancellationToken token)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(row => row.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var holdings = await HoldingsAsync(id, token);
        if (holdings.Total > 0)
            return new SupplierResult(false,
                $"ลบ {supplier.Name} ไม่ได้ — ยังมี {holdings.Describe()} ผูกอยู่"
                + (holdings.Jobs > 0
                    ? " · ถ้าเป็นบริษัทเดียวกับอีกรายให้ใช้ \"รวมรายการซ้ำ\" แทน"
                    : ""));

        var name = supplier.Name;
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var work = await db.Database.BeginTransactionAsync(token);
            db.SupplierAliases.RemoveRange(
                await db.SupplierAliases.Where(alias => alias.SupplierId == id).ToListAsync(token));
            db.Suppliers.Remove(supplier);
            await db.SaveChangesAsync(token);
            await work.CommitAsync(token);
        });

        return new SupplierResult(true, $"ลบ {name} ออกจากทะเบียนแล้ว", id);
    }

    /// <summary>
    /// Every field of a supplier that is typed rather than counted.
    ///
    /// Null means "leave this one alone", so a screen editing one box does not
    /// have to send, and be trusted with, the other eleven.
    /// </summary>
    public record SupplierEdit(
        string? Code, string? Name, string? Status,
        string? VendorNo, string? TaxId, string? Address,
        string? ServiceArea, string? ServiceType,
        bool? DgCapable, bool? ReeferCapable, bool? IsoTankCapable, bool? GpsEquipped);

    /// <summary>
    /// Corrects a company's own details.
    ///
    /// Everything the register stores about a company can be changed here,
    /// because everything here was typed by somebody and anything typed can be
    /// typed wrong. What cannot be changed is the other half of the screen —
    /// jobs, rate lanes and the last score are counted from the register, the
    /// rate book and the evaluations, and a figure you can overwrite is a
    /// figure that no longer means anything. Correct those where they come
    /// from.
    ///
    /// Two of the fields carry a rule the rest do not:
    ///
    /// <b>Code</b> is unique, so a change that collides is refused by name
    /// rather than by a five hundred from the database.
    ///
    /// <b>Name</b> keeps the old spelling as an alias instead of replacing it.
    /// The aliases are the only chain between a job and a company — a job says
    /// "SANGJA", not "supplier 14" — so renaming the row without keeping the
    /// old spelling would silently detach every job the company has ever done.
    /// The new name is added as a spelling too, so a job typed the new way
    /// resolves from now on.
    /// </summary>
    public async Task<SupplierResult> EditAsync(int id, SupplierEdit edit, string by, CancellationToken token)
    {
        var supplier = await db.Suppliers.FirstOrDefaultAsync(row => row.Id == id, token);
        if (supplier is null) return new SupplierResult(false, "ไม่พบผู้ขนส่งรายนี้");

        var changed = new List<string>();

        if (edit.Code is not null)
        {
            var code = edit.Code.Trim().ToUpperInvariant();
            if (code.Length == 0) return new SupplierResult(false, "รหัสว่างไม่ได้");
            if (code != supplier.Code)
            {
                if (await db.Suppliers.AnyAsync(row => row.Id != id && row.Code == code, token))
                    return new SupplierResult(false, $"รหัส {code} มีผู้ขนส่งรายอื่นใช้อยู่แล้ว");
                changed.Add($"รหัส {supplier.Code} → {code}");
                supplier.Code = code;
            }
        }

        if (edit.Name is not null)
        {
            var name = edit.Name.Trim();
            if (name.Length == 0) return new SupplierResult(false, "ชื่อว่างไม่ได้");
            if (name != supplier.Name)
            {
                var key = name.ToUpperInvariant();
                var held = await db.SupplierAliases.AsNoTracking()
                    .FirstOrDefaultAsync(alias => alias.Alias == key, token);
                if (held is not null && held.SupplierId != id)
                    return new SupplierResult(false, $"ชื่อ {name} เป็นของผู้ขนส่งรายอื่นอยู่แล้ว");

                changed.Add($"ชื่อ {supplier.Name} → {name}");
                // The old spelling stays. Every job this company has done says
                // the old name, and nothing else joins the two.
                if (held is null)
                    db.SupplierAliases.Add(new SupplierAlias
                    { SupplierId = id, Alias = key, Source = "manual", Confirmed = true });
                supplier.Name = name;
            }
        }

        if (edit.Status is not null && edit.Status.Trim() != supplier.Status)
        {
            var wanted = edit.Status.Trim();
            var allowed = new[] { "draft", "pending-audit", "approved", "suspended", "rejected" };
            if (!allowed.Contains(wanted)) return new SupplierResult(false, $"สถานะ {wanted} ไม่ถูกต้อง");
            changed.Add($"สถานะ {supplier.Status} → {wanted}");
            supplier.Status = wanted;
            if (wanted == "approved")
            {
                supplier.ApprovedAt = DateTimeOffset.UtcNow;
                supplier.ApprovedBy = by;
            }
        }

        void Text(string label, string? value, Func<string> read, Action<string> write)
        {
            if (value is null) return;
            var trimmed = value.Trim();
            if (trimmed == read()) return;
            changed.Add($"{label} → {(trimmed.Length == 0 ? "(ว่าง)" : trimmed)}");
            write(trimmed);
        }

        Text("เลขผู้ขาย", edit.VendorNo, () => supplier.VendorNo, value => supplier.VendorNo = value);
        Text("เลขประจำตัวผู้เสียภาษี", edit.TaxId, () => supplier.TaxId, value => supplier.TaxId = value);
        Text("ที่อยู่", edit.Address, () => supplier.Address, value => supplier.Address = value);
        Text("พื้นที่ให้บริการ", edit.ServiceArea, () => supplier.ServiceArea, value => supplier.ServiceArea = value);
        Text("ประเภทบริการ", edit.ServiceType, () => supplier.ServiceType, value => supplier.ServiceType = value);

        void Flag(string label, bool? value, Func<bool> read, Action<bool> write)
        {
            if (value is null || value.Value == read()) return;
            changed.Add($"{label}: {(value.Value ? "ใช่" : "ไม่")}");
            write(value.Value);
        }

        Flag("สินค้าอันตราย", edit.DgCapable, () => supplier.DgCapable, value => supplier.DgCapable = value);
        Flag("ตู้เย็น", edit.ReeferCapable, () => supplier.ReeferCapable, value => supplier.ReeferCapable = value);
        Flag("ไอโซแท็งก์", edit.IsoTankCapable, () => supplier.IsoTankCapable, value => supplier.IsoTankCapable = value);
        Flag("มี GPS", edit.GpsEquipped, () => supplier.GpsEquipped, value => supplier.GpsEquipped = value);

        if (changed.Count == 0) return new SupplierResult(true, "ไม่มีอะไรเปลี่ยน", id);

        supplier.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(token);
        return new SupplierResult(true, $"แก้ไข {supplier.Name} แล้ว — {string.Join(" · ", changed)}", id);
    }
}
