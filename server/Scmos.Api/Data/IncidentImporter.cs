using System.Data;
using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Scmos.Api.Auth;
using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>Historical CAR/PAR log import. Preview first; no job-number guessing or overwrites.</summary>
public static class IncidentImporter
{
    public sealed record Outcome(int Read, int Skipped, int Created,
        IReadOnlyList<string> Matched, IReadOnlyList<string> Missing,
        IReadOnlyList<string> Sample, bool Applied,
        IReadOnlyList<string> Duplicates, IReadOnlyDictionary<string, int> Statuses);

    public sealed record Row(string Reference, string Title, string Kind, string Category,
        string Status, string Stage, IReadOnlyDictionary<string, string> Source)
    {
        public string At(string header) => Source.FirstOrDefault(pair =>
            IncidentImport.Heading(pair.Key) == IncidentImport.Heading(header)).Value ?? "";
    }

    public static List<Row> Read(Stream file, out int skipped,
        out List<string> matched, out List<string> missing)
    {
        // XLSX is compressed XML. Bound expansion before ClosedXML allocates it.
        using (var archive = new System.IO.Compression.ZipArchive(file,
            System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true))
        {
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 64L * 1024 * 1024)
                throw new InvalidOperationException("ข้อมูลภายใน Excel ใหญ่เกินขีดจำกัด 64 MB");
        }
        file.Position = 0;
        using var book = new XLWorkbook(file);
        var sheet = book.Worksheets.FirstOrDefault(s => s.Name.Equals("CAR_PAR", StringComparison.OrdinalIgnoreCase))
            ?? book.Worksheets.First();
        var used = sheet.RangeUsed() ?? throw new InvalidOperationException("แผ่นงานว่าง");
        if (used.LastRow().RowNumber() > 5001 || used.LastColumn().ColumnNumber() > 100)
            throw new InvalidOperationException("รองรับไม่เกิน 5,000 รายการและ 100 คอลัมน์ต่อไฟล์");

        var headingRow = 0;
        var headings = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in used.Rows().Take(10))
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in row.Cells())
            {
                var name = IncidentImport.Heading(cell.GetString());
                if (name.Length > 0 && !found.TryAdd(name, cell.Address.ColumnNumber))
                    throw new InvalidOperationException("หัวคอลัมน์ซ้ำ กรุณาตรวจไฟล์");
            }
            if (IncidentImport.ColumnFor("title", found) is not null
                && IncidentImport.ColumnFor("reference", found) is not null)
            { headingRow = row.RowNumber(); headings = found; break; }
        }

        matched = [];
        missing = [];
        var columns = new Dictionary<string, int>();
        foreach (var field in IncidentImport.Fields.Keys)
        {
            var column = IncidentImport.ColumnFor(field, headings);
            if (column is null) { missing.Add(field); continue; }
            columns[field] = column.Value;
            matched.Add($"{field}: {sheet.Cell(headingRow, column.Value).GetString().Trim()}");
        }
        foreach (var required in new[] { "title", "reference", "kind", "status" })
            if (!columns.ContainsKey(required))
                throw new InvalidOperationException($"ไม่พบคอลัมน์ที่จำเป็น: {required}");

        static string CellText(IXLCell cell) => cell.DataType == XLDataType.DateTime
            ? cell.GetDateTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : cell.GetString().Trim();
        string At(int row, string field) => columns.TryGetValue(field, out var column)
            ? CellText(sheet.Cell(row, column)) : "";

        var result = new List<Row>();
        skipped = 0;
        foreach (var row in used.Rows().Where(r => r.RowNumber() > headingRow))
        {
            if (!row.Cells().Any(c => CellText(c).Length > 0)) continue;
            var number = row.RowNumber();
            var title = At(number, "title");
            if (!IncidentImport.Usable(title))
                throw new InvalidOperationException($"แถว {number}: ไม่มีรายละเอียด หยุดนำเข้าเพื่อไม่ให้ข้อมูลตกหล่น");
            var kind = At(number, "kind").ToUpperInvariant();
            if (kind is not ("CAR" or "PAR"))
                throw new InvalidOperationException($"แถว {number}: CAR/PAR ไม่ถูกต้อง");
            var rawReference = At(number, "reference");
            if (rawReference.Length == 0)
                throw new InvalidOperationException($"แถว {number}: ไม่มีเลขที่ CAR/PAR");
            var reference = rawReference.StartsWith(kind, StringComparison.OrdinalIgnoreCase)
                ? rawReference : $"{kind}-{rawReference}";
            if (reference.Length > 40)
                throw new InvalidOperationException($"แถว {number}: เลขที่เคสยาวเกิน 40 ตัวอักษร");
            var status = At(number, "status");
            var source = new Dictionary<string, string>();
            foreach (var column in headings.Values)
                source[sheet.Cell(headingRow, column).GetString().Trim()] = CellText(sheet.Cell(number, column));
            result.Add(new Row(reference, IncidentImport.Title(title), kind,
                IncidentImport.CategoryOf(At(number, "category")), status, IncidentImport.StageOf(status), source));
        }
        return result;
    }

    // Full source cells are retained, including multiline Detail and original dates.
    // Display fields are bounded to the existing schema; no fabricated approval signature.
    public static IncidentCase ToCase(Row row, string importer, DateTimeOffset now)
    {
        static string Limit(string value, int length) => value.Length <= length ? value : value[..length];
        return new IncidentCase
        {
            Reference = row.Reference, Title = row.Title, Kind = row.Kind,
            Category = row.Category, Stage = row.Stage, JobKey = "",
            What = Limit(row.At("Detail") is { Length: > 0 } detail ? detail : row.Title, 1000),
            Company = Limit(row.At("Company"), 20),
            Source = Limit(row.At("Source"), 60),
            ResponsiblePerson = Limit(row.At("Responsible By"), 120),
            RequestedBy = Limit(row.At("Issued By"), 120),
            RequestedOn = Limit(row.At("Issued Date"), 20),
            DueDate = Limit(row.At("Due to Response"), 20),
            NcClause = Limit(row.At("Requirement QMS"), 80),
            TeamNote = "SCMOS Excel import\nExcel status: " + row.Status
                + "\nOriginal columns (unlinked source job number):\n"
                + JsonSerializer.Serialize(row.Source, new JsonSerializerOptions { WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            RaisedBy = importer, RaisedAt = now, UpdatedAt = now
        };
    }

    public static async Task<Outcome> ImportAsync(ScmosDbContext db, AuditService audit, List<Row> rows,
        int skipped, IReadOnlyList<string> matched, IReadOnlyList<string> missing,
        AppUser user, bool apply, CancellationToken token)
    {
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // A failed transient attempt must not leave added entities for the retry.
            db.ChangeTracker.Clear();
            await using var transaction = apply
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token) : null;
            var existing = await db.IncidentCases.AsNoTracking().Select(c => c.Reference).ToListAsync(token);
            var seen = existing.Select(IncidentImport.ReferenceKey).ToHashSet(StringComparer.Ordinal);
            var accepted = new List<Row>();
            var duplicates = new List<string>();
            foreach (var row in rows)
            {
                if (!seen.Add(IncidentImport.ReferenceKey(row.Reference))) duplicates.Add(row.Reference);
                else accepted.Add(row);
            }
            if (apply)
            {
                foreach (var row in accepted)
                {
                    db.IncidentCases.Add(ToCase(row, user.Signature, DateTimeOffset.UtcNow));
                    audit.Stage(user, "import", "incident", row.Reference, row.Title,
                        "source status", "", row.Status, "Excel historical import; original status preserved; no job auto-link");
                }
                await db.SaveChangesAsync(token);
                await transaction!.CommitAsync(token);
            }
            return new Outcome(rows.Count, skipped, accepted.Count, matched, missing,
                rows.Take(5).Select(r => $"{r.Reference} · {r.Status} · {r.Title}").ToList(), apply,
                duplicates, rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()));
        });
    }
}
