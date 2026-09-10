using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>
/// Reads the company's ASL/BSL list out of ABS into the supplier register.
///
/// <code>
///   dotnet run -- --import-asl "D:\path\ASL_BSL List.xlsx"           # counts only
///   dotnet run -- --import-asl "D:\path\ASL_BSL List.xlsx" --apply   # writes
/// </code>
///
/// <para>
/// <b>It prints what it would do and changes nothing unless told to.</b> This
/// is 642 rows landing in the table that decides who a job may be given to, and
/// the difference between "created 642" and "updated 30, created 612" is the
/// difference between an import and a duplicate register. It has to be readable
/// before it is real.
/// </para>
///
/// <para>
/// The file is never committed. It carries the contact name, telephone and
/// email of 642 companies, and it is read from wherever it happens to be on the
/// machine running the import — the path is an argument for exactly that
/// reason, the same way the plan workbooks are handled.
/// </para>
///
/// <para>
/// Matching is the whole difficulty. About eighty of these companies are
/// already in the register because they have been carrying work, spelled
/// however the plan spelled them, and creating a second row for one of them
/// would split its jobs, its rate cards and its score across two suppliers. So
/// a row is matched to an existing supplier by ABS number first, then by the
/// same normalised name key the seeder uses, then by any spelling already
/// recorded as an alias — and only creates a supplier when all three miss.
/// </para>
/// </summary>
public static class AslImporter
{
    /// <summary>
    /// What the import did, or would do.
    ///
    /// Returned rather than printed, because two callers need it: the command
    /// line prints it, and the Supplier Register screen shows it as a preview
    /// before anybody presses the button that writes. Printing from inside the
    /// import would have meant the screen getting a second implementation, and
    /// the two would then disagree about what "updated" counts.
    /// </summary>
    public sealed record Outcome(
        int Read, int Skipped, int Created, int Updated, int Unchanged,
        int Carriers, int RepeatedInFile, int MatchedByTradingName,
        /// <summary>Register spelling to registered name, for the ones matched by prefix.</summary>
        IReadOnlyList<string> Matched,
        /// <summary>Probably the same company; imported separately rather than merged.</summary>
        IReadOnlyList<string> Unsure,
        /// <summary>Not matched because more than one company fitted.</summary>
        IReadOnlyList<string> Ambiguous,
        bool Applied);

    /// <summary>What one spreadsheet row says, after the placeholders are read as empty.</summary>
    public sealed record Row(
        string AbsNo, string ListType, string Name, string Address, string Contact,
        string Telephone, string Fax, string Email, string Website, string CreditTerm,
        string ServicesRequired, string MainSpType, string TypeOfService);

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var index = Array.IndexOf(args, "--import-asl");
        var path = index >= 0 && index + 1 < args.Length && !args[index + 1].StartsWith("--")
            ? args[index + 1]
            : null;

        if (path is null)
        {
            Console.WriteLine("Usage: --import-asl <path to ASL_BSL List.xlsx> [--apply]");
            return 1;
        }
        if (!File.Exists(path))
        {
            Console.WriteLine($"No such file: {path}");
            return 1;
        }

        var apply = args.Contains("--apply");

        await using var file = File.OpenRead(path);
        var rows = Read(file, out var skipped);

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ScmosDbContext>();
        await db.Database.MigrateAsync();

        var outcome = await ImportAsync(db, rows, skipped, apply, CancellationToken.None);
        Print(outcome, Path.GetFileName(path));
        return 0;
    }

    /// <summary>The outcome, for a terminal. The screen renders the same record itself.</summary>
    private static void Print(Outcome outcome, string fileName)
    {
        Console.WriteLine();
        Console.WriteLine($"{outcome.Read} usable row(s) read from {fileName}"
            + (outcome.Skipped > 0 ? $", {outcome.Skipped} skipped for having no company name" : ""));

        Console.WriteLine();
        Console.WriteLine($"  created    {outcome.Created,5}");
        Console.WriteLine($"  updated    {outcome.Updated,5}");
        Console.WriteLine($"  unchanged  {outcome.Unchanged,5}");
        Console.WriteLine($"  of which carriers: {outcome.Carriers} — the rest are agents, liners and brokers,");
        Console.WriteLine("                     recorded but kept out of the scorecard and the job pool.");
        if (outcome.RepeatedInFile > 0)
            Console.WriteLine($"  {outcome.RepeatedInFile} row(s) repeated a company already in the file; the later one won.");

        if (outcome.Matched.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"  {outcome.MatchedByTradingName} carrier(s) matched by trading name. Check these —");
            Console.WriteLine("  each is the only company in the list that begins with that spelling, which");
            Console.WriteLine("  is strong but not proof. The register keeps its own name; the legal name");
            Console.WriteLine("  is recorded beside it and the spelling added as an unconfirmed alias.");
            Console.WriteLine();
            foreach (var line in outcome.Matched) Console.WriteLine("    " + line);
        }

        if (outcome.Unsure.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Possibly the same company, imported separately rather than merged:");
            foreach (var line in outcome.Unsure) Console.WriteLine("    " + line);
        }

        if (outcome.Ambiguous.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("  Not matched, because more than one company fits:");
            foreach (var line in outcome.Ambiguous) Console.WriteLine("    " + line);
        }

        Console.WriteLine();
        Console.WriteLine(outcome.Applied ? "Written." : "Nothing was written. Add --apply to write it.");
    }

    /* ------------------------------------------------------------ reading */

    /// <summary>
    /// The sheet, by column heading rather than by position.
    ///
    /// The file is re-exported from ABS whenever procurement changes something,
    /// and a column that moves would silently load telephone numbers into the
    /// fax field if this counted from the left. A heading that is missing
    /// leaves its field empty rather than throwing: the list is worth importing
    /// without a website column, and an import that refuses the whole file over
    /// one absent heading is an import nobody can run.
    /// </summary>
    public static List<Row> Read(Stream file, out int skipped)
    {
        using var book = new XLWorkbook(file);
        var sheet = book.Worksheets.First();
        var used = sheet.RangeUsed() ?? throw new InvalidOperationException("The sheet is empty.");

        var first = used.FirstRow();
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var cell in first.Cells())
        {
            var name = Heading(cell.GetString());
            if (name.Length > 0 && !columns.ContainsKey(name)) columns[name] = cell.Address.ColumnNumber;
        }

        string At(IXLRangeRow row, string heading) =>
            columns.TryGetValue(heading, out var column)
                ? SupplierRegister.Tidy(row.Worksheet.Cell(row.RowNumber(), column).GetString())
                : "";

        var found = new List<Row>();
        skipped = 0;

        foreach (var row in used.Rows().Skip(1))
        {
            var name = At(row, "suppliername");
            if (!SupplierRegister.Usable(name))
            {
                // A blank line at the bottom of a sheet, not a supplier. Counted
                // rather than ignored, so a file that is mostly blank says so.
                if (row.Cells().Any(cell => cell.GetString().Trim().Length > 0)) skipped++;
                continue;
            }

            found.Add(new Row(
                AbsNo: columns.TryGetValue("absno", out var absColumn)
                    ? SupplierRegister.AbsNo(row.Worksheet.Cell(row.RowNumber(), absColumn).GetString())
                    : "",
                ListType: SupplierRegister.ListOf(At(row, "aslbsl")),
                Name: name,
                Address: At(row, "address"),
                Contact: At(row, "contactperson"),
                Telephone: At(row, "telephone"),
                Fax: At(row, "fax"),
                Email: At(row, "email"),
                Website: At(row, "website"),
                CreditTerm: At(row, "credittermdays"),
                ServicesRequired: At(row, "servicesrequired"),
                MainSpType: At(row, "mainsptype"),
                TypeOfService: At(row, "typeofservice")));
        }

        return found;
    }

    /// <summary>
    /// A heading reduced to letters and digits, lower-cased.
    ///
    /// "ABS_No", "ABS No" and "abs no" are the same column, and the file has
    /// used at least two of those spellings across exports.
    /// </summary>
    private static string Heading(string raw) =>
        new((raw ?? "").ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    /* ---------------------------------------------------------- importing */

    public static async Task<Outcome> ImportAsync(ScmosDbContext db, List<Row> rows,
        int skipped, bool apply, CancellationToken token)
    {
        var suppliers = await db.Suppliers.ToListAsync();
        var aliases = await db.SupplierAliases.AsNoTracking().ToListAsync();

        var byAbs = suppliers.Where(one => one.AbsNo.Length > 0)
            .GroupBy(one => one.AbsNo, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byKey = suppliers
            .GroupBy(one => SupplierRegister.Key(one.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var byAlias = aliases
            .GroupBy(one => SupplierRegister.Key(one.Alias), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SupplierId, StringComparer.Ordinal);
        var byId = suppliers.ToDictionary(one => one.Id);
        var usedCodes = suppliers.Select(one => one.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Which companies are actually carrying work. The register is evidence;
        // the spreadsheet's Type of Service is somebody's description, and a
        // company driving for us today is a carrier whichever way it is
        // described. The two are taken together — see LooksLikeCarrier.
        var carryingWork = (await db.OperationJobs.AsNoTracking()
                .Where(job => job.Trucker != "")
                .Select(job => job.Trucker)
                .Distinct()
                .ToListAsync())
            .Select(SupplierRegister.Key)
            .Where(key => key.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        /*
         * How a short trading name in the register reaches its long legal name
         * in the list.
         *
         * Built once, over every row in the file, because the test is not "does
         * this begin with that" but "does exactly one of the 642 begin with
         * that". A candidate counted one row at a time could never know it was
         * the only one, which is the only thing making the match safe.
         */
        var prefixHits = new Dictionary<int, List<(string Key, string Name)>>();
        foreach (var supplier in suppliers)
        {
            var hits = rows
                .Where(row => SupplierRegister.CouldBeTheSame(supplier.Name, row.Name))
                .Select(row => (Key: SupplierRegister.Key(row.Name), row.Name))
                .DistinctBy(hit => hit.Key, StringComparer.Ordinal)
                .ToList();
            if (hits.Count > 0) prefixHits[supplier.Id] = hits;
        }

        /*
         * Three tests, and all three have to agree.
         *
         * It begins the name; it is the only company in the list it begins; and
         * it stops where one of that name's words stops. The third was added
         * after the second let W.A.K through to Wako Logistics — an
         * initialism that happens to start somebody else's name, and exactly the
         * merge that must never be made quietly, because it would put one
         * company's work on another company's scorecard.
         */
        var byPrefix = new Dictionary<string, Supplier>(StringComparer.Ordinal);
        var ambiguous = new List<string>();
        var unsure = new List<string>();

        foreach (var (supplierId, hits) in prefixHits)
        {
            var supplier = byId[supplierId];
            if (hits.Count > 1)
            {
                ambiguous.Add($"    {supplier.Name} could be any of {hits.Count} companies — not matched");
                continue;
            }

            var only = hits[0];
            if (!SupplierRegister.EndsOnAWord(supplier.Name, only.Name))
            {
                // Very likely the same company; not certain enough to merge on
                // a spreadsheet's say-so. Imported as its own row and listed
                // here, where somebody can fold the two together if they agree.
                unsure.Add($"    {supplier.Name,-12} and  {only.Name}");
                continue;
            }

            // Two register names both reaching one company is equally a guess.
            if (byPrefix.TryGetValue(only.Key, out var already))
            {
                ambiguous.Add($"    {already.Name} and {supplier.Name} both reach {only.Name} — neither matched");
                byPrefix.Remove(only.Key);
                continue;
            }
            byPrefix[only.Key] = supplier;
        }

        var created = 0;
        var updated = 0;
        var unchanged = 0;
        var carriers = 0;
        var mergedInFile = 0;
        var byShortName = 0;
        var seenInFile = new Dictionary<string, string>(StringComparer.Ordinal);
        var samples = new List<string>();
        var matched = new List<string>();

        foreach (var row in rows)
        {
            var key = SupplierRegister.Key(row.Name);

            // The file itself repeats seven companies and ten ABS numbers. The
            // second appearance updates the first rather than making a twin.
            if (seenInFile.TryGetValue(key, out var firstName))
            {
                mergedInFile++;
                if (samples.Count < 8) samples.Add($"    repeated in the file: {firstName}  =  {row.Name}");
            }
            seenInFile[key] = row.Name;

            Supplier? supplier = null;
            if (row.AbsNo.Length > 0 && byAbs.TryGetValue(row.AbsNo, out var byNumber)) supplier = byNumber;
            supplier ??= byKey.TryGetValue(key, out var byName) ? byName : null;
            if (supplier is null && byAlias.TryGetValue(key, out var aliasOwner))
                supplier = byId.TryGetValue(aliasOwner, out var owner) ? owner : null;

            // Last, and only when it is the only candidate: the register's short
            // trading name against this company's registered one.
            if (supplier is null && byPrefix.TryGetValue(key, out var byShort))
            {
                supplier = byShort;
                byShortName++;
                matched.Add($"    {byShort.Code,-8} {byShort.Name,-12} \u2192  {row.Name}");
            }

            var describedAsCarrier = SupplierRegister.LooksLikeCarrier(row.TypeOfService);
            var isCarrier = describedAsCarrier || carryingWork.Contains(key)
                || (supplier is not null && carryingWork.Contains(SupplierRegister.Key(supplier.Name)));
            if (isCarrier) carriers++;

            if (supplier is null)
            {
                var fresh = new Supplier
                {
                    Code = Code(row.Name, usedCodes),
                    Name = row.Name,
                    // On procurement's list is not the same as approved by this
                    // department — that turns on insurance, licences and an
                    // audit this import knows nothing about.
                    Status = SupplierRegister.NewStatus,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    IsCarrier = isCarrier,
                };
                Describe(fresh, row);
                // A new supplier has no trading name of its own yet, so both
                // are what the list calls it.
                fresh.LegalName = row.Name;
                created++;

                if (apply)
                {
                    db.Suppliers.Add(fresh);
                    await db.SaveChangesAsync();
                    byKey[key] = fresh;
                    byId[fresh.Id] = fresh;
                    if (fresh.AbsNo.Length > 0) byAbs[fresh.AbsNo] = fresh;

                    db.SupplierAliases.Add(new SupplierAlias
                    {
                        SupplierId = fresh.Id,
                        Alias = row.Name.ToUpperInvariant(),
                        Source = SupplierRegister.AliasSource,
                        // The alias is the company's own name as procurement
                        // writes it. Nothing is being guessed.
                        Confirmed = true,
                    });
                    byAlias[key] = fresh.Id;
                }
                continue;
            }

            var before = Snapshot(supplier);
            Describe(supplier, row);
            // A supplier already carrying work stays a carrier whatever the
            // spreadsheet calls it. Only ever widened here, never narrowed:
            // demoting one on the strength of a description would stop jobs
            // being assignable to a company that is driving them today.
            supplier.IsCarrier = supplier.IsCarrier || isCarrier;

            if (Snapshot(supplier) == before) { unchanged++; continue; }

            supplier.UpdatedAt = DateTimeOffset.UtcNow;
            updated++;

            // The registered name becomes a spelling that means this supplier,
            // so the next import finds it outright rather than by prefix. Not
            // confirmed: a person should agree that 9ISARA is 9 Isara Transport
            // Co., Ltd. before the system treats it as settled.
            if (apply && !byAlias.ContainsKey(key))
            {
                db.SupplierAliases.Add(new SupplierAlias
                {
                    SupplierId = supplier.Id,
                    Alias = row.Name.ToUpperInvariant(),
                    Source = SupplierRegister.AliasSource,
                    Confirmed = false,
                });
                byAlias[key] = supplier.Id;
            }
        }

        if (apply) await db.SaveChangesAsync(token);

        return new Outcome(
            Read: rows.Count,
            Skipped: skipped,
            Created: created,
            Updated: updated,
            Unchanged: unchanged,
            Carriers: carriers,
            RepeatedInFile: mergedInFile,
            MatchedByTradingName: byShortName,
            Matched: matched,
            Unsure: unsure,
            Ambiguous: ambiguous,
            Applied: apply);
    }

    /// <summary>Copy the list's fields onto a supplier row.</summary>
    private static void Describe(Supplier supplier, Row row)
    {
        supplier.AbsNo = Keep(supplier.AbsNo, row.AbsNo);
        supplier.ListType = Keep(supplier.ListType, row.ListType);
        // Recorded beside the register's own spelling, never over it. See
        // Supplier.LegalName for why a rename here would be a rename of the
        // thing every job row and rate card points at.
        supplier.LegalName = Keep(supplier.LegalName, row.Name);
        supplier.ContactPerson = Keep(supplier.ContactPerson, row.Contact);
        supplier.Telephone = Keep(supplier.Telephone, row.Telephone);
        supplier.Fax = Keep(supplier.Fax, row.Fax);
        supplier.Email = Keep(supplier.Email, row.Email);
        supplier.Website = Keep(supplier.Website, row.Website);
        supplier.CreditTerm = Keep(supplier.CreditTerm, row.CreditTerm);
        supplier.ServicesRequired = Keep(supplier.ServicesRequired, row.ServicesRequired);
        supplier.MainSpType = Keep(supplier.MainSpType, row.MainSpType);
        supplier.TypeOfService = Keep(supplier.TypeOfService, row.TypeOfService);
        // The address is the one field the two registers both hold. The list is
        // procurement's own record and is the better copy, but an empty cell
        // must not wipe an address somebody typed into SCMOS by hand.
        supplier.Address = Keep(supplier.Address, row.Address);
    }

    /// <summary>
    /// The new value, unless there is not one.
    ///
    /// An empty cell in a re-export means "nobody filled this in", not "delete
    /// what you have". Six hundred of these rows have no website and three
    /// hundred no fax; letting those blanks through would make every re-import
    /// erase whatever had been added since the last one.
    /// </summary>
    private static string Keep(string held, string arriving) =>
        arriving.Length > 0 ? arriving : held;

    /// <summary>Everything this import may change, as one string, to tell a no-op from a write.</summary>
    private static string Snapshot(Supplier one) => string.Join('\u001f',
        one.AbsNo, one.ListType, one.LegalName, one.ContactPerson, one.Telephone, one.Fax, one.Email,
        one.Website, one.CreditTerm, one.ServicesRequired, one.MainSpType, one.TypeOfService,
        one.Address, one.IsCarrier);

    /// <summary>
    /// A short unique code, the same shape the seeder gives one.
    ///
    /// Six letters is not enough on its own — TATIYAPOL and TATIYAPON both
    /// start TATIYA — so a clash takes a numbered suffix.
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
}
