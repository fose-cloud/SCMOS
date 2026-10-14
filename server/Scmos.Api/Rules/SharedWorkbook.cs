using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Scmos.Api.Rules;

/// <summary>
/// Basic XLSX editor: patch selected scalar cells in OOXML, never reserialize the workbook through a spreadsheet
/// library. Untouched package parts (styles, drawings, print settings, etc.) remain byte-for-byte intact.
/// Formula cells are read-only. After an edit cached calculations are removed and Excel must recalculate.
/// </summary>
public static partial class SharedWorkbook
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const int MaxRows = 200;
    public const int MaxColumns = 60;
    public sealed record CellEdit(string Sheet, string Address, string Kind, string Value);
    public sealed record Cell(string Address, string Value, string Kind, bool Editable);
    public sealed record Sheet(string Name, IReadOnlyList<Cell> Cells, bool Truncated, string Problem);
    public sealed record View(IReadOnlyList<Sheet> Sheets, string Problem);
    [GeneratedRegex(@"^[A-Z]{1,3}[1-9][0-9]{0,6}$")]
    private static partial Regex AddressPattern();

    private static Dictionary<string, byte[]> Parts(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, false);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        if (zip.Entries.Count > 1500) throw new InvalidDataException("ไฟล์มีส่วนประกอบมากเกินไป — กรุณาใช้ Excel");
        var parts = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            if ((total += entry.Length) > 64 * 1024 * 1024 || entry.Length > 16 * 1024 * 1024)
                throw new InvalidDataException("ไฟล์ขยายใหญ่เกินขีดจำกัด editor — กรุณาใช้ Excel");
            if (entry.FullName.Contains("..") || entry.FullName.StartsWith('/') || entry.FullName.Contains('\\')
                || !parts.TryAdd(entry.FullName, [])) throw new InvalidDataException("โครงสร้าง XLSX ไม่ถูกต้อง");
            using var input = entry.Open();
            using var copy = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer)) > 0)
            {
                if (copy.Length + read > entry.Length || copy.Length + read > 16 * 1024 * 1024)
                    throw new InvalidDataException("ไฟล์ ZIP ขยายเกินขีดจำกัด");
                copy.Write(buffer, 0, read);
            }
            parts[entry.FullName] = copy.ToArray();
        }
        return parts;
    }

    private static XDocument Xml(Dictionary<string, byte[]> parts, string name)
    {
        if (!parts.TryGetValue(name, out var bytes)) throw new InvalidDataException("ไม่พบส่วนประกอบ XLSX: " + name);
        using var reader = XmlReader.Create(new MemoryStream(bytes, false), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024,
        });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (doc.Descendants().Take(100001).Count() > 100000)
            throw new InvalidDataException("โครงสร้าง XML ซับซ้อนเกินขีดจำกัด editor — กรุณาใช้ Excel");
        return doc;
    }

    private static byte[] Bytes(XDocument doc) => Encoding.UTF8.GetBytes(doc.ToString(SaveOptions.DisableFormatting));
    private static string Problem(Dictionary<string, byte[]> parts, XDocument book)
    {
        if (book.Root?.Name != S + "workbook") return "รองรับเฉพาะ XLSX มาตรฐาน (ไม่รองรับ Strict OOXML)";
        var types = Xml(parts, "[Content_Types].xml");
        if (types.Root?.Name != XName.Get("Types", "http://schemas.openxmlformats.org/package/2006/content-types"))
            return "โครงสร้างชนิดไฟล์ XLSX ไม่ถูกต้อง — อ่านอย่างเดียว";
        var declared = types.Root.Elements().Select(e => (string?)e.Attribute("ContentType") ?? "").ToArray();
        if (declared.Any(t => t.Contains("macro", StringComparison.OrdinalIgnoreCase) || t.Contains("vba", StringComparison.OrdinalIgnoreCase)
            || t.Contains("digital-signature", StringComparison.OrdinalIgnoreCase) || t.Contains("activeX", StringComparison.OrdinalIgnoreCase)))
            return "ไฟล์มี macro, ActiveX หรือลายเซ็น — อ่านอย่างเดียว";
        // OPC permits either a part override or a default by extension; OpenXML SDK uses the latter for some workbooks.
        var workbookType = (string?)types.Root.Elements().FirstOrDefault(e => (string?)e.Attribute("PartName") == "/xl/workbook.xml")?.Attribute("ContentType")
            ?? (string?)types.Root.Elements().FirstOrDefault(e => (string?)e.Attribute("Extension") == "xml")?.Attribute("ContentType");
        if (workbookType != "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")
            return "ชนิด workbook ไม่ใช่ XLSX มาตรฐาน — อ่านอย่างเดียว";
        if (book.Descendants(S + "workbookProtection").Any()) return "ไฟล์มีการป้องกัน workbook — อ่านอย่างเดียว";
        if (parts.Keys.Any(k => k.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase)
            || k.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) || k.StartsWith("xl/externalLinks/")
            || k.StartsWith("xl/pivot") || k.StartsWith("xl/queryTables/") || k.StartsWith("xl/connections")))
            return "ไฟล์มีลายเซ็น, macro, ข้อมูลเชื่อมต่อหรือ Pivot — อ่านอย่างเดียวเพื่อรักษาข้อมูล";
        return "";
    }

    private static List<(string Name, string Part)> Sheets(Dictionary<string, byte[]> parts, XDocument book)
    {
        var relations = Xml(parts, "xl/_rels/workbook.xml.rels");
        var result = new List<(string, string)>();
        foreach (var sheet in book.Descendants(S + "sheet"))
        {
            var rel = relations.Root?.Elements().FirstOrDefault(r => (string?)r.Attribute("Id") == (string?)sheet.Attribute(R + "id"));
            var target = (string?)rel?.Attribute("Target") ?? "";
            var part = target.StartsWith("/xl/") ? target[1..] : "xl/" + target;
            if (rel is null || (string?)rel.Attribute("TargetMode") == "External" || !part.StartsWith("xl/worksheets/")
                || part.Contains("..") || !part.EndsWith(".xml") || !parts.ContainsKey(part))
                throw new InvalidDataException("รองรับเฉพาะแผ่นงาน XLSX ภายในไฟล์");
            if ((string?)sheet.Attribute("state") is "hidden" or "veryHidden") continue;
            result.Add(((string?)sheet.Attribute("name") ?? "Sheet", part));
            if (result.Count > 30) throw new InvalidDataException("เปิดได้ไม่เกิน 30 แผ่นงาน");
        }
        return result;
    }

    private static string SheetProblem(XDocument sheet) =>
        sheet.Descendants(S + "sheetProtection").Any() ? "แผ่นงานมีการป้องกัน — อ่านอย่างเดียว"
        : sheet.Descendants(S + "dataValidation").Any() ? "แผ่นงานมี Data validation — อ่านอย่างเดียว"
        : sheet.Descendants(S + "extLst").Any() ? "แผ่นงานมีส่วนขยายที่ editor ไม่รองรับ — อ่านอย่างเดียว"
        : sheet.Descendants(S + "mergeCell").Take(201).Count() > 200 ? "แผ่นงานมีเซลล์รวมซับซ้อนเกินขีดจำกัด — อ่านอย่างเดียว" : "";

    public static (int Column, int Row) Position(string address)
    {
        if (!AddressPattern().IsMatch(address)) throw new InvalidDataException("ตำแหน่งเซลล์ไม่ถูกต้อง");
        var col = 0;
        var pos = 0;
        while (pos < address.Length && char.IsAsciiLetterUpper(address[pos])) col = col * 26 + address[pos++] - 'A' + 1;
        var row = int.Parse(address[pos..], CultureInfo.InvariantCulture);
        if (col > 16384 || row > 1048576) throw new InvalidDataException("ตำแหน่งเซลล์เกินขีดจำกัด Excel");
        return (col, row);
    }

    private static bool Merged(XDocument sheet, string address)
    {
        var (col, row) = Position(address);
        foreach (var merge in sheet.Descendants(S + "mergeCell"))
        {
            var range = ((string?)merge.Attribute("ref") ?? "").Split(':');
            if (range.Length != 2) continue;
            var a = Position(range[0]); var b = Position(range[1]);
            if (col >= a.Column && col <= b.Column && row >= a.Row && row <= b.Row) return true;
        }
        return false;
    }

    private static bool Scalar(XElement cell) => cell.Attribute("cm") is null && cell.Attribute("vm") is null && !cell.Elements(S + "f").Any()
        && ((string?)cell.Attribute("t") is null or "n" or "b" or "s" or "inlineStr" or "str")
        && cell.Elements().All(e => e.Name == S + "v" || e.Name == S + "is");

    public static View Read(byte[] bytes)
    {
        var parts = Parts(bytes);
        var book = Xml(parts, "xl/workbook.xml");
        var problem = Problem(parts, book);
        var strings = parts.ContainsKey("xl/sharedStrings.xml")
            ? Xml(parts, "xl/sharedStrings.xml").Descendants(S + "si").Select(s => string.Concat(s.Descendants(S + "t").Select(t => t.Value))).ToArray()
            : [];
        var result = new List<Sheet>();
        foreach (var (name, part) in Sheets(parts, book))
        {
            var doc = Xml(parts, part);
            var sheetProblem = SheetProblem(doc);
            var cells = new List<Cell>();
            var truncated = false;
            foreach (var c in doc.Descendants(S + "sheetData").Elements(S + "row").Elements(S + "c"))
            {
                var address = (string?)c.Attribute("r") ?? "";
                var at = Position(address);
                if (at.Row > MaxRows || at.Column > MaxColumns) { truncated = true; continue; }
                var type = (string?)c.Attribute("t");
                var value = c.Element(S + "v")?.Value ?? "";
                var kind = type is null or "n" ? "number" : type == "b" ? "boolean" : "text";
                if (type == "s") value = int.TryParse(value, out var i) && i >= 0 && i < strings.Length ? strings[i] : "";
                else if (type == "inlineStr") value = string.Concat(c.Descendants(S + "t").Select(t => t.Value));
                if (c.Element(S + "f") is { } formula) { value = "=" + formula.Value; kind = "formula"; }
                cells.Add(new(address, value, kind, problem.Length == 0 && sheetProblem.Length == 0 && Scalar(c) && !Merged(doc, address)));
            }
            result.Add(new(name, cells, truncated, sheetProblem));
        }
        return new(result, problem);
    }

    public static byte[] Edit(byte[] bytes, IReadOnlyList<CellEdit> edits)
    {
        if (edits.Count is < 1 or > 500) throw new InvalidDataException("บันทึกได้ครั้งละ 1–500 เซลล์");
        var parts = Parts(bytes);
        var book = Xml(parts, "xl/workbook.xml");
        if (Problem(parts, book) is { Length: > 0 } problem) throw new InvalidDataException(problem);
        var sheets = Sheets(parts, book).ToDictionary(s => s.Name, s => s.Part);
        var touched = new Dictionary<string, XDocument>();
        var seen = new HashSet<string>();
        foreach (var edit in edits)
        {
            if (edit is null || edit.Sheet is null || edit.Address is null || edit.Value is null || !sheets.TryGetValue(edit.Sheet, out var part))
                throw new InvalidDataException("ไม่พบแผ่นงานหรือค่าเซลล์");
            if (!seen.Add(part + ":" + edit.Address)) throw new InvalidDataException("เซลล์ซ้ำ");
            var at = Position(edit.Address);
            if (at.Row > MaxRows || at.Column > MaxColumns) throw new InvalidDataException("แก้ไขได้เฉพาะเซลล์ในพื้นที่ preview");
            if (!touched.TryGetValue(part, out var doc)) touched[part] = doc = Xml(parts, part);
            if (SheetProblem(doc) is { Length: > 0 } blocked) throw new InvalidDataException(blocked);
            var cell = doc.Descendants(S + "sheetData").Elements(S + "row").Elements(S + "c")
                .SingleOrDefault(c => (string?)c.Attribute("r") == edit.Address);
            // Existing cells only: inserting a row/column is a structural edit, outside this basic editor.
            if (cell is null || !Scalar(cell) || Merged(doc, edit.Address))
                throw new InvalidDataException("แก้ไขสูตร เซลล์รวม หรือเพิ่มเซลล์ใหม่ไม่ได้");
            if (edit.Value.Length > 10000) throw new InvalidDataException("ข้อความเซลล์ยาวเกิน 10,000 ตัวอักษร");
            cell.Elements(S + "v").Remove(); cell.Elements(S + "is").Remove();
            if (edit.Kind == "text")
            {
                // Treat even '=...' as a literal, never a formula or external link.
                cell.SetAttributeValue("t", "inlineStr");
                cell.Add(new XElement(S + "is", new XElement(S + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), edit.Value)));
            }
            else if (edit.Kind == "number" && double.TryParse(edit.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n))
            { cell.SetAttributeValue("t", "n"); cell.Add(new XElement(S + "v", n.ToString("R", CultureInfo.InvariantCulture))); }
            else if (edit.Kind == "boolean" && edit.Value is "0" or "1")
            { cell.SetAttributeValue("t", "b"); cell.Add(new XElement(S + "v", edit.Value)); }
            else throw new InvalidDataException("ชนิดหรือค่าเซลล์ไม่ถูกต้อง (ตัวเลขใช้จุดทศนิยม ไม่ใส่ comma)");
        }
        // No misleading stale results: clear all formula caches, preserve formulas, request a full Excel recalculation.
        foreach (var part in parts.Keys.Where(k => k.StartsWith("xl/worksheets/") && k.EndsWith(".xml")).ToArray())
        {
            var doc = touched.GetValueOrDefault(part) ?? Xml(parts, part);
            var formulas = doc.Descendants(S + "c").Where(c => c.Element(S + "f") is not null).ToArray();
            foreach (var c in formulas) c.Elements(S + "v").Remove();
            if (formulas.Length > 0 || touched.ContainsKey(part)) parts[part] = Bytes(doc);
        }
        var calc = book.Root!.Element(S + "calcPr");
        if (calc is null)
        {
            calc = new XElement(S + "calcPr");
            var following = book.Root.Elements().FirstOrDefault(e => e.Name.LocalName is "oleSize" or "customWorkbookViews"
                or "pivotCaches" or "smartTagPr" or "smartTagTypes" or "webPublishing" or "fileRecoveryPr" or "webPublishObjects" or "extLst");
            if (following is null) book.Root.Add(calc); else following.AddBeforeSelf(calc);
        }
        calc.SetAttributeValue("calcMode", "auto"); calc.SetAttributeValue("fullCalcOnLoad", "1"); calc.SetAttributeValue("forceFullCalc", "1");
        parts["xl/workbook.xml"] = Bytes(book);
        if (parts.Remove("xl/calcChain.xml"))
        {
            var rels = Xml(parts, "xl/_rels/workbook.xml.rels");
            rels.Root!.Elements().Where(r => ((string?)r.Attribute("Type"))?.EndsWith("/calcChain") == true).Remove();
            parts["xl/_rels/workbook.xml.rels"] = Bytes(rels);
            var types = Xml(parts, "[Content_Types].xml");
            types.Root!.Elements().Where(t => (string?)t.Attribute("PartName") == "/xl/calcChain.xml").Remove();
            parts["[Content_Types].xml"] = Bytes(types);
        }
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var (name, data) in parts) { using var dest = zip.CreateEntry(name).Open(); dest.Write(data); }
        var saved = output.ToArray();
        if (saved.LongLength > SharedFiles.MaxBytes) throw new InvalidDataException("ไฟล์หลังแก้ไขใหญ่เกิน 32 MB");
        return saved;
    }
}
