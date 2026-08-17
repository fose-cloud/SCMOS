using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// Why a shipment was late.
///
/// Eight categories, fixed. A free-text reason box produces a thousand ways to
/// write "traffic" and nothing that can be counted, which is why the July plan
/// can say two jobs were delayed and nothing at all about why. The operator
/// still writes what happened in their own words — that goes in the detail —
/// but the category is what the KPI counts and what tells a supplier meeting
/// whether the problem is their trucks or our documents.
/// </summary>
public enum DelayCategory
{
    Truck,
    Driver,
    Port,
    Depot,
    Customer,
    Documentation,
    Traffic,
    Other,
}

/// <summary>Who owns fixing it. The category usually implies this, and it is worth saying out loud.</summary>
public enum ResponsibleParty
{
    Subcontractor,
    Operation,
    CustomerService,
    Customer,
    Port,
    None,
}

public record DelaySuggestion(DelayCategory Category, ResponsibleParty Responsible, string Basis, double Confidence);

public static partial class DelayReasons
{
    [GeneratedRegex(@"รถเสีย|เครื่องเสีย|ยางแตก|breakdown|broke ?down|truck.*(fail|repair)|ไม่มีรถ|รถไม่พอ|no truck|เสียกลางทาง", RegexOptions.IgnoreCase)]
    private static partial Regex TruckWords();

    // Thai is written without spaces between words, so a short token matches
    // inside longer ones: a bare "ลา" (leave) hits both "กลางทาง" and "ลานตู้",
    // which sent breakdowns and empty-depot delays to the driver category and
    // billed them to the subcontractor. Every token here is long enough to mean
    // only what it says.
    [GeneratedRegex(@"คนขับ|driver|ป่วย|ลาป่วย|ลากิจ|ขาดงาน|sick|ไม่มางาน|no ?show|เมาสุรา|ใบขับขี่|licen[cs]e.*driver", RegexOptions.IgnoreCase)]
    private static partial Regex DriverWords();

    [GeneratedRegex(@"ท่าเรือ|port|terminal|gate.*(close|full)|ปิดท่า|คิวท่า|vessel|เรือ.*(delay|ล่าช้า)|ลานท่า", RegexOptions.IgnoreCase)]
    private static partial Regex PortWords();

    [GeneratedRegex(@"ลานตู้|depot|yard|cy\b|ตู้ไม่พร้อม|ไม่มีตู้|empty.*(not|ไม่)|ลานปิด", RegexOptions.IgnoreCase)]
    private static partial Regex DepotWords();

    [GeneratedRegex(@"ลูกค้า|customer|consignee|shipper|โรงงาน.*(ปิด|ไม่พร้อม)|รอ.*ลูกค้า|คิวโหลด|warehouse.*full", RegexOptions.IgnoreCase)]
    private static partial Regex CustomerWords();

    [GeneratedRegex(@"เอกสาร|document|b/?l\b|ใบขน|customs|ศุลกากร|permit|ใบอนุญาต|invoice|packing|d/?o\b|e-?card", RegexOptions.IgnoreCase)]
    private static partial Regex DocumentWords();

    [GeneratedRegex(@"รถติด|traffic|จราจร|congestion|ฝนตก|น้ำท่วม|อุบัติเหตุ|accident|ถนน.*(ปิด|ซ่อม)|flood|rain", RegexOptions.IgnoreCase)]
    private static partial Regex TrafficWords();

    /// <summary>The category a free-text reason most likely belongs to.</summary>
    private static readonly (Func<string, bool> Test, DelayCategory Category, string Label)[] Matchers =
    [
        // Ordered by how specific the words are. A breakdown is a truck problem
        // even when the sentence also mentions the driver who reported it, so
        // Truck is asked before Driver.
        (text => TruckWords().IsMatch(text), DelayCategory.Truck, "คำที่เกี่ยวกับรถ"),
        (text => DriverWords().IsMatch(text), DelayCategory.Driver, "คำที่เกี่ยวกับคนขับ"),
        (text => DocumentWords().IsMatch(text), DelayCategory.Documentation, "คำที่เกี่ยวกับเอกสาร"),
        (text => DepotWords().IsMatch(text), DelayCategory.Depot, "คำที่เกี่ยวกับลานตู้"),
        (text => PortWords().IsMatch(text), DelayCategory.Port, "คำที่เกี่ยวกับท่าเรือ"),
        (text => TrafficWords().IsMatch(text), DelayCategory.Traffic, "คำที่เกี่ยวกับการจราจร"),
        (text => CustomerWords().IsMatch(text), DelayCategory.Customer, "คำที่เกี่ยวกับลูกค้า"),
    ];

    /// <summary>
    /// Reads a reason written in Thai or English and suggests a category.
    ///
    /// Deterministic and explainable on purpose: it says which words it matched,
    /// so an operator can disagree with it and be right. It is a suggestion the
    /// person confirms, never a classification applied behind their back — a
    /// delay booked to the wrong party is an argument with a supplier that
    /// nobody can win.
    ///
    /// The OpenAI delay agent can be asked for a second opinion on the text this
    /// cannot place; the answer arrives the same way, as a suggestion.
    /// </summary>
    public static DelaySuggestion Classify(string reason)
    {
        var text = Formats.Clean(reason);
        if (text.Length == 0)
            return new DelaySuggestion(DelayCategory.Other, ResponsibleParty.None, "ไม่มีข้อความให้อ่าน", 0);

        var hits = Matchers.Where(matcher => matcher.Test(text)).ToList();

        if (hits.Count == 0)
            return new DelaySuggestion(DelayCategory.Other, ResponsibleParty.None, "ไม่พบคำที่จัดหมวดได้", 0);

        // More than one category matching is a real signal, not a failure: "รอ
        // เอกสารจากลูกค้า" is both. The first match wins because the list is in
        // order of how specific the words are, and the confidence drops to say
        // the reading was not clean.
        var best = hits[0];
        var confidence = hits.Count == 1 ? 0.9 : 0.6;
        var basis = hits.Count == 1
            ? best.Label
            : $"{best.Label} (ยังเข้าได้กับ {string.Join(", ", hits.Skip(1).Select(h => Thai(h.Category)))})";

        return new DelaySuggestion(best.Category, ResponsibleFor(best.Category), basis, confidence);
    }

    /// <summary>Who is normally answerable for a category. A person may override it.</summary>
    public static ResponsibleParty ResponsibleFor(DelayCategory category) => category switch
    {
        DelayCategory.Truck or DelayCategory.Driver => ResponsibleParty.Subcontractor,
        DelayCategory.Documentation => ResponsibleParty.CustomerService,
        DelayCategory.Customer => ResponsibleParty.Customer,
        DelayCategory.Port or DelayCategory.Depot => ResponsibleParty.Port,
        DelayCategory.Traffic => ResponsibleParty.None,
        _ => ResponsibleParty.Operation,
    };

    public static string Thai(DelayCategory category) => category switch
    {
        DelayCategory.Truck => "รถ",
        DelayCategory.Driver => "คนขับ",
        DelayCategory.Port => "ท่าเรือ",
        DelayCategory.Depot => "ลานตู้",
        DelayCategory.Customer => "ลูกค้า",
        DelayCategory.Documentation => "เอกสาร",
        DelayCategory.Traffic => "จราจร",
        _ => "อื่นๆ",
    };

    public static string Thai(ResponsibleParty party) => party switch
    {
        ResponsibleParty.Subcontractor => "ผู้รับเหมาขนส่ง",
        ResponsibleParty.Operation => "ฝ่ายปฏิบัติการ",
        ResponsibleParty.CustomerService => "ฝ่าย CS",
        ResponsibleParty.Customer => "ลูกค้า",
        ResponsibleParty.Port => "ท่าเรือ / ลานตู้",
        _ => "ไม่ระบุ",
    };

    /// <summary>
    /// Whether a delay counts against the subcontractor's KPI. Traffic and port
    /// congestion are nobody's fault in a way a scorecard can use, and counting
    /// them makes the scorecard measure weather.
    /// </summary>
    public static bool CountsAgainstCarrier(DelayCategory category) =>
        category is DelayCategory.Truck or DelayCategory.Driver;
}
