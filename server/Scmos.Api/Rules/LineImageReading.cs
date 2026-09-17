using System.Text.Json;

namespace Scmos.Api.Rules;

/// <summary>
/// What a model's reading of a photograph becomes before SCMOS trusts any of it.
///
/// <para>
/// The model is asked for the container numbers it can see and nothing else,
/// and answers in a fixed JSON shape. This turns that answer into the numbers
/// that pass <see cref="ContainerNumbers.IsValid"/> and the ones that do not.
/// A number one digit off — the ordinary way a photo is misread, a 5 for a 6
/// through mud or shadow — fails the check digit and is refused here, before
/// it can be offered to anybody. Pure, so <c>--check-line</c> can prove it
/// with a handful of answers and no model.
/// </para>
///
/// <para>
/// Read on demand, not on arrival: a photo is looked at only when a text
/// from the same room within a few minutes reports the truck at the site and
/// names no box — "ลอรีอัล รถถึงคลังแล้วนะครับ 13.39 น." after two photos of
/// the door and the seal (17 Sep 2026). The photo says which job; the text
/// says when. See <c>LinePhotoPairing</c>.
/// </para>
/// </summary>
public static class LineImageReading
{
    /// <summary>
    /// What the model is told. Read-only, one job, no invention: a photo of a
    /// delivery note or a driver is answered with an empty list and a note.
    /// </summary>
    public const string Instructions =
        "You are reading a photograph posted by a Thai haulier's driver in a LINE group. " +
        "Report every shipping container number that is legibly painted or printed on a container " +
        "in the photo — the owner code of four letters followed by seven digits, such as MSKU1234567. " +
        "The seventh digit is the check digit and is usually painted in its own small frame after the " +
        "six-digit serial: GCXU 513490 [0] is GCXU5134900 — always include it. The size and type code " +
        "painted below or beside the number (four characters such as 45G1, 22G1 or 42R1) is not part of " +
        "the number; leave it out. Read only what is visible; never guess a digit, and if a number is " +
        "partly hidden or blurred leave it out. If no container number is visible, return an empty list. " +
        "In the note, say in one short sentence what the photo shows.";

    /// <summary>The ISO size and type code — 45G1, 22G1, 42R1 — glued onto the end of a reading.</summary>
    private static readonly System.Text.RegularExpressions.Regex SizeTypeTail =
        new(@"\d{2}[A-Z]\d$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// A reading as the model gave it, cut to the number. The door of the box
    /// that taught this (17 Sep 2026) reads "GCXU 513490 [0]" over "45G1": a
    /// model that runs the two lines together gives GCXU513490045G1, and one
    /// that stops at the frame gives GCXU513490 and is refused. The tail
    /// comes off when what is left is a number; a run longer than a number
    /// is cut to its first eleven characters when those are a number.
    /// </summary>
    public static string Tidy(string? reading)
    {
        var number = ContainerNumbers.Normalise(reading);
        if (number.Length > 11)
        {
            var cut = SizeTypeTail.Replace(number, "");
            if (ContainerNumbers.IsShaped(cut)) return cut;
            if (ContainerNumbers.IsValid(number[..11])) return number[..11];
        }
        return number;
    }

    /// <summary>The shape the model must answer in. Strict: nothing else can come back.</summary>
    public const string Schema =
        "{\"type\":\"object\",\"properties\":{" +
        "\"containers\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
        "\"note\":{\"type\":\"string\"}}," +
        "\"required\":[\"containers\",\"note\"],\"additionalProperties\":false}";

    /// <param name="Valid">Numbers that are shaped like a container and whose check digit agrees, in the order read, without repeats.</param>
    /// <param name="Rejected">Numbers the model offered that failed the shape or the check digit — shown to a person, never used.</param>
    /// <param name="Note">The model's one sentence on what the photo shows, or what went wrong reading its answer.</param>
    public record Reading(IReadOnlyList<string> Valid, IReadOnlyList<string> Rejected, string Note)
    {
        public static readonly Reading Empty = new([], [], "");
    }

    /// <summary>Reads the model's JSON answer. Never throws: an unreadable answer is a Reading with a note.</summary>
    public static Reading Read(string? answer)
    {
        if (string.IsNullOrWhiteSpace(answer)) return new Reading([], [], "ไม่มีคำตอบจากโมเดล");

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(answer).RootElement;
        }
        catch (JsonException)
        {
            return new Reading([], [], "คำตอบจากโมเดลอ่านไม่ออก");
        }

        var note = root.TryGetProperty("note", out var n) && n.ValueKind == JsonValueKind.String
            ? (n.GetString() ?? "").Trim()
            : "";
        if (note.Length > 200) note = note[..200];

        var valid = new List<string>();
        var rejected = new List<string>();
        if (root.TryGetProperty("containers", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var one in list.EnumerateArray())
            {
                if (one.ValueKind != JsonValueKind.String) continue;
                var number = Tidy(one.GetString());
                if (number.Length == 0) continue;
                if (ContainerNumbers.IsValid(number))
                {
                    if (!valid.Contains(number, StringComparer.Ordinal)) valid.Add(number);
                }
                else if (!rejected.Contains(number, StringComparer.Ordinal))
                {
                    rejected.Add(number);
                }
            }
        }
        return new Reading(valid, rejected, note);
    }
}
