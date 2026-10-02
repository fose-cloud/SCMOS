using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;
using Scmos.Api.Services;

/// <summary>
/// Annual Carrier Evaluation, Phase 11 (2 Oct 2026): the department's 2025 results and its ISO / Q-Mark table, read as
/// they were written — the workbooks' own layout — matched to the register by ABS number, imported without replacing
/// what SCMOS calculated, and certificates kept as declarations until somebody has seen one.
/// </summary>
static class LegacyEvaluationChecks
{
    public static async Task RunAsync(Action<bool, string> check, bool sql)
    {
        check(LegacyEvaluationImport.Split("A C N TRANSPORT, CHONBURI (#188052)") == ("A C N TRANSPORT, CHONBURI", "188052")
            && LegacyEvaluationImport.Split("JTC LOGISTICS,CHONBURI ( # 120292 )") == ("JTC LOGISTICS,CHONBURI", "120292")
            && LegacyEvaluationImport.Split("Plain Name") == ("Plain Name", ""),
            "legacy import: a row names its carrier as ABS writes it — the number in brackets is the ABS number");

        List<string> resultsHeader = ["ID", "ชื่อบริษัท (Company)", "Pricing", "On - Time \nDelivery", "SUM", "FULL", "%", "Percentage", "Result"];
        List<string> certificatesHeader = ["Name", "Q-Mark", "ISO 9001", "ISO 14000", "ISO 39001"];
        check(LegacyEvaluationImport.KindOf(resultsHeader) == LegacyEvaluationImport.Results
            && LegacyEvaluationImport.KindOf(certificatesHeader) == LegacyEvaluationImport.Certificates
            && LegacyEvaluationImport.KindOf(["Job", "Date"]) is null
            && SupplierCertificates.TypeOfHeading("ISO 14000") == "iso-14001" && SupplierCertificates.TypeOfHeading("Q-Mark") == "q-mark"
            && SupplierCertificates.TypeOfHeading("ISO 39001") == "iso-39001" && SupplierCertificates.TypeOfHeading("Pricing") is null,
            "legacy import: the workbook says which it is by its headings; the department's ISO 14000 is ISO 14001");

        IReadOnlyList<string> Row(params string[] cells) => cells;
        var results = LegacyEvaluationImport.ReadResults([
            resultsHeader,
            Row("1", "A C N TRANSPORT, CHONBURI (#188052)", "100", "75", "575", "700", "82.14", "86.80952380952381", "PASS"),
            Row("2", "A C N TRANSPORT, CHONBURI (#188052)", "100", "75", "725", "750", "96.66", "", ""),
            Row("3", "A C N TRANSPORT, CHONBURI (#188052)", "50", "75", "625", "700", "89.28", "", ""),
            Row("2", "BSK Logistics, Samutprakarn (#260165)", "50", "75", "575", "750", "76.66", "101", "PASS"),
            Row("3", "CHOKEWANNEE, CHONBURI (#230489)", "50", "75", "575", "750", "76.66", "82.5", ""),
            Row("4", "DGT CROSS, BANGKOK (#264405)", "50", "75", "675", "750", "90", "90.8", "PASS"),
            Row("5", "DGT CROSS, BANGKOK (#264405)", "50", "75", "675", "750", "90", "91", "PASS"),
            Row("", "", "", "", "", "", "", "", "")]);
        check(results.Count == 5 && results[0].Name == "A C N TRANSPORT, CHONBURI" && results[0].Number == "188052" && results[0].Evaluators == 3
            && results[0].FinalPercent == 86.8095m && results[0].Result == "PASS" && results[0].Problems.Count == 0 && results[0].Row == 2
            && results[1].Problems.Any(problem => problem.Contains("0–100")) && results[2].Problems.Any(problem => problem.Contains("ไม่มีผล"))
            && results[3].Problems.Any(problem => problem.Contains("มากกว่าหนึ่งแถว")),
            "legacy import: a carrier's block is its result row and the evaluators under it; the percentage kept to four places, PASS as written; nothing out of range or missing goes in");

        var certificates = LegacyEvaluationImport.ReadCertificates([
            certificatesHeader,
            Row("DGT CROSS, BANGKOK (#264405)", "มี", "มี", "ไม่มี", "มี"),
            Row("SMART WAY, CHONBURI (#138080)", "ไม่มี", "ไม่มี", "ไม่มี", "ไม่มี"),
            Row("ODD ONE (#1)", "maybe", "ไม่มี", "ไม่มี", "ไม่มี")]);
        check(certificates.Count == 3 && certificates[0].Held["q-mark"] == true && certificates[0].Held["iso-14001"] == false
            && certificates[0].Held["iso-39001"] == true && !certificates[0].Held.ContainsKey("iso-45001") && certificates[1].Held.Values.All(held => held == false)
            && certificates[2].Problems.Single().Contains("maybe") && certificates[2].Held["q-mark"] is null,
            "legacy import: มี / ไม่มี per certificate; a cell that says neither is a problem, never a guess");

        List<LegacyEvaluationImport.Candidate> register =
        [
            new(1, "264405", [SupplierRegister.Key("DGT"), SupplierRegister.Key("DGT Cross Haul Co., Ltd.")]),
            new(2, "", [SupplierRegister.Key("JTC LOGISTICS")]),
            new(3, "132143", [SupplierRegister.Key("T.O. LOGISTICS")]),
            new(4, "", [SupplierRegister.Key("SANGJA TRANSPORT")]), new(5, "", [SupplierRegister.Key("SANGJA TRANSPORT")]),
        ];
        check(LegacyEvaluationImport.MatchOf("DGT CROSS, BANGKOK", "264405", register) == new LegacyEvaluationImport.Match(1, "abs")
            && LegacyEvaluationImport.MatchOf("JTC LOGISTICS,CHONBURI", "120292", register) == new LegacyEvaluationImport.Match(2, "name")
            && LegacyEvaluationImport.MatchOf("T.O. LOGISTICS, CHONBURI", "202607", register) is null
            && LegacyEvaluationImport.MatchOf("T.O. LOGISTICS, RAYONG", "132143", register) == new LegacyEvaluationImport.Match(3, "abs")
            && LegacyEvaluationImport.MatchOf("SANGJA TRANSPORT, CHONBURI", "171041", register) is null
            && LegacyEvaluationImport.MatchOf("XY, BANGKOK", "", [new(9, "", [SupplierRegister.Key("XY")])]) is null,
            "legacy import: by ABS number first; by name only where the register row has no other number — another branch, two candidates or a two-letter name match nothing");

        var today = 20261002;
        check(SupplierCertificates.StateOf(false, SupplierCertificates.Declared, "", today) == SupplierCertificates.NotHeld
            && SupplierCertificates.StateOf(true, SupplierCertificates.Declared, "", today) == SupplierCertificates.Declared
            && SupplierCertificates.StateOf(true, SupplierCertificates.Verified, "", today) == SupplierCompliance.State.NoExpiry
            && SupplierCertificates.StateOf(true, SupplierCertificates.Verified, "01/10/2026", today) == SupplierCompliance.State.Expired
            && SupplierCertificates.StateOf(true, SupplierCertificates.Verified, "15/11/2026", today) == SupplierCompliance.State.Expiring
            && SupplierCertificates.StateOf(true, SupplierCertificates.Verified, "15/11/2027", today) == SupplierCompliance.State.Valid
            && SupplierCertificates.Holds(SupplierCertificates.Declared) && !SupplierCertificates.Holds(SupplierCompliance.State.Expired)
            && !SupplierCertificates.Holds(SupplierCertificates.NotHeld)
            && SupplierCertificates.HeldOf(" มี ") == true && SupplierCertificates.HeldOf("ไม่มี") == false && SupplierCertificates.HeldOf("?") is null,
            "certificates: declared is not valid; a certificate's date decides valid, expiring and expired by the register's own rule");

        if (sql) await SqlAsync(check);
    }

    /// <summary>A workbook laid out as the department's — a title row above the headings, as some of its files have.</summary>
    private static MemoryStream Workbook(IReadOnlyList<object?[]> rows, bool title = false)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("EVALUATION");
        var at = 1;
        if (title) sheet.Cell(at++, 1).Value = "Subcontractor Evaluation";
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
                if (row[column] is { } value)
                    sheet.Cell(at, column + 1).Value = value switch { double number => number, int whole => whole, _ => XLCellValue.FromObject(value) };
            at++;
        }
        var stream = new MemoryStream();
        book.SaveAs(stream);
        stream.Position = 0;
        return stream;
    }

    private static async Task SqlAsync(Action<bool, string> check)
    {
        var database = "SCMOS_LEGACY_EVALUATION_TEST_" + Guid.NewGuid().ToString("N");
        var connection = $"Server=(localdb)\\MSSQLLocalDB;Database={database};Integrated Security=true;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<ScmosDbContext>().UseSqlServer(connection,
            s => { s.UseCompatibilityLevel(150); s.EnableRetryOnFailure(3); }).Options;
        await using var db = new ScmosDbContext(options);
        await db.Database.EnsureCreatedAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            Supplier Carrier(string name, string code, string absNo = "", string legal = "") =>
                new() { Name = name, Code = code, AbsNo = absNo, LegalName = legal, Status = "approved", IsCarrier = true, CreatedAt = now, UpdatedAt = now };
            var dgt = Carrier("DGT", "DGT", "264405", "DGT Cross Haul Co., Ltd.");
            var jtc = Carrier("JTC LOGISTICS", "JTC");
            var acn = Carrier("ACN", "ACN");
            var shore = Carrier("SHORE TRANS", "SHO", "296147");
            db.Suppliers.AddRange(dgt, jtc, acn, shore);
            await db.SaveChangesAsync();
            // SHORE already has a 2025 result SCMOS worked out; the import must leave it alone.
            db.SupplierEvaluations.Add(new SupplierEvaluation { SupplierId = shore.Id, Period = "2025", TotalScore = 77, Grade = "B", Stage = "submitted", CreatedAt = now });
            dgt.LastScore = 64;
            dgt.LastEvaluatedPeriod = "AE-2024";
            await db.SaveChangesAsync();

            AppUser User(string id, string role) => new(id.ToLowerInvariant(), id.ToLowerInvariant() + "@test.invalid", id, role, id, "test", true);
            var operation = User("OP-1", Roles.Operation);
            var supervisor = User("SV-1", Roles.Supervisor);
            var auditing = new AuditService(db, new HttpContextAccessor(), NullLogger<AuditService>.Instance);
            var legacy = new LegacyEvaluationService(db, auditing);

            object?[] header = ["ID", "ชื่อบริษัท (Company)", "Pricing", "On - Time \nDelivery", "%", "Percentage", "Result"];
            MemoryStream Results() => Workbook([
                header,
                [1, "A C N TRANSPORT, CHONBURI (#188052)", 100, 75, 82.14, 86.80952380952381, "PASS"],
                [2, "A C N TRANSPORT, CHONBURI (#188052)", 100, 75, 96.66, null, null],
                [3, "DGT CROSS, BANGKOK (#264405)", 50, 75, 90.0, 90.80952380952381, "PASS"],
                [4, "JTC LOGISTICS, CHONBURI (#120292)", 100, 100, 100.0, 93.33333333333334, "PASS"],
                [5, "SHORE TRANS, BANGKOK (#296147)", 100, 100, 90.0, 90.91269841269842, "PASS"],
                [6, "NOBODY KNOWN, RAYONG (#999999)", 100, 100, 90.0, 88.0, "PASS"],
            ], title: true);

            var refused = await legacy.PreviewAsync(operation, Results(), "Subcontractor Evaluation.xlsx", 2025, new Dictionary<int, int>(), default);
            var notWorkbook = await legacy.PreviewAsync(supervisor, new MemoryStream([1, 2, 3]), "x.xlsx", 2025, new Dictionary<int, int>(), default);
            var preview = (await legacy.PreviewAsync(supervisor, Results(), "Subcontractor Evaluation.xlsx", 2025, new Dictionary<int, int>(), default)).Preview!;
            LegacyPreviewRow At(LegacyPreview from, string name) => from.Rows.Single(row => row.Name.StartsWith(name));
            check(refused.Status == StatusCodes.Status403Forbidden && notWorkbook.Status == StatusCodes.Status400BadRequest
                && preview.Kind == LegacyEvaluationImport.Results && preview.Rows.Count == 5
                && At(preview, "DGT").SupplierId == dgt.Id && At(preview, "DGT").MatchedBy == "abs" && !At(preview, "DGT").Skip
                && At(preview, "JTC").SupplierId == jtc.Id && At(preview, "JTC").MatchedBy == "name"
                && At(preview, "A C N").SupplierId is null && At(preview, "A C N").Evaluators == 2 && At(preview, "A C N").Skip
                && At(preview, "SHORE").Skip && At(preview, "SHORE").Existing.Contains("ไม่ทับ")
                && At(preview, "NOBODY").Skip,
                "legacy import: the preview reads the department's own layout under a title row — matched by ABS number or name; unknown carriers wait for a choice; SCMOS's own result for the year is not replaced");

            var chosen = new Dictionary<int, int> { [At(preview, "A C N").Row] = acn.Id, [At(preview, "NOBODY").Row] = 0 };
            var imported = await legacy.ImportAsync(supervisor, Results(), "Subcontractor Evaluation.xlsx", 2025, chosen, default);
            var again = await legacy.ImportAsync(supervisor, Results(), "Subcontractor Evaluation.xlsx", 2025, chosen, default);
            db.ChangeTracker.Clear();
            var rows = await db.SupplierEvaluations.AsNoTracking().Where(row => row.Period == "2025").ToListAsync();
            var dgtRow = rows.Single(row => row.SupplierId == dgt.Id);
            var shoreRow = rows.Single(row => row.SupplierId == shore.Id);
            check(imported.Ok && again.Ok && rows.Count == 4
                && rows.Where(row => row.SupplierId != shore.Id).All(row => row.Source == SupplierEvaluation.LegacyImport && row.Result == "PASS" && row.TotalScore is null)
                && dgtRow.FinalPercent == 90.8095m && dgtRow.ImportedBy.Length > 0 && dgtRow.Note.Contains("ผู้ประเมิน 1 คน")
                && rows.Single(row => row.SupplierId == acn.Id).FinalPercent == 86.8095m
                && shoreRow.Source == SupplierEvaluation.ScmosSource && shoreRow.TotalScore == 77
                && (await db.Suppliers.AsNoTracking().SingleAsync(row => row.Id == dgt.Id)).LastScore == 64,
                "legacy import: the file's percentage and result go into the history as legacy-import — once, however often it is imported; the register's latest score is SCMOS's own");

            MemoryStream Certificates() => Workbook([
                ["Name", "Q-Mark", "ISO 9001", "ISO 14000", "ISO 39001"],
                ["DGT CROSS, BANGKOK (#264405)", "มี", "มี", "ไม่มี", "มี"],
                ["JTC LOGISTICS,CHONBURI (#120292)", "มี", "มี", "ไม่มี", "ไม่มี"],
                ["SHORE TRANS, BANGKOK (#296147)", "ไม่มี", "ไม่มี", "ไม่มี", "ไม่มี"],
                ["SHORE TRANS, LADKRABANG (#296147)", "ไม่มี", "ไม่มี", "ไม่มี", "ไม่มี"],
            ]);
            // JTC's ISO 9001 has been seen and verified already; the import must not turn it back into a declaration.
            var verified = await legacy.SaveCertificateAsync(supervisor, null,
                new CertificateInput(jtc.Id, "iso-9001", 2025, true, "TH-9001-55", "01/02/2025", "31/01/2028", true, "seen at audit"), default);
            var unverifiable = await legacy.SaveCertificateAsync(supervisor, null,
                new CertificateInput(jtc.Id, "q-mark", 2025, true, "", "", "", true, ""), default);
            var certificatePreview = (await legacy.PreviewAsync(supervisor, Certificates(), "ISO & Q-Mark 2025.xlsx", 2025, new Dictionary<int, int>(), default)).Preview!;
            var certificateImport = await legacy.ImportAsync(supervisor, Certificates(), "ISO & Q-Mark 2025.xlsx", 2025, new Dictionary<int, int>(), default);
            db.ChangeTracker.Clear();
            var held = await db.SupplierCertificates.AsNoTracking().ToListAsync();
            check(verified.Ok && !unverifiable.Ok && certificatePreview.Kind == LegacyEvaluationImport.Certificates
                && certificatePreview.Rows.Single(row => row.Name.Contains("LADKRABANG")).Skip
                && certificatePreview.Rows.Single(row => row.Name.Contains("LADKRABANG")).Existing.Contains("ค่าเหมือนกัน")
                && certificateImport.Ok && held.Count(row => row.SupplierId == dgt.Id) == 4 && held.Count(row => row.SupplierId == shore.Id) == 4
                && held.Where(row => row.Source == SupplierCertificates.LegacyImport).All(row => row.Verification == SupplierCertificates.Declared && row.Year == 2025)
                && held.Single(row => row.SupplierId == dgt.Id && row.Type == "iso-39001").Held
                && held.Single(row => row.SupplierId == jtc.Id && row.Type == "iso-9001") is { Verification: SupplierCertificates.Verified, Number: "TH-9001-55" }
                && held.Single(row => row.SupplierId == jtc.Id && row.Type == "q-mark").Verification == SupplierCertificates.Declared,
                "certificates: the table's มี is recorded as declared, ไม่มี as not held; two branches under one ABS number agreeing are one carrier; a verified certificate is never overwritten by a declaration");

            var history = (await legacy.HistoryAsync(operation, default))!;
            var dgtHistory = history.Single(row => row.SupplierId == dgt.Id);
            check(dgtHistory.Evaluations.Single().Source == SupplierEvaluation.LegacyImport && dgtHistory.Certificates.Count == 4
                && dgtHistory.Certificates.First().Type == "q-mark" && dgtHistory.Certificates.First().State == SupplierCertificates.Declared
                && dgtHistory.Certificates.Single(row => row.Type == "iso-14001").State == SupplierCertificates.NotHeld
                && history.Single(row => row.SupplierId == jtc.Id).Certificates.Single(row => row.Type == "iso-9001").State == SupplierCompliance.State.Valid
                && history.Single(row => row.SupplierId == shore.Id).Evaluations.Single().Source == SupplierEvaluation.ScmosSource,
                "history: each carrier's results by year and source, and its certificates in the department's order with their state");

            // The evaluation's snapshot now reads the certificates instead of saying the register has none.
            var directory = new CarrierDirectory(db, new MemoryCache(new MemoryCacheOptions()));
            var campaigns = new AnnualEvaluationService(db, auditing, directory);
            var snapshots = new EvaluationSnapshotService(db, auditing,
                new JobRegisterCache(db, new MemoryCache(new MemoryCacheOptions()), NullLogger<JobRegisterCache>.Instance), directory);
            var id = (int)(await campaigns.CreateAsync(supervisor, new CreateCampaignInput(2026, null, null), default)).Id!.Value;
            await campaigns.ChangeCarriersAsync(supervisor, id, new CarrierChangeInput("add-eligible", null, null), default);
            await snapshots.GenerateAsync(supervisor, id, null, null, default);
            var rowOf = async (int supplier) => (await db.EvaluationCarriers.AsNoTracking().SingleAsync(one => one.CampaignId == id && one.SupplierId == supplier)).Id;
            var dgtEvidence = (await snapshots.ReadAsync(operation, id, await rowOf(dgt.Id), null, default))!;
            var acnEvidence = (await snapshots.ReadAsync(operation, id, await rowOf(acn.Id), null, default))!;
            var iso = dgtEvidence.Metrics.Single(metric => metric.Code == "iso-certificates");
            check(iso.Status == AnnualEvaluationRules.Available && iso.Value == 3 && iso.Denominator == 4 && iso.Note.Contains("แจ้งว่ามี (ปี 2025")
                && iso.Note.Contains("ISO 14001: ไม่มี") && acnEvidence.Metrics.Single(metric => metric.Code == "iso-certificates").Status == AnnualEvaluationRules.NotAvailable,
                "certificates: the evaluation's evidence lists them — three of four held, as declared in 2025 — and says so when nothing is recorded");
            check(await db.AuditEvents.CountAsync(row => row.Field.StartsWith("legacy-import:")) == 3
                && await db.AuditEvents.AnyAsync(row => row.Entity == "supplier" && row.Field == "certificate:iso-9001:2025"),
                "legacy import: every import and every certificate change is in the audit trail");
        }
        finally
        {
            await db.Database.EnsureDeletedAsync();
        }
    }
}
