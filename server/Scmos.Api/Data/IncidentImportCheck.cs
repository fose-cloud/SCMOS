using Scmos.Api.Rules;
using Scmos.Api.Services;

namespace Scmos.Api.Data;

/// <summary>
/// Reading a sheet of CAR/PAR cases, with <c>--check-incident-import</c>.
///
/// <para>
/// This importer was written without the file it will be given, so everything
/// it decides is a guess about how somebody else writes a spreadsheet. That
/// makes the guesses worth stating: which headings it will recognise, what it
/// does with a value it cannot read, and what it refuses.
/// </para>
/// </summary>
public static class IncidentImportCheck
{
    public static int? Run(string[] args)
    {
        if (!args.Contains("--check-incident-import")) return null;

        var failed = 0;

        void Check(bool ok, string why)
        {
            if (!ok) failed++;
            Console.WriteLine($"  {(ok ? "ok  " : "FAIL")}  {why}");
        }

        /// A sheet's heading row, as the reader builds one.
        static Dictionary<string, int> Headings(params string[] cells)
        {
            var found = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < cells.Length; i++)
            {
                var key = IncidentImport.Heading(cells[i]);
                if (key.Length > 0 && !found.ContainsKey(key)) found[key] = i + 1;
            }
            return found;
        }

        Console.WriteLine();
        Console.WriteLine("Finding the columns, in either language.");
        Console.WriteLine();

        var english = Headings("Job No.", "Type", "Category", "Title", "What", "Where", "When", "Who");
        Check(IncidentImport.ColumnFor("title", english) == 4, "Title");
        Check(IncidentImport.ColumnFor("jobKey", english) == 1, "Job No. — punctuation and spacing ignored");
        Check(IncidentImport.ColumnFor("kind", english) == 2, "Type");
        Check(IncidentImport.ColumnFor("who", english) == 8, "Who");

        /*
         * The regression this file exists for.
         *
         * Heading() drops everything that is not a letter or a digit, and Thai
         * vowel and tone marks are combining marks — IsLetterOrDigit is false
         * for them — so "หัวข้อ" reduces to "หวขอ". The first version compared
         * that against the unnormalised "หัวข้อ" in its own list, matched
         * nothing, and refused a sheet with perfectly good Thai headings as
         * unreadable. Both sides go through Heading now.
         */
        var thai = Headings("เลขงาน", "ประเภท", "หมวด", "หัวข้อ", "อะไร", "ที่ไหน", "เมื่อไหร่", "ใคร");
        Check(IncidentImport.ColumnFor("title", thai) == 4,
            "หัวข้อ — the one that was broken: Thai tone marks are not letters");
        Check(IncidentImport.ColumnFor("jobKey", thai) == 1, "เลขงาน");
        Check(IncidentImport.ColumnFor("kind", thai) == 2, "ประเภท");
        Check(IncidentImport.ColumnFor("category", thai) == 3, "หมวด");
        Check(IncidentImport.ColumnFor("where", thai) == 6, "ที่ไหน");
        Check(IncidentImport.ColumnFor("when", thai) == 7, "เมื่อไหร่");

        Check(IncidentImport.ColumnFor("title", Headings("เลขงาน", "ลูกค้า")) is null,
            "and a sheet with no title column says so rather than guessing one");

        Console.WriteLine();
        Console.WriteLine("CAR or PAR.");
        Console.WriteLine();

        Check(IncidentImport.KindOf("PAR") == IncidentImport.Par, "PAR");
        Check(IncidentImport.KindOf("par (ป้องกัน)") == IncidentImport.Par, "however it is written");
        Check(IncidentImport.KindOf("ป้องกัน") == IncidentImport.Par, "and in Thai");
        Check(IncidentImport.KindOf("CAR") == IncidentImport.Car, "CAR");
        // A log of things that went wrong is far likelier than a log of things
        // somebody prevented, so silence means CAR rather than a refusal.
        Check(IncidentImport.KindOf("") == IncidentImport.Car, "and an empty cell is CAR, not a refusal");
        Check(IncidentImport.KindOf("อะไรก็ไม่รู้") == IncidentImport.Car, "as is anything unrecognised");

        Console.WriteLine();
        Console.WriteLine("Which category, from what somebody wrote.");
        Console.WriteLine();

        Check(IncidentImport.CategoryOf("อุบัติเหตุ") == "accident", "อุบัติเหตุ");
        Check(IncidentImport.CategoryOf("อุบัติเหตุรถชน") == "accident", "and inside a longer phrase");
        Check(IncidentImport.CategoryOf("Traffic accident") == "accident", "and in English");
        Check(IncidentImport.CategoryOf("ความล่าช้า") == "delay", "ความล่าช้า");
        Check(IncidentImport.CategoryOf("คุณภาพ") == "quality", "คุณภาพ");
        Check(IncidentImport.CategoryOf("เสียหาย") == "damage", "เสียหาย");
        Check(IncidentImport.CategoryOf("ความปลอดภัย") == "safety", "ความปลอดภัย");
        Check(IncidentImport.CategoryOf("") == "other", "nothing at all is other");
        Check(IncidentImport.CategoryOf("เรื่องอื่น") == "other", "and so is something nobody listed");

        // Whatever this decides has to be a category the service will store.
        // Anything else and the service quietly rewrites it to other, and the
        // preview would have promised something that did not happen.
        var everyCategoryReal = new[] { "อุบัติเหตุ", "เสียหาย", "ล่าช้า", "ปลอดภัย", "คุณภาพ", "อื่น", "" }
            .Select(IncidentImport.CategoryOf)
            .All(IncidentService.Categories.Contains);
        Check(everyCategoryReal, "and every answer is one the register actually stores");

        Console.WriteLine();
        Console.WriteLine("Which rows are cases.");
        Console.WriteLine();

        Check(IncidentImport.Usable("รถชนที่ประตูโรงงาน"), "a row with a title is a case");
        Check(!IncidentImport.Usable(""), "a row without one is not");
        Check(!IncidentImport.Usable("   "), "nor is a row of spaces");
        Check(!IncidentImport.Usable("-"), "nor a dash, which is how these sheets write 'none'");

        Check(IncidentImport.Tidy("  รถชน   ที่ประตู  ") == "รถชน ที่ประตู",
            "a value is trimmed and its inner spaces collapsed");
        Check(IncidentImport.Tidy("N/A") == "", "and N/A reads as empty");

        // Some of these sheets put a paragraph in the title column, and the
        // column stores 300. Cut here rather than at SaveChanges, after the
        // import has done its work.
        var long_ = new string('ก', IncidentImport.TitleLength + 50);
        Check(IncidentImport.Title(long_).Length == IncidentImport.TitleLength,
            $"a title past {IncidentImport.TitleLength} is cut to the column, not to SaveChanges");

        Console.WriteLine();
        Check(IncidentImport.StageOf("Closed") == "closed", "Closed is never reopened");
        Check(IncidentImport.StageOf("Request Evidence") == "follow-up", "evidence waits in follow-up");
        Check(IncidentImport.StageOf("Waiting Response") == "open", "response waits in open");
        Check(IncidentImport.ReferenceKey("CAR-26/002") == IncidentImport.ReferenceKey("CAR-26-002"),
            "punctuation does not bypass duplicate detection");
        try { IncidentImport.StageOf("unknown"); Check(false, "unknown status refused"); }
        catch (InvalidOperationException) { Check(true, "unknown status refused"); }
        var fixture = new IncidentImporter.Row("CAR-26/999", "title", "CAR", "other",
            "Closed", "closed", new Dictionary<string, string> {
                ["Detail"] = new string('ก', 1500) + "\nsecond line",
                ["ABS job no./ Job no."] = "123456",
                ["Close Date (Actual)"] = "19/08/2026" });
        var entity = IncidentImporter.ToCase(fixture, "tester", DateTimeOffset.UtcNow);
        Check(entity.Stage == "closed" && entity.ApprovedBy == "" && entity.ApprovedAt is null,
            "historical closed case has no fabricated approval");
        Check(entity.JobKey == "" && entity.TeamNote.Contains("123456"), "source job is preserved, not guessed");
        Check(entity.What.Length == 1000 && entity.TeamNote.Contains(new string('ก', 1500)),
            "full long detail survives bounded display field");

        // Exercise the actual XLSX parser in CI, not only the mapping helpers.
        using (var book = new ClosedXML.Excel.XLWorkbook())
        using (var buffer = new MemoryStream())
        {
            var sheet = book.AddWorksheet("CAR_PAR");
            var headers = new[] { "Detail", "CAR/PAR No.", "CAR/PAR", "Status", "Issued\nDate" };
            for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
            sheet.Cell(2, 1).Value = "Multiline\nDetail";
            sheet.Cell(2, 2).Value = "26/999";
            sheet.Cell(2, 3).Value = "CAR";
            sheet.Cell(2, 4).Value = "Closed";
            sheet.Cell(2, 5).Value = new DateTime(2026, 8, 19);
            book.SaveAs(buffer);
            buffer.Position = 0;
            var parsed = IncidentImporter.Read(buffer, out _, out _, out _).Single();
            Check(parsed.Stage == "closed" && parsed.Reference == "CAR-26/999",
                "XLSX parser preserves reference and Closed");
            Check(parsed.At("Issued Date") == "19/08/2026" && parsed.At("Detail") == "Multiline\nDetail",
                "XLSX parser preserves date and multiline detail");
        }
        var fileIndex = Array.IndexOf(args, "--workbook");
        if (fileIndex >= 0 && fileIndex + 1 < args.Length)
        {
            using var input = File.OpenRead(args[fileIndex + 1]);
            var rows = IncidentImporter.Read(input, out var skipped, out _, out _);
            Check(rows.Count == 29 && skipped == 0, "real workbook: all 29 rows read");
            Check(rows.Count(r => r.Status == "Closed") == 7, "real workbook: Closed 7");
            Check(rows.Count(r => r.Status == "Request Evidence") == 16, "real workbook: Request Evidence 16");
            Check(rows.Count(r => r.Status == "Waiting Response") == 6, "real workbook: Waiting Response 6");
            Check(rows.All(r => r.Source.Count == 30), "real workbook: all 30 columns retained");
        }
        Console.WriteLine(failed == 0
            ? "All incident import checks passed."
            : $"{failed} incident import check(s) failed.");
        return failed == 0 ? 0 : 1;
    }
}
