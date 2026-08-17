using ClosedXML.Excel;

namespace Scmos.Api.Services;

/// <summary>
/// The KPI report as a workbook.
///
/// The last step of the KPI flow is Excel, because that is what gets attached to
/// a management mail and taken into a supplier meeting. What matters here is
/// that a measure with no data reads as "ยังวัดไม่ได้" and not as a blank cell
/// that a reader fills in with an assumption — a blank in a spreadsheet is
/// nearly always read as zero.
/// </summary>
public static class KpiWorkbook
{
    private static readonly XLColor Navy = XLColor.FromHtml("#0A2240");
    private static readonly XLColor Head = XLColor.FromHtml("#F1F5F9");
    private static readonly XLColor Muted = XLColor.FromHtml("#7B8CA0");

    public static byte[] Build(KpiEngineReport report, KpiReport operational)
    {
        using var workbook = new XLWorkbook();

        Summary(workbook.AddWorksheet("KPI Summary"), report, operational);
        Suppliers(workbook.AddWorksheet("Supplier Performance"), report);
        Breakdowns(workbook.AddWorksheet("Breakdown"), report);
        Operational(workbook.AddWorksheet("Operational"), operational);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void Summary(IXLWorksheet sheet, KpiEngineReport report, KpiReport operational)
    {
        sheet.Cell(1, 1).Value = "SCMOS · Operational KPI";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 15;
        sheet.Cell(1, 1).Style.Font.FontColor = Navy;

        sheet.Cell(2, 1).Value = $"ช่วงเวลา: {PeriodLabel(report.Period)} · งานในช่วง {report.Jobs:N0} · คำนวณเมื่อ {report.ComputedAt}";
        sheet.Cell(2, 1).Style.Font.FontColor = Muted;

        var head = new[] { "Measure", "ตัวชี้วัด", "ค่า", "หน่วย", "ฐานที่วัด", "วัดได้", "หมายเหตุ" };
        for (var c = 0; c < head.Length; c++)
        {
            var cell = sheet.Cell(4, c + 1);
            cell.Value = head[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = Head;
        }

        var row = 5;
        foreach (var measure in report.Measures)
        {
            sheet.Cell(row, 1).Value = measure.English;
            sheet.Cell(row, 2).Value = measure.Thai;

            if (measure.Available && measure.Value is not null)
            {
                sheet.Cell(row, 3).Value = measure.Value.Value;
            }
            else
            {
                // Spelled out rather than left blank: an empty cell in a report
                // is read as zero by whoever opens it next.
                sheet.Cell(row, 3).Value = "ยังวัดไม่ได้";
                sheet.Cell(row, 3).Style.Font.FontColor = XLColor.FromHtml("#B45309");
            }

            sheet.Cell(row, 4).Value = measure.Unit;
            sheet.Cell(row, 5).Value = measure.Base;
            sheet.Cell(row, 6).Value = measure.Available ? "ใช่" : "ไม่";
            sheet.Cell(row, 7).Value = measure.Note;
            row++;
        }

        sheet.Columns(1, 7).AdjustToContents();
        sheet.Column(7).Width = 70;
        sheet.SheetView.FreezeRows(4);
    }

    private static void Suppliers(IXLWorksheet sheet, KpiEngineReport report)
    {
        var head = new[]
        {
            "ผู้ขนส่ง", "งาน", "ตรงเวลา %", "ฐานตรงเวลา", "ตอบยืนยัน %", "ฐานตอบยืนยัน",
            "ไม่มีความล่าช้า %", "งานที่ล่าช้า", "คะแนนรวม",
        };
        for (var c = 0; c < head.Length; c++)
        {
            var cell = sheet.Cell(1, c + 1);
            cell.Value = head[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = Head;
        }

        var row = 2;
        foreach (var supplier in report.Suppliers)
        {
            sheet.Cell(row, 1).Value = supplier.Carrier;
            sheet.Cell(row, 2).Value = supplier.Jobs;
            Put(sheet.Cell(row, 3), supplier.OnTime);
            sheet.Cell(row, 4).Value = supplier.OnTimeBase;
            Put(sheet.Cell(row, 5), supplier.Confirmation);
            sheet.Cell(row, 6).Value = supplier.ConfirmationBase;
            Put(sheet.Cell(row, 7), supplier.DelayFree);
            sheet.Cell(row, 8).Value = supplier.DelayCount;
            Put(sheet.Cell(row, 9), supplier.Score);
            row++;
        }

        sheet.Columns(1, 9).AdjustToContents();
        sheet.SheetView.FreezeRows(1);
    }

    private static void Breakdowns(IXLWorksheet sheet, KpiEngineReport report)
    {
        var row = 1;
        foreach (var measure in report.Measures.Where(m => m.Breakdown.Count > 0))
        {
            sheet.Cell(row, 1).Value = $"{measure.English} · {measure.Thai}";
            sheet.Cell(row, 1).Style.Font.Bold = true;
            sheet.Cell(row, 1).Style.Font.FontColor = Navy;
            row++;

            foreach (var entry in measure.Breakdown)
            {
                sheet.Cell(row, 1).Value = entry.Label;
                sheet.Cell(row, 2).Value = entry.Value;
                row++;
            }
            row++;
        }

        if (row == 1) sheet.Cell(1, 1).Value = "ยังไม่มีรายละเอียดให้แสดง";
        sheet.Columns(1, 2).AdjustToContents();
    }

    private static void Operational(IXLWorksheet sheet, KpiReport operational)
    {
        void Section(string title, IEnumerable<(string Label, object Value)> rows, ref int at)
        {
            sheet.Cell(at, 1).Value = title;
            sheet.Cell(at, 1).Style.Font.Bold = true;
            sheet.Cell(at, 1).Style.Font.FontColor = Navy;
            at++;
            foreach (var (label, value) in rows)
            {
                sheet.Cell(at, 1).Value = label;
                sheet.Cell(at, 2).Value = XLCellValue.FromObject(value);
                at++;
            }
            at++;
        }

        var row = 1;
        Section("ภาพรวม", new (string, object)[]
        {
            ("งานทั้งหมด", operational.Total),
            ("ต้องดำเนินการ", operational.ActionRequired),
            ("รูปแบบข้อมูลผิด", operational.FormatErrors),
            ("เสี่ยงตกเรือ (Export)", operational.GateInRisk),
            ("ไม่มีวันที่ใช้ได้", operational.Undated),
        }, ref row);

        Section("แยกตามหมวด", operational.ByCategory.Select(c => (c.Label, (object)c.Value)), ref row);
        Section("แยกตามสถานะ", operational.ByStatus.Select(c => (c.Label, (object)c.Value)), ref row);
        Section("ภาระงานแต่ละคน", operational.Team.Select(t => (t.Owner, (object)t.Total)), ref row);

        sheet.Columns(1, 2).AdjustToContents();
    }

    private static void Put(IXLCell cell, double? value)
    {
        if (value is null)
        {
            cell.Value = "—";
            cell.Style.Font.FontColor = Muted;
            return;
        }
        cell.Value = value.Value;
    }

    private static void Put(IXLCell cell, int? value)
    {
        if (value is null)
        {
            cell.Value = "—";
            cell.Style.Font.FontColor = Muted;
            return;
        }
        cell.Value = value.Value;
    }

    private static string PeriodLabel(Period period)
    {
        if (period.IsAll) return "ทั้งหมด";
        var parts = new List<string>();
        if (period.Day.Length > 0) parts.Add(period.Day);
        if (period.Month.Length > 0) parts.Add(period.Month);
        if (period.Year.Length > 0) parts.Add(period.Year);
        return string.Join("/", parts);
    }
}
