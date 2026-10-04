using System.Globalization;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Data;
using Scmos.Api.Rules;

namespace Scmos.Api.Services;

/// <param name="Existing">What the register already holds for this carrier and year, and what the import will do about it.</param>
/// <param name="Skip">Whether the import leaves this row out — a problem, no carrier, a duplicate or a record it will not replace.</param>
public record LegacyPreviewRow(int Row, string Name, string Number, int? SupplierId, string Supplier, string MatchedBy, decimal? FinalPercent,
    string Result, int Evaluators, IReadOnlyDictionary<string, bool?>? Held, IReadOnlyList<string> Problems, string Existing, bool Skip);
public record LegacyPreview(string Kind, int Year, string FileName, IReadOnlyList<LegacyPreviewRow> Rows, IReadOnlyList<DecisionOption> Certificates);
public record LegacyOutcome(bool Ok, string Message, int Status = StatusCodes.Status200OK, LegacyPreview? Preview = null);

public record HistoryEvaluation(string Period, string Source, decimal? FinalPercent, int? TotalScore, string Grade, string Result, string Note);
public record HistoryCertificate(long Id, string Type, string Label, int Year, bool Held, string State, string Verification, string Source,
    string Number, string IssuedOn, string ExpiresOn, string Note);
public record HistoryRow(int SupplierId, string Code, string Name, string Status, IReadOnlyList<HistoryEvaluation> Evaluations,
    IReadOnlyList<HistoryCertificate> Certificates);
public record CertificateInput(int SupplierId, string? Type, int Year, bool Held, string? Number, string? IssuedOn, string? ExpiresOn, bool Verified,
    string? Note);

/// <summary>
/// The evaluations before SCMOS and the carriers' certificates (2 Oct 2026, Annual Evaluation Phase 11). A workbook is read
/// twice — once to show what it says and which carrier each row is, once to import it, with whatever carriers somebody
/// chose by hand — so what is stored is the file's own figures, never a figure typed into the preview.
///
/// <para>
/// A year's result goes into the carrier's evaluation history as <c>legacy-import</c>: its final percentage and its result
/// as written, nothing reconstructed. It does not replace a result SCMOS calculated for the same year, and it does not move
/// the register's "latest score" — that stays the one SCMOS worked out. A certificate the table says the carrier holds is
/// recorded as declared, never as valid, and never over one somebody has verified.
/// </para>
/// </summary>
public class LegacyEvaluationService(ScmosDbContext db, AuditService audit, ILogger<LegacyEvaluationService>? log = null)
{
    private const int FirstYear = 2015;

    public async Task<LegacyOutcome> PreviewAsync(AppUser user, Stream file, string fileName, int year, IReadOnlyDictionary<int, int> chosen,
        CancellationToken token)
    {
        var (preview, refusal) = await PrepareAsync(user, file, fileName, year, chosen, token);
        return preview is null ? refusal! : new LegacyOutcome(true, "", Preview: preview);
    }

    public async Task<LegacyOutcome> ImportAsync(AppUser user, Stream file, string fileName, int year, IReadOnlyDictionary<int, int> chosen,
        CancellationToken token)
    {
        var (preview, refusal) = await PrepareAsync(user, file, fileName, year, chosen, token);
        if (preview is null) return refusal!;
        var rows = preview.Rows.Where(row => !row.Skip && row.SupplierId is not null).ToList();
        if (rows.Count == 0) return new LegacyOutcome(false, "ไม่มีแถวที่นำเข้าได้", StatusCodes.Status400BadRequest, preview);
        var now = DateTimeOffset.UtcNow;
        var period = year.ToString(CultureInfo.InvariantCulture);
        var ids = rows.Select(row => row.SupplierId!.Value).ToList();
        var written = 0;

        if (preview.Kind == LegacyEvaluationImport.Results)
        {
            var existing = await db.SupplierEvaluations.Where(row => ids.Contains(row.SupplierId) && row.Period == period).ToListAsync(token);
            foreach (var row in rows)
            {
                var record = existing.FirstOrDefault(one => one.SupplierId == row.SupplierId);
                if (record is null) db.SupplierEvaluations.Add(record = new SupplierEvaluation { SupplierId = row.SupplierId!.Value, Period = period, CreatedAt = now });
                record.Source = SupplierEvaluation.LegacyImport;
                record.FinalPercent = row.FinalPercent;
                record.Result = row.Result;
                record.TotalScore = null;
                record.Grade = "";
                record.Stage = "approved";
                record.Note = Cut($"นำเข้าจาก {fileName} แถว {row.Row} · ผู้ประเมิน {row.Evaluators} คน", 1000);
                record.ImportedAt = now;
                record.ImportedBy = Cut(user.Signature, 200);
                written++;
            }
        }
        else
        {
            var existing = await db.SupplierCertificates.Where(row => ids.Contains(row.SupplierId) && row.Year == year).ToListAsync(token);
            foreach (var row in rows)
                foreach (var (type, held) in row.Held!.Where(pair => pair.Value is not null))
                {
                    var record = existing.FirstOrDefault(one => one.SupplierId == row.SupplierId && one.Type == type);
                    if (record?.Verification == SupplierCertificates.Verified) continue;
                    if (record is null)
                        db.SupplierCertificates.Add(record = new SupplierCertificate { SupplierId = row.SupplierId!.Value, Type = type, Year = year });
                    record.Held = held!.Value;
                    record.Verification = SupplierCertificates.Declared;
                    record.Source = SupplierCertificates.LegacyImport;
                    record.Note = Cut($"นำเข้าจาก {fileName} แถว {row.Row}", 500);
                    record.RecordedBy = Cut(user.Signature, 200);
                    record.RecordedAt = now;
                    written++;
                }
        }
        await db.SaveChangesAsync(token);
        var label = preview.Kind == LegacyEvaluationImport.Results ? "ผลประเมิน" : "ใบรับรอง ISO / Q-Mark";
        await audit.RecordAsync(user, AuditActions.Register, "annual-evaluation", "history", $"{label} {year}", $"legacy-import:{preview.Kind}", "",
            Cut($"{rows.Count} ราย · {written} รายการ: " + string.Join(", ", rows.Select(row => row.Supplier)), 2000), fileName, token);
        return new LegacyOutcome(true, $"นำเข้า{label}ปี {year}: {rows.Count} ราย · ข้าม {preview.Rows.Count - rows.Count} แถว", Preview: preview);
    }

    public async Task<IReadOnlyList<HistoryRow>?> HistoryAsync(AppUser user, CancellationToken token)
    {
        if (!user.Can(Capability.ViewAnnualEvaluation)) return null;
        var evaluations = (await db.SupplierEvaluations.AsNoTracking().ToListAsync(token)).ToLookup(row => row.SupplierId);
        var certificates = (await db.SupplierCertificates.AsNoTracking().ToListAsync(token)).ToLookup(row => row.SupplierId);
        var ids = evaluations.Select(group => group.Key).Concat(certificates.Select(group => group.Key)).ToHashSet();
        var suppliers = await db.Suppliers.AsNoTracking().Where(row => ids.Contains(row.Id)).ToListAsync(token);
        var today = SupplierCompliance.Today();
        return suppliers.OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Select(supplier => new HistoryRow(supplier.Id, supplier.Code,
            supplier.LegalName.Length > 0 ? supplier.LegalName : supplier.Name, supplier.Status,
            evaluations[supplier.Id].OrderByDescending(row => row.Period, StringComparer.Ordinal).Select(row => new HistoryEvaluation(row.Period, row.Source,
                row.FinalPercent, row.TotalScore, row.Grade, row.Result, row.Note)).ToList(),
            certificates[supplier.Id].OrderBy(row => Position(row.Type)).ThenByDescending(row => row.Year).Select(row => Describe(row, today)).ToList())).ToList();
    }

    /// <summary>
    /// A certificate entered or corrected by hand — one a carrier, type and year. Marked verified only with its number and
    /// expiry, which is what "seen" means; otherwise it stays a declaration.
    /// </summary>
    public async Task<AnnualEvaluationResult> SaveCertificateAsync(AppUser user, long? id, CertificateInput input, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation)) return Refused("บัญชีนี้ไม่มีสิทธิ์แก้ใบรับรอง", StatusCodes.Status403Forbidden);
        var type = (input.Type ?? "").Trim();
        if (!SupplierCertificates.IsType(type)) return Refused("ประเภทใบรับรองไม่ถูกต้อง");
        if (input.Year < FirstYear || input.Year > DateTime.UtcNow.Year + 1) return Refused("ปีไม่ถูกต้อง");
        string issued = (input.IssuedOn ?? "").Trim(), expires = (input.ExpiresOn ?? "").Trim(), number = (input.Number ?? "").Trim();
        if (issued.Length > 0 && Formats.DateNumber(issued) == 0) return Refused("วันที่ออกต้องเป็น DD/MM/YYYY");
        if (expires.Length > 0 && Formats.DateNumber(expires) == 0) return Refused("วันหมดอายุต้องเป็น DD/MM/YYYY");
        if (input.Verified && (!input.Held || number.Length == 0 || expires.Length == 0))
            return Refused("ยืนยันใบรับรองได้เมื่อมีเลขที่และวันหมดอายุจากใบรับรองจริง");
        if (!await db.Suppliers.AnyAsync(row => row.Id == input.SupplierId, token)) return Refused("ไม่พบผู้ขนส่ง", StatusCodes.Status404NotFound);

        var record = id is { } known
            ? await db.SupplierCertificates.FirstOrDefaultAsync(row => row.Id == known, token)
            : await db.SupplierCertificates.FirstOrDefaultAsync(row => row.SupplierId == input.SupplierId && row.Type == type && row.Year == input.Year, token);
        if (id is not null && (record is null || record.SupplierId != input.SupplierId)) return Refused("ไม่พบใบรับรองนี้", StatusCodes.Status404NotFound);
        var now = DateTimeOffset.UtcNow;
        var before = record is null ? "" : $"{(record.Held ? "มี" : "ไม่มี")} · {record.Verification} · {record.Number} · {record.ExpiresOn}";
        if (record is null)
            db.SupplierCertificates.Add(record = new SupplierCertificate { SupplierId = input.SupplierId, RecordedBy = Cut(user.Signature, 200), RecordedAt = now });
        record.Type = type;
        record.Year = input.Year;
        record.Held = input.Held;
        record.Number = Cut(number, 80);
        record.IssuedOn = issued;
        record.ExpiresOn = expires;
        record.Verification = input.Verified ? SupplierCertificates.Verified : SupplierCertificates.Declared;
        if (record.Source.Length == 0) record.Source = SupplierCertificates.Manual;
        record.Note = Cut((input.Note ?? "").Trim(), 500);
        record.UpdatedBy = Cut(user.Signature, 200);
        record.UpdatedAt = now;
        try { await db.SaveChangesAsync(token); }
        catch (DbUpdateException) { return Refused("มีใบรับรองประเภทนี้ของปีนี้อยู่แล้ว", StatusCodes.Status409Conflict); }
        await audit.RecordAsync(user, AuditActions.Update, "supplier", input.SupplierId.ToString(CultureInfo.InvariantCulture), "",
            $"certificate:{type}:{input.Year}", before, $"{(record.Held ? "มี" : "ไม่มี")} · {record.Verification} · {record.Number} · {record.ExpiresOn}",
            record.Note, token);
        return new AnnualEvaluationResult(true, $"บันทึก {SupplierCertificates.LabelOf(type)} ปี {input.Year} แล้ว", Id: record.Id);
    }

    /* ------------------------------------------------------------------ reading the workbook */

    private async Task<(LegacyPreview? Preview, LegacyOutcome? Refusal)> PrepareAsync(AppUser user, Stream file, string fileName, int year,
        IReadOnlyDictionary<int, int> chosen, CancellationToken token)
    {
        if (!user.Can(Capability.ManageAnnualEvaluation))
            return (null, new LegacyOutcome(false, "บัญชีนี้ไม่มีสิทธิ์นำเข้าผลประเมิน", StatusCodes.Status403Forbidden));
        if (year < FirstYear || year > DateTime.UtcNow.Year) return (null, new LegacyOutcome(false, "ปีไม่ถูกต้อง", StatusCodes.Status400BadRequest));
        var (kind, rows) = Read(file);
        if (kind is null) log?.LogWarning("Annual evaluation history: {File} from {User} is neither the results workbook nor the ISO / Q-Mark table",
            fileName, user.Signature);
        if (kind is null) return (null, new LegacyOutcome(false,
            "อ่านไฟล์ไม่ออก — ต้องเป็นไฟล์ผลประเมิน (มีคอลัมน์ Percentage และ Result) หรือตาราง ISO / Q-Mark", StatusCodes.Status400BadRequest));

        var suppliers = await db.Suppliers.AsNoTracking().Select(row => new { row.Id, row.Code, row.Name, row.LegalName, row.AbsNo }).ToListAsync(token);
        var aliases = (await db.SupplierAliases.AsNoTracking().Select(row => new { row.SupplierId, row.Alias }).ToListAsync(token)).ToLookup(row => row.SupplierId);
        var candidates = suppliers.Select(one => new LegacyEvaluationImport.Candidate(one.Id, one.AbsNo,
            new[] { one.Name, one.LegalName }.Concat(aliases[one.Id].Select(alias => alias.Alias)).Select(SupplierRegister.Key)
                .Where(key => key.Length > 0).Distinct().ToList())).ToList();
        string NameOf(int id) => suppliers.Where(one => one.Id == id).Select(one => one.LegalName.Length > 0 ? one.LegalName : one.Name).FirstOrDefault() ?? "";

        // Each row's carrier: the one somebody chose for it, else the one the register matches; 0 chosen means leave it out.
        IEnumerable<(int Row, string Name, string Number, IReadOnlyList<string> Problems)> Rows() => kind == LegacyEvaluationImport.Results
            ? LegacyEvaluationImport.ReadResults(rows).Select(row => (row.Row, row.Name, row.Number, row.Problems))
            : LegacyEvaluationImport.ReadCertificates(rows).Select(row => (row.Row, row.Name, row.Number, row.Problems));
        var results = kind == LegacyEvaluationImport.Results ? LegacyEvaluationImport.ReadResults(rows).ToDictionary(row => row.Row) : [];
        var certificates = kind == LegacyEvaluationImport.Certificates ? LegacyEvaluationImport.ReadCertificates(rows).ToDictionary(row => row.Row) : [];
        var matched = Rows().Select(row =>
        {
            var choice = chosen.TryGetValue(row.Row, out var picked) ? picked : (int?)null;
            var match = choice is null ? LegacyEvaluationImport.MatchOf(row.Name, row.Number, candidates) : null;
            int? supplier = choice is > 0 && suppliers.Any(one => one.Id == choice) ? choice : choice is null ? match?.SupplierId : null;
            var how = choice is > 0 ? "chosen" : choice == 0 ? "skipped" : match?.How ?? "";
            return (row.Row, row.Name, row.Number, Supplier: supplier, How: how, row.Problems);
        }).ToList();

        var period = year.ToString(CultureInfo.InvariantCulture);
        var ids = matched.Where(row => row.Supplier is not null).Select(row => row.Supplier!.Value).Distinct().ToList();
        var evaluationsHeld = await db.SupplierEvaluations.AsNoTracking().Where(row => ids.Contains(row.SupplierId) && row.Period == period)
            .Select(row => new { row.SupplierId, row.Source }).ToListAsync(token);
        var certificatesHeld = await db.SupplierCertificates.AsNoTracking().Where(row => ids.Contains(row.SupplierId) && row.Year == year)
            .Select(row => new { row.SupplierId, row.Type, row.Verification }).ToListAsync(token);

        var preview = new List<LegacyPreviewRow>();
        foreach (var row in matched)
        {
            var problems = row.Problems.ToList();
            var existing = "";
            var skip = problems.Count > 0 || row.Supplier is null;
            if (row.Supplier is null && row.How != "skipped") problems.Add("ไม่พบผู้ขนส่งในทะเบียน — เลือกเอง หรือข้ามแถวนี้");
            var earlier = preview.FirstOrDefault(one => row.Supplier is not null && one.SupplierId == row.Supplier && !one.Skip);
            if (kind == LegacyEvaluationImport.Results)
            {
                var result = results[row.Row];
                if (earlier is not null) { problems.Add($"ผู้ขนส่งรายเดียวกับแถว {earlier.Row}"); skip = true; }
                var held = evaluationsHeld.FirstOrDefault(one => one.SupplierId == row.Supplier);
                if (held is not null && held.Source != SupplierEvaluation.LegacyImport) { existing = $"มีผลปี {year} ที่ SCMOS คำนวณแล้ว — ไม่ทับ"; skip = true; }
                else if (held is not null) existing = "นำเข้าไว้แล้ว — จะแทนด้วยค่าจากไฟล์นี้";
                preview.Add(new LegacyPreviewRow(row.Row, row.Name, row.Number, row.Supplier, row.Supplier is { } id ? NameOf(id) : "", row.How,
                    result.FinalPercent, result.Result, result.Evaluators, null, problems, existing, skip));
            }
            else
            {
                var certificate = certificates[row.Row];
                // The same carrier twice (two branches under one ABS number) is fine when both rows say the same.
                if (earlier is not null)
                {
                    if (earlier.Held!.All(pair => certificate.Held.GetValueOrDefault(pair.Key) == pair.Value)) existing = $"ซ้ำกับแถว {earlier.Row} (ค่าเหมือนกัน)";
                    else problems.Add($"ผู้ขนส่งรายเดียวกับแถว {earlier.Row} แต่ค่าไม่ตรงกัน");
                    skip = true;
                }
                var verified = certificatesHeld.Where(one => one.SupplierId == row.Supplier && one.Verification == SupplierCertificates.Verified)
                    .Select(one => SupplierCertificates.LabelOf(one.Type)).ToList();
                if (verified.Count > 0) existing = $"ยืนยันแล้ว ({string.Join(", ", verified)}) — ไม่ทับ";
                else if (certificatesHeld.Any(one => one.SupplierId == row.Supplier) && existing.Length == 0) existing = "มีบันทึกปีนี้แล้ว — จะแทนด้วยค่าจากไฟล์นี้";
                preview.Add(new LegacyPreviewRow(row.Row, row.Name, row.Number, row.Supplier, row.Supplier is { } id ? NameOf(id) : "", row.How,
                    null, "", 0, certificate.Held, problems, existing, skip));
            }
        }
        return (new LegacyPreview(kind, year, fileName, preview,
            SupplierCertificates.Types.Select(type => new DecisionOption(type.Code, type.Label)).ToList()), null);
    }

    /// <summary>The workbook's first sheet that is one of the two, from its heading row on, each cell as text.</summary>
    private static (string? Kind, IReadOnlyList<IReadOnlyList<string>> Rows) Read(Stream file)
    {
        XLWorkbook book;
        try { book = new XLWorkbook(file); }
        catch (Exception) { return (null, []); }
        using (book)
            foreach (var sheet in book.Worksheets)
            {
                var used = sheet.RangeUsed();
                if (used is null) continue;
                var last = used.LastColumn().ColumnNumber();
                var rows = used.Rows().Select(row => (IReadOnlyList<string>)Enumerable.Range(1, last)
                    .Select(column => Text(sheet.Cell(row.RowNumber(), column))).ToList()).ToList();
                for (var start = 0; start < Math.Min(10, rows.Count); start++)
                    if (LegacyEvaluationImport.KindOf(rows[start]) is { } kind) return (kind, rows.Skip(start).ToList());
            }
        return (null, []);
    }

    private static string Text(IXLCell cell)
    {
        var value = cell.Value;
        if (value.IsBlank) return "";
        if (value.IsNumber) return value.GetNumber().ToString("R", CultureInfo.InvariantCulture);
        if (value.IsText) return value.GetText().Trim();
        return cell.GetFormattedString().Trim();
    }

    private static int Position(string type) =>
        SupplierCertificates.Types.Select((one, at) => (one.Code, at)).Where(one => one.Code == type).Select(one => one.at).DefaultIfEmpty(99).First();

    internal static HistoryCertificate Describe(SupplierCertificate row, int today) => new(row.Id, row.Type, SupplierCertificates.LabelOf(row.Type), row.Year,
        row.Held, SupplierCertificates.StateOf(row.Held, row.Verification, row.ExpiresOn, today), row.Verification, row.Source, row.Number, row.IssuedOn,
        row.ExpiresOn, row.Note);

    private static string Cut(string text, int length) => text.Length > length ? text[..length] : text;

    private static AnnualEvaluationResult Refused(string message, int status = StatusCodes.Status400BadRequest) => new(false, message, status);
}
