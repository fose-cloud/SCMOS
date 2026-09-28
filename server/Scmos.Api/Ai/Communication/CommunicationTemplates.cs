using System.Text.RegularExpressions;

namespace Scmos.Api.Ai.Communication;

/// <param name="Code">The spec's template code, recorded on every draft (§30 "Template-First Communication").</param>
/// <param name="Thai">What the draft is, as a person reads it in the list.</param>
/// <param name="Audience">Who it is written to. Only <c>carrier</c> exists today; nothing is written to a customer.</param>
/// <param name="Text">The wording, with <c>{variable}</c> holes and nothing else to fill.</param>
/// <param name="Variables">The approved variables — the only values that may enter the text.</param>
public sealed record CommunicationTemplate(string Code, string Thai, string Audience, string Text, IReadOnlyList<string> Variables);

/// <summary>
/// The wording the Communication Agent drafts from (AI Agent Platform
/// specification §30): a fixed text and approved variables, no model and no
/// free wording. A template is only a draft — SCMOS sends nothing to a carrier
/// (LINE has been inbound-only since v2.7.54; whether reminders may go out at
/// all is the department's open decision). A person copies it, sends it on
/// their own channel, and says so.
///
/// <para>
/// Three of the spec's examples are here because a rule SCMOS already keeps
/// raises them. The other three are not written: the delay alert and the
/// missing-document alert are staff-facing, and the bell already raises both
/// (<c>TruckDelay</c>, <c>PodMissing</c>) — a second copy would be the same
/// alert twice; the booking request belongs to the Booking Agent, not yet built.
/// </para>
/// </summary>
public static partial class CommunicationTemplates
{
    public const string ConfirmationReminder = "CARRIER_CONFIRMATION_REMINDER";
    public const string TruckDetailReminder = "TRUCK_DETAIL_REMINDER";
    public const string PodReminder = "POD_REMINDER";

    /// <summary>The longest a filled value may be; a register cell longer than this is cut, never trusted whole.</summary>
    public const int MaxValue = 60;

    public static readonly IReadOnlyList<CommunicationTemplate> All =
    [
        new(ConfirmationReminder, "เตือนยืนยันรับงาน", "carrier",
            "เรียน {carrier} รบกวนยืนยันรับงาน {jobCode} ลูกค้า {customer} วันที่ {date} เวลา {planTime} (ขอรถเมื่อ {requestedAt}) ขอบคุณ",
            ["carrier", "jobCode", "customer", "date", "planTime", "requestedAt"]),
        new(TruckDetailReminder, "ขอทะเบียนรถและคนขับ", "carrier",
            "เรียน {carrier} รบกวนแจ้งทะเบียนรถ ชื่อคนขับ และเบอร์ติดต่อ สำหรับงาน {jobCode} ตู้ {container} วันที่ {date} เวลา {planTime} ขอบคุณ",
            ["carrier", "jobCode", "container", "date", "planTime"]),
        new(PodReminder, "ขอใบรับของ (POD)", "carrier",
            "เรียน {carrier} รบกวนส่งใบรับของ (POD) งาน {jobCode} ตู้ {container} ที่ส่งถึงวันที่ {doneDate} เพื่อใช้วางบิล ขอบคุณ",
            ["carrier", "jobCode", "container", "doneDate"]),
    ];

    public static CommunicationTemplate Of(string code) => All.Single(one => one.Code == code);

    [GeneratedRegex(@"\{([A-Za-z]+)\}")]
    private static partial Regex Hole();

    /// <summary>The variable names a text asks for, in order.</summary>
    public static IReadOnlyList<string> Holes(string text) => Hole().Matches(text).Select(match => match.Groups[1].Value).ToList();

    /// <summary>
    /// The template filled from approved values only. A value is cleaned — one
    /// line, no control characters, no braces, at most <see cref="MaxValue"/> —
    /// and a missing one reads "—", so a gap in the register shows in the draft
    /// instead of disappearing from it. A variable the template does not approve
    /// is a programming error, not a blank.
    /// </summary>
    public static string Render(CommunicationTemplate template, IReadOnlyDictionary<string, string> values)
    {
        foreach (var name in values.Keys)
            if (!template.Variables.Contains(name, StringComparer.Ordinal))
                throw new InvalidOperationException($"{template.Code} does not approve the variable {name}");
        return Hole().Replace(template.Text, match =>
        {
            var name = match.Groups[1].Value;
            if (!template.Variables.Contains(name, StringComparer.Ordinal))
                throw new InvalidOperationException($"{template.Code} uses the unapproved variable {name}");
            return values.TryGetValue(name, out var value) && Clean(value) is { Length: > 0 } clean ? clean : "—";
        });
    }

    private static string Clean(string value)
    {
        var text = new string((value ?? "").Where(ch => ch is not '{' and not '}').Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray());
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length <= MaxValue ? text : text[..MaxValue];
    }
}
