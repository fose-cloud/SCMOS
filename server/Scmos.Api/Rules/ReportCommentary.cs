using System.Globalization;
using System.Text.RegularExpressions;

namespace Scmos.Api.Rules;

/// <summary>
/// The management summary an assistant may draft, and the fence around it.
///
/// <para>
/// The commentary is the one part of a report nobody computed. Everything else
/// on the page is arithmetic over the register; this is prose, and prose is
/// where a number can appear that nothing measured. A sentence reading "OTD
/// improved by 4.7 points on July" in a document sent to a customer is worse
/// than no commentary at all, because it is indistinguishable from the figures
/// beside it that are true.
/// </para>
///
/// <para>
/// So the arrangement is: the figures are computed here and handed to the model
/// as the only material it may use, and every number it writes back is checked
/// against that list before the text is allowed near the report. A draft that
/// cites a figure nobody gave it is rejected whole, not edited down — a summary
/// that is wrong in one clause is not made safe by deleting the clause.
/// </para>
/// </summary>
public static partial class ReportCommentary
{
    /// <summary>
    /// What the model is allowed to know, written out for it.
    ///
    /// Deliberately only the figures already printed on the report. No prices,
    /// no rates, no driver or contact details — the commentary explains the
    /// performance page and needs nothing else, and what is not sent cannot
    /// leak.
    /// </summary>
    public static string Facts(MonthlyReportView report)
    {
        var summary = report.Summary;
        // Invariant throughout: the figures are compared as text against what
        // comes back, so a decimal comma here and a decimal point there would
        // make every rate look invented.
        string N(double? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

        var lines = new List<string>
        {
            $"Customer: {report.Customer}",
            $"Month: {report.Month}",
            $"Total trips: {summary.Trips}",
            $"Trips that could be measured for arrival time: {summary.Measurable}",
            $"On time: {summary.OnTime}",
            $"Late: {summary.Late}",
            summary.Otd is null
                ? "On-time delivery rate: CANNOT BE MEASURED (no trips carry an arrival time)"
                : $"On-time delivery rate: {N(summary.Otd)}%",
            $"Target: {N(report.Target)}%",
            $"Share of trips that could be measured: {summary.Coverage}%",
        };

        if (report.Vendors.Count > 0)
        {
            lines.Add("");
            lines.Add("Carriers (name, trips, measurable, on time, late, rate):");
            lines.AddRange(report.Vendors.Select(vendor =>
                $"- {vendor.Name}: {vendor.Trips} trips, {vendor.Measurable} measurable, "
                + $"{vendor.OnTime} on time, {vendor.Late} late, "
                + (vendor.Otd is null ? "rate not calculated (sample too small)" : $"{N(vendor.Otd)}%")));
        }

        if (report.Trend.Count > 1)
        {
            lines.Add("");
            lines.Add("Previous months (month, rate, measurable trips):");
            lines.AddRange(report.Trend.Select(point =>
                $"- {point.Month}: {(point.Otd is null ? "not measurable" : N(point.Otd) + "%")}, "
                + $"{point.Measurable} measurable"));
        }

        if (report.DelayReasons.Count > 0)
        {
            lines.Add("");
            lines.Add("Recorded delay reasons (reason, trips):");
            lines.AddRange(report.DelayReasons.Select(reason => $"- {reason.Label}: {reason.Value}"));
        }

        return string.Join(Environment.NewLine, lines).TrimEnd();
    }

    /// <summary>
    /// What the model is told to do, and what it is told never to do.
    ///
    /// The refusals are specific because the general instruction — "be accurate"
    /// — is the one every model already believes it is following. Naming the two
    /// failures that matter here (arithmetic it was not given, and a confident
    /// reading of an unmeasured month) is what makes them checkable afterwards.
    /// </summary>
    public const string Instruction = """
        You write the management commentary for a logistics performance report.

        Rules, in order of importance:

        1. Use ONLY the figures given to you below. Do not calculate new ones —
           no differences, no averages, no percentages of your own. If a
           comparison would need a number that is not listed, do not make the
           comparison.
        2. If the on-time rate cannot be measured, say plainly that it cannot be
           measured and why. Never describe an unmeasured month as good or bad.
        3. Where the share of measurable trips is below 90%, say that the rate
           covers only part of the month.
        4. Name carriers only as they are spelled below.
        5. Do not recommend commercial action such as changing a carrier or a
           price. Describe what happened and what should be looked into.

        Write 3 to 5 sentences in Thai, plain and factual, for a manager who will
        send this to the customer. No headings, no bullet points, no preamble.
        """;

    /// <summary>Every number that appears in the text, as it was written.</summary>
    [GeneratedRegex(@"\d+(?:[.,]\d+)*")]
    private static partial Regex Numbers();

    /// <summary>
    /// Whether the draft cites anything nobody measured.
    ///
    /// <para>
    /// Compares every number in the prose against the numbers handed over. It is
    /// a blunt instrument on purpose: it cannot tell whether a figure is used
    /// correctly, only whether it exists at all — and "exists at all" is the
    /// failure that produces an invented statistic in a customer's inbox.
    /// </para>
    ///
    /// <para>
    /// Years and the month are allowed through: "07/2026" is the period being
    /// written about and appears in the facts as text rather than as a figure.
    /// So is a bare ordinal under ten, which is how prose counts things — "the
    /// three carriers below target" is a sentence, not a statistic.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> Invented(string draft, string facts)
    {
        var allowed = Numbers().Matches(facts)
            .Select(one => Normalise(one.Value))
            .ToHashSet();

        return Numbers().Matches(draft ?? "")
            .Select(one => Normalise(one.Value))
            .Where(one => one.Length > 0)
            // Small whole numbers are how prose counts, not how it cites.
            .Where(one => !(double.TryParse(one, NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
                            && value < 10 && value == Math.Floor(value)))
            .Where(one => !allowed.Contains(one))
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// "1,234" and "1234" are the same figure; "88.8" and "88.80" are not
    /// necessarily written the same way either.
    /// </summary>
    private static string Normalise(string number)
    {
        var text = number.Replace(",", "");
        if (!text.Contains('.')) return text.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0";
        return text.TrimEnd('0').TrimEnd('.');
    }

    /// <summary>
    /// Why the assistant could not be reached, in words that say what to do next.
    ///
    /// <para>
    /// The distinction that matters is inside the 429. OpenAI returns it both
    /// for "you are calling too fast" and for "this account has no credit", and
    /// they are opposite instructions: the first means wait a moment, the second
    /// means no amount of waiting will ever work and somebody has to go and pay.
    /// A single "try again later" for both sends a manager back to the button
    /// every ten minutes for a fault only the finance department can clear.
    /// </para>
    ///
    /// <para>
    /// The others separate the two audiences. A bad key is for whoever
    /// administers the system; an empty account is for whoever owns the card;
    /// and neither of them is the person looking at the report, who mostly needs
    /// to know they should type the paragraph themselves and move on.
    /// </para>
    /// </summary>
    public static string Explain(int status, string? detail)
    {
        var text = (detail ?? "").ToLowerInvariant();

        // Checked before the bare 429, because it arrives as one.
        if (text.Contains("insufficient_quota") || text.Contains("exceeded your current quota")
            || text.Contains("billing"))
        {
            return "บัญชี OpenAI ไม่มีเครดิตเหลือ — ต้องเติมเครดิตก่อนจึงจะใช้ปุ่มนี้ได้ "
                 + "· ระหว่างนี้พิมพ์บทสรุปเองได้ตามปกติ";
        }

        return status switch
        {
            401 or 403 =>
                "คีย์ของผู้ช่วย AI ไม่ถูกต้องหรือถูกยกเลิกแล้ว — แจ้งผู้ดูแลระบบ "
                + "· ระหว่างนี้พิมพ์บทสรุปเองได้ตามปกติ",
            404 =>
                "บัญชี OpenAI นี้ยังไม่มีสิทธิ์ใช้โมเดลที่ตั้งไว้ — แจ้งผู้ดูแลระบบ "
                + "· ระหว่างนี้พิมพ์บทสรุปเองได้ตามปกติ",
            429 =>
                "เรียกใช้ถี่เกินไป — รอสักครู่แล้วกดใหม่",
            >= 500 =>
                "ฝั่ง OpenAI ขัดข้องอยู่ — ลองใหม่ภายหลัง · ระหว่างนี้พิมพ์บทสรุปเองได้ตามปกติ",
            _ =>
                "ขอบทสรุปจากผู้ช่วยไม่สำเร็จ — พิมพ์บทสรุปเองได้ในช่องด้านล่าง",
        };
    }

    /// <summary>
    /// The draft, or why it was thrown away.
    ///
    /// Rejected whole rather than trimmed: a summary that is wrong in one clause
    /// is not made right by deleting that clause, and a reader who saw the first
    /// version would have no way of knowing which one they got.
    /// </summary>
    public static (string? Text, string? Refusal) Judge(string draft, string facts)
    {
        var text = (draft ?? "").Trim();
        if (text.Length == 0) return (null, "ผู้ช่วยไม่ได้เขียนอะไรกลับมา");
        if (text.Length > 1200) return (null, "ข้อความยาวเกินกว่าที่จะเป็นบทสรุปผู้บริหาร");

        var invented = Invented(text, facts);
        return invented.Count == 0
            ? (text, null)
            : (null, "ผู้ช่วยอ้างตัวเลขที่ไม่มีอยู่ในรายงาน (" + string.Join(", ", invented.Take(5))
                     + ") จึงไม่ถูกนำมาใช้ — กรุณาเขียนสรุปเอง");
    }
}
