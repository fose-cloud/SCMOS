using System.Globalization;
using Scmos.Api.Services;

namespace Scmos.Api.Rules;

public record QuoteSelection(int Id, decimal Quantity);
public record QuoteJourney(string? FromPlace, string? ToPlace, decimal Km, Dictionary<string, int>? ExpectedTotals);
public record QuoteSaveBody(string? RequestId, string? FromPlace, string? ToPlace,
    string? Customer, bool Fcl, bool Lcl, bool Domestic, string? Remark,
    decimal Km, bool DangerousGoods, decimal MarginPercent, List<string>? Vehicles,
    List<QuoteSelection>? Options, Dictionary<string, int>? ExpectedTotals, List<QuoteJourney>? Routes = null);
public record CalculatedQuote(string Error, Dictionary<string, int> Prices,
    Dictionary<string, int> Totals, string Remark);

/** Server-side counterpart of quoteRate.ts: each line rounds before summing. */
public static class QuoteCalculation
{
    public static List<QuoteSaveBody> Journeys(QuoteSaveBody body) => body.Routes is null
        ? [body]
        : body.Routes.Select(route => body with { FromPlace = route.FromPlace, ToPlace = route.ToPlace,
            Km = route.Km, ExpectedTotals = route.ExpectedTotals, Routes = null }).ToList();

    public static string DateAt(DateTimeOffset now) =>
        now.ToOffset(TimeSpan.FromHours(7)).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    public static string? SheetVehicle(string code, bool dg)
    {
        var key = dg ? code + " DG" : code;
        return RateVehicles.IsKnown(key) ? RateVehicles.Canonical(key) : null;
    }

    public static CalculatedQuote Calculate(QuoteCardView card, QuoteSaveBody body)
    {
        static CalculatedQuote Refuse(string reason) => new(reason, [], [], "");
        if (body.Km <= 0 || body.Km > 3000) return Refuse("ระยะทางต้องมากกว่า 0 และไม่เกิน 3,000 กม.");
        if (body.MarginPercent < 0 || body.MarginPercent > 100) return Refuse("กำไรต้องอยู่ระหว่าง 0 ถึง 100 เปอร์เซ็นต์");
        if (body.Vehicles is null || body.Vehicles.Count == 0 || body.Vehicles.Count > 50)
            return Refuse("เลือกประเภทรถอย่างน้อย 1 แบบ และไม่เกิน 50 แบบ");
        var vehicles = body.Vehicles.Distinct(StringComparer.Ordinal).ToList();
        var selected = body.Options ?? [];
        if (selected.Any(one => one is null)) return Refuse("ข้อมูลรายการเพิ่มเติมไม่ครบถ้วน");
        if (selected.Count > 50 || selected.Select(one => one.Id).Distinct().Count() != selected.Count)
            return Refuse("รายการเพิ่มเติมซ้ำหรือเกิน 50 รายการ");
        var extras = new List<(QuoteExtraView Rate, decimal Quantity)>();
        foreach (var selection in selected)
        {
            var extra = card.Extras.FirstOrDefault(one => one.Id == selection.Id && one.Active);
            if (extra is null) return Refuse("รายการเพิ่มเติมเปลี่ยนแปลงแล้ว กรุณาโหลดสูตรใหม่");
            if (selection.Quantity < 0 || selection.Quantity > 10000 || extra.Rate < 0 || !QuoteBasis.All.Contains(extra.Basis))
                return Refuse("จำนวนหรืออัตรารายการเพิ่มเติมไม่ถูกต้อง");
            extras.Add((extra, selection.Quantity));
        }
        var prices = new Dictionary<string, int>();
        var totals = new Dictionary<string, int>();
        static decimal Round(decimal value) => decimal.Round(value, 0, MidpointRounding.AwayFromZero);
        try
        {
            foreach (var code in vehicles)
            {
                var rate = card.Vehicles.FirstOrDefault(one => one.Code == code);
                if (rate is null) return Refuse($"ไม่พบอัตราสำหรับรถ {code}");
                var sheet = SheetVehicle(code, body.DangerousGoods);
                if (sheet is null) return Refuse($"ตารางอัตราไม่มีคอลัมน์สำหรับ {rate.Label} DG กรุณานำประเภทนี้ออกก่อนบันทึก");
                if (rate.PerKm <= 0 || rate.BaseCharge < 0 || rate.Chill < 1 || rate.DangerousGoods < 0)
                    return Refuse($"อัตราของ {rate.Label} ไม่ถูกต้อง กรุณาตรวจสูตร");
                var travel = body.Km * rate.PerKm;
                var cost = Round(travel) + Round(rate.BaseCharge);
                if (rate.Chill != 1) cost += Round((travel + rate.BaseCharge) * (rate.Chill - 1));
                if (body.DangerousGoods) cost += Round(rate.DangerousGoods);
                foreach (var (extra, quantity) in extras.Where(one => one.Rate.Basis != QuoteBasis.Percent))
                    cost += Round(extra.Basis switch
                    {
                        QuoteBasis.Flat => extra.Rate,
                        QuoteBasis.PerKm => extra.Rate * body.Km,
                        _ => extra.Rate * quantity,
                    });
                var beforeShares = cost;
                foreach (var (extra, _) in extras.Where(one => one.Rate.Basis == QuoteBasis.Percent))
                    cost += Round(beforeShares * extra.Rate / 100);
                var total = cost + Round(cost * body.MarginPercent / 100);
                if (total < 0 || total > int.MaxValue) return Refuse("ราคาที่คำนวณได้อยู่นอกช่วงที่บันทึกได้");
                prices.Add(sheet, (int)total);
                totals.Add(code, (int)total);
            }
        }
        catch (OverflowException) { return Refuse("อัตราหรือจำนวนรายการเพิ่มเติมสูงเกินไป"); }
        var detail = FormattableString.Invariant($"Rate Calculator · {body.Km} km · margin {body.MarginPercent}% · {(body.DangerousGoods ? "DG" : "NON-DG")} · ราคาขายรวมกำไร/รายการเพิ่มเติม");
        foreach (var (extra, quantity) in extras)
            detail += FormattableString.Invariant($" · {extra.Label}: {extra.Rate} {extra.Basis} × {quantity}");
        if (!string.IsNullOrWhiteSpace(body.Remark)) detail += " · " + body.Remark.Trim();
        if (detail.Length > 600) return Refuse("รายละเอียดรวมหมายเหตุเกิน 600 ตัวอักษร กรุณาย่อหมายเหตุหรือรายการเพิ่มเติม");
        return new("", prices, totals, detail);
    }

    public static bool MatchesPreview(CalculatedQuote result, Dictionary<string, int>? expected) =>
        expected is not null && expected.Count == result.Totals.Count &&
        result.Totals.All(one => expected.TryGetValue(one.Key, out var value) && value == one.Value);
}
