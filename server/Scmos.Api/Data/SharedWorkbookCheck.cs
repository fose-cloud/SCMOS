using System.IO.Compression;
using System.Text;
using System.Xml;
using Scmos.Api.Rules;

namespace Scmos.Api.Data;

/// <summary>Small in-memory OOXML packages, never customer spreadsheets or external storage.</summary>
internal static class SharedWorkbookCheck
{
    public static int Run()
    {
        var failed = 0;
        void Check(string why, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}: {why}"); if (!ok) failed++; }
        bool Refuses(byte[] bytes, params SharedWorkbook.CellEdit[] patches)
        { try { SharedWorkbook.Edit(bytes, patches); return false; } catch (Exception e) when (e is InvalidDataException or XmlException) { return true; } }
        var fixture = Fixture();
        var view = SharedWorkbook.Read(fixture);
        Check("XLSX exposes scalar cells but not formulas or merged cells for editing",
            view.Sheets[0].Cells.Single(c => c.Address == "A1").Editable
            && !view.Sheets[0].Cells.Single(c => c.Address == "B1").Editable
            && !view.Sheets[0].Cells.Single(c => c.Address == "C1").Editable);
        var saved = SharedWorkbook.Edit(fixture, [new("Rates", "A1", "number", "123.45")]);
        Check("a scalar XLSX price changes", SharedWorkbook.Read(saved).Sheets[0].Cells.Single(c => c.Address == "A1").Value == "123.45");
        Check("styles, images, print settings and unknown package parts remain byte-identical",
            new[] { "xl/styles.xml", "xl/media/image1.png", "docProps/custom.xml" }.All(p => Part(fixture, p).SequenceEqual(Part(saved, p))));
        var sheetXml = Encoding.UTF8.GetString(Part(saved, "xl/worksheets/sheet1.xml"));
        Check("formula expressions survive but stale cached results are cleared",
            sheetXml.Contains("<f>A1*2</f>") && !sheetXml.Contains("<v>20</v>"));
        Check("style ids and print settings on edited sheets survive", sheetXml.Contains("s=\"1\"") && sheetXml.Contains("paperSize=\"9\""));
        Check("Excel is explicitly asked to recalculate", Encoding.UTF8.GetString(Part(saved, "xl/workbook.xml")).Contains("fullCalcOnLoad=\"1\""));
        Check("formula, merged and new cells cannot be changed", Refuses(fixture, new SharedWorkbook.CellEdit("Rates", "B1", "number", "1"))
            && Refuses(fixture, new SharedWorkbook.CellEdit("Rates", "C1", "text", "bad")) && Refuses(fixture, new SharedWorkbook.CellEdit("Rates", "E1", "text", "new")));
        Check("out-of-preview, duplicate and invalid numeric edits are rejected", Refuses(fixture, new SharedWorkbook.CellEdit("Rates", "A201", "text", "bad"))
            && Refuses(fixture, new SharedWorkbook.CellEdit("Rates", "A1", "number", "NaN"))
            && Refuses(fixture, new("Rates", "A1", "text", "a"), new("Rates", "A1", "text", "b")));
        var literal = SharedWorkbook.Edit(fixture, [new("Rates", "A1", "text", "=HYPERLINK(\"https://example.test\")")]);
        Check("formula-looking text is stored as inert text", SharedWorkbook.Read(literal).Sheets[0].Cells.Single(c => c.Address == "A1") is { Kind: "text", Editable: true });
        Check("protected workbooks, macros, signatures, external links and data validation are read-only",
            Refuses(Fixture(bookProtection: true), new SharedWorkbook.CellEdit("Rates", "A1", "text", "bad"))
            && new[] { "xl/vbaProject.bin", "_xmlsignatures/sig1.xml", "xl/externalLinks/externalLink1.xml" }
                .All(part => Refuses(Fixture(extraPart: part), new SharedWorkbook.CellEdit("Rates", "A1", "text", "bad")))
            && Refuses(Fixture(validation: true), new SharedWorkbook.CellEdit("Rates", "A1", "text", "bad")));
        Check("DTD/external-entity XML is rejected", Refuses(Fixture(dtd: true), new SharedWorkbook.CellEdit("Rates", "A1", "text", "bad")));
        var real = ValidFixture();
        var edited = SharedWorkbook.Edit(real, [new("Rates", "A1", "number", "456.78")]);
        using var reopened = new ClosedXML.Excel.XLWorkbook(new MemoryStream(edited));
        Check("a standard XLSX roundtrips with styles/formulas and the existing spreadsheet engine computes the new price",
            reopened.Worksheet("Rates").Cell("A1").GetDouble() == 456.78
            && reopened.Worksheet("Rates").Cell("A1").Style.NumberFormat.Format == "#,##0.00"
            && reopened.Worksheet("Rates").Cell("A1").Style.Font.Bold
            && reopened.Worksheet("Rates").Cell("B1").FormulaA1 == "A1*2"
            && reopened.Worksheet("Rates").Cell("B1").GetDouble() == 913.56);
        return failed;
    }

    internal static byte[] ValidFixture()
    {
        using var book = new ClosedXML.Excel.XLWorkbook();
        var sheet = book.AddWorksheet("Rates");
        sheet.Cell("A1").Value = 10;
        sheet.Cell("A1").Style.NumberFormat.Format = "#,##0.00";
        sheet.Cell("A1").Style.Font.Bold = true;
        sheet.Cell("B1").FormulaA1 = "A1*2";
        sheet.Cell("A2").Value = "ราคาใหม่";
        sheet.PageSetup.PaperSize = ClosedXML.Excel.XLPaperSize.A4Paper;
        using var stream = new MemoryStream(); book.SaveAs(stream); return stream.ToArray();
    }

    internal static byte[] Fixture(bool bookProtection = false, string? extraPart = null, bool validation = false, bool dtd = false)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var parts = new Dictionary<string, byte[]>
        {
            ["xl/workbook.xml"] = Encoding.UTF8.GetBytes((dtd ? "<!DOCTYPE workbook [<!ENTITY x SYSTEM 'file:///never-read'>]>" : "")
                + $"<workbook xmlns=\"{ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Rates\" sheetId=\"1\" r:id=\"rId1\"/></sheets>"
                + (bookProtection ? "<workbookProtection lockStructure=\"1\"/>" : "") + "</workbook>"),
            ["xl/_rels/workbook.xml.rels"] = Encoding.UTF8.GetBytes("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\"/></Relationships>"),
            ["xl/worksheets/sheet1.xml"] = Encoding.UTF8.GetBytes($"<worksheet xmlns=\"{ns}\"><sheetData><row r=\"1\"><c r=\"A1\" s=\"1\"><v>10</v></c><c r=\"B1\"><f>A1*2</f><v>20</v></c><c r=\"C1\" t=\"inlineStr\"><is><t>merged</t></is></c></row></sheetData><mergeCells><mergeCell ref=\"C1:D1\"/></mergeCells><pageSetup paperSize=\"9\"/>"
                + (validation ? "<dataValidations><dataValidation type=\"list\" sqref=\"A1\"/></dataValidations>" : "") + "</worksheet>"),
            ["xl/styles.xml"] = Encoding.UTF8.GetBytes("<styleSheet>original styles untouched</styleSheet>"),
            ["xl/media/image1.png"] = [137, 80, 78, 71, 1, 2, 3],
            ["docProps/custom.xml"] = Encoding.UTF8.GetBytes("<custom>unrecognized features untouched</custom>"),
        };
        if (extraPart is not null) parts[extraPart] = [1];
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (name, bytes) in parts) { using var dest = zip.CreateEntry(name).Open(); dest.Write(bytes); }
        return stream.ToArray();
    }

    private static byte[] Part(byte[] bytes, string name)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = zip.GetEntry(name)!.Open(); using var output = new MemoryStream(); stream.CopyTo(output); return output.ToArray();
    }
}
